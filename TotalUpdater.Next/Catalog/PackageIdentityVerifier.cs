using System;
using System.IO;
using System.IO.Compression;
using System.Linq;

namespace TotalUpdater.Next.Catalog
{
    public static class PackageIdentityVerifier
    {
        public static bool ContainsCatalogAlias(Stream zipStream, PluginCatalogEntry entry)
        {
            if (zipStream == null || entry == null || entry.Aliases == null || entry.Aliases.Count == 0) return false;
            using (var zip = new ZipArchive(zipStream, ZipArchiveMode.Read, true))
                return zip.Entries.Any(file => entry.Aliases.Any(alias =>
                    String.Equals(Path.GetFileName(file.FullName), alias, StringComparison.OrdinalIgnoreCase)));
        }

        public static bool ContainsCatalogAlias(string zipPath, PluginCatalogEntry entry)
        {
            using (var stream = File.OpenRead(zipPath)) return ContainsCatalogAlias(stream, entry);
        }
    }
}
