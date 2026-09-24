using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using TotalUpdater.Next.Core;
using TotalUpdater.Next.Core.Versions;
using TotalUpdater.Next.Infrastructure.Installation;

namespace TotalUpdater.Next.Settings
{
    public enum ProxyMode { System, Direct, Custom }
    public enum DiscoveryMode { RegisteredOnly, RegisteredAndDirectories }
    public enum PostDownloadAction { Nothing, OfferInstall }

    public sealed class AppSettings
    {
        public bool IncludePrerelease { get; set; }
        public bool CheckTotalCommander { get; set; } = true;
        public bool CheckPlugins { get; set; } = true;
        public bool UseExtendedVersionDetection { get; set; } = true;
        public bool HideUnknownVersion { get; set; }
        public DiscoveryMode DiscoveryMode { get; set; } = DiscoveryMode.RegisteredOnly;
        public bool ShowUnregisteredPlugins { get; set; } = true;
        public string DownloadDirectory { get; set; } = "";
        public PostDownloadAction PostDownloadAction { get; set; } = PostDownloadAction.Nothing;
        public bool DeleteZipAfterInstall { get; set; }
        public ProxyMode ProxyMode { get; set; } = ProxyMode.System;
        public string ProxyAddress { get; set; } = "";
        public int ProxyPort { get; set; }
        public string ProxyUsername { get; set; } = "";
        public int FirstRequestTimeoutSeconds { get; set; } = 20;
        public int RetryTimeoutSeconds { get; set; } = 30;
        public bool UsePersistentMetadataCache { get; set; } = true;
        public int CacheMaxAgeDays { get; set; } = 180;
        public string LastFilter { get; set; } = "All";
        public double WindowLeft { get; set; } = Double.NaN;
        public double WindowTop { get; set; } = Double.NaN;
        public double WindowWidth { get; set; } = 980;
        public double WindowHeight { get; set; } = 680;
        public IList<string> ExcludedCatalogIds { get; set; } = new List<string>();
        public IList<string> ExcludedUnknownPaths { get; set; } = new List<string>();
        public IList<AcceptedVersionOverride> AcceptedVersions { get; set; } = new List<AcceptedVersionOverride>();
    }
    public sealed class AcceptedVersionOverride { public string PluginId { get; set; } = ""; public string LocalSha256 { get; set; } = ""; public string AcceptedRemoteVersion { get; set; } = ""; public DateTime AcceptedUtc { get; set; } }
    public sealed class AcceptedVersionService
    {
        private readonly AppSettings _settings;
        public AcceptedVersionService(AppSettings settings) { _settings = settings; }
        public void Accept(InstalledPlugin plugin, VersionValue remote)
        {
            if (plugin == null || !remote.IsKnown || String.IsNullOrWhiteSpace(plugin.PrimaryPath) || !File.Exists(plugin.PrimaryPath)) return;
            var id = plugin.Identity == null ? "" : plugin.Identity.Id; _settings.AcceptedVersions = _settings.AcceptedVersions.Where(x => !x.PluginId.Equals(id, StringComparison.OrdinalIgnoreCase)).ToList();
            _settings.AcceptedVersions.Add(new AcceptedVersionOverride { PluginId = id, LocalSha256 = PackageInspector.Hash(plugin.PrimaryPath), AcceptedRemoteVersion = remote.Raw, AcceptedUtc = DateTime.UtcNow });
        }
        public bool IsAccepted(InstalledPlugin plugin, VersionValue remote)
        {
            if (plugin == null || !remote.IsKnown || !File.Exists(plugin.PrimaryPath)) return false; var id = plugin.Identity == null ? "" : plugin.Identity.Id;
            var record = (_settings.AcceptedVersions ?? new List<AcceptedVersionOverride>()).FirstOrDefault(x => x.PluginId.Equals(id, StringComparison.OrdinalIgnoreCase)); if (record == null || !String.Equals(record.LocalSha256, PackageInspector.Hash(plugin.PrimaryPath), StringComparison.OrdinalIgnoreCase)) return false;
            return remote.CompareTo(VersionValue.Parse(record.AcceptedRemoteVersion)) != VersionComparison.Greater;
        }
    }

    public sealed class SettingsValidationResult { public AppSettings Settings { get; set; } public IList<string> Warnings { get; } = new List<string>(); }
    public sealed class SettingsValidator
    {
        public SettingsValidationResult Validate(AppSettings value)
        {
            var result = new SettingsValidationResult { Settings = value ?? new AppSettings() }; var s = result.Settings;
            if (s.FirstRequestTimeoutSeconds < 5 || s.FirstRequestTimeoutSeconds > 120) { s.FirstRequestTimeoutSeconds = 20; result.Warnings.Add("Timeout первого запроса сброшен к 20 сек."); }
            if (s.RetryTimeoutSeconds < 5 || s.RetryTimeoutSeconds > 120) { s.RetryTimeoutSeconds = 30; result.Warnings.Add("Timeout повтора сброшен к 30 сек."); }
            if (s.CacheMaxAgeDays < 1 || s.CacheMaxAgeDays > 3650) { s.CacheMaxAgeDays = 180; result.Warnings.Add("Возраст cache сброшен к 180 дням."); }
            if (s.ProxyMode == ProxyMode.Custom && (String.IsNullOrWhiteSpace(s.ProxyAddress) || s.ProxyPort < 1 || s.ProxyPort > 65535)) { s.ProxyMode = ProxyMode.System; result.Warnings.Add("Некорректный proxy сброшен к системному."); }
            if (String.IsNullOrWhiteSpace(s.LastFilter)) s.LastFilter = "All";
            s.ExcludedCatalogIds = (s.ExcludedCatalogIds ?? new List<string>()).Where(x => !String.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            s.ExcludedUnknownPaths = (s.ExcludedUnknownPaths ?? new List<string>()).Where(x => !String.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).ToList(); return result;
        }
    }

    public sealed class SettingsService
    {
        private readonly string _path; private readonly SettingsValidator _validator;
        public SettingsService(string path, SettingsValidator validator = null) { _path = path; _validator = validator ?? new SettingsValidator(); }
        public string Path { get { return _path; } }
        public SettingsValidationResult Load()
        {
            if (!File.Exists(_path)) return _validator.Validate(new AppSettings());
            try
            {
                var lines = File.ReadAllLines(_path, Encoding.UTF8); if (lines.Any(x => !String.IsNullOrWhiteSpace(x) && x.IndexOf('=') <= 0)) throw new InvalidDataException("Invalid INI"); var d = lines.Where(x => x.IndexOf('=') > 0).ToDictionary(x => x.Substring(0, x.IndexOf('=')).Trim(), x => x.Substring(x.IndexOf('=') + 1), StringComparer.OrdinalIgnoreCase); var s = new AppSettings();
                B(d,"IncludePrerelease",v=>s.IncludePrerelease=v); B(d,"CheckTotalCommander",v=>s.CheckTotalCommander=v); B(d,"CheckPlugins",v=>s.CheckPlugins=v); B(d,"UseExtendedVersionDetection",v=>s.UseExtendedVersionDetection=v); B(d,"HideUnknownVersion",v=>s.HideUnknownVersion=v); B(d,"ShowUnregisteredPlugins",v=>s.ShowUnregisteredPlugins=v); B(d,"DeleteZipAfterInstall",v=>s.DeleteZipAfterInstall=v); B(d,"UsePersistentMetadataCache",v=>s.UsePersistentMetadataCache=v);
                E<DiscoveryMode>(d,"DiscoveryMode",v=>s.DiscoveryMode=v); E<PostDownloadAction>(d,"PostDownloadAction",v=>s.PostDownloadAction=v); E<ProxyMode>(d,"ProxyMode",v=>s.ProxyMode=v); T(d,"DownloadDirectory",v=>s.DownloadDirectory=v); T(d,"ProxyAddress",v=>s.ProxyAddress=v); T(d,"ProxyUsername",v=>s.ProxyUsername=v); T(d,"LastFilter",v=>s.LastFilter=v); I(d,"ProxyPort",v=>s.ProxyPort=v); I(d,"FirstRequestTimeoutSeconds",v=>s.FirstRequestTimeoutSeconds=v); I(d,"RetryTimeoutSeconds",v=>s.RetryTimeoutSeconds=v); I(d,"CacheMaxAgeDays",v=>s.CacheMaxAgeDays=v); D(d,"WindowLeft",v=>s.WindowLeft=v); D(d,"WindowTop",v=>s.WindowTop=v); D(d,"WindowWidth",v=>s.WindowWidth=v); D(d,"WindowHeight",v=>s.WindowHeight=v); s.ExcludedCatalogIds=L(d,"ExcludedCatalogIds"); s.ExcludedUnknownPaths=L(d,"ExcludedUnknownPaths"); s.AcceptedVersions=A(d); return _validator.Validate(s);
            }
            catch { var fallback = _validator.Validate(new AppSettings()); fallback.Warnings.Add("Настройки повреждены; использованы значения по умолчанию."); return fallback; }
        }
        public void Save(AppSettings settings)
        {
            var s=_validator.Validate(settings).Settings; Directory.CreateDirectory(System.IO.Path.GetDirectoryName(_path)); var temp=_path+".tmp-"+Guid.NewGuid().ToString("N");
            try { File.WriteAllText(temp, Serialize(s), new UTF8Encoding(false)); if(File.Exists(_path)) File.Replace(temp,_path,null); else File.Move(temp,_path); } finally { if(File.Exists(temp)) File.Delete(temp); }
        }
        private static string Serialize(AppSettings s) { return String.Join("\r\n", new[] { "IncludePrerelease="+s.IncludePrerelease,"CheckTotalCommander="+s.CheckTotalCommander,"CheckPlugins="+s.CheckPlugins,"UseExtendedVersionDetection="+s.UseExtendedVersionDetection,"HideUnknownVersion="+s.HideUnknownVersion,"DiscoveryMode="+s.DiscoveryMode,"ShowUnregisteredPlugins="+s.ShowUnregisteredPlugins,"DownloadDirectory="+(s.DownloadDirectory??""),"PostDownloadAction="+s.PostDownloadAction,"DeleteZipAfterInstall="+s.DeleteZipAfterInstall,"ProxyMode="+s.ProxyMode,"ProxyAddress="+(s.ProxyAddress??""),"ProxyPort="+s.ProxyPort,"ProxyUsername="+(s.ProxyUsername??""),"FirstRequestTimeoutSeconds="+s.FirstRequestTimeoutSeconds,"RetryTimeoutSeconds="+s.RetryTimeoutSeconds,"UsePersistentMetadataCache="+s.UsePersistentMetadataCache,"CacheMaxAgeDays="+s.CacheMaxAgeDays,"LastFilter="+(s.LastFilter??"All"),"WindowLeft="+s.WindowLeft.ToString(CultureInfo.InvariantCulture),"WindowTop="+s.WindowTop.ToString(CultureInfo.InvariantCulture),"WindowWidth="+s.WindowWidth.ToString(CultureInfo.InvariantCulture),"WindowHeight="+s.WindowHeight.ToString(CultureInfo.InvariantCulture),"ExcludedCatalogIds="+String.Join("|",s.ExcludedCatalogIds),"ExcludedUnknownPaths="+String.Join("|",s.ExcludedUnknownPaths),"AcceptedVersions="+String.Join("|",s.AcceptedVersions.Select(x=>x.PluginId+"~"+x.LocalSha256+"~"+x.AcceptedRemoteVersion+"~"+x.AcceptedUtc.ToString("o")))})+"\r\n"; }
        private static void B(IDictionary<string,string>d,string k,Action<bool>a){bool v;if(d.TryGetValue(k,out var x)&&Boolean.TryParse(x,out v))a(v);} private static void I(IDictionary<string,string>d,string k,Action<int>a){int v;if(d.TryGetValue(k,out var x)&&Int32.TryParse(x,out v))a(v);} private static void D(IDictionary<string,string>d,string k,Action<double>a){double v;if(d.TryGetValue(k,out var x)&&Double.TryParse(x,NumberStyles.Float,CultureInfo.InvariantCulture,out v))a(v);} private static void T(IDictionary<string,string>d,string k,Action<string>a){if(d.TryGetValue(k,out var x))a(x);} private static void E<T>(IDictionary<string,string>d,string k,Action<T>a)where T:struct{if(d.TryGetValue(k,out var x)&&Enum.TryParse(x,true,out T v))a(v);} private static IList<string>L(IDictionary<string,string>d,string k){return d.TryGetValue(k,out var x)?x.Split(new[]{'|'},StringSplitOptions.RemoveEmptyEntries).ToList():new List<string>();}
        private static IList<AcceptedVersionOverride> A(IDictionary<string,string>d){var result=new List<AcceptedVersionOverride>(); if(!d.TryGetValue("AcceptedVersions",out var raw))return result; foreach(var item in raw.Split(new[]{'|'},StringSplitOptions.RemoveEmptyEntries)){var p=item.Split('~');DateTime utc;if(p.Length==4&&DateTime.TryParse(p[3],null,DateTimeStyles.RoundtripKind,out utc))result.Add(new AcceptedVersionOverride{PluginId=p[0],LocalSha256=p[1],AcceptedRemoteVersion=p[2],AcceptedUtc=utc});}return result;}
    }
}
