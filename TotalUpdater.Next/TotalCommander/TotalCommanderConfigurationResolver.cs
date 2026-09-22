using System;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace TotalUpdater.Next.TotalCommander
{
    public sealed class TotalCommanderConfigurationResolver
    {
        public TotalCommanderConfiguration Resolve(string explicitIniPath)
        {
            var iniPath = String.IsNullOrWhiteSpace(explicitIniPath) ? FindIniPath() : explicitIniPath;
            if (String.IsNullOrWhiteSpace(iniPath) || !File.Exists(iniPath)) return null;
            var iniDirectory = Path.GetDirectoryName(Path.GetFullPath(iniPath));
            var installDirectory = ReadInstallDirectory(iniPath) ?? iniDirectory;
            return new TotalCommanderConfiguration { IniPath = iniPath, InstallDirectory = installDirectory };
        }

        public string ExpandPath(string value, TotalCommanderConfiguration configuration)
        {
            var expanded = Regex.Replace(value, "%COMMANDER_PATH(?:64)?%", configuration.InstallDirectory.TrimEnd('\\'), RegexOptions.IgnoreCase);
            expanded = Environment.ExpandEnvironmentVariables(expanded);
            return Path.IsPathRooted(expanded) ? expanded : Path.GetFullPath(Path.Combine(configuration.InstallDirectory, expanded));
        }

        private static string ReadInstallDirectory(string iniPath)
        {
            var section = "";
            foreach (var raw in File.ReadLines(iniPath, Encoding.Default))
            {
                var line = raw.Trim();
                if (line.StartsWith("[") && line.EndsWith("]")) { section = line.Substring(1, line.Length - 2); continue; }
                if (!section.Equals("Configuration", StringComparison.OrdinalIgnoreCase)) continue;
                var equals = line.IndexOf('=');
                if (equals <= 0 || !line.Substring(0, equals).Trim().Equals("InstallDir", StringComparison.OrdinalIgnoreCase)) continue;
                var candidate = Environment.ExpandEnvironmentVariables(line.Substring(equals + 1).Trim());
                if (Directory.Exists(candidate)) return candidate;
            }
            return null;
        }

        private static string FindIniPath()
        {
            foreach (var hive in new[] { RegistryHive.CurrentUser, RegistryHive.LocalMachine })
            foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
            {
                try
                {
                    using (var baseKey = RegistryKey.OpenBaseKey(hive, view))
                    using (var key = baseKey.OpenSubKey(@"Software\Ghisler\Total Commander"))
                    {
                        var path = key == null ? null : key.GetValue("IniFileName") as string;
                        if (!String.IsNullOrWhiteSpace(path) && File.Exists(path)) return path;
                    }
                }
                catch { }
            }
            return null;
        }
    }
}
