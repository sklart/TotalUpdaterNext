using System;
using System.IO;
using System.Text;

namespace TotalUpdater.Next.Services
{
    public static class TotalCommanderLocator
    {
        public static string GetInstallDirectory(string iniPath)
        {
            var iniDirectory = Path.GetDirectoryName(Path.GetFullPath(iniPath));
            var section = "";
            foreach (var raw in File.ReadLines(iniPath, Encoding.Default))
            {
                var line = raw.Trim();
                if (line.StartsWith("[") && line.EndsWith("]"))
                {
                    section = line.Substring(1, line.Length - 2);
                    continue;
                }
                if (!section.Equals("Configuration", StringComparison.OrdinalIgnoreCase)) continue;
                var separator = line.IndexOf('=');
                if (separator < 1 || !line.Substring(0, separator).Trim().Equals("InstallDir", StringComparison.OrdinalIgnoreCase)) continue;
                var configured = Environment.ExpandEnvironmentVariables(line.Substring(separator + 1).Trim());
                if (Directory.Exists(configured)) return configured;
            }
            return iniDirectory;
        }
    }
}
