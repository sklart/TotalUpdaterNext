using System;
using System.IO;
using System.Linq;
using TotalUpdater.Next.Catalog;
using TotalUpdater.Next.Core;
using TotalUpdater.Next.Core.Versions;
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
                Versions(); Paths(); DiscoveryAndLocalVersion(); CatalogAliases();
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
        private static void DiscoveryAndLocalVersion()
        {
            var root = Path.Combine(Path.GetTempPath(), "tunext-tests-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
            try
            {
                var plug = Path.Combine(root, "plugins"); Directory.CreateDirectory(plug); File.WriteAllBytes(Path.Combine(plug, "fileinfo.wlx"), new byte[0]); File.WriteAllText(Path.Combine(plug, "version.txt"), "Version: 2.23"); File.WriteAllText(Path.Combine(plug, "history.txt"), "Version: 2.22"); File.WriteAllText(Path.Combine(plug, "readme.txt"), "Version: 2.21");
                var ini = Path.Combine(root, "wincmd.ini"); File.WriteAllText(ini, "[Configuration]\r\nInstallDir=" + root + "\r\n[PackerPlugins]\r\n0=plugins\\missing.wcx\r\n[ListerPlugins]\r\n0=%COMMANDER_PATH%\\plugins\\fileinfo.wlx\r\n[FileSystemPlugins]\r\n0=plugins\\missing.wfx\r\n[ContentPlugins]\r\n0=plugins\\missing.wdx\r\n");
                var catalog = new CatalogService(Path.Combine(root, "user.json")); var resolver = new TotalCommanderConfigurationResolver(); var configuration = resolver.Resolve(ini);
                var found = new PluginDiscoveryService(resolver, new LocalVersionResolver(), catalog).Discover(configuration);
                Assert(found.Count(x => x.Type == PluginType.Wcx) == 1 && found.Count(x => x.Type == PluginType.Wlx) == 1 && found.Count(x => x.Type == PluginType.Wfx) == 1 && found.Count(x => x.Type == PluginType.Wdx) == 1, "all plugin sections");
                var fileInfo = found.Single(x => x.Identity.Id == "fileinfo"); Assert(fileInfo.LocalVersion.ParsedValue.Raw == "2.23" && fileInfo.LocalVersion.Source == VersionSource.TextFile, "text version fallback");
            }
            finally { Directory.Delete(root, true); }
        }
        private static void CatalogAliases()
        {
            var catalog = new CatalogService(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".json"));
            Assert(catalog.FindByAlias("fileinfo.wlx").Id == "fileinfo", "fileinfo alias"); Assert(catalog.FindByAlias("fileinfo64.wlx").Id == "fileinfo", "fileinfo64 alias");
        }
        private static void Assert(bool condition, string name) { if (!condition) throw new InvalidOperationException(name); _count++; }
    }
}
