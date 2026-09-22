using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace TotalUpdater.Next.TotalCommander
{
    public sealed class RegistryConfigurationEntry { public string IniFileName { get; set; } public string InstallDirectory { get; set; } public int UseIniInProgramDir { get; set; } }
    public interface IRegistryConfigurationReader { IEnumerable<RegistryConfigurationEntry> Read(); }
    public sealed class WindowsRegistryConfigurationReader : IRegistryConfigurationReader
    {
        public IEnumerable<RegistryConfigurationEntry> Read()
        {
            var result = new List<RegistryConfigurationEntry>();
            foreach (var hive in new[] { RegistryHive.CurrentUser, RegistryHive.LocalMachine }) foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
            {
                try { using (var baseKey = RegistryKey.OpenBaseKey(hive, view)) using (var key = baseKey.OpenSubKey(@"Software\Ghisler\Total Commander")) { if (key != null) result.Add(new RegistryConfigurationEntry { IniFileName = key.GetValue("IniFileName") as string, InstallDirectory = key.GetValue("InstallDir") as string, UseIniInProgramDir = ConvertToInt(key.GetValue("UseIniInProgramDir")) }); } } catch { }
            }
            return result;
        }
        private static int ConvertToInt(object value) { if (value is int) return (int)value; int result; return value != null && Int32.TryParse(value.ToString(), out result) ? result : 0; }
    }
    public interface IEnvironmentProvider { string Get(string name); string Expand(string value); string AppData { get; } string WindowsDirectory { get; } }
    public sealed class WindowsEnvironmentProvider : IEnvironmentProvider
    {
        public string Get(string name) { return Environment.GetEnvironmentVariable(name); }
        public string Expand(string value) { return Environment.ExpandEnvironmentVariables(value); }
        public string AppData { get { return Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData); } }
        public string WindowsDirectory { get { return Environment.GetFolderPath(Environment.SpecialFolder.Windows); } }
    }
    public sealed class TotalCommanderConfigurationResolver
    {
        private readonly IIniDocumentReader _reader; private readonly IRegistryConfigurationReader _registry; private readonly IEnvironmentProvider _environment;
        public TotalCommanderConfigurationResolver() : this(new IniDocumentReader(), new WindowsRegistryConfigurationReader(), new WindowsEnvironmentProvider()) { }
        public TotalCommanderConfigurationResolver(IIniDocumentReader reader, IRegistryConfigurationReader registry, IEnvironmentProvider environment) { _reader = reader; _registry = registry; _environment = environment; }
        public TotalCommanderConfiguration Resolve(string explicitIniPath)
        {
            string registryInstall = null;
            var path = String.IsNullOrWhiteSpace(explicitIniPath) ? FindIniPath(out registryInstall) : explicitIniPath;
            return String.IsNullOrWhiteSpace(path) || !File.Exists(path) ? null : CreateConfiguration(path, registryInstall);
        }
        public string ExpandPath(string value, TotalCommanderConfiguration configuration) { return ExpandPath(value, configuration, configuration == null ? null : Path.GetDirectoryName(configuration.IniPath)); }
        public string ExpandPath(string value, TotalCommanderConfiguration configuration, string relativeDirectory)
        {
            if (String.IsNullOrWhiteSpace(value)) return value;
            var install = configuration == null ? "" : configuration.InstallDirectory.TrimEnd('\\'); var ini = configuration == null ? "" : configuration.IniPath; var drive = String.IsNullOrWhiteSpace(install) ? "" : Path.GetPathRoot(install).TrimEnd('\\');
            var expanded = Regex.Replace(value, "%COMMANDER_PATH(?:64)?%", install, RegexOptions.IgnoreCase); expanded = Regex.Replace(expanded, "%COMMANDER_INI%", ini, RegexOptions.IgnoreCase); expanded = Regex.Replace(expanded, "%COMMANDER_DRIVE%", drive, RegexOptions.IgnoreCase); expanded = _environment.Expand(expanded);
            var baseDirectory = String.IsNullOrWhiteSpace(relativeDirectory) ? install : relativeDirectory;
            return Path.IsPathRooted(expanded) ? Path.GetFullPath(expanded) : Path.GetFullPath(Path.Combine(baseDirectory, expanded));
        }
        private TotalCommanderConfiguration CreateConfiguration(string path, string registryInstallDirectory)
        {
            var fullPath = Path.GetFullPath(path); var document = _reader.Read(fullPath); var iniDirectory = Path.GetDirectoryName(fullPath); var result = new TotalCommanderConfiguration { IniPath = fullPath, InstallDirectory = registryInstallDirectory ?? iniDirectory, Document = document };
            var section = document.GetSection("Configuration"); var install = section == null ? null : section.GetValue("InstallDir"); if (!String.IsNullOrWhiteSpace(install)) result.InstallDirectory = ExpandPath(install, result, iniDirectory); if (String.IsNullOrWhiteSpace(result.InstallDirectory)) result.InstallDirectory = iniDirectory;
            var alternate = section == null ? null : section.GetValue("AlternateUserIni"); result.AlternateUserIni = String.IsNullOrWhiteSpace(alternate) ? "" : ExpandPath(alternate, result, iniDirectory); return result;
        }
        private string FindIniPath(out string detectedInstallDirectory)
        {
            detectedInstallDirectory = null;
            var environmentIni = _environment.Get("COMMANDER_INI"); if (File.Exists(environmentIni)) return environmentIni;
            var commanderPath = _environment.Get("COMMANDER_PATH"); if (!String.IsNullOrWhiteSpace(commanderPath)) { var portable = Path.Combine(commanderPath, "wincmd.ini"); if (File.Exists(portable)) return portable; }
            foreach (var entry in _registry.Read())
            {
                var install = entry.InstallDirectory; if (!String.IsNullOrWhiteSpace(install) && !Path.IsPathRooted(install)) install = null;
                if (UsesIniInProgramDirectory(entry.UseIniInProgramDir) && !String.IsNullOrWhiteSpace(install)) { var portable = Path.Combine(install, "wincmd.ini"); if (File.Exists(portable)) { detectedInstallDirectory = install; return portable; } }
                if (!String.IsNullOrWhiteSpace(entry.IniFileName)) { var candidate = Path.IsPathRooted(entry.IniFileName) ? entry.IniFileName : Path.Combine(install ?? "", entry.IniFileName); if (File.Exists(candidate)) { detectedInstallDirectory = install; return candidate; } }
            }
            var appData = Path.Combine(_environment.AppData, "Ghisler", "wincmd.ini"); if (File.Exists(appData)) return appData; var windows = Path.Combine(_environment.WindowsDirectory, "wincmd.ini"); return File.Exists(windows) ? windows : null;
        }
        private static bool UsesIniInProgramDirectory(int value) { return (value & 1) != 0 || (value & 4) != 0; }
    }
}
