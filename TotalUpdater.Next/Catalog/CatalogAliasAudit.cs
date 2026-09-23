using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace TotalUpdater.Next.Catalog
{
    public sealed class CatalogAliasIssue
    {
        public string Kind { get; set; } = "";
        public string Alias { get; set; } = "";
        public string Entries { get; set; } = "";
    }

    public static class CatalogAliasAudit
    {
        public static IList<CatalogAliasIssue> Audit(IEnumerable<PluginCatalogEntry> entries)
        {
            var issues = new List<CatalogAliasIssue>();
            var all = new List<Tuple<PluginCatalogEntry, string>>();
            foreach (var entry in entries ?? Enumerable.Empty<PluginCatalogEntry>())
            {
                var local = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var alias in entry.Aliases ?? new List<string>())
                {
                    if (!Regex.IsMatch(alias ?? "", @"(?i)^[^\\/:*?""<>|]+\.(?:u?w(?:cx|lx|fx|dx)(?:64)?|exe)$") ||
                        !IsCompatible(entry, alias))
                        issues.Add(new CatalogAliasIssue { Kind = "InvalidExtension", Alias = alias, Entries = entry.Id });
                    if (!local.Add(alias)) issues.Add(new CatalogAliasIssue { Kind = "DuplicateAlias", Alias = alias, Entries = entry.Id });
                    all.Add(Tuple.Create(entry, alias));
                }
            }
            foreach (var group in all.GroupBy(x => NormalizeCompanion(x.Item2), StringComparer.OrdinalIgnoreCase).Where(x => x.Select(y => y.Item1.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count() > 1))
                issues.Add(new CatalogAliasIssue { Kind = group.Select(x => x.Item1.PluginType).Distinct().Count() == 1 ? "SameTypeCollision" : "CrossTypeCollision",
                    Alias = group.Key, Entries = String.Join(",", group.Select(x => x.Item1.Id).Distinct(StringComparer.OrdinalIgnoreCase)) });
            return issues;
        }

        private static string NormalizeCompanion(string alias)
        {
            return Regex.Replace(Regex.Replace(alias ?? "", @"(?i)\.uw(cx|lx|fx|dx)$", ".w$1"), @"(?i)\.w(cx|lx|fx|dx)64$", ".w$1");
        }

        private static bool IsCompatible(PluginCatalogEntry entry, string alias)
        {
            var ext = Path.GetExtension(alias ?? "").ToLowerInvariant();
            var code = entry.PluginType == Core.PluginType.Wcx ? "cx" : entry.PluginType == Core.PluginType.Wlx ? "lx" :
                entry.PluginType == Core.PluginType.Wfx ? "fx" : entry.PluginType == Core.PluginType.Wdx ? "dx" : "";
            if (entry.PluginType == Core.PluginType.TotalCommander) return ext == ".exe";
            return code.Length > 0 && (ext == ".w" + code || ext == ".uw" + code || ext == ".w" + code + "64" || ext == ".uw" + code + "64");
        }
    }
}
