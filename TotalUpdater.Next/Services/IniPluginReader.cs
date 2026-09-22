using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using TotalUpdater.Next.Models;

namespace TotalUpdater.Next.Services
{
    public static class IniPluginReader
    {
        private static readonly Dictionary<string, string> Categories = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "PackerPlugins", "Архиваторные плагины (WCX)" },
            { "ListerPlugins", "Плагины просмотра (WLX)" },
            { "FileSystemPlugins", "Плагины файловой системы (WFX)" },
            { "ContentPlugins", "Контентные плагины (WDX)" },
        };

        public static IList<PluginRecord> Read(string iniPath)
        {
            var result = new List<PluginRecord>();
            var baseDir = TotalCommanderLocator.GetInstallDirectory(iniPath);
            var section = "";

            foreach (var raw in File.ReadLines(iniPath, Encoding.Default))
            {
                var line = raw.Trim();
                if (line.Length == 0 || line.StartsWith(";")) continue;
                if (line.StartsWith("[") && line.EndsWith("]"))
                {
                    section = line.Substring(1, line.Length - 2);
                    continue;
                }

                string category;
                if (!Categories.TryGetValue(section, out category)) continue;
                var divider = line.IndexOf('=');
                int index;
                if (divider <= 0 || !int.TryParse(line.Substring(0, divider), out index)) continue;
                var filePath = ResolvePath(line.Substring(divider + 1).Trim(), baseDir);
                var fileName = Path.GetFileName(filePath);
                if (String.IsNullOrWhiteSpace(fileName)) continue;

                result.Add(new PluginRecord
                {
                    Category = category,
                    Name = Path.GetFileNameWithoutExtension(fileName),
                    FilePath = filePath,
                    LocalVersion = ReadVersion(filePath),
                    Status = File.Exists(filePath) ? "Не проверено" : "Файл не найден"
                });
            }
            return result.OrderBy(x => x.Category).ThenBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
        }

        private static string ResolvePath(string value, string baseDir)
        {
            var commanderPath = baseDir.TrimEnd('\\') + "\\";
            value = Regex.Replace(value, "%COMMANDER_PATH64?%", commanderPath.Replace("$", "$$"), RegexOptions.IgnoreCase);
            value = Environment.ExpandEnvironmentVariables(value);
            return Path.IsPathRooted(value) ? value : Path.GetFullPath(Path.Combine(baseDir, value));
        }

        private static string ReadVersion(string path)
        {
            if (!File.Exists(path)) return "не найден";
            try
            {
                var version = FileVersionInfo.GetVersionInfo(path).FileVersion;
                if (!String.IsNullOrWhiteSpace(version) && !IsEmptyVersion(version)) return version.Trim();
            }
            catch { }
            var textVersion = ReadVersionFromText(Path.GetDirectoryName(path));
            return textVersion == null ? "не определена" : textVersion + " (текст)";
        }

        private static bool IsEmptyVersion(string version)
        {
            return Regex.IsMatch(version, "^0(?:\\.0){1,3}(?:\\s.*)?$");
        }

        private static string? ReadVersionFromText(string directory)
        {
            if (String.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory)) return null;
            var candidates = new List<string>();
            foreach (var pattern in new[] { "*version*", "*history*", "*change*", "readme*" })
            {
                try { candidates.AddRange(Directory.GetFiles(directory, pattern, SearchOption.TopDirectoryOnly)); }
                catch { }
            }

            foreach (var file in candidates.Distinct(StringComparer.OrdinalIgnoreCase).OrderByDescending(File.GetLastWriteTimeUtc))
            {
                try
                {
                    if (new FileInfo(file).Length > 2 * 1024 * 1024) continue;
                    var content = File.ReadAllText(file, Encoding.Default);
                    var match = Regex.Match(content, "(?:version|ver\\.|versione|versi[oó]n|v)\\s*[:=#-]?\\s*([0-9]+(?:\\.[0-9A-Za-z]+){1,3})", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
                    if (match.Success) return match.Groups[1].Value;
                }
                catch { }
            }
            return null;
        }
    }
}
