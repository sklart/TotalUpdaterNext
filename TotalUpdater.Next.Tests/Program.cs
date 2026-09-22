using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using TotalUpdater.Next.Catalog;
using TotalUpdater.Next.Core;
using TotalUpdater.Next.Core.Versions;
using TotalUpdater.Next.Infrastructure;
using TotalUpdater.Next.Sources;
using TotalUpdater.Next.TotalCommander;
using TotalUpdater.Next.UI;

namespace TotalUpdater.Next.Tests
{
    internal static class Program
    {
        private static int _count;
        private static int Main(string[] args)
        {
            try
            {
                if (args != null && args.Any(x => x.Equals("--live-sources", StringComparison.OrdinalIgnoreCase))) { LiveSources(); return 0; }
                if (args != null && args.Any(x => x.Equals("--validate-catalog", StringComparison.OrdinalIgnoreCase))) { ValidateCatalog(); return 0; }
                if (args != null && args.Any(x => x.Equals("--audit-catalog-sources", StringComparison.OrdinalIgnoreCase))) return AuditCatalogSources();
                if (args != null && args.Any(x => x.Equals("--audit-catalog-packages", StringComparison.OrdinalIgnoreCase))) return AuditCatalogPackages();
                Versions(); Paths(); DiscoveryRealIniFormats(); ArchitectureAwareDiscovery(); FamilyIdentityAndConflict(); CatalogV2AndProviders(); CatalogScaleAndCache(); AuthorityResolution(); ScalableCheckRunner(); FileInfoPeVersionStrategy(); StrategyPriorityAndFallback(); ConfigurationDetection(); ConfigurationPrecedenceFinalization(); RedirectSections(); IniEncodingsAndPathExpansion(); CatalogAliases(); ApplicationMetadataAndUserAgent();
                Console.WriteLine("PASS " + _count + " tests"); return 0;
            }
            catch (Exception ex) { Console.Error.WriteLine("FAIL: " + ex.Message); return 1; }
        }
        private static void ValidateCatalog()
        {
            var catalog = new CatalogService(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".json")); var result = catalog.LoadWithDiagnostics();
            foreach (var entry in result.Entries) Console.WriteLine(entry.Id + " | " + entry.PluginType + " | " + String.Join(",", entry.Sources.Select(x => x.Provider)));
            foreach (var diagnostic in result.Diagnostics) Console.WriteLine(diagnostic.Severity + " | " + diagnostic.EntryId + " | " + diagnostic.Message);
            Console.WriteLine("Entries=" + result.Entries.Count + "; errors=" + result.Diagnostics.Count(x => x.Severity == CatalogDiagnosticSeverity.Error));
        }
        private static void LiveSources()
        {
            using (var http = new HttpService(ApplicationMetadata.Version))
            {
                var catalog = new CatalogService(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".json"));
                var providers = new IUpdateSourceProvider[] { new TotalCmdNetSourceProvider(http), new TotalCmdNetIndexProvider(http), new GhislerSourceProvider(http), new GhislerPluginsSourceProvider(http), new GitHubReleaseSourceProvider(http), new GenericHtmlSourceProvider(http) };
                var service = new UpdateService(catalog, providers);
                foreach (var id in new[] { "totalcmd", "fileinfo", "total7zip", "7zip-plugin", "imagine", "webdav", "sftp", "anytag", "glimpse-wlx", "glimpse-wcx" })
                {
                    var entry = catalog.FindById(id); var source = entry.Sources.OrderByDescending(x => x.Priority).First(); var provider = providers.First(x => x.CanHandle(source)); var cache = new SourceResponseCache(); var cachedProvider = provider as ICachedUpdateSourceProvider;
                    var query = (cachedProvider == null ? provider.QueryAsync(source, System.Threading.CancellationToken.None) : cachedProvider.QueryAsync(source, cache, System.Threading.CancellationToken.None)).GetAwaiter().GetResult(); var plugin = new InstalledPlugin { Identity = new PluginIdentity { Id = id }, Architecture = PluginArchitecture.X86 | PluginArchitecture.X64, LocalVersion = FileVersionProbe.Create("0.0", VersionSource.FileVersion, VersionConfidence.Exact) };
                    var result = service.CheckAsync(plugin, System.Threading.CancellationToken.None, cache).GetAwaiter().GetResult();
                    Console.WriteLine(id + "\n  provider.QueryAsync: " + query.Status + ", version=" + (query.Release == null ? "" : query.Release.Version.Raw) + ", packages=" + (query.Release == null ? 0 : query.Release.Packages.Count));
                    if (query.Release != null) foreach (var package in query.Release.Packages) Console.WriteLine("    " + package.FileName + " | " + package.Architecture + " | " + package.Url);
                    Console.WriteLine("  UpdateService.CheckAsync: " + result.State + ", selected=" + (result.DownloadUrl == null ? "none/ambiguous" : result.DownloadUrl.AbsoluteUri) + ", details=" + result.Details);
                }
            }
        }
        private static int AuditCatalogSources()
        {
            using (var http = new HttpService(ApplicationMetadata.Version))
            {
                var catalog = new CatalogService(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".json")); var entries = catalog.Load();
                var providers = new IUpdateSourceProvider[] { new TotalCmdNetSourceProvider(http), new TotalCmdNetIndexProvider(http), new GhislerSourceProvider(http), new GhislerPluginsSourceProvider(http), new GitHubReleaseSourceProvider(http), new GenericHtmlSourceProvider(http) };
                var failures = 0; var output = new System.Collections.Concurrent.ConcurrentBag<string>();
                using (var gate = new System.Threading.SemaphoreSlim(4))
                {
                    var tasks = entries.Select(async entry =>
                    {
                        var source = entry.Sources.OrderByDescending(x => x.Priority).First(); var provider = providers.FirstOrDefault(x => x.CanHandle(source)); SourceQueryResult result = null;
                        try { await gate.WaitAsync(); try { result = await provider.QueryAsync(source, System.Threading.CancellationToken.None); } finally { gate.Release(); } }
                        catch (Exception ex) { result = new SourceQueryResult { Status = SourceQueryStatus.Unavailable, Details = ex.Message }; }
                        if (result.Status != SourceQueryStatus.Success) System.Threading.Interlocked.Increment(ref failures);
                        output.Add(entry.Id + " | " + result.Status + " | " + (result.Release == null ? "" : result.Release.Version.Raw) + " | " + result.Details);
                    }).ToArray(); System.Threading.Tasks.Task.WaitAll(tasks);
                }
                foreach (var line in output.OrderBy(x => x, StringComparer.OrdinalIgnoreCase)) Console.WriteLine(line);
                Console.WriteLine("Source audit: entries=" + entries.Count + "; failures=" + failures); return failures == 0 ? 0 : 1;
            }
        }
        // Deliberately separate from --live-sources: this maintenance audit reads archive contents.
        private static int AuditCatalogPackages()
        {
            using (var http = new HttpService(ApplicationMetadata.Version))
            {
                var catalog = new CatalogService(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".json")); var entries = catalog.Load().Where(x => x.PluginType != PluginType.TotalCommander).ToList();
                var providers = new IUpdateSourceProvider[] { new TotalCmdNetSourceProvider(http), new TotalCmdNetIndexProvider(http), new GhislerSourceProvider(http), new GhislerPluginsSourceProvider(http), new GitHubReleaseSourceProvider(http), new GenericHtmlSourceProvider(http) };
                var failed = 0; var output = new System.Collections.Concurrent.ConcurrentBag<string>();
                using (var gate = new System.Threading.SemaphoreSlim(4))
                {
                    var tasks = entries.Select(async entry =>
                    {
                        var source = entry.Sources.OrderByDescending(x => x.Priority).First(); var provider = providers.First(x => x.CanHandle(source));
                        try
                        {
                            await gate.WaitAsync();
                            try
                            {
                                var release = await provider.QueryAsync(source, System.Threading.CancellationToken.None); var package = release.Release == null ? null : release.Release.Packages.FirstOrDefault(x => x.Architecture == RemotePackageArchitecture.Combined) ?? release.Release.Packages.FirstOrDefault();
                                if (package == null || package.Url == null) { System.Threading.Interlocked.Increment(ref failed); output.Add(entry.Id + " | FAIL | no package"); return; }
                                using (var response = await http.GetAsync(package.Url.AbsoluteUri, System.Net.Http.HttpCompletionOption.ResponseContentRead, System.Threading.CancellationToken.None))
                                {
                                    response.EnsureSuccessStatusCode(); var bytes = await response.Content.ReadAsByteArrayAsync();
                                    IList<string> actual;
                                    try { using (var archive = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read)) actual = archive.Entries.Select(x => Path.GetFileName(x.FullName)).Where(IsPluginBinary).ToList(); }
                                    catch (InvalidDataException) { actual = FindEmbeddedPluginNames(bytes); }
                                    var matchingAliases = entry.Aliases.Where(alias => actual.Any(x => x.Equals(alias, StringComparison.OrdinalIgnoreCase))).ToList();
                                    if (actual.Count == 0 || matchingAliases.Count == 0) { System.Threading.Interlocked.Increment(ref failed); output.Add(entry.Id + " | FAIL | actual=" + String.Join(",", actual) + " | aliases=" + String.Join(",", entry.Aliases)); }
                                    else output.Add(entry.Id + " | PASS | " + String.Join(",", actual));
                                }
                            }
                            finally { gate.Release(); }
                        }
                        catch (Exception ex) { System.Threading.Interlocked.Increment(ref failed); output.Add(entry.Id + " | FAIL | " + ex.GetType().Name); }
                    }).ToArray(); System.Threading.Tasks.Task.WaitAll(tasks);
                }
                foreach (var line in output.OrderBy(x => x, StringComparer.OrdinalIgnoreCase)) Console.WriteLine(line);
                Console.WriteLine("Package audit: entries=" + entries.Count + "; failures=" + failed); return failed == 0 ? 0 : 1;
            }
        }
        private static bool IsPluginBinary(string name)
        {
            return name.EndsWith(".wcx", StringComparison.OrdinalIgnoreCase) || name.EndsWith(".wlx", StringComparison.OrdinalIgnoreCase) || name.EndsWith(".wfx", StringComparison.OrdinalIgnoreCase) || name.EndsWith(".wdx", StringComparison.OrdinalIgnoreCase);
        }
        private static IList<string> FindEmbeddedPluginNames(byte[] bytes)
        {
            var text = Encoding.ASCII.GetString(bytes ?? new byte[0]);
            return System.Text.RegularExpressions.Regex.Matches(text, @"(?<![A-Za-z0-9_.-])[A-Za-z0-9_.-]+\.(?:wcx|wlx|wfx|wdx)(?![A-Za-z0-9_.-])", System.Text.RegularExpressions.RegexOptions.IgnoreCase)
                .Cast<System.Text.RegularExpressions.Match>().Select(x => x.Value).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
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
        private static void FamilyIdentityAndConflict()
        {
            var root = NewRoot();
            try
            {
                var first = Path.Combine(root, "first"); var second = Path.Combine(root, "second"); Directory.CreateDirectory(first); Directory.CreateDirectory(second);
                File.WriteAllBytes(Path.Combine(first, "fileinfo.wlx"), new byte[0]); File.WriteAllBytes(Path.Combine(second, "fileinfo.wlx"), new byte[0]);
                File.WriteAllBytes(Path.Combine(first, "shared.wcx"), new byte[0]);
                var ini = WriteIni(root, "families.ini", "[Configuration]\r\nInstallDir=" + root + "\r\n[ListerPlugins]\r\n0=first\\fileinfo.wlx\r\n1=first\\fileinfo.wlx\r\n2=second\\fileinfo.wlx\r\n[PackerPlugins]\r\n7z=1,first\\shared.wcx\r\nzip=1,first\\shared.wcx\r\nrar=1,first\\shared.wcx");
                var resolver = new TotalCommanderConfigurationResolver(); var discovered = new PluginDiscoveryService(resolver, new LocalVersionResolver(), new CatalogService(Path.Combine(root, "user.json"))).Discover(resolver.Resolve(ini));
                var fileInfos = discovered.Where(x => x.Identity.Id == "fileinfo").ToList();
                Assert(fileInfos.Count == 2, "same catalog id in different directories remains two families");
                Assert(fileInfos.Single(x => x.PrimaryPath.StartsWith(first, StringComparison.OrdinalIgnoreCase)).ConfigurationKeys.Count == 2, "same catalog id and family path merges into one family");
                Assert(discovered.Single(x => x.Type == PluginType.Wcx).ConfigurationKeys.Count == 3, "same WCX for 7z zip rar merges into one family");

                var equal = new InstalledPlugin { FileExists = true, Architecture = PluginArchitecture.X86 | PluginArchitecture.X64, LocalVersion = FileVersionProbe.Create("1.0", VersionSource.FileVersion, VersionConfidence.Exact), Binaries = new System.Collections.Generic.List<PluginBinary>
                {
                    new PluginBinary { Exists = true, Architecture = PluginArchitecture.X86, LocalVersion = FileVersionProbe.Create("1.0", VersionSource.FileVersion, VersionConfidence.Exact) },
                    new PluginBinary { Exists = true, Architecture = PluginArchitecture.X64, LocalVersion = FileVersionProbe.Create("1.0", VersionSource.FileVersion, VersionConfidence.Exact) }
                } };
                Assert(!equal.HasVersionConflict && equal.LocalVersion.ParsedValue.IsKnown, "equal x86 and x64 versions have no conflict");

                var conflict = new InstalledPlugin
                {
                    Identity = new PluginIdentity { Id = "fileinfo", Name = "FileInfo", Type = PluginType.Wlx }, Type = PluginType.Wlx, DisplayName = "FileInfo", PrimaryPath = "fileinfo.wlx", FileExists = true,
                    Architecture = PluginArchitecture.X86 | PluginArchitecture.X64, HasVersionConflict = true, LocalVersion = LocalVersion.Unknown,
                    Binaries = new System.Collections.Generic.List<PluginBinary>
                    {
                        new PluginBinary { Path = @"C:\plugins\fileinfo.wlx", Exists = true, Architecture = PluginArchitecture.X86, Variant = PluginBinaryVariant.Ansi, LocalVersion = FileVersionProbe.Create("1.0", VersionSource.FileVersion, VersionConfidence.Exact) },
                        new PluginBinary { Path = @"C:\plugins\fileinfo.wlx64", Exists = true, Architecture = PluginArchitecture.X64, Variant = PluginBinaryVariant.Native64, LocalVersion = FileVersionProbe.Create("2.0", VersionSource.FileVersion, VersionConfidence.Exact) }
                    }
                };
                var service = new UpdateService(new CatalogService(Path.Combine(root, "conflict-user.json")), new IUpdateSourceProvider[] { new FixedRemoteProvider("3.0") });
                var candidate = service.CheckAsync(conflict, System.Threading.CancellationToken.None).GetAwaiter().GetResult();
                Assert(candidate.State == UpdateState.LocalVersionConflict && candidate.AvailableVersion.Raw == "3.0", "conflict survives online check and still receives remote version");
                Assert(candidate.State != UpdateState.UpdateAvailable && candidate.State != UpdateState.UpToDate && candidate.State != UpdateState.DevelopmentVersion, "conflict never becomes normal update state");
                var row = new PluginRowViewModel(conflict); row.SetChecking(); row.Apply(candidate);
                Assert(row.Status == "Версии вариантов плагина различаются" && row.Candidate.State == UpdateState.LocalVersionConflict, "conflict survives UI Apply");
                Assert(!row.CanDownload, "conflict cannot be downloaded normally");
                Assert(row.Information.Contains("Внимание: версии вариантов плагина различаются") && row.Information.Contains("x86: 1.0") && row.Information.Contains("x64: 2.0") && row.Information.Contains(@"C:\plugins\fileinfo.wlx64"), "information contains individual binary versions");
            }
            finally { Directory.Delete(root, true); }
        }
        private static void CatalogV2AndProviders()
        {
            var root = NewRoot();
            try
            {
                var user = Path.Combine(root, "user.json"); var catalog = new CatalogService(user); var baseCatalog = catalog.LoadWithDiagnostics();
                var fileInfoEntry = baseCatalog.Entries.Single(x => x.Id == "fileinfo");
                Assert(baseCatalog.Entries.Count >= 10 && fileInfoEntry.Sources.Count >= 2 && fileInfoEntry.Sources.Any(x => x.AuthorityValue == SourceAuthority.OfficialTotalCommander) && fileInfoEntry.LocalVersionStrategy == "fileinfo", "Catalog v2 embedded deserialize");
                File.WriteAllText(user, "[{\"id\":\"fileinfo\",\"name\":\"FileInfo override\",\"type\":\"Wlx\",\"aliases\":[\"myfileinfo.wlx\"],\"localVersionStrategy\":\"fileinfo\",\"sources\":[{\"provider\":\"totalcmd.net\",\"id\":\"fileinfo\",\"priority\":200}]}]");
                Assert(catalog.Load().Single(x => x.Id == "fileinfo").Name == "FileInfo override", "user catalog completely overrides embedded entry");
                File.WriteAllText(user, "[{\"id\":\"fileinfo\",\"name\":\"\",\"type\":\"Wlx\",\"aliases\":[\"bad.wlx\"],\"sources\":[]}]");
                var invalidOverride = catalog.LoadWithDiagnostics(); Assert(invalidOverride.Entries.Single(x => x.Id == "fileinfo").Name == "FileInfo" && invalidOverride.Diagnostics.Count > 0, "invalid user override keeps embedded entry");
                File.WriteAllText(user, "{"); var malformed = catalog.LoadWithDiagnostics(); Assert(malformed.Entries.Any(x => x.Id == "fileinfo") && malformed.Diagnostics.Any(x => x.Message.IndexOf("Пользовательский каталог", StringComparison.OrdinalIgnoreCase) >= 0), "malformed user catalog keeps embedded catalog");
                File.WriteAllText(user, ""); var empty = catalog.LoadWithDiagnostics(); Assert(empty.Entries.Any(x => x.Id == "fileinfo") && empty.Diagnostics.Count > 0, "empty user catalog recovery");
                File.WriteAllText(user, "[{\"id\":\"alias-conflict\",\"name\":\"Alias\",\"type\":\"Wlx\",\"aliases\":[\"fileinfo.uwlx\"],\"sources\":[{\"provider\":\"totalcmd.net\",\"id\":\"a\",\"priority\":1}]},{\"id\":\"unknown\",\"name\":\"Unknown\",\"type\":\"Wlx\",\"aliases\":[\"unknown.wlx\"],\"sources\":[{\"provider\":\"unknown\",\"priority\":1}]},{\"id\":\"regex\",\"name\":\"Regex\",\"type\":\"Wlx\",\"aliases\":[\"regex.wlx\"],\"sources\":[{\"provider\":\"generic-html\",\"url\":\"https://example.test\",\"versionPattern\":\"[\",\"priority\":1}]},{\"id\":\"duplicate\",\"name\":\"Duplicate\",\"type\":\"Wlx\",\"aliases\":[\"dup.wlx\"],\"sources\":[{\"provider\":\"totalcmd.net\",\"id\":\"d\",\"priority\":1}]},{\"id\":\"duplicate\",\"name\":\"Duplicate 2\",\"type\":\"Wlx\",\"aliases\":[\"dup2.wlx\"],\"sources\":[{\"provider\":\"totalcmd.net\",\"id\":\"d\",\"priority\":1}]}]");
                var invalid = catalog.LoadWithDiagnostics(); Assert(invalid.Diagnostics.Count >= 4 && invalid.Entries.All(x => x.Id != "alias-conflict" && x.Id != "unknown" && x.Id != "regex") && invalid.Diagnostics.Any(x => x.Message.IndexOf("Повторяющийся", StringComparison.OrdinalIgnoreCase) >= 0), "conflicting alias unknown provider bad regex and duplicate id are rejected");
                File.WriteAllText(user, "[{\"id\":\"strategy\",\"name\":\"Strategy\",\"type\":\"Wlx\",\"aliases\":[\"strategy.wlx\"],\"localVersionStrategy\":\"typo\",\"sources\":[{\"provider\":\"totalcmd.net\",\"id\":\"s\",\"priority\":1}]}]");
                Assert(catalog.LoadWithDiagnostics().Diagnostics.Any(x => x.Message.IndexOf("localVersionStrategy", StringComparison.OrdinalIgnoreCase) >= 0), "unknown localVersionStrategy rejected");
                File.WriteAllText(user, "[{\"id\":\"html\",\"name\":\"Html\",\"type\":\"Wlx\",\"aliases\":[\"html.wlx\"],\"sources\":[{\"provider\":\"generic-html\",\"url\":\"https://example.test\",\"versionPattern\":\"v ([0-9.]+)\",\"downloadUrl\":\"https://example.test/a.zip\",\"priority\":1}]}]");
                Assert(catalog.Load().Any(x => x.Id == "html"), "valid GenericHtml downloadUrl");

                var totalCmdNet = TotalCmdNetSourceProvider.Parse("FileInfo", "<h1>FileInfo 2.23</h1><a href=\"https://x.test/FileInfo_x32.zip\">x32</a>");
                var total7zip = TotalCmdNetSourceProvider.Parse("Total7zip", "<h1>Total7zip 0.8.5.6</h1><p>7zip v9.20</p><a href=\"https://x.test/t7_x64.zip\">x64</a><a href=\"https://x.test/t7_both.zip\">x32 x64</a>");
                var slugDifferent = TotalCmdNetSourceProvider.Parse("7zip_plugin", "<h1>7Zip Plugin 0.7.6.6</h1>"); var titleFallback = TotalCmdNetSourceProvider.Parse("fileinfo", "<title>FileInfo 2.23 - Total Commander</title>");
                Assert(totalCmdNet.Status == SourceQueryStatus.Success && totalCmdNet.Release.Version.Raw == "2.23" && totalCmdNet.Release.Packages.Single().Architecture == RemotePackageArchitecture.X86, "TotalCmdNet real heading and x32 package parsing");
                Assert(total7zip.Release.Version.Raw == "0.8.5.6" && total7zip.Release.Packages.Any(x => x.Architecture == RemotePackageArchitecture.X64) && total7zip.Release.Packages.Any(x => x.Architecture == RemotePackageArchitecture.Combined), "TotalCmdNet heading ignores foreign body version");
                Assert(slugDifferent.Release.Version.Raw == "0.7.6.6" && titleFallback.Release.Version.Raw == "2.23", "TotalCmdNet slug independent heading and title fallback");
                var links = TotalCmdNetSourceProvider.ParsePackages("<a href=\"download.php?id=fileinfo\">Download x32</a><a href=\"/download.php?id=fileinfo\">Download x64</a><a href=\"https://x.test/mirror.zip\">Mirror</a><a href=\"https://x.test/forum\">Forum</a>");
                Assert(links.Count == 2 && links.All(x => x.Url.AbsoluteUri == "https://totalcmd.net/download.php?id=fileinfo"), "relative download links resolve and navigation links are ignored");
                var ghisler = GhislerSourceProvider.Parse("Download version 11.50 of Total Commander <a href=\"https://g.test/tc_x32.exe\">x32</a><a href=\"https://g.test/tc_x64.exe\">x64</a><a href=\"https://g.test/tc_both.exe\">x32 x64</a>"); Assert(ghisler.Status == SourceQueryStatus.Success && ghisler.Release.Packages.Count == 3, "Ghisler Total Commander package parsing");
                var githubJson = "[{\"tag_name\":\"v2.0\",\"prerelease\":true,\"draft\":false,\"html_url\":\"https://github.test/pre\",\"assets\":[]},{\"tag_name\":\"v1.5\",\"prerelease\":false,\"draft\":false,\"html_url\":\"https://github.test/stable\",\"assets\":[{\"name\":\"plugin-win64.zip\",\"browser_download_url\":\"https://github.test/x64\"},{\"name\":\"plugin-win32.zip\",\"browser_download_url\":\"https://github.test/x86\"}]}]";
                var gitStable = GitHubReleaseSourceProvider.Parse(githubJson, new CatalogSource { IncludePrerelease = false, AssetPattern = "win64" });
                var gitPre = GitHubReleaseSourceProvider.Parse(githubJson, new CatalogSource { IncludePrerelease = true });
                Assert(gitStable.Status == SourceQueryStatus.Success && gitStable.Release.Version.Raw == "1.5" && gitStable.Release.Packages.Count == 1, "GitHub stable and assetPattern");
                Assert(gitPre.Status == SourceQueryStatus.Success && gitPre.Release.Version.Raw == "2.0", "GitHub prerelease allowed");
                Assert(GitHubReleaseSourceProvider.DetectArchitecture("plugin-win64.zip") == RemotePackageArchitecture.X64 && GitHubReleaseSourceProvider.DetectArchitecture("plugin-amd64.zip") == RemotePackageArchitecture.X64 && GitHubReleaseSourceProvider.DetectArchitecture("plugin-win32.zip") == RemotePackageArchitecture.X86 && GitHubReleaseSourceProvider.DetectArchitecture("plugin-32bit.zip") == RemotePackageArchitecture.X86 && GitHubReleaseSourceProvider.DetectArchitecture("plugin-1.64.zip") == RemotePackageArchitecture.Unknown && GitHubReleaseSourceProvider.DetectArchitecture("plugin-arm64.zip") == RemotePackageArchitecture.Unknown, "GitHub token architecture detection");
                Assert(baseCatalog.Entries.Single(x => x.Id == "webdav").Aliases.Count == 3 && baseCatalog.Entries.First(x => x.Id == "sftp").Sources[0].Id == "sftp4tc" && baseCatalog.Entries.First(x => x.Id == "anytag").Sources[0].Id == "wdx_anytag", "embedded aliases and source IDs");
                Assert(catalog.FindByAlias("davplug.wfx").Id == "webdav" && catalog.FindByAlias("davplug.uwfx").Id == "webdav" && catalog.FindByAlias("davplug.wfx64").Id == "webdav", "WebDAV companion aliases");

                File.WriteAllText(user, "[{\"id\":\"fileinfo\",\"name\":\"FileInfo\",\"type\":\"Wlx\",\"aliases\":[\"fileinfo.wlx\"],\"localVersionStrategy\":\"fileinfo\",\"sources\":[{\"provider\":\"generic-html\",\"url\":\"https://example.test/one\",\"versionPattern\":\"([0-9.]+)\",\"priority\":100},{\"provider\":\"generic-html\",\"url\":\"https://example.test/two\",\"versionPattern\":\"([0-9.]+)\",\"priority\":90}]}]"); catalog = new CatalogService(user);
                var plugin = new InstalledPlugin { Identity = new PluginIdentity { Id = "fileinfo", Type = PluginType.Wlx }, PrimaryPath = "unrelated-name.wlx", FileExists = true, Architecture = PluginArchitecture.X86, LocalVersion = FileVersionProbe.Create("1.0", VersionSource.FileVersion, VersionConfidence.Exact) };
                var fallback = new ScriptedProvider(new SourceQueryResult { Status = SourceQueryStatus.Unavailable, Details = "offline" }, new SourceQueryResult { Status = SourceQueryStatus.Success, Release = Release("2.0", RemotePackageArchitecture.Combined) });
                var update = new UpdateService(catalog, new IUpdateSourceProvider[] { fallback }).CheckAsync(plugin, System.Threading.CancellationToken.None).GetAwaiter().GetResult();
                Assert(update.State == UpdateState.UpdateAvailable && update.SourceName == "script-2", "source #1 failure falls back to #2 and identity lookup is used");
                var allFail = new UpdateService(catalog, new IUpdateSourceProvider[] { new ScriptedProvider(new SourceQueryResult { Status = SourceQueryStatus.NotFound, Details = "missing" }) }).CheckAsync(plugin, System.Threading.CancellationToken.None).GetAwaiter().GetResult();
                Assert(allFail.State == UpdateState.SourceUnavailable, "all sources fail gives SourceUnavailable");
                var availableRow = new PluginRowViewModel(plugin); availableRow.Apply(update); Assert(availableRow.CanDownload, "UpdateAvailable with package is downloadable");
                foreach (var state in new[] { UpdateState.UpToDate, UpdateState.DevelopmentVersion, UpdateState.VersionComparisonUnknown, UpdateState.LocalVersionConflict }) { var row = new PluginRowViewModel(plugin); row.Apply(new UpdateCandidate { Plugin = plugin, State = state, DownloadUrl = new Uri("https://example.test/file") }); Assert(!row.CanDownload, state + " is not downloadable"); }
                var dual = new InstalledPlugin { Identity = plugin.Identity, PrimaryPath = plugin.PrimaryPath, FileExists = true, Architecture = PluginArchitecture.X86 | PluginArchitecture.X64, LocalVersion = plugin.LocalVersion };
                var split = new UpdateService(catalog, new IUpdateSourceProvider[] { new ScriptedProvider(new SourceQueryResult { Status = SourceQueryStatus.Success, Release = Release("2.0", RemotePackageArchitecture.X86, RemotePackageArchitecture.X64) }) }).CheckAsync(dual, System.Threading.CancellationToken.None).GetAwaiter().GetResult();
                Assert(split.State == UpdateState.UpdateAvailable && split.DownloadUrl == null, "separate x86 and x64 packages are ambiguous");
                var combined = new UpdateService(catalog, new IUpdateSourceProvider[] { new ScriptedProvider(new SourceQueryResult { Status = SourceQueryStatus.Success, Release = Release("2.0", RemotePackageArchitecture.X86, RemotePackageArchitecture.X64, RemotePackageArchitecture.Combined) }) }).CheckAsync(dual, System.Threading.CancellationToken.None).GetAwaiter().GetResult();
                Assert(combined.DownloadUrl != null && combined.DownloadUrl.AbsoluteUri.EndsWith("package2"), "dual Total Commander selects combined package");
                foreach (var packageSet in new[] { new[] { RemotePackageArchitecture.X86 }, new[] { RemotePackageArchitecture.X64 }, new[] { RemotePackageArchitecture.Unknown }, new[] { RemotePackageArchitecture.X86, RemotePackageArchitecture.X64 }, new[] { RemotePackageArchitecture.X86, RemotePackageArchitecture.Unknown }, new[] { RemotePackageArchitecture.X64, RemotePackageArchitecture.Unknown } })
                {
                    var candidate = new UpdateService(catalog, new IUpdateSourceProvider[] { new ScriptedProvider(new SourceQueryResult { Status = SourceQueryStatus.Success, Release = Release("2.0", packageSet) }) }).CheckAsync(dual, System.Threading.CancellationToken.None).GetAwaiter().GetResult();
                    Assert(candidate.DownloadUrl == null, "dual architecture never selects non-Combined package");
                }
                var x86Plugin = new InstalledPlugin { Identity = plugin.Identity, PrimaryPath = plugin.PrimaryPath, FileExists = true, Architecture = PluginArchitecture.X86, LocalVersion = plugin.LocalVersion };
                var x64Plugin = new InstalledPlugin { Identity = plugin.Identity, PrimaryPath = plugin.PrimaryPath, FileExists = true, Architecture = PluginArchitecture.X64, LocalVersion = plugin.LocalVersion };
                Assert(new UpdateService(catalog, new IUpdateSourceProvider[] { new ScriptedProvider(new SourceQueryResult { Status = SourceQueryStatus.Success, Release = Release("2.0", RemotePackageArchitecture.X86, RemotePackageArchitecture.Unknown) }) }).CheckAsync(x86Plugin, System.Threading.CancellationToken.None).GetAwaiter().GetResult().DownloadUrl.AbsoluteUri.EndsWith("package0"), "x86 exact package beats Unknown");
                Assert(new UpdateService(catalog, new IUpdateSourceProvider[] { new ScriptedProvider(new SourceQueryResult { Status = SourceQueryStatus.Success, Release = Release("2.0", RemotePackageArchitecture.X64, RemotePackageArchitecture.Unknown) }) }).CheckAsync(x64Plugin, System.Threading.CancellationToken.None).GetAwaiter().GetResult().DownloadUrl.AbsoluteUri.EndsWith("package0"), "x64 exact package beats Unknown");
            }
            finally { Directory.Delete(root, true); }
        }
        private static RemoteRelease Release(string version, params RemotePackageArchitecture[] architectures)
        {
            return new RemoteRelease { VersionText = version, Version = VersionValue.Parse(version), SourceUrl = new Uri("https://example.test/source"), Packages = architectures.Select((x, i) => new RemotePackage { Architecture = x, FileName = "package" + i + ".zip", Url = new Uri("https://example.test/package" + i) }).ToList() };
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
            var fileInfo = resolver.Resolve("not-used", new PluginIdentity { Id = "fileinfo", Type = PluginType.Wlx, LocalVersionStrategy = "fileinfo" });
            Assert(fileInfo.ParsedValue.Raw == "2.23" && fileInfo.Source == VersionSource.CustomRule, "FileInfo strategy precedes generic FileVersion");

            var fallbackResolver = new LocalVersionResolver(new IPluginSpecificVersionStrategy[] { new FixedFileInfoStrategy(LocalVersion.Unknown) }, new IVersionProbe[] { new FixedProbe(generic) });
            Assert(fallbackResolver.Resolve("not-used", new PluginIdentity { Id = "fileinfo", Type = PluginType.Wlx, LocalVersionStrategy = "fileinfo" }).Source == VersionSource.FileVersion, "Unknown strategy falls back to FileVersion");
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
        private static void CatalogScaleAndCache()
        {
            var root = NewRoot();
            try
            {
                var catalog = new CatalogService(Path.Combine(root, "user.json")); var loaded = catalog.LoadWithDiagnostics();
                Assert(loaded.Diagnostics.Count(x => x.Severity == CatalogDiagnosticSeverity.Error) == 0, "embedded catalog has no validation errors");
                Assert(loaded.Entries.Count >= 50, "catalog has at least 50 entries");
                Assert(loaded.Entries.Count(x => x.PluginType == PluginType.Wcx) >= 15 && loaded.Entries.Count(x => x.PluginType == PluginType.Wlx) >= 15 && loaded.Entries.Count(x => x.PluginType == PluginType.Wfx) >= 10 && loaded.Entries.Count(x => x.PluginType == PluginType.Wdx) >= 10, "catalog type distribution");
                var cache = new SourceResponseCache(); var provider = new CachedTestProvider(); var service = new UpdateService(catalog, new IUpdateSourceProvider[] { provider });
                var first = new InstalledPlugin { Identity = new PluginIdentity { Id = "glimpse-wlx" }, LocalVersion = FileVersionProbe.Create("0.1", VersionSource.FileVersion, VersionConfidence.Exact) };
                var second = new InstalledPlugin { Identity = new PluginIdentity { Id = "glimpse-wcx" }, LocalVersion = FileVersionProbe.Create("0.1", VersionSource.FileVersion, VersionConfidence.Exact) };
                System.Threading.Tasks.Task.WaitAll(service.CheckAsync(first, System.Threading.CancellationToken.None, cache), service.CheckAsync(second, System.Threading.CancellationToken.None, cache));
                Assert(provider.Fetches == 1 && provider.Filters == 2, "same GitHub source reuses raw response and filters each entry");
            }
            finally { Directory.Delete(root, true); }
        }
        private static void AuthorityResolution()
        {
            var resolver = new SourceAuthorityResolver();
            Func<SourceAuthority, string, RemoteVersionObservation> observation = (authority, version) => new RemoteVersionObservation { Authority = authority, Purpose = SourcePurpose.MetadataAndDownload, Status = SourceQueryStatus.Success, Release = Release(version, RemotePackageArchitecture.Combined) };
            var official = resolver.Resolve(new[] { observation(SourceAuthority.OfficialAuthor, "2.0"), observation(SourceAuthority.CommunityCatalog, "1.9") }); Assert(official.Canonical.Release.Version.Raw == "2.0" && official.HasDisagreement, "official author wins community");
            var disagreement = resolver.Resolve(new[] { observation(SourceAuthority.OfficialAuthor, "2.0"), observation(SourceAuthority.CommunityCatalog, "2.1") }); Assert(disagreement.Canonical.Release.Version.Raw == "2.0" && disagreement.HasDisagreement, "lower authority newer version disagrees");
            var sameTier = resolver.Resolve(new[] { observation(SourceAuthority.OfficialTotalCommander, "2.0"), observation(SourceAuthority.OfficialTotalCommander, "2.1") }); Assert(sameTier.Canonical.Release.Version.Raw == "2.1" && sameTier.HasDisagreement, "same authority chooses newest and marks disagreement");
            var ghisler = GhislerPluginsSourceProvider.Parse("Diskdir", "<a>Diskdir</a><td>1.3</td>"); Assert(ghisler.Status == SourceQueryStatus.Success && ghisler.Release.Version.Raw == "1.3", "Ghisler plugin fixture parses version");
            var index = TotalCmdNetIndexProvider.Parse("dirsizecalc", "dirsizecalc|DirSizeCalc|2.22|19.08.2015|content|x32+x64||\r\n"); Assert(index.Status == SourceQueryStatus.Success && index.Release.Version.Raw == "2.22", "legacy totalcmd index fixture parses version");
        }
        private static void ScalableCheckRunner()
        {
            var root = NewRoot();
            try
            {
                var catalog = new CatalogService(Path.Combine(root, "user.json")); var provider = new MeasuredProvider(40, 4); var runner = new UpdateCheckRunner(new UpdateService(catalog, new IUpdateSourceProvider[] { provider }));
                var plugins = Enumerable.Range(0, 20).Select(x => new InstalledPlugin { Identity = new PluginIdentity { Id = "total7zip" }, LocalVersion = FileVersionProbe.Create("0.1", VersionSource.FileVersion, VersionConfidence.Exact) }).ToList();
                var applied = 0; var result = runner.RunAsync(plugins, (p, c) => System.Threading.Interlocked.Increment(ref applied), (n, total) => { }, System.Threading.CancellationToken.None).GetAwaiter().GetResult();
                Assert(provider.Peak > 1 && provider.Peak <= 4, "bounded runner has parallelism up to four");
                Assert(applied == 20 && result.Completed == 20, "one failing provider does not stop remaining checks");
                var slow = new MeasuredProvider(500, -1); runner = new UpdateCheckRunner(new UpdateService(catalog, new IUpdateSourceProvider[] { slow })); var cancelledApplied = 0; var cts = new System.Threading.CancellationTokenSource();
                var task = runner.RunAsync(plugins, (p, c) => System.Threading.Interlocked.Increment(ref cancelledApplied), (n, total) => { }, cts.Token); System.Threading.Thread.Sleep(70); cts.Cancel(); var cancelled = task.GetAwaiter().GetResult();
                Assert(cancelled.WasCancelled && cancelledApplied < 20, "cancellation stops unfinished checks without error candidates");
                var first = new System.Threading.CancellationTokenSource(); var firstRun = runner.RunAsync(plugins, (p, c) => { }, (n, total) => { }, first.Token); System.Threading.Thread.Sleep(50); first.Cancel(); firstRun.GetAwaiter().GetResult();
                var secondApplied = 0; var second = runner.RunAsync(plugins.Take(2), (p, c) => System.Threading.Interlocked.Increment(ref secondApplied), (n, total) => { }, System.Threading.CancellationToken.None).GetAwaiter().GetResult();
                Assert(secondApplied == 2 && !second.WasCancelled, "second check run completes after first cancellation");
            }
            finally { Directory.Delete(root, true); }
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
            public string Name { get { return "fileinfo"; } }
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
        private sealed class FixedRemoteProvider : IUpdateSourceProvider
        {
            private readonly VersionValue _version;
            public FixedRemoteProvider(string version) { _version = VersionValue.Parse(version); }
            public string Name { get { return "test"; } }
            public bool CanHandle(CatalogSource source) { return true; }
            public System.Threading.Tasks.Task<SourceQueryResult> QueryAsync(CatalogSource source, System.Threading.CancellationToken cancellationToken)
            {
                return System.Threading.Tasks.Task.FromResult(new SourceQueryResult { Status = SourceQueryStatus.Success, Release = new RemoteRelease { VersionText = _version.Raw, Version = _version, SourceUrl = new Uri("https://example.test/source"), Packages = new System.Collections.Generic.List<RemotePackage> { new RemotePackage { Architecture = RemotePackageArchitecture.Combined, Url = new Uri("https://example.test/download") } } } });
            }
        }
        private sealed class ScriptedProvider : IUpdateSourceProvider
        {
            private readonly System.Collections.Generic.Queue<SourceQueryResult> _results;
            private int _calls;
            public ScriptedProvider(params SourceQueryResult[] results) { _results = new System.Collections.Generic.Queue<SourceQueryResult>(results); }
            public string Name { get { return "script-" + _calls; } }
            public bool CanHandle(CatalogSource source) { return true; }
            public System.Threading.Tasks.Task<SourceQueryResult> QueryAsync(CatalogSource source, System.Threading.CancellationToken cancellationToken)
            {
                _calls++; return System.Threading.Tasks.Task.FromResult(_results.Count == 0 ? new SourceQueryResult { Status = SourceQueryStatus.Unavailable, Details = "no scripted result" } : _results.Dequeue());
            }
        }
        private sealed class CachedTestProvider : ICachedUpdateSourceProvider
        {
            public int Fetches; public int Filters;
            public string Name { get { return "cached-test"; } }
            public bool CanHandle(CatalogSource source) { return source != null && source.Provider == "github"; }
            public System.Threading.Tasks.Task<SourceQueryResult> QueryAsync(CatalogSource source, System.Threading.CancellationToken cancellationToken) { return QueryAsync(source, null, cancellationToken); }
            public async System.Threading.Tasks.Task<SourceQueryResult> QueryAsync(CatalogSource source, SourceResponseCache cache, System.Threading.CancellationToken cancellationToken)
            {
                var raw = cache == null ? "raw" : await cache.GetOrAdd("github:" + source.Repository, () => { Fetches++; return System.Threading.Tasks.Task.FromResult("raw"); });
                Filters++; return new SourceQueryResult { Status = SourceQueryStatus.Success, Release = new RemoteRelease { VersionText = "1.0", Version = VersionValue.Parse("1.0"), SourceUrl = new Uri("https://example.test/" + raw) } };
            }
        }
        private sealed class MeasuredProvider : IUpdateSourceProvider
        {
            private readonly int _delay; private readonly int _throwAt; private int _calls; private int _active; public int Peak;
            public MeasuredProvider(int delay, int throwAt) { _delay = delay; _throwAt = throwAt; }
            public string Name { get { return "measured"; } } public bool CanHandle(CatalogSource source) { return true; }
            public async System.Threading.Tasks.Task<SourceQueryResult> QueryAsync(CatalogSource source, System.Threading.CancellationToken token)
            {
                var active = System.Threading.Interlocked.Increment(ref _active); int peak; while ((peak = Peak) < active && System.Threading.Interlocked.CompareExchange(ref Peak, active, peak) != peak) { }
                var call = System.Threading.Interlocked.Increment(ref _calls);
                try { await System.Threading.Tasks.Task.Delay(_delay, token); if (call == _throwAt) throw new InvalidOperationException("expected"); return new SourceQueryResult { Status = SourceQueryStatus.Success, Release = new RemoteRelease { VersionText = "1.0", Version = VersionValue.Parse("1.0"), SourceUrl = new Uri("https://example.test/"), Packages = new System.Collections.Generic.List<RemotePackage>() } }; }
                finally { System.Threading.Interlocked.Decrement(ref _active); }
            }
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
