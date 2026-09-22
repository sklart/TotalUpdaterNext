using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using TotalUpdater.Next.Core;
using TotalUpdater.Next.Core.Versions;

namespace TotalUpdater.Next.TotalCommander
{
    public interface IVersionProbe { LocalVersion Probe(string filePath); }

    public sealed class LocalVersionResolver
    {
        private readonly IList<IVersionProbe> _probes;
        public LocalVersionResolver() { _probes = new List<IVersionProbe> { new FileVersionProbe(), new ProductVersionProbe(), new TextVersionProbe() }; }
        public LocalVersion Resolve(string path)
        {
            foreach (var probe in _probes)
            {
                var version = probe.Probe(path);
                if (version.ParsedValue.IsKnown) return version;
            }
            return LocalVersion.Unknown;
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
