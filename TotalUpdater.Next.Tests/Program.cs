using System;
using System.IO;
using System.Linq;
using TotalUpdater.Next.Catalog;
using TotalUpdater.Next.Core;
using TotalUpdater.Next.Core.Versions;
using TotalUpdater.Next.Infrastructure;
using TotalUpdater.Next.Sources;
using TotalUpdater.Next.TotalCommander;

namespace TotalUpdater.Next.Tests
{
    internal static class Program
    {
        private static int _count;
        private static int Main()
        {
            try
            {
                Versions(); Paths(); DiscoveryRealIniFormats(); FileInfoSpecificStrategy(); CatalogAliases(); ApplicationMetadataAndUserAgent();
                Console.WriteLine("PASS " + _count + " tests"); return 0;
            }
            catch (Exception ex) { Console.Error.WriteLine("FAIL: " + ex.Message); return 1; }
        }
        private static void Versions()
        {
            Assert(VersionValue.Parse("1.0").CompareTo(VersionValue.Parse("1.1")) == VersionComparison.Less, "1.0 < 1.1");
            Assert(VersionValue.Parse("1.9").CompareTo(VersionValue.Parse("1.10")) == VersionComparison.Less, "1.9 < 1.10");
            Assert(VersionValue.Parse("2.0 beta 2").CompareTo(VersionValue.Parse("2.0")) == VersionComparison.Less, "beta < final");
            Assert(VersionValue.Parse("2.5a").CompareTo(VersionValue.Parse("2.5b")) == VersionComparison.Less, "2.5a < 2.5b");
            Assert(VersionValue.Parse("2.0 rc1").CompareTo(VersionValue.Parse("2.0")) == VersionComparison.Less, "rc < final");
            Assert(VersionValue.Parse("2.0").CompareTo(VersionValue.Parse("2.0 beta")) == VersionComparison.Greater, "final > beta");
            Assert(VersionValue.Parse("not a version").CompareTo(VersionValue.Parse("2.0")) == VersionComparison.Unknown, "unknown comparison");
            Assert(VersionValue.Parse("0, 8, 5, 6").CompareTo(VersionValue.Parse("0.8.5.6")) == VersionComparison.Equal, "comma PE version equals dotted version");
            Assert(!VersionValue.Parse("1.2 trailing text").IsKnown, "partial version is rejected");
            Assert(!VersionValue.Parse("1.2.3.4.5").IsKnown, "too many version parts are rejected");
        }
        private static void Paths()
        {
            var root = Path.Combine(Path.GetTempPath(), "tunext-tests-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
            try
            {
                var config = new TotalCommanderConfiguration { IniPath = Path.Combine(root, "wincmd.ini"), InstallDirectory = root };
                var resolver = new TotalCommanderConfigurationResolver();
                Assert(resolver.ExpandPath("%COMMANDER_PATH%\\plugins\\x.wcx", config) == Path.Combine(root, "plugins", "x.wcx"), "COMMANDER_PATH");
                Assert(resolver.ExpandPath("plugins\\x.wcx", config) == Path.Combine(root, "plugins", "x.wcx"), "relative path");
                Assert(resolver.ExpandPath(Path.Combine(root, "x.wcx"), config) == Path.Combine(root, "x.wcx"), "absolute path");
                Assert(resolver.ExpandPath("%TEMP%\\x.wcx", config) == Path.GetFullPath(Path.Combine(Environment.ExpandEnvironmentVariables("%TEMP%"), "x.wcx")), "environment variable");
            }
            finally { Directory.Delete(root, true); }
        }
        private static void DiscoveryRealIniFormats()
        {
            var root = Path.Combine(Path.GetTempPath(), "tunext-tests-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
            try
            {
                var plug = Path.Combine(root, "plugins"); Directory.CreateDirectory(plug);
                File.WriteAllBytes(Path.Combine(plug, "Total7zip.wcx"), new byte[0]);
                File.WriteAllBytes(Path.Combine(plug, "fileinfo.wlx"), new byte[0]);
                File.WriteAllBytes(Path.Combine(plug, "CloudDrive.wfx"), new byte[0]);
                File.WriteAllBytes(Path.Combine(plug, "sample.wdx"), new byte[0]);
                var ini = Path.Combine(root, "wincmd.ini"); File.WriteAllText(ini,
                    "[Configuration]\r\nInstallDir=" + root + "\r\n" +
                    "[PackerPlugins]\r\nzip=735,%COMMANDER_PATH%\\plugins\\Total7zip.wcx\r\n" +
                    "[ListerPlugins]\r\n0=%COMMANDER_PATH%\\plugins\\fileinfo.wlx\r\n" +
                    "[FileSystemPlugins]\r\nCloudDrive=%COMMANDER_PATH%\\plugins\\CloudDrive.wfx\r\n" +
                    "[ContentPlugins]\r\n0=%COMMANDER_PATH%\\plugins\\sample.wdx\r\n");
                var catalog = new CatalogService(Path.Combine(root, "user.json")); var resolver = new TotalCommanderConfigurationResolver(); var configuration = resolver.Resolve(ini);
                var found = new PluginDiscoveryService(resolver, new LocalVersionResolver(), catalog).Discover(configuration);
                Assert(found.Count(x => x.Type == PluginType.Wcx) == 1 && found.Count(x => x.Type == PluginType.Wlx) == 1 && found.Count(x => x.Type == PluginType.Wfx) == 1 && found.Count(x => x.Type == PluginType.Wdx) == 1, "real WCX/WLX/WFX/WDX sections");
                Assert(found.Single(x => x.Type == PluginType.Wcx).Identity.Id == "total7zip", "WCX flags,path format");
                Assert(found.Single(x => x.Type == PluginType.Wfx).DisplayName == "CloudDrive", "WFX plugin name key");
            }
            finally { Directory.Delete(root, true); }
        }
        private static void FileInfoSpecificStrategy()
        {
            var root = Path.Combine(Path.GetTempPath(), "tunext-tests-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
            try
            {
                var plugin = Path.Combine(root, "fileinfo.wlx"); File.WriteAllBytes(plugin, new byte[0]);
                File.WriteAllText(Path.Combine(root, "fileinfo.version"), "Public version: 2.23");
                var version = new LocalVersionResolver().Resolve(plugin, new PluginIdentity { Id = "fileinfo", Name = "FileInfo", Type = PluginType.Wlx });
                Assert(version.ParsedValue.Raw == "2.23" && version.Source == VersionSource.CustomRule, "FileInfo custom public-version strategy");
            }
            finally { Directory.Delete(root, true); }
        }
        private static void CatalogAliases()
        {
            var catalog = new CatalogService(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".json"));
            Assert(catalog.FindByAlias("fileinfo.wlx").Id == "fileinfo", "fileinfo alias"); Assert(catalog.FindByAlias("fileinfo64.wlx").Id == "fileinfo", "fileinfo64 alias");
        }
        private static void ApplicationMetadataAndUserAgent()
        {
            Assert(ApplicationMetadata.Version == typeof(ApplicationMetadata).Assembly.GetName().Version.ToString(3), "application metadata version");
            using (var http = new HttpService(ApplicationMetadata.Version))
                Assert(http.UserAgent == "TotalUpdaterNext/" + ApplicationMetadata.Version, "user agent follows application version");
        }
        private static void Assert(bool condition, string name) { if (!condition) throw new InvalidOperationException(name); _count++; }
    }
}
