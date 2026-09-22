using System;
using System.IO;
using System.Linq;
using System.Text;
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
                Versions(); Paths(); DiscoveryRealIniFormats(); ArchitectureAwareDiscovery(); FileInfoPeVersionStrategy(); StrategyPriorityAndFallback(); ConfigurationDetection(); ConfigurationPrecedenceFinalization(); RedirectSections(); IniEncodingsAndPathExpansion(); CatalogAliases(); ApplicationMetadataAndUserAgent();
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
        private static void ArchitectureAwareDiscovery()
        {
            var root = NewRoot();
            try
            {
                var plugins = Path.Combine(root, "Programs64", "plugins"); Directory.CreateDirectory(plugins);
                foreach (var file in new[] { "fileinfo.wlx", "fileinfo.uwlx", "fileinfo.wlx64", "x64only.wlx64", "physical64.wlx64", "reverse.wlx", "shared.wcx", "shared.wcx64", "cloud.wfx", "cloud.wfx64", "content.wdx", "content.wdx64", "plain.wlx", "conflict.wlx", "conflict.wlx64", "TOTALCMD.EXE", "TOTALCMD64.EXE" })
                    File.WriteAllBytes(Path.Combine(plugins, file), new byte[0]);
                var ini = WriteIni(root, "wincmd.ini",
                    "[Configuration]\r\nInstallDir=" + plugins + "\r\n" +
                    "[PackerPlugins]\r\n7z=735,shared.wcx\r\nzip=735,shared.wcx\r\n" +
                    "[PackerPlugins64]\r\n7z=1\r\nzip=1\r\n" +
                    "[ListerPlugins]\r\n0=fileinfo.wlx\r\n1=x64only.wlx\r\n2=physical64.wlx\r\n3=reverse.wlx64\r\n4=plain.wlx\r\n5=conflict.wlx\r\n6=missing.wlx\r\n" +
                    "[ListerPlugins64]\r\n0=1\r\n1=1\r\n6=1\r\n" +
                    "[FileSystemPlugins]\r\nCloud=cloud.wfx\r\n" +
                    "[FileSystemPlugins64]\r\nCloud=1\r\n" +
                    "[ContentPlugins]\r\n0=content.wdx\r\n" +
                    "[ContentPlugins64]\r\n0=1\r\n");
                var resolver = new TotalCommanderConfigurationResolver(); var configuration = resolver.Resolve(ini);
                var versions = new PathProbe(new System.Collections.Generic.Dictionary<string, LocalVersion>(StringComparer.OrdinalIgnoreCase)
                {
                    [Path.Combine(plugins, "conflict.wlx")] = FileVersionProbe.Create("1.0", VersionSource.FileVersion, VersionConfidence.Exact),
                    [Path.Combine(plugins, "conflict.wlx64")] = FileVersionProbe.Create("2.0", VersionSource.FileVersion, VersionConfidence.Exact),
                    [Path.Combine(plugins, "fileinfo.wlx")] = FileVersionProbe.Create("2.23", VersionSource.FileVersion, VersionConfidence.Exact),
                    [Path.Combine(plugins, "fileinfo.wlx64")] = FileVersionProbe.Create("2.23", VersionSource.FileVersion, VersionConfidence.Exact)
                });
                var found = new PluginDiscoveryService(resolver, new LocalVersionResolver(new IPluginSpecificVersionStrategy[0], new IVersionProbe[] { versions }), new CatalogService(Path.Combine(root, "user.json"))).Discover(configuration);
                var fileInfo = found.Single(x => x.Identity.Id == "fileinfo");
                Assert(fileInfo.Binaries.Count == 3 && fileInfo.RelatedFiles.Count == 3 && fileInfo.Architecture == (PluginArchitecture.X86 | PluginArchitecture.X64), "ANSI Unicode and x64 companions merge into one family");
                Assert(!fileInfo.HasVersionConflict && fileInfo.LocalVersion.RawValue == "2.23", "same binary versions form family version");
                var x64only = found.Single(x => x.PrimaryPath.EndsWith("x64only.wlx64", StringComparison.OrdinalIgnoreCase));
                Assert(x64only.FileExists && x64only.Architecture == PluginArchitecture.X64, "x64-only plugin discovered from normal section and marker");
                Assert(found.Single(x => x.PrimaryPath.EndsWith("physical64.wlx64", StringComparison.OrdinalIgnoreCase)).Architecture == PluginArchitecture.X64, "physical x64 companion found without marker");
                Assert(found.Single(x => x.PrimaryPath.EndsWith("reverse.wlx", StringComparison.OrdinalIgnoreCase)).Architecture == PluginArchitecture.X86, "configured wlx64 finds physical wlx companion");
                Assert(found.Single(x => x.PrimaryPath.EndsWith("plain.wlx", StringComparison.OrdinalIgnoreCase)).Architecture == PluginArchitecture.X86, "directory name containing 64 does not change binary architecture");
                var packer = found.Single(x => x.Type == PluginType.Wcx);
                Assert(packer.ConfigurationKeys.Count == 2 && packer.Binaries.Count == 3 && packer.Architecture == (PluginArchitecture.X86 | PluginArchitecture.X64), "one WCX assigned to many extensions stays one family");
                Assert(found.Single(x => x.Type == PluginType.Wfx).Architecture == (PluginArchitecture.X86 | PluginArchitecture.X64) && found.Single(x => x.Type == PluginType.Wdx).Architecture == (PluginArchitecture.X86 | PluginArchitecture.X64), "WFX and WDX x86+x64 companions merge");
                Assert(found.Single(x => x.PrimaryPath.EndsWith("conflict.wlx", StringComparison.OrdinalIgnoreCase)).HasVersionConflict, "different x86 and x64 versions are not silently selected");
                var commander = found.Single(x => x.Type == PluginType.TotalCommander);
                Assert(commander.Binaries.Count == 2 && commander.Architecture == (PluginArchitecture.X86 | PluginArchitecture.X64), "TOTALCMD.EXE and TOTALCMD64.EXE merge");
                Assert(configuration.Warnings.Any(x => x.IndexOf("missing", StringComparison.OrdinalIgnoreCase) >= 0), "missing marked x64 companion is diagnostic only");

                var redirected = WriteIni(root, "plugins64.ini", "[ListerPlugins64]\r\n0=1");
                var redirectMain = WriteIni(root, "redirect-main.ini", "[Configuration]\r\nInstallDir=" + plugins + "\r\n[ListerPlugins]\r\n0=x64only.wlx\r\n[ListerPlugins64]\r\nRedirectSection=plugins64.ini");
                var redirectFound = new PluginDiscoveryService(resolver, new LocalVersionResolver(), new CatalogService(Path.Combine(root, "second-user.json"))).Discover(resolver.Resolve(redirectMain));
                Assert(redirectFound.Single(x => x.Type == PluginType.Wlx).Architecture == PluginArchitecture.X64, "Plugins64 marker follows RedirectSection");
            }
            finally { Directory.Delete(root, true); }
        }
        private static void FileInfoPeVersionStrategy()
        {
            var x86 = FileInfoVersionStrategy.FromPeFileVersion("2.2.3.0");
            var x64 = FileInfoVersionStrategy.FromPeFileVersion("2.2.3.0");
            Assert(x86.ParsedValue.Raw == "2.23" && x86.Source == VersionSource.CustomRule, "FileInfo PE 2.2.3.0 -> public 2.23");
            Assert(x86.ParsedValue.CompareTo(x64.ParsedValue) == VersionComparison.Equal, "FileInfo x86/x64 public version matches");
            Assert(!FileInfoVersionStrategy.FromPeFileVersion("2.2.3").ParsedValue.IsKnown, "FileInfo requires a PE four-part version");
        }
        private static void StrategyPriorityAndFallback()
        {
            var custom = FileInfoVersionStrategy.FromPeFileVersion("2.2.3.0");
            var generic = FileVersionProbe.Create("9.9.9.9", VersionSource.FileVersion, VersionConfidence.Exact);
            var resolver = new LocalVersionResolver(new IPluginSpecificVersionStrategy[] { new FixedFileInfoStrategy(custom) }, new IVersionProbe[] { new FixedProbe(generic) });
            var fileInfo = resolver.Resolve("not-used", new PluginIdentity { Id = "fileinfo", Type = PluginType.Wlx });
            Assert(fileInfo.ParsedValue.Raw == "2.23" && fileInfo.Source == VersionSource.CustomRule, "FileInfo strategy precedes generic FileVersion");

            var fallbackResolver = new LocalVersionResolver(new IPluginSpecificVersionStrategy[] { new FixedFileInfoStrategy(LocalVersion.Unknown) }, new IVersionProbe[] { new FixedProbe(generic) });
            Assert(fallbackResolver.Resolve("not-used", new PluginIdentity { Id = "fileinfo", Type = PluginType.Wlx }).Source == VersionSource.FileVersion, "Unknown strategy falls back to FileVersion");
            Assert(resolver.Resolve("not-used", new PluginIdentity { Id = "ordinary", Type = PluginType.Wlx }).Source == VersionSource.FileVersion, "ordinary plugin uses FileVersion fallback");
        }
        private static void ConfigurationDetection()
        {
            var root = NewRoot();
            try
            {
                var install = Path.Combine(root, "TotalCmd"); Directory.CreateDirectory(install);
                var explicitIni = WriteIni(root, "explicit.ini", "[Configuration]"); var environmentIni = WriteIni(root, "environment.ini", "[Configuration]"); var registryIni = WriteIni(root, "registry.ini", "[Configuration]");
                var localIni = WriteIni(install, "wincmd.ini", "[Configuration]\r\nUseIniInProgramDir=1");
                var fakeEnvironment = new FakeEnvironment { Values = { ["COMMANDER_INI"] = environmentIni, ["COMMANDER_PATH"] = install } };
                var registry = new FakeRegistry(new RegistryConfigurationEntry { IniFileName = registryIni, InstallDirectory = install });
                var resolver = new TotalCommanderConfigurationResolver(new IniDocumentReader(), registry, fakeEnvironment);
                Assert(resolver.Resolve(explicitIni).IniPath == Path.GetFullPath(explicitIni), "explicit INI priority");
                Assert(resolver.Resolve("").IniPath == Path.GetFullPath(environmentIni), "COMMANDER_INI detection");
                fakeEnvironment.Values.Remove("COMMANDER_INI");
                Assert(resolver.DetectInstallDirectory() == Path.GetFullPath(install) && resolver.Resolve("").IniPath == Path.GetFullPath(registryIni), "COMMANDER_PATH determines install, registry determines INI");
                foreach (var value in new[] { 1 })
                {
                    File.WriteAllText(localIni, "[Configuration]\r\nUseIniInProgramDir=" + value);
                    Assert(resolver.Resolve("").IniPath == Path.GetFullPath(registryIni), "UseIniInProgramDir=1 registry wins");
                }
                foreach (var value in new[] { 4, 5, 7 })
                {
                    File.WriteAllText(localIni, "[Configuration]\r\nUseIniInProgramDir=" + value);
                    Assert(resolver.Resolve("").IniPath == Path.GetFullPath(localIni), "UseIniInProgramDir=" + value + " local wins");
                }
                File.WriteAllText(localIni, "[Configuration]");
                var relativeIni = WriteIni(install, "relative.ini", "[Configuration]");
                resolver = new TotalCommanderConfigurationResolver(new IniDocumentReader(), new FakeRegistry(new RegistryConfigurationEntry { IniFileName = "relative.ini", InstallDirectory = install }), fakeEnvironment);
                var relative = resolver.Resolve(""); Assert(relative.IniPath == Path.GetFullPath(relativeIni) && relative.InstallDirectory == install, "relative registry IniFileName");
                var hkcuIni = WriteIni(root, "hkcu.ini", "[Configuration]"); var hklmIni = WriteIni(root, "hklm.ini", "[Configuration]");
                Assert(new TotalCommanderConfigurationResolver(new IniDocumentReader(), new FakeRegistry(new RegistryConfigurationEntry { IniFileName = hkcuIni }), fakeEnvironment).Resolve("").IniPath == Path.GetFullPath(hkcuIni), "HKCU registry detection");
                Assert(new TotalCommanderConfigurationResolver(new IniDocumentReader(), new FakeRegistry(new RegistryConfigurationEntry { IniFileName = hklmIni }), fakeEnvironment).Resolve("").IniPath == Path.GetFullPath(hklmIni), "HKLM registry detection");
                var appDataRoot = Path.Combine(root, "appdata"); var windowsRoot = Path.Combine(root, "windows"); Directory.CreateDirectory(Path.Combine(appDataRoot, "Ghisler")); var appDataIni = WriteIni(Path.Combine(appDataRoot, "Ghisler"), "wincmd.ini", "[Configuration]");
                fakeEnvironment.Values["APPDATA"] = appDataRoot; fakeEnvironment.Values["WINDIR"] = windowsRoot;
                Assert(new TotalCommanderConfigurationResolver(new IniDocumentReader(), new FakeRegistry(), fakeEnvironment).Resolve("").IniPath == Path.GetFullPath(appDataIni), "AppData fallback detection");
                var expandedIni = WriteIni(Path.Combine(appDataRoot, "Ghisler"), "expanded.ini", "[Configuration]");
                Assert(new TotalCommanderConfigurationResolver(new IniDocumentReader(), new FakeRegistry(new RegistryConfigurationEntry { IniFileName = "%APPDATA%\\Ghisler\\expanded.ini", InstallDirectory = install }), fakeEnvironment).Resolve("").IniPath == Path.GetFullPath(expandedIni), "registry environment expansion");
                Assert(new TotalCommanderConfigurationResolver(new IniDocumentReader(), new FakeRegistry(), fakeEnvironment).Resolve(appDataIni).InstallDirectory == Path.GetFullPath(install), "explicit AppData INI keeps COMMANDER_PATH install directory");
                var pluginDirectory = Path.Combine(install, "plugins", "wlx"); Directory.CreateDirectory(pluginDirectory); File.WriteAllBytes(Path.Combine(pluginDirectory, "x.wlx"), new byte[0]);
                var pluginIni = WriteIni(Path.Combine(appDataRoot, "Ghisler"), "plugins.ini", "[ListerPlugins]\r\n0=plugins\\wlx\\x.wlx");
                var pluginConfig = new TotalCommanderConfigurationResolver(new IniDocumentReader(), new FakeRegistry(), fakeEnvironment).Resolve(pluginIni);
                var plugin = new PluginDiscoveryService(new TotalCommanderConfigurationResolver(new IniDocumentReader(), new FakeRegistry(), fakeEnvironment), new LocalVersionResolver(), new CatalogService(Path.Combine(root, "user.json"))).Discover(pluginConfig).Single(x => x.Type == PluginType.Wlx);
                Assert(plugin.PrimaryPath == Path.Combine(install, "plugins", "wlx", "x.wlx"), "relative plugin path uses TC install directory");
                fakeEnvironment.Values.Remove("COMMANDER_PATH");
                var fallbackIni = WriteIni(root, "install-fallback.ini", "[Configuration]\r\nInstallDir=" + install);
                Assert(new TotalCommanderConfigurationResolver(new IniDocumentReader(), new FakeRegistry(), fakeEnvironment).Resolve(fallbackIni).InstallDirectory == Path.GetFullPath(install), "Configuration InstallDir fallback");
                File.Delete(appDataIni); Directory.CreateDirectory(windowsRoot); var windowsIni = WriteIni(windowsRoot, "wincmd.ini", "[Configuration]");
                Assert(new TotalCommanderConfigurationResolver(new IniDocumentReader(), new FakeRegistry(), fakeEnvironment).Resolve("").IniPath == Path.GetFullPath(windowsIni), "Windows directory fallback detection");
            }
            finally { Directory.Delete(root, true); }
        }
        private static void RedirectSections()
        {
            var root = NewRoot();
            try
            {
                var plugins = Path.Combine(root, "plugins"); Directory.CreateDirectory(plugins);
                foreach (var file in new[] { "a.wcx", "b.wlx", "c.wfx", "d.wdx", "old.wcx", "first.wcx", "second.wcx" }) File.WriteAllBytes(Path.Combine(plugins, file), new byte[0]);
                var redirected = WriteIni(root, "plugins.ini", "[PackerPlugins]\r\nzip=735,%COMMANDER_PATH%\\plugins\\a.wcx\r\n[ListerPlugins]\r\n0=plugins\\b.wlx\r\n[FileSystemPlugins]\r\nCloud=plugins\\c.wfx\r\n[ContentPlugins]\r\n0=plugins\\d.wdx");
                var main = WriteIni(root, "wincmd.ini", "[Configuration]\r\nInstallDir=" + root + "\r\n[PackerPlugins]\r\nRedirectSection=plugins.ini\r\nold=0,plugins\\old.wcx\r\n[ListerPlugins]\r\nRedirectSection=plugins.ini\r\n[FileSystemPlugins]\r\nRedirectSection=plugins.ini\r\n[ContentPlugins]\r\nRedirectSection=plugins.ini");
                var resolver = new TotalCommanderConfigurationResolver(); var configuration = resolver.Resolve(main); var discovery = new PluginDiscoveryService(resolver, new LocalVersionResolver(), new CatalogService(Path.Combine(root, "user.json")));
                var found = discovery.Discover(configuration);
                Assert(found.Count(x => x.Type == PluginType.Wcx) == 1 && found.Count(x => x.Type == PluginType.Wlx) == 1 && found.Count(x => x.Type == PluginType.Wfx) == 1 && found.Count(x => x.Type == PluginType.Wdx) == 1, "redirected WCX/WLX/WFX/WDX");
                Assert(!found.Any(x => x.PrimaryPath.EndsWith("old.wcx", StringComparison.OrdinalIgnoreCase)), "original section ignored after redirect");
                var alternate = WriteIni(root, "alternate.ini", "[PackerPlugins]\r\nzip=1,plugins\\a.wcx");
                var alternateMain = WriteIni(root, "alternate-main.ini", "[Configuration]\r\nInstallDir=" + root + "\r\nAlternateUserIni=" + alternate + "\r\n[PackerPlugins]\r\nRedirectSection=1");
                Assert(discovery.Discover(resolver.Resolve(alternateMain)).Count(x => x.Type == PluginType.Wcx) == 1, "RedirectSection=1 AlternateUserIni");
                var zeroMain = WriteIni(root, "zero-main.ini", "[Configuration]\r\nInstallDir=" + root + "\r\n[PackerPlugins]\r\nRedirectSection=0\r\nzip=1,plugins\\a.wcx");
                Assert(discovery.Discover(resolver.Resolve(zeroMain)).Count(x => x.Type == PluginType.Wcx) == 1, "RedirectSection=0 uses local section");
                var first = WriteIni(root, "first.ini", "[PackerPlugins]\r\nRedirectSection=second.ini\r\nzip=1,plugins\\first.wcx"); WriteIni(root, "second.ini", "[PackerPlugins]\r\nzip=1,plugins\\second.wcx");
                var recursiveMain = WriteIni(root, "recursive-main.ini", "[Configuration]\r\nInstallDir=" + root + "\r\n[PackerPlugins]\r\nRedirectSection=" + first);
                var recursive = discovery.Discover(resolver.Resolve(recursiveMain)); Assert(recursive.Any(x => x.PrimaryPath.EndsWith("first.wcx")) && !recursive.Any(x => x.PrimaryPath.EndsWith("second.wcx")), "non-recursive redirect");
                var missingMain = WriteIni(root, "missing-main.ini", "[Configuration]\r\nInstallDir=" + root + "\r\n[PackerPlugins]\r\nRedirectSection=missing.ini");
                var missing = resolver.Resolve(missingMain); Assert(discovery.Discover(missing).Count == 0 && missing.Warnings.Count == 1, "missing redirect file warning");
                var unsupportedMain = WriteIni(root, "unsupported-main.ini", "[Configuration]\r\nInstallDir=" + root + "\r\n[PackerPlugins]\r\nRedirectSection=subdir\\plugins.ini");
                var unsupported = resolver.Resolve(unsupportedMain); Assert(discovery.Discover(unsupported).Count == 0 && unsupported.Warnings.Count == 1, "non-bare relative redirect is not accepted");
            }
            finally { Directory.Delete(root, true); }
        }
        private static void ConfigurationPrecedenceFinalization()
        {
            var root = NewRoot();
            try
            {
                var userInstall = Path.Combine(root, "TotalCmdUser"); var machineInstall = Path.Combine(root, "TotalCmdMachine"); var oldInstall = Path.Combine(root, "OldTotalCmd");
                Directory.CreateDirectory(userInstall); Directory.CreateDirectory(machineInstall); Directory.CreateDirectory(oldInstall);
                var userIni = WriteIni(userInstall, "user.ini", "[Configuration]\r\nInstallDir=" + oldInstall); var machineIni = WriteIni(machineInstall, "machine.ini", "[Configuration]\r\nInstallDir=" + oldInstall);
                var environment = new FakeEnvironment { Values = { ["COMMANDER_PATH"] = userInstall } };
                var entries = new FakeRegistry(new RegistryConfigurationEntry { InstallDirectory = userInstall, IniFileName = "user.ini" }, new RegistryConfigurationEntry { InstallDirectory = machineInstall, IniFileName = "machine.ini" });
                var resolver = new TotalCommanderConfigurationResolver(new IniDocumentReader(), entries, environment);
                var commanderPathConfiguration = resolver.Resolve(userIni);
                Assert(commanderPathConfiguration.InstallDirectory == userInstall && commanderPathConfiguration.InstallDirectorySource == InstallDirectorySource.CommanderPath, "COMMANDER_PATH > Configuration.InstallDir");
                environment.Values.Remove("COMMANDER_PATH");
                var registryConfiguration = resolver.Resolve(userIni);
                Assert(registryConfiguration.InstallDirectory == userInstall && registryConfiguration.InstallDirectorySource == InstallDirectorySource.Registry, "registry InstallDir > Configuration.InstallDir");
                Assert(resolver.Resolve("").IniPath == Path.GetFullPath(userIni), "relative IniFileName uses same registry entry InstallDir");
                Assert(resolver.Resolve("").IniPath == Path.GetFullPath(userIni), "HKCU remains higher priority than HKLM");
                File.Delete(userIni);
                Assert(resolver.Resolve("").IniPath == Path.GetFullPath(machineIni), "HKCU relative INI does not borrow HKLM InstallDir");
            }
            finally { Directory.Delete(root, true); }
        }
        private static void IniEncodingsAndPathExpansion()
        {
            var root = NewRoot();
            try
            {
                var utf16 = Path.Combine(root, "utf16.ini"); File.WriteAllText(utf16, ";comment\r\n[PaCkErPlUgInS]\r\nzip=1,path=with=equals.wcx", Encoding.Unicode);
                var document = new IniDocumentReader().Read(utf16); Assert(document.GetSection("packerplugins").GetValue("ZIP") == "1,path=with=equals.wcx", "UTF-16 INI and first equals");
                var ini = WriteIni(root, "paths.ini", "[Configuration]\r\nInstallDir=" + root); var environment = new FakeEnvironment { Values = { ["APPDATA"] = Path.Combine(root, "appdata"), ["TEMP"] = Path.Combine(root, "temp") } };
                var resolver = new TotalCommanderConfigurationResolver(new IniDocumentReader(), new FakeRegistry(), environment); var config = resolver.Resolve(ini);
                Assert(resolver.ExpandPath("%COMMANDER_PATH%\\p", config).EndsWith("\\p"), "COMMANDER_PATH expansion");
                Assert(resolver.ExpandPath("%COMMANDER_INI%", config) == Path.GetFullPath(ini), "COMMANDER_INI expansion");
                Assert(resolver.ExpandPath("%COMMANDER_DRIVE%\\p", config).StartsWith(Path.GetPathRoot(root)), "COMMANDER_DRIVE expansion");
                Assert(resolver.ExpandPath("%APPDATA%\\p", config).StartsWith(environment.Values["APPDATA"]), "APPDATA expansion"); Assert(resolver.ExpandPath("%TEMP%\\p", config).StartsWith(environment.Values["TEMP"]), "TEMP expansion");
            }
            finally { Directory.Delete(root, true); }
        }
        private static string NewRoot() { var root = Path.Combine(Path.GetTempPath(), "tunext-tests-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root); return root; }
        private static string WriteIni(string directory, string name, string content) { var path = Path.Combine(directory, name); File.WriteAllText(path, content); return path; }
        private static void CatalogAliases()
        {
            var catalog = new CatalogService(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".json"));
            Assert(catalog.FindByAlias("fileinfo.wlx").Id == "fileinfo", "fileinfo alias"); Assert(catalog.FindByAlias("fileinfo64.wlx").Id == "fileinfo", "fileinfo64 alias");
            Assert(catalog.FindByAlias("fileinfo.uwlx").Id == "fileinfo" && catalog.FindByAlias("fileinfo.wlx64").Id == "fileinfo", "catalog recognizes Unicode and x64 companion aliases");
        }
        private static void ApplicationMetadataAndUserAgent()
        {
            Assert(ApplicationMetadata.Version == typeof(ApplicationMetadata).Assembly.GetName().Version.ToString(3), "application metadata version");
            using (var http = new HttpService(ApplicationMetadata.Version))
                Assert(http.UserAgent == "TotalUpdaterNext/" + ApplicationMetadata.Version, "user agent follows application version");
        }
        private sealed class FixedFileInfoStrategy : IPluginSpecificVersionStrategy
        {
            private readonly LocalVersion _version;
            public FixedFileInfoStrategy(LocalVersion version) { _version = version; }
            public bool CanHandle(PluginIdentity identity) { return identity != null && identity.Id == "fileinfo"; }
            public LocalVersion Probe(string filePath) { return _version; }
        }
        private sealed class FixedProbe : IVersionProbe
        {
            private readonly LocalVersion _version;
            public FixedProbe(LocalVersion version) { _version = version; }
            public LocalVersion Probe(string filePath) { return _version; }
        }
        private sealed class PathProbe : IVersionProbe
        {
            private readonly System.Collections.Generic.IDictionary<string, LocalVersion> _versions;
            public PathProbe(System.Collections.Generic.IDictionary<string, LocalVersion> versions) { _versions = versions; }
            public LocalVersion Probe(string filePath) { LocalVersion value; return _versions.TryGetValue(filePath, out value) ? value : LocalVersion.Unknown; }
        }
        private sealed class FakeRegistry : IRegistryConfigurationReader
        {
            private readonly RegistryConfigurationEntry[] _entries;
            public FakeRegistry(params RegistryConfigurationEntry[] entries) { _entries = entries; }
            public System.Collections.Generic.IEnumerable<RegistryConfigurationEntry> Read() { return _entries; }
        }
        private sealed class FakeEnvironment : IEnvironmentProvider
        {
            public System.Collections.Generic.Dictionary<string, string> Values { get; } = new System.Collections.Generic.Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            public string Get(string name) { string value; return Values.TryGetValue(name, out value) ? value : null; }
            public string Expand(string value) { return System.Text.RegularExpressions.Regex.Replace(value, "%([^%]+)%", m => { string item; return Values.TryGetValue(m.Groups[1].Value, out item) ? item : m.Value; }); }
            public string AppData { get { return Get("APPDATA") ?? Path.GetTempPath(); } }
            public string WindowsDirectory { get { return Get("WINDIR") ?? Path.GetTempPath(); } }
        }
        private static void Assert(bool condition, string name) { if (!condition) throw new InvalidOperationException(name); _count++; }
    }
}
