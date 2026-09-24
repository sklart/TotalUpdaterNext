using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using TotalUpdater.Next.Settings;

namespace TotalUpdater.Next.Sources
{
    // Per-check cache. Shared sources receive exactly one retry; after two
    // failures their host is circuit-open until the next check run.
    public sealed class SourceResponseCache
    {
        private readonly ConcurrentDictionary<string, Lazy<Task<string>>> _responses = new ConcurrentDictionary<string, Lazy<Task<string>>>(StringComparer.Ordinal);
        private readonly ConcurrentDictionary<string, Lazy<Task<byte[]>>> _binaryResponses = new ConcurrentDictionary<string, Lazy<Task<byte[]>>>(StringComparer.Ordinal);
        private readonly ConcurrentDictionary<string, Lazy<Task<SharedSourceResponse>>> _sharedMetadata = new ConcurrentDictionary<string, Lazy<Task<SharedSourceResponse>>>(StringComparer.Ordinal);
        private readonly SourceHealthMonitor _health = new SourceHealthMonitor();
        private readonly PersistentSourceCache _persistent;
        private readonly bool _persistentEnabled;
        public SourceResponseCache(PersistentSourceCache persistent = null, AppSettings settings = null) { _persistent = persistent ?? new PersistentSourceCache(); _persistentEnabled = settings == null || settings.UsePersistentMetadataCache; }
        public SourceHealthMonitor Health { get { return _health; } }

        public async Task<string> GetOrAdd(string key, Func<Task<string>> factory)
        {
            var lazy = _responses.GetOrAdd(key, _ => new Lazy<Task<string>>(factory, LazyThreadSafetyMode.ExecutionAndPublication));
            try { return await lazy.Value.ConfigureAwait(false); }
            catch { Lazy<Task<string>> ignored; _responses.TryRemove(key, out ignored); throw; }
        }

        public async Task<string> GetSharedOrAdd(string host, string key, Func<Task<string>> factory)
        {
            if (_health.IsUnavailable(host)) throw new InvalidOperationException("shared source unavailable for this check: " + host);
            try { var value = await GetOrAdd(key, factory).ConfigureAwait(false); _health.RecordSuccess(host); return value; }
            catch
            {
                try { var value = await GetOrAdd(key, factory).ConfigureAwait(false); _health.RecordSuccess(host); return value; }
                catch (Exception ex) { _health.RecordFailure(host, ex); throw; }
            }
        }

        public async Task<byte[]> GetOrAddBytes(string key, Func<Task<byte[]>> factory)
        {
            var lazy = _binaryResponses.GetOrAdd(key, _ => new Lazy<Task<byte[]>>(factory, LazyThreadSafetyMode.ExecutionAndPublication));
            try { return await lazy.Value.ConfigureAwait(false); }
            catch { Lazy<Task<byte[]>> ignored; _binaryResponses.TryRemove(key, out ignored); throw; }
        }

        public Task<SharedSourceResponse> GetSharedMetadataAsync(string host, string key, string sourceUrl, Func<Task<byte[]>> factory, Func<byte[], string> decode, string contentType, string encodingName, CancellationToken cancellationToken = default(CancellationToken))
        {
            return GetSharedMetadataAsync(host, key, sourceUrl, factory, factory, decode, contentType, encodingName, cancellationToken);
        }

        public async Task<SharedSourceResponse> GetSharedMetadataAsync(string host, string key, string sourceUrl, Func<Task<byte[]>> firstAttempt, Func<Task<byte[]>> retryAttempt, Func<byte[], string> decode, string contentType, string encodingName, CancellationToken cancellationToken = default(CancellationToken))
        {
            if (_health.IsUnavailable(host))
            {
                if (_health.IsTransient(host)) return ReadPersistentOrThrow(key, decode, host);
                throw new InvalidOperationException("shared source unavailable for this check: " + host);
            }
            var lazy = _sharedMetadata.GetOrAdd(key, _ => new Lazy<Task<SharedSourceResponse>>(() => FetchSharedMetadataAsync(host, key, sourceUrl, firstAttempt, retryAttempt, decode, contentType, encodingName, cancellationToken), LazyThreadSafetyMode.ExecutionAndPublication));
            try { return await lazy.Value.ConfigureAwait(false); }
            catch { Lazy<Task<SharedSourceResponse>> ignored; _sharedMetadata.TryRemove(key, out ignored); throw; }
        }

        private async Task<SharedSourceResponse> FetchSharedMetadataAsync(string host, string key, string sourceUrl, Func<Task<byte[]>> firstAttempt, Func<Task<byte[]>> retryAttempt, Func<byte[], string> decode, string contentType, string encodingName, CancellationToken cancellationToken)
        {
            try
            {
                var bytes = await firstAttempt().ConfigureAwait(false);
                if (_persistentEnabled) _persistent.Save(key, sourceUrl, bytes, contentType, encodingName);
                _health.RecordSuccess(host);
                return new SharedSourceResponse { Text = decode(bytes) };
            }
            catch (Exception firstFailure)
            {
                if (!IsTransientFailure(firstFailure, cancellationToken)) { _health.RecordFailure(host, firstFailure); throw; }
                try
                {
                    var bytes = await retryAttempt().ConfigureAwait(false);
                    if (_persistentEnabled) _persistent.Save(key, sourceUrl, bytes, contentType, encodingName);
                    _health.RecordSuccess(host);
                    return new SharedSourceResponse { Text = decode(bytes) };
                }
                catch (Exception ex)
                {
                    if (!IsTransientFailure(ex, cancellationToken)) { _health.RecordFailure(host, ex); throw; }
                    _health.RecordFailure(host, ex);
                    return ReadPersistentOrThrow(key, decode, host, ex);
                }
            }
        }

        private static bool IsTransientFailure(Exception error, CancellationToken cancellationToken)
        {
            if (cancellationToken.IsCancellationRequested && error is OperationCanceledException) return false;
            var health = SourceHealthMonitor.Classify(error);
            return health == SourceHealth.Timeout || health == SourceHealth.DnsFailure || health == SourceHealth.ConnectionFailure;
        }

        private SharedSourceResponse ReadPersistentOrThrow(string key, Func<byte[], string> decode, string host, Exception liveFailure = null)
        {
            CachedSourceResponse cached;
            if (_persistentEnabled && _persistent.TryRead(key, out cached))
                return new SharedSourceResponse { Text = decode(cached.Bytes), IsCached = true, CachedAt = cached.FetchedUtc, IsStale = cached.IsStale(DateTime.UtcNow) };
            throw new InvalidOperationException("shared source unavailable for this check: " + host, liveFailure);
        }
    }

    public sealed class SharedSourceResponse
    {
        public string Text { get; set; }
        public bool IsCached { get; set; }
        public DateTime? CachedAt { get; set; }
        public bool IsStale { get; set; }
    }

    public interface ICachedUpdateSourceProvider : IUpdateSourceProvider
    {
        Task<SourceQueryResult> QueryAsync(TotalUpdater.Next.Catalog.CatalogSource source, SourceResponseCache cache, CancellationToken cancellationToken);
    }
}
