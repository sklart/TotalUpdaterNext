using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization.Json;

namespace TotalUpdater.Next.Catalog
{
    public sealed class CatalogService
    {
        private const string EmbeddedResourceName = "TotalUpdater.Next.Catalog.plugin-catalog.json";
        private readonly string _userCatalogPath;

        public CatalogService(string userCatalogPath)
        {
            _userCatalogPath = userCatalogPath;
        }

        public IList<PluginCatalogEntry> Load()
        {
            var entries = ReadEmbedded().Concat(ReadFile(_userCatalogPath))
                .GroupBy(x => x.Id, StringComparer.OrdinalIgnoreCase)
                .Select(x => x.Last()).ToList();
            return entries;
        }

        public PluginCatalogEntry FindByAlias(string fileName)
        {
            var entries = Load();
            var entry = entries.FirstOrDefault(x => x.Aliases.Any(alias => alias.Equals(fileName, StringComparison.OrdinalIgnoreCase)));
            return entry ?? entries.FirstOrDefault(x => x.Aliases.Any(alias => alias.Equals(NormalizeCompanionAlias(fileName), StringComparison.OrdinalIgnoreCase)));
        }

        private static string NormalizeCompanionAlias(string fileName)
        {
            var extension = Path.GetExtension(fileName);
            if (extension.Length < 4 || (!extension.StartsWith(".w", StringComparison.OrdinalIgnoreCase) && !extension.StartsWith(".uw", StringComparison.OrdinalIgnoreCase))) return fileName;
            if (extension.StartsWith(".uw", StringComparison.OrdinalIgnoreCase)) return Path.GetFileNameWithoutExtension(fileName) + ".w" + extension.Substring(3);
            if (extension.EndsWith("64", StringComparison.OrdinalIgnoreCase)) return Path.GetFileNameWithoutExtension(fileName) + extension.Substring(0, extension.Length - 2);
            return fileName;
        }

        public void EnsureUserCatalog()
        {
            if (File.Exists(_userCatalogPath)) return;
            Directory.CreateDirectory(Path.GetDirectoryName(_userCatalogPath));
            File.WriteAllText(_userCatalogPath, "[]");
        }

        public IList<PluginCatalogEntry> LoadUserCatalog()
        {
            return ReadFile(_userCatalogPath);
        }

        private static IList<PluginCatalogEntry> ReadEmbedded()
        {
            var assembly = Assembly.GetExecutingAssembly();
            using (var stream = assembly.GetManifestResourceStream(EmbeddedResourceName))
            {
                if (stream == null) throw new InvalidOperationException("Встроенный каталог не найден.");
                return Read(stream);
            }
        }

        private static IList<PluginCatalogEntry> ReadFile(string path)
        {
            if (!File.Exists(path)) return new List<PluginCatalogEntry>();
            using (var stream = File.OpenRead(path)) return Read(stream);
        }

        private static IList<PluginCatalogEntry> Read(Stream stream)
        {
            var serializer = new DataContractJsonSerializer(typeof(List<PluginCatalogEntry>));
            return (serializer.ReadObject(stream) as List<PluginCatalogEntry>) ?? new List<PluginCatalogEntry>();
        }
    }
}
