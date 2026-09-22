using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using TotalUpdater.Next.Catalog;
using TotalUpdater.Next.Core;

namespace TotalUpdater.Next.TotalCommander
{
    public sealed class PluginDiscoveryService
    {
        private static readonly IDictionary<string, PluginType> Sections = new Dictionary<string, PluginType>(StringComparer.OrdinalIgnoreCase)
        {
            { "PackerPlugins", PluginType.Wcx }, { "ListerPlugins", PluginType.Wlx }, { "FileSystemPlugins", PluginType.Wfx }, { "ContentPlugins", PluginType.Wdx }
        };
        private readonly TotalCommanderConfigurationResolver _configurationResolver;
        private readonly LocalVersionResolver _versions;
        private readonly CatalogService _catalog;

        public PluginDiscoveryService(TotalCommanderConfigurationResolver configurationResolver, LocalVersionResolver versions, CatalogService catalog)
        {
            _configurationResolver = configurationResolver; _versions = versions; _catalog = catalog;
        }

        public IList<InstalledPlugin> Discover(TotalCommanderConfiguration configuration)
        {
            var result = new List<InstalledPlugin>();
            AddTotalCommander(configuration, result);
            var section = "";
            foreach (var raw in File.ReadLines(configuration.IniPath, Encoding.Default))
            {
                var line = raw.Trim();
                if (line.Length == 0 || line.StartsWith(";")) continue;
                if (line.StartsWith("[") && line.EndsWith("]")) { section = line.Substring(1, line.Length - 2); continue; }
                PluginType type;
                if (!Sections.TryGetValue(section, out type)) continue;
                var equals = line.IndexOf('=');
                if (equals <= 0) continue;
                var key = line.Substring(0, equals).Trim();
                var pathValue = GetPluginPath(type, key, line.Substring(equals + 1).Trim());
                if (String.IsNullOrWhiteSpace(pathValue)) continue;
                var path = _configurationResolver.ExpandPath(pathValue, configuration);
                var entry = _catalog.FindByAlias(Path.GetFileName(path));
                var fallbackName = type == PluginType.Wfx ? key : Path.GetFileNameWithoutExtension(path);
                var identity = entry == null ? new PluginIdentity { Id = "file:" + Path.GetFileName(path).ToLowerInvariant(), Name = fallbackName, Type = type } :
                    new PluginIdentity { Id = entry.Id, Name = entry.Name, Type = entry.PluginType };
                var exists = File.Exists(path);
                result.Add(new InstalledPlugin
                {
                    Identity = identity, Type = type, DisplayName = identity.Name, PrimaryPath = path, FileExists = exists,
                    Architecture = DetectArchitecture(path), LocalVersion = exists ? _versions.Resolve(path, identity) : LocalVersion.Unknown
                });
            }
            return MergeDuplicates(result);
        }

        private static string GetPluginPath(PluginType type, string key, string value)
        {
            if (type == PluginType.Wcx)
            {
                var separator = value.IndexOf(',');
                return separator < 0 ? value : value.Substring(separator + 1).Trim();
            }
            if (type == PluginType.Wfx) return value;
            int ignored;
            return Int32.TryParse(key, out ignored) ? value : null;
        }

        private void AddTotalCommander(TotalCommanderConfiguration configuration, ICollection<InstalledPlugin> result)
        {
            foreach (var name in new[] { "TOTALCMD64.EXE", "TOTALCMD.EXE" })
            {
                var path = Path.Combine(configuration.InstallDirectory, name);
                if (!File.Exists(path)) continue;
                var entry = _catalog.FindByAlias(name);
                result.Add(new InstalledPlugin
                {
                    Identity = new PluginIdentity { Id = entry == null ? "totalcmd" : entry.Id, Name = entry == null ? "Total Commander" : entry.Name, Type = PluginType.TotalCommander },
                    Type = PluginType.TotalCommander, DisplayName = name.Equals("TOTALCMD64.EXE", StringComparison.OrdinalIgnoreCase) ? "Total Commander (x64)" : "Total Commander",
                    PrimaryPath = path, FileExists = true, Architecture = name.Contains("64") ? PluginArchitecture.X64 : PluginArchitecture.X86, LocalVersion = _versions.Resolve(path, new PluginIdentity { Id = entry == null ? "totalcmd" : entry.Id, Name = "Total Commander", Type = PluginType.TotalCommander })
                });
            }
        }

        private static PluginArchitecture DetectArchitecture(string path)
        {
            if (path.IndexOf("64", StringComparison.OrdinalIgnoreCase) >= 0) return PluginArchitecture.X64;
            return PluginArchitecture.Unknown;
        }

        private static IList<InstalledPlugin> MergeDuplicates(IEnumerable<InstalledPlugin> plugins)
        {
            return plugins.GroupBy(x => x.Type + "|" + x.PrimaryPath, StringComparer.OrdinalIgnoreCase).Select(x => x.First())
                .OrderBy(x => x.Type).ThenBy(x => x.DisplayName, StringComparer.CurrentCultureIgnoreCase).ToList();
        }
    }
}
