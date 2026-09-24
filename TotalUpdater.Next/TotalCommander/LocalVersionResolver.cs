using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using TotalUpdater.Next.Core;
using TotalUpdater.Next.Core.Versions;
using TotalUpdater.Next.Settings;

namespace TotalUpdater.Next.TotalCommander
{
    public interface IVersionProbe { LocalVersion Probe(string filePath); }

    public interface ILocalVersionStrategyRegistry { bool Contains(string name); IEnumerable<IPluginSpecificVersionStrategy> CreateStrategies(); }
    public sealed class LocalVersionStrategyRegistry : ILocalVersionStrategyRegistry
    {
        public static readonly LocalVersionStrategyRegistry Default = new LocalVersionStrategyRegistry();
        public bool Contains(string name) { return String.IsNullOrWhiteSpace(name) || String.Equals(name, "fileinfo", StringComparison.OrdinalIgnoreCase) || String.Equals(name, "total7zip", StringComparison.OrdinalIgnoreCase); }
        public IEnumerable<IPluginSpecificVersionStrategy> CreateStrategies() { return new IPluginSpecificVersionStrategy[] { new FileInfoVersionStrategy(), new Total7zipVersionStrategy() }; }
    }

    public sealed class LocalVersionResolver
    {
        private readonly IList<IPluginSpecificVersionStrategy> _strategies;
        private readonly IList<IVersionProbe> _genericProbes;
        private readonly AppSettings _settings;

        public LocalVersionResolver(AppSettings settings = null)
            : this(LocalVersionStrategyRegistry.Default.CreateStrategies(), new IVersionProbe[] { new FileVersionProbe(), new ProductVersionProbe(), new TextVersionProbe() }, settings) { }

        public LocalVersionResolver(IEnumerable<IPluginSpecificVersionStrategy> strategies, IEnumerable<IVersionProbe> genericProbes, AppSettings settings = null)
        {
            _strategies = (strategies ?? Enumerable.Empty<IPluginSpecificVersionStrategy>()).ToList();
            _genericProbes = (genericProbes ?? Enumerable.Empty<IVersionProbe>()).ToList(); _settings = settings;
        }

        public LocalVersion Resolve(string path) { return Resolve(path, new PluginIdentity()); }
        public LocalVersion Resolve(string path, PluginIdentity identity)
        {
            foreach (var strategy in _strategies)
            {
                if (identity == null || !strategy.Name.Equals(identity.LocalVersionStrategy, StringComparison.OrdinalIgnoreCase)) continue;
                var version = strategy.Probe(path);
                if (version.ParsedValue.IsKnown) return version;
            }
            foreach (var probe in _genericProbes)
            {
                if (_settings != null && !_settings.UseExtendedVersionDetection && probe is TextVersionProbe) continue;
                var version = probe.Probe(path);
                if (version.ParsedValue.IsKnown) return version;
            }
            return LocalVersion.Unknown;
        }
    }

    public interface IPluginSpecificVersionStrategy : IVersionProbe { string Name { get; } }

    public sealed class FileInfoVersionStrategy : IPluginSpecificVersionStrategy
    {
        public string Name { get { return "fileinfo"; } }
        public LocalVersion Probe(string path)
        {
            try
            {
                return FromPeFileVersion(FileVersionInfo.GetVersionInfo(path).FileVersion);
            }
            catch { return LocalVersion.Unknown; }
        }

        public static LocalVersion FromPeFileVersion(string peVersion)
        {
            var pe = VersionValue.Parse(peVersion);
            if (!pe.IsKnown || pe.Numbers.Count != 4) return LocalVersion.Unknown;
            var publicVersion = pe.Numbers[0].ToString() + "." + pe.Numbers[1].ToString() + pe.Numbers[2].ToString();
            var parsed = VersionValue.Parse(publicVersion);
            return new LocalVersion { RawValue = publicVersion, ParsedValue = parsed, Source = VersionSource.CustomRule, Confidence = VersionConfidence.Exact };
        }
    }

    public sealed class Total7zipVersionStrategy : IPluginSpecificVersionStrategy
    {
        public string Name { get { return "total7zip"; } }
        public LocalVersion Probe(string path)
        {
            try { return FromPeFileVersion(FileVersionInfo.GetVersionInfo(path).FileVersion); }
            catch { return LocalVersion.Unknown; }
        }
        public static LocalVersion FromPeFileVersion(string peVersion)
        {
            var pe = VersionValue.Parse(peVersion);
            if (!pe.IsKnown || pe.Numbers.Count != 4 || pe.Numbers[0] != 0 || pe.Numbers[2] > 9 || pe.Numbers[3] > 9)
                return LocalVersion.Unknown;
            var publicVersion = pe.Numbers[1].ToString() + "." + pe.Numbers[2] + pe.Numbers[3];
            return FileVersionProbe.Create(publicVersion, VersionSource.CustomRule, VersionConfidence.Exact);
        }
    }

    public sealed class FileVersionProbe : IVersionProbe
    {
        public LocalVersion Probe(string path)
        {
            try { return Create(FileVersionInfo.GetVersionInfo(path).FileVersion, VersionSource.FileVersion, VersionConfidence.Exact); }
            catch { return LocalVersion.Unknown; }
        }
        public static LocalVersion Create(string value, VersionSource source, VersionConfidence confidence)
        {
            var parsed = VersionValue.Parse(value);
            return parsed.IsKnown ? new LocalVersion { RawValue = value.Trim(), ParsedValue = parsed, Source = source, Confidence = confidence } : LocalVersion.Unknown;
        }
    }

    public sealed class ProductVersionProbe : IVersionProbe
    {
        public LocalVersion Probe(string path)
        {
            try { return FileVersionProbe.Create(FileVersionInfo.GetVersionInfo(path).ProductVersion, VersionSource.ProductVersion, VersionConfidence.Probable); }
            catch { return LocalVersion.Unknown; }
        }
    }

    public sealed class TextVersionProbe : IVersionProbe
    {
        private static readonly Regex Pattern = new Regex(@"(?:version|ver\.|versione|versi[oó]n|v)\s*[:=#-]?\s*([0-9]+(?:\.[0-9A-Za-z]+){1,3}(?:\s*(?:beta|b|rc)\s*\d*)?)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        public LocalVersion Probe(string path)
        {
            var directory = Path.GetDirectoryName(path);
            if (String.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory)) return LocalVersion.Unknown;
            var names = new[] { "version*.txt", "history*", "changelog*", "change*", "readme*" };
            foreach (var file in names.SelectMany(pattern => SafeFiles(directory, pattern).OrderByDescending(File.GetLastWriteTimeUtc)).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                try
                {
                    if (new FileInfo(file).Length > 2 * 1024 * 1024) continue;
                    var match = Pattern.Match(File.ReadAllText(file, Encoding.Default));
                    if (match.Success)
                    {
                        var parsed = VersionValue.Parse(match.Groups[1].Value);
                        if (parsed.IsKnown) return new LocalVersion { RawValue = match.Groups[1].Value, ParsedValue = parsed, Source = VersionSource.TextFile, Confidence = VersionConfidence.Heuristic };
                    }
                }
                catch { }
            }
            return LocalVersion.Unknown;
        }
        private static IEnumerable<string> SafeFiles(string directory, string pattern)
        {
            try { return Directory.GetFiles(directory, pattern, SearchOption.TopDirectoryOnly); }
            catch { return new string[0]; }
        }
    }
}
