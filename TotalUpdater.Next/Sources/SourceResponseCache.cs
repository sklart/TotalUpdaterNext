using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace TotalUpdater.Next.Sources
{
    // A cache belongs to one check run only.  It deliberately stores raw responses,
    // so entries from the same GitHub release can apply different asset filters.
    public sealed class SourceResponseCache
    {
        private readonly ConcurrentDictionary<string, Lazy<Task<string>>> _responses = new ConcurrentDictionary<string, Lazy<Task<string>>>(StringComparer.Ordinal);
        private readonly ConcurrentDictionary<string, Lazy<Task<byte[]>>> _binaryResponses = new ConcurrentDictionary<string, Lazy<Task<byte[]>>>(StringComparer.Ordinal);
        public Task<string> GetOrAdd(string key, Func<Task<string>> factory)
        {
            return _responses.GetOrAdd(key, _ => new Lazy<Task<string>>(factory, LazyThreadSafetyMode.ExecutionAndPublication)).Value;
        }
        public Task<byte[]> GetOrAddBytes(string key, Func<Task<byte[]>> factory)
        {
            return _binaryResponses.GetOrAdd(key, _ => new Lazy<Task<byte[]>>(factory, LazyThreadSafetyMode.ExecutionAndPublication)).Value;
        }
    }

    public interface ICachedUpdateSourceProvider : IUpdateSourceProvider
    {
        Task<SourceQueryResult> QueryAsync(TotalUpdater.Next.Catalog.CatalogSource source, SourceResponseCache cache, CancellationToken cancellationToken);
    }
}
