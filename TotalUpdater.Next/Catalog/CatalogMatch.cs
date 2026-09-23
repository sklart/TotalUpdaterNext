using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TotalUpdater.Next.Core;

namespace TotalUpdater.Next.Catalog
{
    public enum CatalogMatchKind { Exact, Alias, RemoteExact, Ambiguous, NotFound }

    public sealed class CatalogMatchResult
    {
        public CatalogMatchKind Kind { get; set; }
        public PluginCatalogEntry Entry { get; set; }
        public IList<PluginCatalogEntry> Candidates { get; set; } = new List<PluginCatalogEntry>();
    }

    public static class CatalogMatcher
    {
        public static CatalogMatchResult Match(PluginType type, string fileName, IEnumerable<PluginCatalogEntry> entries, bool remote = false)
        {
            var name = Path.GetFileName(fileName ?? "");
            var matches = (entries ?? Enumerable.Empty<PluginCatalogEntry>())
                .Where(x => x.PluginType == type && x.Aliases != null && x.Aliases.Any(a => a.Equals(name, StringComparison.OrdinalIgnoreCase)))
                .GroupBy(x => x.Id, StringComparer.OrdinalIgnoreCase).Select(x => x.First()).ToList();
            if (matches.Count == 0) return new CatalogMatchResult { Kind = CatalogMatchKind.NotFound };
            if (matches.Count > 1) return new CatalogMatchResult { Kind = CatalogMatchKind.Ambiguous, Candidates = matches };
            var entry = matches[0];
            var stem = Path.GetFileNameWithoutExtension(name);
            return new CatalogMatchResult { Kind = remote ? CatalogMatchKind.RemoteExact :
                stem.Equals(entry.Id, StringComparison.OrdinalIgnoreCase) ? CatalogMatchKind.Exact : CatalogMatchKind.Alias,
                Entry = entry, Candidates = matches };
        }
    }
}
