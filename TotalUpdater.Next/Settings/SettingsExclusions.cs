using System;
using System.Collections.Generic;
using System.Linq;

namespace TotalUpdater.Next.Settings
{
    public sealed class SettingsExclusion
    {
        public string Key { get; set; }
        public string Kind { get; set; }
    }

    public static class SettingsExclusions
    {
        public static IList<SettingsExclusion> List(AppSettings settings)
        {
            if (settings == null) return new List<SettingsExclusion>();
            return (settings.ExcludedCatalogIds ?? new List<string>()).Where(x => !String.IsNullOrWhiteSpace(x)).Select(x => new SettingsExclusion { Key = x, Kind = "Catalog ID" })
                .Concat((settings.ExcludedUnknownPaths ?? new List<string>()).Where(x => !String.IsNullOrWhiteSpace(x)).Select(x => new SettingsExclusion { Key = x, Kind = DescribePath(x) }))
                .OrderBy(x => x.Kind).ThenBy(x => x.Key, StringComparer.OrdinalIgnoreCase).ToList();
        }

        public static void Restore(AppSettings settings, SettingsExclusion exclusion)
        {
            if (settings == null || exclusion == null || String.IsNullOrWhiteSpace(exclusion.Key)) return;
            if (String.Equals(exclusion.Kind, "Catalog ID", StringComparison.Ordinal)) settings.ExcludedCatalogIds = Without(settings.ExcludedCatalogIds, exclusion.Key);
            else settings.ExcludedUnknownPaths = Without(settings.ExcludedUnknownPaths, exclusion.Key);
        }

        public static void RestoreAll(AppSettings settings)
        {
            if (settings == null) return;
            settings.ExcludedCatalogIds = new List<string>(); settings.ExcludedUnknownPaths = new List<string>();
        }

        public static bool ContainsUnknown(AppSettings settings, string key)
        {
            return settings != null && (settings.ExcludedUnknownPaths ?? new List<string>()).Contains(key, StringComparer.OrdinalIgnoreCase);
        }

        public static IList<string> NormalizeUnknownPaths(IEnumerable<string> values)
        {
            return (values ?? Enumerable.Empty<string>()).Where(x => !String.IsNullOrWhiteSpace(x)).Select(MigrateLegacyUnknownPath)
                .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }

        public static string MigrateLegacyUnknownPath(string value)
        {
            if (String.IsNullOrWhiteSpace(value)) return value;
            foreach (var type in new[] { "Wcx", "Wlx", "Wfx", "Wdx" })
            {
                var prefix = type + "|";
                if (value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return type + "::" + value.Substring(prefix.Length);
            }
            return value;
        }

        private static IList<string> Without(IEnumerable<string> values, string key)
        {
            return (values ?? Enumerable.Empty<string>()).Where(x => !String.Equals(x, key, StringComparison.OrdinalIgnoreCase)).ToList();
        }
        private static string DescribePath(string value)
        {
            var separator = value.IndexOf("::", StringComparison.Ordinal); return separator > 0 ? "Path (" + value.Substring(0, separator) + ")" : "Path";
        }
    }
}
