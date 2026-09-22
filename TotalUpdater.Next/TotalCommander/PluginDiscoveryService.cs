using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TotalUpdater.Next.Catalog;
using TotalUpdater.Next.Core;
using TotalUpdater.Next.Core.Versions;

namespace TotalUpdater.Next.TotalCommander
{
    public sealed class PluginDiscoveryService
    {
        private static readonly IDictionary<string, PluginType> Sections = new Dictionary<string, PluginType>(StringComparer.OrdinalIgnoreCase)
        {
            { "PackerPlugins", PluginType.Wcx }, { "ListerPlugins", PluginType.Wlx }, { "FileSystemPlugins", PluginType.Wfx }, { "ContentPlugins", PluginType.Wdx }
        };
        private static readonly IDictionary<string, PluginType> ArchitectureMarkerSections = new Dictionary<string, PluginType>(StringComparer.OrdinalIgnoreCase)
        {
            { "PackerPlugins64", PluginType.Wcx }, { "ListerPlugins64", PluginType.Wlx }, { "FileSystemPlugins64", PluginType.Wfx }, { "ContentPlugins64", PluginType.Wdx }
        };
        private readonly TotalCommanderConfigurationResolver _configurationResolver;
        private readonly LocalVersionResolver _versions;
        private readonly CatalogService _catalog;
        private readonly RedirectedSectionResolver _sections;

        public PluginDiscoveryService(TotalCommanderConfigurationResolver configurationResolver, LocalVersionResolver versions, CatalogService catalog, RedirectedSectionResolver sections = null)
        {
            _configurationResolver = configurationResolver; _versions = versions; _catalog = catalog; _sections = sections ?? new RedirectedSectionResolver(new IniDocumentReader(), configurationResolver);
        }

        public IList<InstalledPlugin> Discover(TotalCommanderConfiguration configuration)
        {
            var result = new List<InstalledPlugin>();
            AddTotalCommander(configuration, result);
            var architectureMarkers = ReadArchitectureMarkers(configuration);
            var families = new Dictionary<string, PluginFamily>(StringComparer.OrdinalIgnoreCase);
            foreach (var pair in Sections)
            {
                foreach (var item in _sections.GetEntries(configuration, pair.Key))
                {
                    var key = item.Key;
                    var pathValue = GetPluginPath(pair.Value, key, item.Value);
                    if (String.IsNullOrWhiteSpace(pathValue)) continue;
                    var path = _configurationResolver.ExpandPath(pathValue, configuration);
                    var familyPath = NormalizeFamilyPath(pair.Value, path);
                    var entry = _catalog.FindByAlias(Path.GetFileName(familyPath));
                    var fallbackName = pair.Value == PluginType.Wfx ? key : Path.GetFileNameWithoutExtension(familyPath);
                    var identity = entry == null
                        ? new PluginIdentity { Id = "family:" + pair.Value + "|" + familyPath.ToLowerInvariant(), Name = fallbackName, Type = pair.Value }
                        : new PluginIdentity { Id = entry.Id, Name = entry.Name, Type = entry.PluginType };
                    var familyKey = entry == null ? identity.Id : "catalog:" + identity.Id;
                    PluginFamily family;
                    if (!families.TryGetValue(familyKey, out family))
                    {
                        family = new PluginFamily { Type = pair.Value, Identity = identity, FamilyPath = familyPath };
                        families.Add(familyKey, family);
                    }
                    if (!family.ConfigurationKeys.Contains(key, StringComparer.OrdinalIgnoreCase)) family.ConfigurationKeys.Add(key);
                    family.MarkerX64 |= architectureMarkers.Contains(MarkerKey(pair.Value, key));
                }
            }
            foreach (var family in families.Values) result.Add(CreateInstalledPlugin(family, configuration));
            return result.OrderBy(x => x.Type).ThenBy(x => x.DisplayName, StringComparer.CurrentCultureIgnoreCase).ToList();
        }

        private ISet<string> ReadArchitectureMarkers(TotalCommanderConfiguration configuration)
        {
            var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var section in ArchitectureMarkerSections)
            {
                foreach (var entry in _sections.GetEntries(configuration, section.Key))
                {
                    if (IsSpecialKey(entry.Key)) continue;
                    if (String.Equals(entry.Value.Trim(), "1", StringComparison.OrdinalIgnoreCase)) result.Add(MarkerKey(section.Value, entry.Key));
                }
            }
            return result;
        }

        private InstalledPlugin CreateInstalledPlugin(PluginFamily family, TotalCommanderConfiguration configuration)
        {
            var binaries = GetBinaryCandidates(family.Type, family.FamilyPath)
                .Select(x => new PluginBinary
                {
                    Path = x.Path, Architecture = x.Architecture, Variant = x.Variant, Exists = File.Exists(x.Path), LocalVersion = LocalVersion.Unknown
                }).ToList();
            foreach (var binary in binaries.Where(x => x.Exists)) binary.LocalVersion = _versions.Resolve(binary.Path, family.Identity);
            var actualBinaries = binaries.Where(x => x.Exists).ToList();
            var architectures = actualBinaries.Aggregate(PluginArchitecture.Unknown, (current, binary) => current | binary.Architecture);
            if (family.MarkerX64 && !actualBinaries.Any(x => x.Architecture == PluginArchitecture.X64))
                configuration.Warnings.Add("Для " + family.DisplayName + " отмечен 64-битный вариант, но файл не найден: " + family.FamilyPath + "64");
            var localVersion = DetermineFamilyVersion(actualBinaries, out var hasVersionConflict);
            var primary = actualBinaries.FirstOrDefault() ?? binaries.FirstOrDefault();
            return new InstalledPlugin
            {
                Identity = family.Identity, Type = family.Type, DisplayName = family.Identity.Name,
                PrimaryPath = primary == null ? family.FamilyPath : primary.Path,
                Binaries = binaries, RelatedFiles = actualBinaries.Select(x => x.Path).ToList(),
                ConfigurationKeys = family.ConfigurationKeys, FileExists = actualBinaries.Any(), Architecture = architectures,
                LocalVersion = localVersion, HasVersionConflict = hasVersionConflict
            };
        }

        private static LocalVersion DetermineFamilyVersion(IEnumerable<PluginBinary> binaries, out bool hasConflict)
        {
            var known = binaries.Select(x => x.LocalVersion).Where(x => x.ParsedValue.IsKnown).ToList();
            hasConflict = known.Count > 1 && known.Skip(1).Any(x => x.ParsedValue.CompareTo(known[0].ParsedValue) != VersionComparison.Equal);
            if (hasConflict || known.Count == 0) return LocalVersion.Unknown;
            return known[0];
        }

        private static string GetPluginPath(PluginType type, string key, string value)
        {
            if (IsSpecialKey(key)) return null;
            if (type == PluginType.Wcx)
            {
                var separator = value.IndexOf(',');
                return separator < 0 ? value : value.Substring(separator + 1).Trim();
            }
            if (type == PluginType.Wfx) return value;
            int ignored;
            return Int32.TryParse(key, out ignored) ? value : null;
        }

        private static bool IsSpecialKey(string key)
        {
            return String.Equals(key, "$checksum$", StringComparison.OrdinalIgnoreCase) || String.Equals(key, "RedirectSection", StringComparison.OrdinalIgnoreCase);
        }

        private static string MarkerKey(PluginType type, string key) { return type + "|" + key; }

        private static string NormalizeFamilyPath(PluginType type, string path)
        {
            var code = GetPluginCode(type);
            var extension = Path.GetExtension(path);
            if (String.IsNullOrEmpty(code)) return path;
            if (extension.Equals(".w" + code, StringComparison.OrdinalIgnoreCase) || extension.Equals(".uw" + code, StringComparison.OrdinalIgnoreCase) || extension.Equals(".w" + code + "64", StringComparison.OrdinalIgnoreCase))
                return Path.Combine(Path.GetDirectoryName(path) ?? String.Empty, Path.GetFileNameWithoutExtension(path) + ".w" + code);
            return path;
        }

        private static IEnumerable<BinaryCandidate> GetBinaryCandidates(PluginType type, string familyPath)
        {
            var code = GetPluginCode(type);
            if (String.IsNullOrEmpty(code) || !Path.GetExtension(familyPath).Equals(".w" + code, StringComparison.OrdinalIgnoreCase))
            {
                yield return new BinaryCandidate { Path = familyPath, Architecture = PluginArchitecture.Unknown, Variant = PluginBinaryVariant.Other };
                yield break;
            }
            var stem = Path.Combine(Path.GetDirectoryName(familyPath) ?? String.Empty, Path.GetFileNameWithoutExtension(familyPath));
            yield return new BinaryCandidate { Path = stem + ".w" + code, Architecture = PluginArchitecture.X86, Variant = PluginBinaryVariant.Ansi };
            yield return new BinaryCandidate { Path = stem + ".uw" + code, Architecture = PluginArchitecture.X86, Variant = PluginBinaryVariant.Unicode };
            yield return new BinaryCandidate { Path = stem + ".w" + code + "64", Architecture = PluginArchitecture.X64, Variant = PluginBinaryVariant.Native64 };
        }

        private static string GetPluginCode(PluginType type)
        {
            switch (type)
            {
                case PluginType.Wcx: return "cx";
                case PluginType.Wlx: return "lx";
                case PluginType.Wfx: return "fx";
                case PluginType.Wdx: return "dx";
                default: return null;
            }
        }

        private void AddTotalCommander(TotalCommanderConfiguration configuration, ICollection<InstalledPlugin> result)
        {
            var entry = _catalog.FindByAlias("TOTALCMD.EXE");
            var identity = new PluginIdentity { Id = entry == null ? "totalcmd" : entry.Id, Name = entry == null ? "Total Commander" : entry.Name, Type = PluginType.TotalCommander };
            var binaries = new[]
            {
                new PluginBinary { Path = Path.Combine(configuration.InstallDirectory, "TOTALCMD.EXE"), Architecture = PluginArchitecture.X86, Variant = PluginBinaryVariant.Ansi },
                new PluginBinary { Path = Path.Combine(configuration.InstallDirectory, "TOTALCMD64.EXE"), Architecture = PluginArchitecture.X64, Variant = PluginBinaryVariant.Native64 }
            };
            foreach (var binary in binaries) { binary.Exists = File.Exists(binary.Path); binary.LocalVersion = binary.Exists ? _versions.Resolve(binary.Path, identity) : LocalVersion.Unknown; }
            var existing = binaries.Where(x => x.Exists).ToList();
            if (existing.Count == 0) return;
            var version = DetermineFamilyVersion(existing, out var conflict);
            result.Add(new InstalledPlugin
            {
                Identity = identity, Type = PluginType.TotalCommander, DisplayName = identity.Name,
                PrimaryPath = existing[0].Path, Binaries = binaries.ToList(), RelatedFiles = existing.Select(x => x.Path).ToList(),
                FileExists = true, Architecture = existing.Aggregate(PluginArchitecture.Unknown, (current, binary) => current | binary.Architecture),
                LocalVersion = version, HasVersionConflict = conflict
            });
        }

        private sealed class PluginFamily
        {
            public PluginType Type { get; set; }
            public PluginIdentity Identity { get; set; }
            public string FamilyPath { get; set; }
            public bool MarkerX64 { get; set; }
            public List<string> ConfigurationKeys { get; } = new List<string>();
            public string DisplayName { get { return Identity == null ? FamilyPath : Identity.Name; } }
        }

        private sealed class BinaryCandidate
        {
            public string Path { get; set; }
            public PluginArchitecture Architecture { get; set; }
            public PluginBinaryVariant Variant { get; set; }
        }
    }
}
