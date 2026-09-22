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
        private readonly ConcurrentDictionary<string, Task<string>> _responses = new ConcurrentDictionary<string, Task<string>>(StringComparer.OrdinalIgnoreCase);
        public Task<string> GetOrAdd(string key, Func<Task<string>> factory)
        {
            return _responses.GetOrAdd(key, _ => factory());
        }
    }

    public interface ICachedUpdateSourceProvider : IUpdateSourceProvider
    {
        Task<SourceQueryResult> QueryAsync(TotalUpdater.Next.Catalog.CatalogSource source, SourceResponseCache cache, CancellationToken cancellationToken);
    }
}
