using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace TotalUpdater.Next.TotalCommander
{
    public sealed class RegistryConfigurationEntry { public string IniFileName { get; set; } public string InstallDirectory { get; set; } }
    public interface IRegistryConfigurationReader { IEnumerable<RegistryConfigurationEntry> Read(); }
    public sealed class WindowsRegistryConfigurationReader : IRegistryConfigurationReader
    {
        public IEnumerable<RegistryConfigurationEntry> Read()
        {
            var result = new List<RegistryConfigurationEntry>();
            foreach (var hive in new[] { RegistryHive.CurrentUser, RegistryHive.LocalMachine }) foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
            {
                try { using (var baseKey = RegistryKey.OpenBaseKey(hive, view)) using (var key = baseKey.OpenSubKey(@"Software\Ghisler\Total Commander")) { if (key != null) result.Add(new RegistryConfigurationEntry { IniFileName = key.GetValue("IniFileName") as string, InstallDirectory = key.GetValue("InstallDir") as string }); } } catch { }
            }
            return result;
        }
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
            var install = DetectInstallDirectory();
            var path = DetectIniPath(explicitIniPath, install);
            return String.IsNullOrWhiteSpace(path) || !File.Exists(path) ? null : CreateConfiguration(path, install);
        }
        public string DetectInstallDirectory()
        {
            var commanderPath = _environment.Expand(_environment.Get("COMMANDER_PATH") ?? "");
            if (!String.IsNullOrWhiteSpace(commanderPath) && Directory.Exists(commanderPath)) return Path.GetFullPath(commanderPath);
            foreach (var entry in _registry.Read())
            {
                var candidate = ExpandRegistryPath(entry.InstallDirectory, null);
                if (!String.IsNullOrWhiteSpace(candidate) && Directory.Exists(candidate)) return candidate;
            }
            return AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\');
        }
        public string DetectIniPath(string explicitIniPath) { return DetectIniPath(explicitIniPath, DetectInstallDirectory()); }
        public string DetectIniPath(string explicitIniPath, string installDirectory)
        {
            if (!String.IsNullOrWhiteSpace(explicitIniPath) && File.Exists(explicitIniPath)) return Path.GetFullPath(explicitIniPath);
            var environmentIni = _environment.Expand(_environment.Get("COMMANDER_INI") ?? ""); if (File.Exists(environmentIni)) return Path.GetFullPath(environmentIni);
            var localIni = String.IsNullOrWhiteSpace(installDirectory) ? null : Path.Combine(installDirectory, "wincmd.ini");
            var localUse = ReadUseIniInProgramDir(localIni);
            var registryIni = FindRegistryIni(installDirectory);
            if ((localUse & 4) != 0 && File.Exists(localIni)) return localIni;
            if (!String.IsNullOrWhiteSpace(registryIni)) return registryIni;
            if ((localUse & 1) != 0 && File.Exists(localIni)) return localIni;
            var appData = Path.Combine(_environment.AppData, "Ghisler", "wincmd.ini"); if (File.Exists(appData)) return appData;
            var windows = Path.Combine(_environment.WindowsDirectory, "wincmd.ini"); return File.Exists(windows) ? windows : null;
        }
        public string ExpandPath(string value, TotalCommanderConfiguration configuration) { return ExpandPath(value, configuration, configuration == null ? null : configuration.InstallDirectory); }
        public string ExpandPath(string value, TotalCommanderConfiguration configuration, string relativeDirectory)
        {
            if (String.IsNullOrWhiteSpace(value)) return value;
            var expanded = ExpandMacros(value, configuration);
            var baseDirectory = String.IsNullOrWhiteSpace(relativeDirectory) ? (configuration == null ? AppDomain.CurrentDomain.BaseDirectory : configuration.InstallDirectory) : relativeDirectory;
            return Path.IsPathRooted(expanded) ? Path.GetFullPath(expanded) : Path.GetFullPath(Path.Combine(baseDirectory, expanded));
        }
        public string ExpandRedirectPath(string value, TotalCommanderConfiguration configuration)
        {
            if (String.IsNullOrWhiteSpace(value)) return null;
            var expanded = ExpandMacros(value, configuration);
            if (Path.IsPathRooted(expanded)) return Path.GetFullPath(expanded);
            return String.Equals(Path.GetFileName(expanded), expanded, StringComparison.Ordinal) ? Path.Combine(Path.GetDirectoryName(configuration.IniPath), expanded) : null;
        }
        private TotalCommanderConfiguration CreateConfiguration(string path, string registryInstallDirectory)
        {
            var fullPath = Path.GetFullPath(path); var document = _reader.Read(fullPath); var iniDirectory = Path.GetDirectoryName(fullPath); var result = new TotalCommanderConfiguration { IniPath = fullPath, InstallDirectory = registryInstallDirectory, Document = document };
            var section = document.GetSection("Configuration"); var install = section == null ? null : section.GetValue("InstallDir"); if (!String.IsNullOrWhiteSpace(install)) result.InstallDirectory = ExpandPath(install, result, result.InstallDirectory); if (String.IsNullOrWhiteSpace(result.InstallDirectory)) result.InstallDirectory = AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\');
            var alternate = section == null ? null : section.GetValue("AlternateUserIni"); result.AlternateUserIni = String.IsNullOrWhiteSpace(alternate) ? "" : ExpandPath(alternate, result, iniDirectory); return result;
        }
        private string ExpandMacros(string value, TotalCommanderConfiguration configuration)
        {
            var install = configuration == null ? "" : configuration.InstallDirectory.TrimEnd('\\'); var ini = configuration == null ? "" : configuration.IniPath; var drive = String.IsNullOrWhiteSpace(install) ? "" : Path.GetPathRoot(install).TrimEnd('\\');
            var expanded = Regex.Replace(value, "%COMMANDER_PATH(?:64)?%", install, RegexOptions.IgnoreCase); expanded = Regex.Replace(expanded, "%COMMANDER_INI%", ini, RegexOptions.IgnoreCase); expanded = Regex.Replace(expanded, "%COMMANDER_DRIVE%", drive, RegexOptions.IgnoreCase); return _environment.Expand(expanded);
        }
        private string FindRegistryIni(string installDirectory)
        {
            foreach (var entry in _registry.Read()) { var candidate = ExpandRegistryPath(entry.IniFileName, installDirectory); if (!String.IsNullOrWhiteSpace(candidate) && File.Exists(candidate)) return candidate; }
            return null;
        }
        private string ExpandRegistryPath(string value, string installDirectory)
        {
            if (String.IsNullOrWhiteSpace(value)) return null;
            var expanded = _environment.Expand(value);
            if (Path.IsPathRooted(expanded)) return Path.GetFullPath(expanded);
            return String.IsNullOrWhiteSpace(installDirectory) ? null : Path.GetFullPath(Path.Combine(installDirectory, expanded));
        }
        private int ReadUseIniInProgramDir(string localIni)
        {
            if (String.IsNullOrWhiteSpace(localIni) || !File.Exists(localIni)) return 0;
            try { var value = _reader.Read(localIni).GetSection("Configuration").GetValue("UseIniInProgramDir"); int result; return Int32.TryParse(value, out result) ? result : 0; } catch { return 0; }
        }
    }
}
