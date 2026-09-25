using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Diagnostics;
using TotalUpdater.Next.Catalog;
using TotalUpdater.Next.Core;
using TotalUpdater.Next.Core.Versions;
using TotalUpdater.Next.Infrastructure;
using TotalUpdater.Next.Sources;
using TotalUpdater.Next.TotalCommander;
using TotalUpdater.Next.UI;
using TotalUpdater.Next.Settings;

namespace TotalUpdater.Next.Tests
{
    internal static partial class Program
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
                if (args != null && args.Any(x => x.Equals("--audit-catalog-version-fidelity", StringComparison.OrdinalIgnoreCase))) return AuditCatalogVersionFidelity();
                if (args != null && args.Any(x => x.Equals("--diagnose-sources", StringComparison.OrdinalIgnoreCase))) return DiagnoseSources();
                if (args != null && args.Any(x => x.Equals("--validate-harvest-evidence", StringComparison.OrdinalIgnoreCase))) return ValidateHarvestEvidence();
                if (args != null && args.Any(x => x.Equals("--harvest-full-catalog", StringComparison.OrdinalIgnoreCase))) return RunMaintenanceScript("Harvest-FullSourceCatalog.ps1");
                if (args != null && args.Any(x => x.Equals("--audit-catalog-aliases", StringComparison.OrdinalIgnoreCase))) return AuditCatalogAliases();
                if (args != null && args.Any(x => x.Equals("--audit-full-catalog", StringComparison.OrdinalIgnoreCase))) return AuditFullCatalog();
                if (args != null && args.Any(x => x.Equals("--audit-wcx-registration", StringComparison.OrdinalIgnoreCase))) return AuditWcxRegistration(args);
                if (args != null && args.Any(x => x.Equals("--validate-wcx-registration-evidence", StringComparison.OrdinalIgnoreCase))) return ValidateWcxRegistrationEvidence();
                if (args != null && args.Any(x => x.Equals("--harvest-wcx-registration", StringComparison.OrdinalIgnoreCase))) return HarvestWcxRegistration(args);
                if (args != null && args.Any(x => x.Equals("--merge-wcx-registration-evidence", StringComparison.OrdinalIgnoreCase))) return MergeWcxRegistrationEvidence(args);
                if (args != null && args.Any(x => x.Equals("--audit-installed-coverage", StringComparison.OrdinalIgnoreCase))) return AuditInstalledCoverage(args);
                if (args != null && args.Any(x => x.Equals("--audit-installed-version-drift", StringComparison.OrdinalIgnoreCase))) return AuditInstalledVersionDrift(args);
                var groups = TestGroups();
                if (args != null && args.Any(x => x.Equals("--list-test-groups", StringComparison.OrdinalIgnoreCase))) { foreach (var name in groups.Keys) { Console.WriteLine(name); Console.Out.Flush(); } return 0; }
                var runIndex = args == null ? -1 : Array.FindIndex(args, x => x.Equals("--run-test-group", StringComparison.OrdinalIgnoreCase));
                if (runIndex >= 0) { if (runIndex + 1 >= args.Length || !groups.TryGetValue(args[runIndex + 1], out var selected)) throw new ArgumentException("Unknown test group."); RunGroup(args[runIndex + 1], selected); Console.WriteLine("PASS " + _count + " tests"); return 0; }
                foreach (var group in groups) RunGroup(group.Key, group.Value);
                Console.WriteLine("PASS " + _count + " tests"); return 0;
            }
            catch (Exception ex) { Console.Error.WriteLine("FAIL: " + ex); return 1; }
        }
        private static IDictionary<string, Action> TestGroups()
        {
            return new Dictionary<string, Action>(StringComparer.OrdinalIgnoreCase) {
                { "Versions", Versions }, { "Paths", Paths }, { "Discovery", () => { DiscoveryRealIniFormats(); ArchitectureAwareDiscovery(); FamilyIdentityAndConflict(); } },
                { "Catalog", () => { CatalogV2AndProviders(); CatalogScaleAndCache(); CatalogAliases(); CatalogCoverageMatching(); } },
                { "Sources", () => { RunStep("Sources.AuthorityResolution", AuthorityResolution); RunStep("Sources.DownloadProvenance", DownloadProvenance); RunStep("Sources.AuthorityRuntimeFinalization", AuthorityRuntimeFinalization); RunStep("Sources.LazySourcesAndCache", LazySourcesAndCache); RunStep("Sources.SourceInputHardening", SourceInputHardening); RunStep("Sources.ScalableCheckRunner", ScalableCheckRunner); RunStep("Sources.SourceFidelityRegressions", SourceFidelityRegressions); RunStep("Sources.PersistentSourceCacheContracts", PersistentSourceCacheContracts); } },
                { "Configuration", () => { FileInfoPeVersionStrategy(); StrategyPriorityAndFallback(); ConfigurationDetection(); ConfigurationPrecedenceFinalization(); RedirectSections(); IniEncodingsAndPathExpansion(); } },
                { "Settings", SettingsContracts }, { "ApplicationMetadata", ApplicationMetadataAndUserAgent },
                { "WcxProbe", () => WcxProbeTests.Run(Assert) },
                { "InstallationTests", () => InstallationTests.Run(Assert) }, { "RecoveryTests", () => RecoveryTests.Run(Assert) }, { "NewPluginInstallationTests", () => NewPluginInstallationTests.Run(Assert) }
            };
        }
        private static void RunGroup(string name, Action action)
        {
            Console.WriteLine("[RUN ] " + name); Console.Out.Flush(); var stopwatch = Stopwatch.StartNew(); action(); stopwatch.Stop(); Console.WriteLine("[PASS] " + name + " (" + stopwatch.ElapsedMilliseconds + " ms)"); Console.Out.Flush();
        }
        private static void RunStep(string name, Action action) { RunGroup(name, action); }
        private static void ValidateCatalog()
        {
            var catalog = new CatalogService(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".json")); var result = catalog.LoadWithDiagnostics();
            foreach (var entry in result.Entries) Console.WriteLine(entry.Id + " | " + entry.PluginType + " | " + String.Join(",", entry.Sources.Select(x => x.Provider + " | " + x.AuthorityValue + " | " + x.PurposeValue + " | valid")));
            foreach (var diagnostic in result.Diagnostics) Console.WriteLine(diagnostic.Severity + " | " + diagnostic.EntryId + " | " + diagnostic.Message);
            Console.WriteLine("Entries=" + result.Entries.Count + "; errors=" + result.Diagnostics.Count(x => x.Severity == CatalogDiagnosticSeverity.Error));
        }

        private static int DiagnoseSources()
        {
            using (var http = new HttpService(ApplicationMetadata.Version))
            {
                var results = new SourceDiagnostics(http).CheckAsync(System.Threading.CancellationToken.None).GetAwaiter().GetResult();
                foreach (var item in results) Console.WriteLine(item.Name + " | " + item.Status + " | " + item.ResponseTime + (String.IsNullOrWhiteSpace(item.Details) ? "" : " | " + item.Details));
                return results.All(x => x.IsAvailable) ? 0 : 1;
            }
        }

        private static int RunMaintenanceScript(string scriptName, string[] commandArgs = null)
        {
            var script = FindMaintenanceScript(scriptName);
            if (script != null)
            {
                var start = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "powershell.exe", Arguments = "-NoProfile -ExecutionPolicy Bypass -File \"" + script + "\" " + String.Join(" ", (commandArgs ?? new string[0]).Where(x => !String.Equals(x, "--harvest-wcx-registration", StringComparison.OrdinalIgnoreCase) && !String.Equals(x, "--merge-wcx-registration-evidence", StringComparison.OrdinalIgnoreCase)).Select(x => String.Equals(x, "--input", StringComparison.OrdinalIgnoreCase) ? "-InputPath" : String.Equals(x, "--dry-run", StringComparison.OrdinalIgnoreCase) ? "-DryRun" : "\"" + x.Replace("\"", "\\\"") + "\"")),
                    UseShellExecute = false
                };
                using (var process = System.Diagnostics.Process.Start(start)) { process.WaitForExit(); return process.ExitCode; }
            }
            Console.Error.WriteLine("Не найден maintenance-скрипт: tools\\" + scriptName);
            return 1;
        }

        internal static string FindMaintenanceScript(string scriptName)
        {
            foreach (var start in new[] { Environment.CurrentDirectory, AppDomain.CurrentDomain.BaseDirectory }.Where(x => !String.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                var directory = new DirectoryInfo(start);
                while (directory != null)
                {
                    var script = Path.Combine(directory.FullName, "tools", scriptName);
                    if (File.Exists(script)) return script;
                    directory = directory.Parent;
                }
            }
            return null;
        }
        private static int AuditCatalogAliases()
        {
            var catalog = new CatalogService(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".json"));
            var issues = CatalogAliasAudit.Audit(catalog.Load());
            foreach (var issue in issues) Console.WriteLine(issue.Kind + " | " + issue.Alias + " | " + issue.Entries);
            Console.WriteLine("Alias audit: entries=" + catalog.Load().Count + "; issues=" + issues.Count);
            return issues.Count == 0 ? 0 : 1;
        }
        private static int AuditInstalledCoverage(string[] args)
        {
            var explicitIni = args.FirstOrDefault(x => x.StartsWith("--ini=", StringComparison.OrdinalIgnoreCase));
            var ini = explicitIni == null ? null : explicitIni.Substring(6).Trim('"');
            var config = new TotalCommanderConfigurationResolver().Resolve(ini);
            if (config == null || !File.Exists(config.IniPath)) { Console.Error.WriteLine("INI не найден: " + (ini ?? "auto")); return 1; }
            var catalog = new CatalogService(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".json"));
            var plugins = new PluginDiscoveryService(new TotalCommanderConfigurationResolver(), new LocalVersionResolver(), catalog).Discover(config);
            if (!args.Any(x => x.Equals("--offline", StringComparison.OrdinalIgnoreCase)))
            {
                using (var http = new HttpService(ApplicationMetadata.Version))
                {
                    var remote = new RemoteCatalogLookup(http); var cache = new SourceResponseCache();
                    foreach (var plugin in plugins.Where(x => x.CatalogMatchKind == CatalogMatchKind.NotFound))
                    {
                        var match = remote.FindAsync(plugin, cache, System.Threading.CancellationToken.None).GetAwaiter().GetResult();
                        plugin.CatalogMatchKind = match.Kind;
                    }
                }
            }
            var counts = Enum.GetValues(typeof(CatalogMatchKind)).Cast<CatalogMatchKind>().ToDictionary(x => x, x => plugins.Count(p => p.CatalogMatchKind == x));
            var covered = counts[CatalogMatchKind.Exact] + counts[CatalogMatchKind.Alias] + counts[CatalogMatchKind.RemoteExact];
            Console.WriteLine("InstallDirectory: " + config.InstallDirectory);
            Console.WriteLine("Existing binaries: " + plugins.Count(x => x.FileExists));
            Console.WriteLine("Installed: " + plugins.Count);
            foreach (var kind in Enum.GetValues(typeof(CatalogMatchKind)).Cast<CatalogMatchKind>()) Console.WriteLine(kind + ": " + counts[kind]);
            Console.WriteLine("Coverage: " + (plugins.Count == 0 ? 0 : 100.0 * covered / plugins.Count).ToString("F1", System.Globalization.CultureInfo.InvariantCulture) + "%");
            Console.WriteLine("Missing:"); foreach (var p in plugins.Where(x => x.CatalogMatchKind == CatalogMatchKind.NotFound)) Console.WriteLine("- " + p.Type + " | " + p.DisplayName + " | " + Path.GetFileName(p.PrimaryPath));
            Console.WriteLine("Ambiguous:"); foreach (var p in plugins.Where(x => x.CatalogMatchKind == CatalogMatchKind.Ambiguous)) Console.WriteLine("- " + p.Type + " | " + p.DisplayName + " | " + Path.GetFileName(p.PrimaryPath));
            return 0;
        }
        private static int AuditInstalledVersionDrift(string[] args)
        {
            var explicitIni = args.FirstOrDefault(x => x.StartsWith("--ini=", StringComparison.OrdinalIgnoreCase));
            var ini = explicitIni == null ? null : explicitIni.Substring(6).Trim('"');
            var config = new TotalCommanderConfigurationResolver().Resolve(ini);
            if (config == null || !File.Exists(config.IniPath)) return 1;
            var catalog = new CatalogService(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".json"));
            var plugins = new PluginDiscoveryService(new TotalCommanderConfigurationResolver(), new LocalVersionResolver(), catalog).Discover(config)
                .Where(x => x.CatalogMatchKind != CatalogMatchKind.NotFound && x.LocalVersion.ParsedValue.IsKnown).ToList();
            using (var http = new HttpService(ApplicationMetadata.Version))
            {
                var providers = new IUpdateSourceProvider[] { new TotalCmdNetSourceProvider(http), new TotalCmdNetIndexProvider(http), new GhislerSourceProvider(http), new GhislerPluginsSourceProvider(http), new GitHubReleaseSourceProvider(http), new GenericHtmlSourceProvider(http) };
                var candidates = new System.Collections.Concurrent.ConcurrentBag<UpdateCandidate>();
                new UpdateCheckRunner(new UpdateService(catalog, providers)).RunAsync(plugins, (p, c) => candidates.Add(c), (a, b) => { }, System.Threading.CancellationToken.None).GetAwaiter().GetResult();
                foreach (var c in candidates.Where(x => x.State == UpdateState.DevelopmentVersion || x.State == UpdateState.SourceOutdated).OrderBy(x => x.Plugin.DisplayName))
                    Console.WriteLine(c.Plugin.Identity.Id + " | " + c.State + " | local=" + c.Plugin.LocalVersion.RawValue + " | remote=" + c.AvailableVersion.Raw + " | " + c.SourceName);
                Console.WriteLine("Checked=" + candidates.Count + "; development=" + candidates.Count(x => x.State == UpdateState.DevelopmentVersion) + "; source_outdated=" + candidates.Count(x => x.State == UpdateState.SourceOutdated) + "; unavailable=" + candidates.Count(x => x.State == UpdateState.SourceUnavailable));
            }
            return 0;
        }
        private static void CatalogCoverageMatching()
        {
            var exact = new PluginCatalogEntry { Id = "CatalogMaker", Name = "CatalogMaker", Type = "Wcx", Aliases = new List<string> { "CatalogMaker.wcx", "CatalogMaker.wcx64" } };
            var alias = new PluginCatalogEntry { Id = "webdav", Name = "WebDAV", Type = "Wfx", Aliases = new List<string> { "davplug.wfx", "davplug.uwfx", "davplug.wfx64" } };
            Assert(CatalogMatcher.Match(PluginType.Wcx, "catalogmaker.WCX", new[] { exact }).Kind == CatalogMatchKind.Exact, "embedded exact case insensitive");
            Assert(CatalogMatcher.Match(PluginType.Wfx, "davplug.uwfx", new[] { alias }).Kind == CatalogMatchKind.Alias, "historical alias");
            Assert(CatalogMatcher.Match(PluginType.Wfx, "davplug.wfx64", new[] { alias }).Kind == CatalogMatchKind.Alias, "historical x64 alias");
            Assert(CatalogMatcher.Match(PluginType.Wlx, "CatalogMaker.wcx", new[] { exact }).Kind == CatalogMatchKind.NotFound, "wrong type rejected");
            Assert(CatalogMatcher.Match(PluginType.Wcx, "CatalogMaker.wcx", new[] { exact }, true).Kind == CatalogMatchKind.RemoteExact, "remote exact");
            Assert(CatalogMatcher.Match(PluginType.Wcx, "CatalogMaker.wcx", new[] { exact, new PluginCatalogEntry { Id = "other", Name = "Other", Type = "Wcx", Aliases = new List<string> { "CatalogMaker.wcx" } } }, true).Kind == CatalogMatchKind.Ambiguous, "two remote candidates ambiguous");
            Assert(CatalogMatcher.Match(PluginType.Wcx, "missing.wcx", new[] { exact }, true).Kind == CatalogMatchKind.NotFound, "no remote candidate");
            var index = "catalogmaker|CatalogMaker|4.1.3|3.01.2022|packer|x32+x64||src\nwrong|CatalogMaker|1.0|1.1.2020|lister|x32||";
            Assert(RemoteCatalogLookup.ParseTotalCmdIndex(index, PluginType.Wcx).Count == 1, "remote index type filter");
            var ghislerFixture = "<table><tr><td><strong>Sample</strong> 1.2</td><td><a href=\"https://plugins.ghisler.com/lsplugins/sample.zip\">Download</a></td></tr></table>";
            Assert(RemoteCatalogLookup.ParseGhislerIndex(ghislerFixture, PluginType.Wlx).Count == 1 && RemoteCatalogLookup.ParseGhislerIndex(ghislerFixture, PluginType.Wcx).Count == 0, "Ghisler index type filter");
            Assert(RemoteCatalogLookup.MatchesName("catalogmaker", "", RemoteCatalogLookup.ParseTotalCmdIndex(index, PluginType.Wcx)[0]), "remote index exact ID");
            Assert(!RemoteCatalogLookup.MatchesName("catalog", "", RemoteCatalogLookup.ParseTotalCmdIndex(index, PluginType.Wcx)[0]), "similarity not enough");
            using (var stream = new MemoryStream())
            {
                using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, true)) { zip.CreateEntry("plugin/CatalogMaker.wcx"); zip.CreateEntry("plugin/CatalogMaker.wcx64"); zip.CreateEntry("readme.txt"); }
                var binaryNames = RemoteCatalogLookup.ArchiveAliases(stream.ToArray(), PluginType.Wcx);
                Assert(binaryNames.Count == 2 && binaryNames.Contains("CatalogMaker.wcx") && binaryNames.Contains("CatalogMaker.wcx64"), "archive filename verification");
                Assert(RemoteCatalogLookup.ArchiveAliases(stream.ToArray(), PluginType.Wlx).Count == 0, "archive wrong type rejected");
            }
            var cache = new SourceResponseCache(); var fetches = 0;
            var first = cache.GetOrAdd("totalcmd.net:index", () => { System.Threading.Interlocked.Increment(ref fetches); return System.Threading.Tasks.Task.FromResult(index); });
            var second = cache.GetOrAdd("totalcmd.net:index", () => { System.Threading.Interlocked.Increment(ref fetches); return System.Threading.Tasks.Task.FromResult("wrong"); });
            Assert(first.GetAwaiter().GetResult() == second.GetAwaiter().GetResult() && fetches == 1, "remote index fetched once per run");
            var issues = CatalogAliasAudit.Audit(new[] { exact, new PluginCatalogEntry { Id = "other", Name = "Other", Type = "Wcx", Aliases = new List<string> { "catalogmaker.wcx", "bad.wlx", "bad.wlx" } } });
            Assert(issues.Any(x => x.Kind == "SameTypeCollision") && issues.Any(x => x.Kind == "InvalidExtension") && issues.Any(x => x.Kind == "DuplicateAlias"), "alias audit collisions and extensions");
            var catalog = new CatalogService(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".json"));
            var unknown = new InstalledPlugin { Identity = new PluginIdentity { Id = "family:unknown" }, Type = PluginType.Wcx, PrimaryPath = "unknown.wcx", LocalVersion = LocalVersion.Unknown };
            var ambiguous = new UpdateService(catalog, new IUpdateSourceProvider[0], new FixedRemoteLookup(CatalogMatchKind.Ambiguous)).CheckAsync(unknown, System.Threading.CancellationToken.None).GetAwaiter().GetResult();
            Assert(ambiguous.State == UpdateState.CatalogAmbiguous && ambiguous.DownloadUrl == null, "ambiguous blocks update and download");
            var missing = new UpdateService(catalog, new IUpdateSourceProvider[0], new FixedRemoteLookup(CatalogMatchKind.NotFound)).CheckAsync(unknown, System.Threading.CancellationToken.None).GetAwaiter().GetResult();
            Assert(missing.State == UpdateState.PluginNotRecognized, "not found remains not recognized");
            var ahead = new InstalledPlugin { Identity = new PluginIdentity { Id = "ampview" }, Type = PluginType.Wlx, Architecture = PluginArchitecture.X86,
                LocalVersion = FileVersionProbe.Create("3.5.0.0", VersionSource.FileVersion, VersionConfidence.Exact) };
            var stale = new UpdateService(catalog, new IUpdateSourceProvider[] { new FixedRemoteProvider("3.3") }).CheckAsync(ahead, System.Threading.CancellationToken.None).GetAwaiter().GetResult();
            Assert(stale.State == UpdateState.SourceOutdated && stale.DownloadUrl == null, "stale source does not report development or download");
            var imagine = catalog.FindById("imagine");
            var author = imagine.Sources.Single(x => x.AuthorityValue == SourceAuthority.OfficialAuthor);
            Assert(GenericHtmlSourceProvider.Parse(author, "Main Program v2.7.0 (Sep 12 2026)").Release.Version.Raw == "2.7.0", "Imagine author version is parsed");
            var timedOut = new UpdateService(catalog, new IUpdateSourceProvider[] { new TimeoutProvider() }).CheckAsync(ahead, System.Threading.CancellationToken.None).GetAwaiter().GetResult();
            Assert(timedOut.State == UpdateState.SourceUnavailable, "source timeout does not cancel whole check");
        }
        private sealed class FixedRemoteLookup : IRemoteCatalogLookup
        {
            private readonly CatalogMatchKind _kind;
            public FixedRemoteLookup(CatalogMatchKind kind) { _kind = kind; }
            public System.Threading.Tasks.Task<CatalogMatchResult> FindAsync(InstalledPlugin plugin, SourceResponseCache cache, System.Threading.CancellationToken token)
            { return System.Threading.Tasks.Task.FromResult(new CatalogMatchResult { Kind = _kind }); }
        }
        private sealed class TimeoutProvider : IUpdateSourceProvider
        {
            public string Name { get { return "timeout"; } }
            public bool CanHandle(CatalogSource source) { return true; }
            public System.Threading.Tasks.Task<SourceQueryResult> QueryAsync(CatalogSource source, System.Threading.CancellationToken token)
            { return System.Threading.Tasks.Task.FromException<SourceQueryResult>(new System.Threading.Tasks.TaskCanceledException("HTTP timeout")); }
        }
        private static void LiveSources()
        {
            using (var http = new HttpService(ApplicationMetadata.Version))
            {
                var catalog = new CatalogService(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".json"));
                var providers = new IUpdateSourceProvider[] { new TotalCmdNetSourceProvider(http), new TotalCmdNetIndexProvider(http), new GhislerSourceProvider(http), new GhislerPluginsSourceProvider(http), new GitHubReleaseSourceProvider(http), new GenericHtmlSourceProvider(http) };
                var service = new UpdateService(catalog, providers);
                var cache = new SourceResponseCache();
                foreach (var id in new[] { "totalcmd", "fileinfo", "total7zip", "7zip-plugin", "imagine", "webdav", "sftp", "anytag", "glimpse-wlx", "glimpse-wcx" })
                {
                    var entry = catalog.FindById(id); Console.WriteLine(id);
                    foreach (var source in entry.Sources.OrderByDescending(x => x.Priority))
                    {
                        if (source.AuthorityValue == SourceAuthority.ManualOverride) { Console.WriteLine("  provider.QueryAsync: Manual override | Success | " + source.ManualOverride.Version + " | packages=0 | " + source.ManualOverride.EvidenceUrl); continue; }
                        var provider = providers.FirstOrDefault(x => x.CanHandle(source)); if (provider == null) { Console.WriteLine("  provider.QueryAsync: provider missing | " + source.Provider); continue; }
                        var cachedProvider = provider as ICachedUpdateSourceProvider; var query = (cachedProvider == null ? provider.QueryAsync(source, System.Threading.CancellationToken.None) : cachedProvider.QueryAsync(source, cache, System.Threading.CancellationToken.None)).GetAwaiter().GetResult();
                        Console.WriteLine("  provider.QueryAsync: " + provider.Name + " | authority=" + source.AuthorityValue + " | purpose=" + source.PurposeValue + " | " + query.Status + " | version=" + (query.Release == null ? "" : query.Release.Version.Raw) + " | packages=" + (query.Release == null ? 0 : query.Release.Packages.Count));
                        if (query.Release != null) foreach (var package in query.Release.Packages) Console.WriteLine("    " + package.FileName + " | " + package.Architecture + " | " + package.Url);
                    }
                    var plugin = new InstalledPlugin { Identity = new PluginIdentity { Id = id }, Architecture = PluginArchitecture.X86 | PluginArchitecture.X64, LocalVersion = FileVersionProbe.Create("0.0", VersionSource.FileVersion, VersionConfidence.Exact) };
                    var result = service.CheckAsync(plugin, System.Threading.CancellationToken.None, cache, SourceQueryMode.AuditAllSources).GetAwaiter().GetResult();
                    Console.WriteLine("  UpdateService.CheckAsync: " + result.State + " | selected=" + (result.DownloadUrl == null ? "none/ambiguous" : result.DownloadUrl.AbsoluteUri) + " | details=" + result.Details);
                }
            }
        }
        private static int AuditCatalogSources()
        {
            using (var http = new HttpService(ApplicationMetadata.Version))
            {
                var catalog = new CatalogService(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".json")); var entries = catalog.Load();
                var providers = new IUpdateSourceProvider[] { new TotalCmdNetSourceProvider(http), new TotalCmdNetIndexProvider(http), new GhislerSourceProvider(http), new GhislerPluginsSourceProvider(http), new GitHubReleaseSourceProvider(http), new GenericHtmlSourceProvider(http) };
                var failures = 0; var output = new System.Collections.Concurrent.ConcurrentBag<string>(); var service = new UpdateService(catalog, providers); var cache = new SourceResponseCache();
                using (var gate = new System.Threading.SemaphoreSlim(4))
                {
                    var tasks = entries.Select(async entry =>
                    {
                        UpdateCandidate candidate;
                        try { await gate.WaitAsync(); try { candidate = await service.CheckAsync(new InstalledPlugin { Identity = new PluginIdentity { Id = entry.Id }, LocalVersion = FileVersionProbe.Create("0", VersionSource.FileVersion, VersionConfidence.Exact) }, System.Threading.CancellationToken.None, cache, SourceQueryMode.AuditAllSources); } finally { gate.Release(); } }
                        catch (Exception ex) { candidate = new UpdateCandidate { State = UpdateState.Error, Details = ex.Message }; }
                        if (candidate.Observations.Any(x => x.Status != SourceQueryStatus.Success)) System.Threading.Interlocked.Increment(ref failures);
                        var observations = String.Join("; ", candidate.Observations.Select(x => x.ProviderName + " | " + x.Authority + " | " + x.Purpose + " | " + x.Status + " | " + (x.Release == null ? "" : x.Release.Version.Raw)));
                        output.Add(entry.Id + "\n  " + observations + "\n  canonical | " + (candidate.CanonicalVersionSource == null ? "" : candidate.AvailableVersion.Raw + " | " + candidate.CanonicalVersionSource.ProviderName) + "\n  disagreement | " + (candidate.HasSourceDisagreement ? "yes" : "no") + " | conflict | " + (candidate.AuthorityConflict ? "yes" : "no") + "\n  download | " + (candidate.DownloadSource == null ? "none" : candidate.DownloadSource.ProviderName));
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
                var failed = 0; var skipped = 0; var checkedPackages = 0; var output = new System.Collections.Concurrent.ConcurrentBag<string>(); var service = new UpdateService(catalog, providers); var cache = new SourceResponseCache();
                using (var gate = new System.Threading.SemaphoreSlim(4))
                {
                    var tasks = entries.Select(async entry =>
                    {
                        try
                        {
                            await gate.WaitAsync();
                            try
                            {
                                var candidate = await service.CheckAsync(new InstalledPlugin { Identity = new PluginIdentity { Id = entry.Id }, Architecture = PluginArchitecture.X86 | PluginArchitecture.X64, LocalVersion = FileVersionProbe.Create("0", VersionSource.FileVersion, VersionConfidence.Exact) }, System.Threading.CancellationToken.None, cache, SourceQueryMode.AuditAllSources);
                                var package = candidate.DownloadSource == null || candidate.DownloadSource.Release == null || candidate.DownloadUrl == null ? null : candidate.DownloadSource.Release.Packages.FirstOrDefault(x => x.Url != null && x.Url == candidate.DownloadUrl);
                                if (package == null || package.Url == null) { System.Threading.Interlocked.Increment(ref skipped); output.Add(entry.Id + " | SKIP | no production-selected package | canonical=" + (candidate.AvailableVersion.IsKnown ? candidate.AvailableVersion.Raw : "")); return; }
                                System.Threading.Interlocked.Increment(ref checkedPackages);
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
                Console.WriteLine("Package audit: entries=" + entries.Count + "; checked=" + checkedPackages + "; skipped=" + skipped + "; failures=" + failed); return failed == 0 ? 0 : 1;
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
                File.WriteAllText(user, "[{\"id\":\"fileinfo\",\"name\":\"FileInfo override\",\"type\":\"Wlx\",\"aliases\":[\"myfileinfo.wlx\"],\"identityEvidence\":\"VerifiedPackage\",\"localVersionStrategy\":\"fileinfo\",\"sources\":[{\"provider\":\"totalcmd.net\",\"id\":\"fileinfo\",\"priority\":200}]}]");
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
                Assert(baseCatalog.Entries.Single(x => x.Id == "webdav").Aliases.Count == 3 && baseCatalog.Entries.First(x => x.Id == "sftp").Sources.Any(x => x.Id == "sftp4tc") && baseCatalog.Entries.First(x => x.Id == "anytag").Sources.Any(x => x.Id == "wdx_anytag"), "embedded aliases and source IDs");
                Assert(catalog.FindByAlias("davplug.wfx").Id == "webdav" && catalog.FindByAlias("davplug.uwfx").Id == "webdav" && catalog.FindByAlias("davplug.wfx64").Id == "webdav", "WebDAV companion aliases");

                File.WriteAllText(user, "[{\"id\":\"fileinfo\",\"name\":\"FileInfo\",\"type\":\"Wlx\",\"aliases\":[\"fileinfo.wlx\"],\"identityEvidence\":\"VerifiedPackage\",\"localVersionStrategy\":\"fileinfo\",\"sources\":[{\"provider\":\"generic-html\",\"url\":\"https://example.test/one\",\"versionPattern\":\"([0-9.]+)\",\"priority\":100},{\"provider\":\"generic-html\",\"url\":\"https://example.test/two\",\"versionPattern\":\"([0-9.]+)\",\"priority\":90}]}]"); catalog = new CatalogService(user);
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
            var root = NewRoot();
            try
            {
                var file = Path.Combine(root, "unknown.wfx"); File.WriteAllBytes(file, new byte[0]);
                var user = Path.Combine(root, "registration.json");
                File.WriteAllText(user, "[{\"id\":\"registration-only\",\"name\":\"Registration only\",\"type\":\"Wfx\",\"aliases\":[],\"registrationAliases\":[\"Cloud\"],\"identityEvidence\":\"OfficialRegistrationName\",\"sources\":[{\"provider\":\"totalcmd.net-index\",\"id\":\"cloud\",\"authority\":\"OfficialTotalCommander\",\"purpose\":\"Metadata\",\"priority\":100}]}]");
                var configured = WriteIni(root, "wincmd.ini", "[Configuration]\r\nInstallDir=" + root + "\r\n[FileSystemPlugins]\r\nCloud=" + file + "\r\n");
                var service = new PluginDiscoveryService(new TotalCommanderConfigurationResolver(), new LocalVersionResolver(), new CatalogService(user));
                var found = service.Discover(new TotalCommanderConfigurationResolver().Resolve(configured)).Single(x => x.Type == PluginType.Wfx);
                Assert(found.Identity.Id == "registration-only" && found.CatalogMatchKind == CatalogMatchKind.Alias, "WFX registration name resolves OfficialRegistrationName only");
                found.LocalVersion = FileVersionProbe.Create("1.0", VersionSource.FileVersion, VersionConfidence.Exact);
                var update = new UpdateService(new CatalogService(user), new IUpdateSourceProvider[] { new FixedRemoteProvider("2.0") }).CheckAsync(found, System.Threading.CancellationToken.None).GetAwaiter().GetResult();
                Assert(update.State == UpdateState.UpdateAvailable && update.PackageAvailability == PackageAvailability.MetadataOnly && update.DownloadUrl == null, "registration-name-only match cannot auto-install");
                File.WriteAllText(user, "[{\"id\":\"first\",\"name\":\"First\",\"type\":\"Wfx\",\"aliases\":[],\"registrationAliases\":[\"Cloud\"],\"identityEvidence\":\"OfficialRegistrationName\",\"sources\":[{\"provider\":\"totalcmd.net-index\",\"id\":\"first\",\"priority\":100}]},{\"id\":\"second\",\"name\":\"Second\",\"type\":\"Wfx\",\"aliases\":[],\"registrationAliases\":[\"cloud\"],\"identityEvidence\":\"OfficialRegistrationName\",\"sources\":[{\"provider\":\"totalcmd.net-index\",\"id\":\"second\",\"priority\":100}]}]");
                Assert(new CatalogService(user).LoadWithDiagnostics().Diagnostics.Any(x => x.Severity == CatalogDiagnosticSeverity.Error && x.Message.Contains("Alias конфликтует")), "duplicate WFX registration alias is rejected");
            }
            finally { Directory.Delete(root, true); }
        }
        private static void CatalogScaleAndCache()
        {
            var root = NewRoot();
            try
            {
                var catalog = new CatalogService(Path.Combine(root, "user.json")); var loaded = catalog.LoadWithDiagnostics();
                Assert(loaded.Diagnostics.Count(x => x.Severity == CatalogDiagnosticSeverity.Error) == 0, "embedded catalog has no validation errors");
                Assert(loaded.Entries.Count >= 300, "full source catalog has at least 300 records");
                Assert(loaded.Entries.Count(x => x.PluginType == PluginType.Wcx) >= 20 && loaded.Entries.Count(x => x.PluginType == PluginType.Wlx) >= 20 && loaded.Entries.Count(x => x.PluginType == PluginType.Wfx) >= 20 && loaded.Entries.Count(x => x.PluginType == PluginType.Wdx) >= 20, "full catalog type distribution");
                var ghsilerEntries = loaded.Entries.Where(x => x.Id.StartsWith("ghisler-", StringComparison.OrdinalIgnoreCase) && x.Sources.Any(s => s.Provider.Equals("ghisler-plugins", StringComparison.OrdinalIgnoreCase))).ToList();
                Assert(loaded.Entries.Any(x => x.Sources.Any(s => s.Provider.Equals("totalcmd.net-index", StringComparison.OrdinalIgnoreCase))) &&
                    ghsilerEntries.Select(x => x.PluginType).Distinct().Count() == 4 && ghsilerEntries.All(x => x.Sources.Where(s => s.Provider.Equals("ghisler-plugins", StringComparison.OrdinalIgnoreCase)).All(s => !String.IsNullOrWhiteSpace(s.PackageUrl))),
                    "full catalog imports typed TotalCmd.net and Ghisler source records with package URLs");
                Assert(ghsilerEntries.Where(x => x.IdentityEvidence == IdentityEvidence.MetadataOnly).All(x => x.Aliases.Count == 0) &&
                    ghsilerEntries.Where(x => x.IdentityEvidence == IdentityEvidence.VerifiedPackage).All(x => x.Aliases.Count > 0 && x.Sources.Any(s => s.Provider.Equals("ghisler-plugins", StringComparison.OrdinalIgnoreCase) && s.PurposeValue == SourcePurpose.MetadataAndDownload)),
                    "Ghisler aliases are present only after ZIP evidence verification");
                Assert(File.Exists(FindMaintenanceScript("Harvest-FullSourceCatalog.ps1")), "maintenance full-catalog command resolves its harvest script");
                var cache = new SourceResponseCache(); var provider = new CachedTestProvider(); var service = new UpdateService(catalog, new IUpdateSourceProvider[] { provider });
                var first = new InstalledPlugin { Identity = new PluginIdentity { Id = "glimpse-wlx" }, LocalVersion = FileVersionProbe.Create("0.1", VersionSource.FileVersion, VersionConfidence.Exact) };
                var second = new InstalledPlugin { Identity = new PluginIdentity { Id = "glimpse-wcx" }, LocalVersion = FileVersionProbe.Create("0.1", VersionSource.FileVersion, VersionConfidence.Exact) };
                System.Threading.Tasks.Task.WaitAll(service.CheckAsync(first, System.Threading.CancellationToken.None, cache), service.CheckAsync(second, System.Threading.CancellationToken.None, cache));
                Assert(provider.Fetches == 1 && provider.Filters == 2, "same GitHub source reuses raw response and filters each entry");

                var ghsilerCatalogPath = Path.Combine(root, "ghisler-package.json");
                File.WriteAllText(ghsilerCatalogPath, "[{\"id\":\"ghisler-verified\",\"name\":\"Ghisler verified\",\"type\":\"Wlx\",\"aliases\":[\"verified.wlx\"],\"identityEvidence\":\"VerifiedPackage\",\"sources\":[{\"provider\":\"ghisler-plugins\",\"id\":\"Verified\",\"packageUrl\":\"https://example.test/verified.zip\",\"authority\":\"OfficialTotalCommander\",\"purpose\":\"MetadataAndDownload\",\"priority\":200}]}]");
                var verified = new UpdateService(new CatalogService(ghsilerCatalogPath), new IUpdateSourceProvider[] { new FixedRemoteProvider("2.0", "https://example.test/verified.zip") }).CheckAsync(
                    new InstalledPlugin { Identity = new PluginIdentity { Id = "ghisler-verified" }, Architecture = PluginArchitecture.X86, LocalVersion = FileVersionProbe.Create("1.0", VersionSource.FileVersion, VersionConfidence.Exact) }, System.Threading.CancellationToken.None).GetAwaiter().GetResult();
                Assert(verified.DownloadUrl != null && verified.PackageAvailability == PackageAvailability.Verified, "persisted harvested Ghisler package evidence enables only its exact URL");
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
            var fallback = resolver.Resolve(new[] { new RemoteVersionObservation { Authority = SourceAuthority.OfficialAuthor, Purpose = SourcePurpose.Metadata, Status = SourceQueryStatus.Unavailable }, observation(SourceAuthority.OfficialTotalCommander, "2.0"), observation(SourceAuthority.CommunityCatalog, "2.0") }); Assert(fallback.Canonical.Authority == SourceAuthority.OfficialTotalCommander, "unavailable official author falls back to Ghisler");
            var community = resolver.Resolve(new[] { new RemoteVersionObservation { Authority = SourceAuthority.OfficialTotalCommander, Purpose = SourcePurpose.Metadata, Status = SourceQueryStatus.Unavailable }, observation(SourceAuthority.CommunityCatalog, "2.0") }); Assert(community.Canonical.Authority == SourceAuthority.CommunityCatalog, "unavailable official falls back to community");
                var ghisler = GhislerPluginsSourceProvider.Parse("Diskdir", "<tr><td><a>Diskdir</a></td><td>directory listing</td><td><a href='details'>details</a></td><td>1.3</td></tr>"); Assert(ghisler.Status == SourceQueryStatus.Success && ghisler.Release.Version.Raw == "1.3", "Ghisler realistic plugin fixture parses version");
            var index = TotalCmdNetIndexProvider.Parse("dirsizecalc", "dirsizecalc|DirSizeCalc|2.22|19.08.2015|content|x32+x64||\r\n"); Assert(index.Status == SourceQueryStatus.Success && index.Release.Version.Raw == "2.22", "legacy totalcmd index fixture parses version");
        }
        private static void DownloadProvenance()
        {
            var root = NewRoot();
            try
            {
                File.WriteAllText(Path.Combine(root, "user.json"), "[{\"id\":\"total7zip\",\"name\":\"Total7zip\",\"type\":\"Wcx\",\"aliases\":[\"total7zip.wcx\"],\"identityEvidence\":\"VerifiedPackage\",\"sources\":[{\"provider\":\"totalcmd.net\",\"id\":\"Total7zip\",\"priority\":100}]}]");                var plugin = new InstalledPlugin { Identity = new PluginIdentity { Id = "total7zip" }, Architecture = PluginArchitecture.X86, LocalVersion = FileVersionProbe.Create("1.0", VersionSource.FileVersion, VersionConfidence.Exact) };
                var allowed = new UpdateService(new CatalogService(Path.Combine(root, "user.json")), new IUpdateSourceProvider[] { new AuthorityProvider("2.0", SourcePurpose.MetadataAndDownload) }).CheckAsync(plugin, System.Threading.CancellationToken.None).GetAwaiter().GetResult();
                Assert(allowed.AvailableVersion.Raw == "2.0" && allowed.DownloadUrl != null, "same canonical release package is allowed");
                var blocked = new UpdateService(new CatalogService(Path.Combine(root, "user2.json")), new IUpdateSourceProvider[] { new AuthorityProvider("1.9", SourcePurpose.MetadataAndDownload) }).CheckAsync(plugin, System.Threading.CancellationToken.None).GetAwaiter().GetResult();
                Assert(blocked.AvailableVersion.Raw == "2.0" && blocked.DownloadUrl == null, "lower source release package is blocked");
            }
            finally { Directory.Delete(root, true); }
        }
        private static void AuthorityRuntimeFinalization()
        {
            var root = NewRoot();
            try
            {
                var user = Path.Combine(root, "user.json");
                File.WriteAllText(user, "[{\"id\":\"fileinfo\",\"name\":\"FileInfo\",\"type\":\"Wlx\",\"aliases\":[\"fileinfo.wlx\"],\"identityEvidence\":\"VerifiedPackage\",\"sources\":[{\"provider\":\"totalcmd.net\",\"id\":\"manual\",\"authority\":\"ManualOverride\",\"purpose\":\"Metadata\",\"priority\":300,\"manualOverride\":{\"version\":\"2.1\",\"evidenceUrl\":\"https://evidence.test/manual\",\"reason\":\"verified\",\"verifiedAt\":\"2026-09-22\"}},{\"provider\":\"totalcmd.net\",\"id\":\"official\",\"authority\":\"OfficialAuthor\",\"purpose\":\"MetadataAndDownload\",\"priority\":100}]}]");
                var catalog = new CatalogService(user); var provider = new SourceRouteProvider("2.0", true); var plugin = new InstalledPlugin { Identity = new PluginIdentity { Id = "fileinfo" }, Architecture = PluginArchitecture.X86, LocalVersion = FileVersionProbe.Create("1.0", VersionSource.FileVersion, VersionConfidence.Exact) };
                var manual = new UpdateService(catalog, new IUpdateSourceProvider[] { provider }).CheckAsync(plugin, System.Threading.CancellationToken.None).GetAwaiter().GetResult();
                Assert(manual.AvailableVersion.Raw == "2.1" && manual.CanonicalVersionSource.Authority == SourceAuthority.ManualOverride && provider.Calls == 1, "ManualOverride creates runtime observation without HTTP provider");
                Assert(manual.DownloadUrl == null && manual.DownloadSource == null, "ManualOverride never supplies a download package");

                File.WriteAllText(user, "[{\"id\":\"fileinfo\",\"name\":\"FileInfo\",\"type\":\"Wlx\",\"aliases\":[\"fileinfo.wlx\"],\"identityEvidence\":\"VerifiedPackage\",\"sources\":[{\"provider\":\"totalcmd.net\",\"id\":\"community\",\"authority\":\"CommunityCatalog\",\"purpose\":\"MetadataAndDownload\",\"priority\":300},{\"provider\":\"totalcmd.net\",\"id\":\"official\",\"authority\":\"OfficialAuthor\",\"purpose\":\"MetadataAndDownload\",\"priority\":100}]}]");
                var officialPackage = new UpdateService(new CatalogService(user), new IUpdateSourceProvider[] { new SourceRouteProvider("2.0", true) }).CheckAsync(plugin, System.Threading.CancellationToken.None).GetAwaiter().GetResult();
                Assert(officialPackage.DownloadSource.Authority == SourceAuthority.OfficialAuthor, "official package beats higher-priority community package");
                var fallbackPackage = new UpdateService(new CatalogService(user), new IUpdateSourceProvider[] { new SourceRouteProvider("2.0", false) }).CheckAsync(plugin, System.Threading.CancellationToken.None).GetAwaiter().GetResult();
                Assert(fallbackPackage.DownloadSource.Authority == SourceAuthority.CommunityCatalog, "same-version community package is fallback when official has none");
                var mismatch = new UpdateService(new CatalogService(user), new IUpdateSourceProvider[] { new SourceRouteProvider("1.9", false) }).CheckAsync(plugin, System.Threading.CancellationToken.None).GetAwaiter().GetResult();
                Assert(mismatch.DownloadUrl == null, "different-version lower authority package is blocked");

                File.WriteAllText(user, "[{\"id\":\"fileinfo\",\"name\":\"FileInfo\",\"type\":\"Wlx\",\"aliases\":[\"fileinfo.wlx\"],\"identityEvidence\":\"VerifiedPackage\",\"sources\":[{\"provider\":\"totalcmd.net\",\"id\":\"community\",\"authority\":\"CommunityCatalog\",\"purpose\":\"MetadataAndDownload\",\"priority\":300},{\"provider\":\"totalcmd.net\",\"id\":\"ghisler\",\"authority\":\"OfficialTotalCommander\",\"purpose\":\"MetadataAndDownload\",\"priority\":100}]}]");
                var ghislerPackage = new UpdateService(new CatalogService(user), new IUpdateSourceProvider[] { new SourceRouteProvider("2.0", true) }).CheckAsync(plugin, System.Threading.CancellationToken.None).GetAwaiter().GetResult();
                Assert(ghislerPackage.DownloadSource.Authority == SourceAuthority.OfficialTotalCommander, "Ghisler same-version package beats community package");

                File.WriteAllText(user, "[{\"id\":\"bad\",\"name\":\"Bad\",\"type\":\"Wlx\",\"aliases\":[\"bad.wlx\"],\"sources\":[{\"provider\":\"totalcmd.net\",\"authority\":\"ManualOverride\",\"purpose\":\"Metadata\",\"priority\":1,\"manualOverride\":{\"version\":\"bad\"}}]}]");
                Assert(new CatalogService(user).LoadWithDiagnostics().Diagnostics.Any(x => x.Severity == CatalogDiagnosticSeverity.Error && x.Message.IndexOf("ManualOverride", StringComparison.OrdinalIgnoreCase) >= 0), "malformed ManualOverride is catalog error");
                File.WriteAllText(user, "[{\"id\":\"old\",\"name\":\"Old\",\"type\":\"Wlx\",\"aliases\":[\"old.wlx\"],\"sources\":[{\"provider\":\"totalcmd.net\",\"id\":\"old\",\"authority\":\"ManualOverride\",\"purpose\":\"Metadata\",\"priority\":1,\"manualOverride\":{\"version\":\"1.0\",\"evidenceUrl\":\"https://evidence.test/old\",\"reason\":\"old\",\"verifiedAt\":\"2000-01-01\"}}]}]");
                Assert(new CatalogService(user).LoadWithDiagnostics().Diagnostics.Any(x => x.Severity == CatalogDiagnosticSeverity.Warning && x.Message.IndexOf("180", StringComparison.OrdinalIgnoreCase) >= 0), "stale ManualOverride is catalog warning");

                var embedded = new CatalogService(Path.Combine(root, "none.json")).Load(); Assert(embedded.All(x => x.Sources.All(s => !String.IsNullOrWhiteSpace(s.Authority) && !String.IsNullOrWhiteSpace(s.Purpose))), "every embedded source has explicit authority and purpose");
                var conflictRow = new PluginRowViewModel(plugin); conflictRow.Apply(new UpdateCandidate { Plugin = plugin, State = UpdateState.UpdateAvailable, DownloadUrl = new Uri("https://example.test/blocked"), AuthorityConflict = true });
                Assert(!conflictRow.CanDownload && conflictRow.Information.Contains("Конфликт источников одного уровня доверия.") && conflictRow.Information.Contains("Автоматическая загрузка отключена."), "AuthorityConflict blocks download and has UI diagnostic");
                var indexCache = new SourceResponseCache(); var indexProvider = new IndexCacheProvider(); var indexService = new UpdateService(new CatalogService(Path.Combine(root, "index.json")), new IUpdateSourceProvider[] { indexProvider });
                var indexed = new[] { "total7zip", "fileinfo", "diskdir" }.Select(id => indexService.CheckAsync(new InstalledPlugin { Identity = new PluginIdentity { Id = id }, LocalVersion = FileVersionProbe.Create("0", VersionSource.FileVersion, VersionConfidence.Exact) }, System.Threading.CancellationToken.None, indexCache)).ToArray(); System.Threading.Tasks.Task.WaitAll(indexed);
                Assert(indexProvider.Fetches == 1 && indexed.All(x => x.Result.Observations.Any(o => o.Source != null && o.Source.Provider == "totalcmd.net-index")), "three index entries share one raw index fetch and retain observations");
            }
            finally { Directory.Delete(root, true); }
        }
        private static void LazySourcesAndCache()
        {
            var root = NewRoot();
            try
            {
                var user = Path.Combine(root, "lazy.json");
                File.WriteAllText(user, "[{\"id\":\"fileinfo\",\"name\":\"FileInfo\",\"type\":\"Wlx\",\"aliases\":[\"fileinfo.wlx\"],\"identityEvidence\":\"VerifiedPackage\",\"sources\":[{\"provider\":\"totalcmd.net-index\",\"id\":\"fileinfo\",\"authority\":\"CommunityCatalog\",\"purpose\":\"Metadata\",\"priority\":150},{\"provider\":\"totalcmd.net\",\"id\":\"fileinfo\",\"authority\":\"CommunityCatalog\",\"purpose\":\"MetadataAndDownload\",\"priority\":100}]}]");
                var catalog = new CatalogService(user);
                var current = new InstalledPlugin { Identity = new PluginIdentity { Id = "fileinfo" }, Architecture = PluginArchitecture.X86, LocalVersion = FileVersionProbe.Create("2.0", VersionSource.FileVersion, VersionConfidence.Exact) };
                var older = new InstalledPlugin { Identity = current.Identity, Architecture = PluginArchitecture.X86, LocalVersion = FileVersionProbe.Create("1.0", VersionSource.FileVersion, VersionConfidence.Exact) };
                var provider = new LazyTestProvider(false); var service = new UpdateService(catalog, new IUpdateSourceProvider[] { provider });
                var upToDate = service.CheckAsync(current, System.Threading.CancellationToken.None).GetAwaiter().GetResult();
                Assert(upToDate.State == UpdateState.UpToDate && provider.IndexCalls == 1 && provider.DetailCalls == 0, "UpToDate skips detail page");
                var update = service.CheckAsync(older, System.Threading.CancellationToken.None).GetAwaiter().GetResult();
                Assert(update.DownloadUrl != null && provider.DetailCalls == 1, "UpdateAvailable fetches detail package once");
                var audit = service.CheckAsync(current, System.Threading.CancellationToken.None, null, SourceQueryMode.AuditAllSources).GetAwaiter().GetResult();
                Assert(audit.Observations.Count == 2 && provider.DetailCalls == 2, "AuditAllSources queries detail even when local version is current");
                var unavailable = new LazyTestProvider(true); var fallback = new UpdateService(catalog, new IUpdateSourceProvider[] { unavailable }).CheckAsync(older, System.Threading.CancellationToken.None).GetAwaiter().GetResult();
                Assert(fallback.AvailableVersion.Raw == "2.0" && unavailable.DetailCalls == 1, "index unavailable falls back to detail");

                var cache = new SourceResponseCache(); var calls = 0;
                var tasks = Enumerable.Range(0, 20).Select(_ => System.Threading.Tasks.Task.Run(() => cache.GetOrAdd("path/Item?X=1", () => { System.Threading.Interlocked.Increment(ref calls); return System.Threading.Tasks.Task.Delay(30).ContinueWith(t => "value"); }))).ToArray();
                System.Threading.Tasks.Task.WaitAll(tasks); Assert(calls == 1 && tasks.All(x => x.Result == "value"), "20 simultaneous same-key calls invoke factory once");
                cache.GetOrAdd("path/item?X=1", () => { System.Threading.Interlocked.Increment(ref calls); return System.Threading.Tasks.Task.FromResult("other"); }).GetAwaiter().GetResult();
                Assert(calls == 2, "cache keys preserve path case");
            }
            finally { Directory.Delete(root, true); }
        }

        private static void SourceInputHardening()
        {
            var root = NewRoot();
            try
            {
                var user = Path.Combine(root, "input.json");
                File.WriteAllText(user, "[{\"id\":\"fileinfo\",\"name\":\"FileInfo\",\"type\":\"Wlx\",\"aliases\":[\"fileinfo.wlx\"],\"identityEvidence\":\"VerifiedPackage\",\"sources\":[{\"provider\":\"manual\",\"authority\":\"ManualOverride\",\"purpose\":\"Metadata\",\"priority\":100,\"manualOverride\":{\"version\":\"2.1\",\"evidenceUrl\":\"https://evidence.test/v2\",\"reason\":\"checked\",\"verifiedAt\":\"2026-09-22\"}}]}]");
                var catalog = new CatalogService(user); var plugin = new InstalledPlugin { Identity = new PluginIdentity { Id = "fileinfo" }, LocalVersion = FileVersionProbe.Create("1.0", VersionSource.FileVersion, VersionConfidence.Exact) };
                var manual = new UpdateService(catalog, new IUpdateSourceProvider[0]).CheckAsync(plugin, System.Threading.CancellationToken.None).GetAwaiter().GetResult();
                Assert(manual.AvailableVersion.Raw == "2.1" && manual.Observations.Single().ProviderName == "Manual override" && manual.DownloadUrl == null, "provider manual works without HTTP provider");
                Assert(catalog.LoadWithDiagnostics().Diagnostics.All(x => x.Severity != CatalogDiagnosticSeverity.Error), "new manual provider validates");
                var legacy = "[{\"id\":\"fileinfo\",\"name\":\"FileInfo\",\"type\":\"Wlx\",\"aliases\":[\"fileinfo.wlx\"],\"identityEvidence\":\"VerifiedPackage\",\"sources\":[{\"provider\":\"totalcmd.net\",\"id\":\"fileinfo\",\"authority\":\"ManualOverride\",\"purpose\":\"Metadata\",\"priority\":100,\"manualOverride\":{\"version\":\"2.1\",\"evidenceUrl\":\"https://evidence.test/v2\",\"reason\":\"checked\",\"verifiedAt\":\"2026-09-22\"}}]}]";
                File.WriteAllText(user, legacy); Assert(new CatalogService(user).LoadWithDiagnostics().Diagnostics.Any(x => x.Severity == CatalogDiagnosticSeverity.Warning && x.Message.Contains("provider")), "0.7.1 legacy manual format retained with warning");
                File.WriteAllText(user, legacy.Replace("https://evidence.test/v2", "file:///bad")); Assert(new CatalogService(user).LoadWithDiagnostics().Diagnostics.Any(x => x.Severity == CatalogDiagnosticSeverity.Error), "manual evidence requires HTTP or HTTPS");
                File.WriteAllText(user, legacy.Replace("2026-09-22", "22.09.2026")); Assert(new CatalogService(user).LoadWithDiagnostics().Diagnostics.Any(x => x.Severity == CatalogDiagnosticSeverity.Error), "manual date requires yyyy-MM-dd");
                File.WriteAllText(user, "[{\"id\":\"mirror\",\"name\":\"Mirror\",\"type\":\"Wlx\",\"aliases\":[\"mirror.wlx\"],\"sources\":[{\"provider\":\"totalcmd.net\",\"id\":\"mirror\",\"authority\":\"Mirror\",\"purpose\":\"Metadata\",\"priority\":10}]}]");
                Assert(new CatalogService(user).LoadWithDiagnostics().Diagnostics.Any(x => x.Severity == CatalogDiagnosticSeverity.Error), "mirror alone cannot provide metadata");
                File.WriteAllText(user, "[{\"id\":\"mirror\",\"name\":\"Mirror\",\"type\":\"Wlx\",\"aliases\":[\"mirror.wlx\"],\"sources\":[{\"provider\":\"totalcmd.net\",\"id\":\"mirror\",\"authority\":\"Mirror\",\"purpose\":\"Metadata\",\"priority\":10},{\"provider\":\"totalcmd.net-index\",\"id\":\"mirror\",\"authority\":\"CommunityCatalog\",\"purpose\":\"Metadata\",\"priority\":20}]}]");
                Assert(new CatalogService(user).LoadWithDiagnostics().Entries.Any(x => x.Id == "mirror"), "mirror metadata with authoritative metadata is valid");
                File.WriteAllText(user, "[{\"id\":\"unsafe\",\"name\":\"Unsafe\",\"type\":\"Wlx\",\"aliases\":[\"unsafe.wlx\"],\"sources\":[{\"provider\":\"generic-html\",\"url\":\"file:///etc/passwd\",\"versionPattern\":\"(1.0)\",\"priority\":10}]}]");
                Assert(new CatalogService(user).LoadWithDiagnostics().Diagnostics.Any(x => x.Severity == CatalogDiagnosticSeverity.Error), "non-HTTP source URL rejected");
                var regex = GenericHtmlSourceProvider.Parse(new CatalogSource { Url = "https://example.test", VersionPattern = "(a+)+$" }, new string('a', 4000) + "!");
                Assert(regex.Status == SourceQueryStatus.InvalidResponse && regex.Details.Contains("время"), "user versionPattern timeout returns InvalidResponse");
                var githubJson = "[{\"tag_name\":\"v1.0\",\"draft\":false,\"prerelease\":false,\"html_url\":\"https://github.test/release\",\"assets\":[{\"name\":\"" + new string('a', 4000) + "!\",\"browser_download_url\":\"https://github.test/archive.zip\"}]}]";
                var githubRegex = GitHubReleaseSourceProvider.Parse(githubJson, new CatalogSource { AssetPattern = "(a+)+$" });
                Assert(githubRegex.Status == SourceQueryStatus.InvalidResponse, "user assetPattern timeout returns InvalidResponse");
                var bom = "\uFEFFfileinfo | FileInfo | 2.23 | today | lister | x32+x64 ||\r\n";
                Assert(TotalCmdNetIndexProvider.Parse("fileinfo", bom).Release.Version.Raw == "2.23", "index trims BOM fields and CRLF");
                var duplicate = "fileinfo|FileInfo|2.23|d|lister|x32||\nfileinfo|FileInfo|2.24|d|lister|x32||\n";
                Assert(TotalCmdNetIndexProvider.Parse("fileinfo", duplicate).Status == SourceQueryStatus.InvalidResponse, "duplicate index ID has diagnostic");
                Assert(TotalCmdNetIndexProvider.Parse("fileinfo", "broken|1.0\nfileinfo|FileInfo|2.23|d|lister|x32||\n").Release.Version.Raw == "2.23", "malformed index rows ignored");
                Assert(TotalCmdNetIndexProvider.Parse("fileinfo", "fileinfo|FileInfo|not-a-version|d|lister|x32||\nfileinfo|FileInfo|2.23|d|lister|x32||\n").Release.Version.Raw == "2.23", "matching malformed index row does not mask a valid row");
                Assert(!VersionValue.Parse("999999999999999999999.1").IsKnown, "overflowed version is unknown rather than an exception");
                var legacyBytes = Encoding.GetEncoding(1251).GetBytes("russian|Русский|1.0|d|lister|x32||\n");
                Assert(HttpService.DecodeTotalCmdIndex(legacyBytes).Contains("Русский"), "index Windows-1251 decoding");
                var utf8Bom = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes("fileinfo|FileInfo|2.23|d|lister|x32||\n")).ToArray();
                Assert(TotalCmdNetIndexProvider.Parse("fileinfo", HttpService.DecodeTotalCmdIndex(utf8Bom)).Status == SourceQueryStatus.Success, "index UTF-8 BOM and LF accepted");
            }
            finally { Directory.Delete(root, true); }
        }
        private static void ScalableCheckRunner()
        {
            var root = NewRoot();
            try
            {
                var catalog = new CatalogService(Path.Combine(root, "user.json")); var provider = new MeasuredProvider(40, 4); var runner = new UpdateCheckRunner(new UpdateService(catalog, new IUpdateSourceProvider[] { provider }));
                var plugins = Enumerable.Range(0, 20).Select(x => new InstalledPlugin { Identity = new PluginIdentity { Id = "total7zip" }, LocalVersion = FileVersionProbe.Create("0.1", VersionSource.FileVersion, VersionConfidence.Exact) }).ToList();
                var applied = 0; var result = runner.RunAsync(plugins, (p, c) => System.Threading.Interlocked.Increment(ref applied), (n, total) => { }, System.Threading.CancellationToken.None).GetAwaiter().GetResult();
                Assert(UpdateCheckRunner.MaximumParallelism == 4 && provider.Peak >= 1 && provider.Peak <= UpdateCheckRunner.MaximumParallelism, "bounded runner has configured parallelism up to four");
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
            private readonly Uri _packageUrl;
            public FixedRemoteProvider(string version, string packageUrl = "https://example.test/download") { _version = VersionValue.Parse(version); _packageUrl = new Uri(packageUrl); }
            public string Name { get { return "test"; } }
            public bool CanHandle(CatalogSource source) { return true; }
            public System.Threading.Tasks.Task<SourceQueryResult> QueryAsync(CatalogSource source, System.Threading.CancellationToken cancellationToken)
            {
                return System.Threading.Tasks.Task.FromResult(new SourceQueryResult { Status = SourceQueryStatus.Success, Release = new RemoteRelease { VersionText = _version.Raw, Version = _version, SourceUrl = new Uri("https://example.test/source"), Packages = new System.Collections.Generic.List<RemotePackage> { new RemotePackage { Architecture = RemotePackageArchitecture.Combined, Url = _packageUrl } } } });
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
        private sealed class AuthorityProvider : IUpdateSourceProvider
        {
            private readonly string _communityVersion; private readonly SourcePurpose _purpose;
            public AuthorityProvider(string communityVersion, SourcePurpose purpose) { _communityVersion = communityVersion; _purpose = purpose; }
            public string Name { get { return "authority-test"; } } public bool CanHandle(CatalogSource source) { return true; }
            public System.Threading.Tasks.Task<SourceQueryResult> QueryAsync(CatalogSource source, System.Threading.CancellationToken token)
            {
                var official = source.AuthorityValue == SourceAuthority.OfficialTotalCommander;
                var version = official ? "2.0" : _communityVersion;
                var packages = !official && _purpose != SourcePurpose.Metadata ? new System.Collections.Generic.List<RemotePackage> { new RemotePackage { Architecture = RemotePackageArchitecture.Combined, Url = new Uri("https://example.test/package") } } : new System.Collections.Generic.List<RemotePackage>();
                return System.Threading.Tasks.Task.FromResult(new SourceQueryResult { Status = SourceQueryStatus.Success, Release = new RemoteRelease { VersionText = version, Version = VersionValue.Parse(version), SourceUrl = new Uri("https://example.test/source"), Packages = packages } });
            }
        }
        private sealed class SourceRouteProvider : IUpdateSourceProvider
        {
            private readonly string _communityVersion; private readonly bool _officialPackage;
            public int Calls;
            public SourceRouteProvider(string communityVersion, bool officialPackage) { _communityVersion = communityVersion; _officialPackage = officialPackage; }
            public string Name { get { return "route"; } }
            public bool CanHandle(CatalogSource source) { return true; }
            public System.Threading.Tasks.Task<SourceQueryResult> QueryAsync(CatalogSource source, System.Threading.CancellationToken token)
            {
                Calls++; var official = source.AuthorityValue == SourceAuthority.OfficialAuthor || source.AuthorityValue == SourceAuthority.OfficialTotalCommander; var version = official ? "2.0" : _communityVersion;
                var packages = (official ? _officialPackage : true) ? new List<RemotePackage> { new RemotePackage { Architecture = RemotePackageArchitecture.Combined, Url = new Uri("https://example.test/" + (official ? "official" : "community")) } } : new List<RemotePackage>();
                return System.Threading.Tasks.Task.FromResult(new SourceQueryResult { Status = SourceQueryStatus.Success, Release = new RemoteRelease { VersionText = version, Version = VersionValue.Parse(version), SourceUrl = new Uri("https://example.test/source"), Packages = packages } });
            }
        }
        private sealed class IndexCacheProvider : ICachedUpdateSourceProvider
        {
            public int Fetches;
            public string Name { get { return "index-cache"; } }
            public bool CanHandle(CatalogSource source) { return source != null && (source.Provider == "totalcmd.net-index" || source.Provider == "ghisler-plugins" || source.Provider == "totalcmd.net"); }
            public System.Threading.Tasks.Task<SourceQueryResult> QueryAsync(CatalogSource source, System.Threading.CancellationToken token) { return QueryAsync(source, null, token); }
            public async System.Threading.Tasks.Task<SourceQueryResult> QueryAsync(CatalogSource source, SourceResponseCache cache, System.Threading.CancellationToken token)
            {
                if (source.Provider == "totalcmd.net-index")
                {
                    var raw = cache == null ? "raw" : await cache.GetOrAdd("totalcmd.net:index", () => { Fetches++; return System.Threading.Tasks.Task.FromResult("raw"); });
                    return new SourceQueryResult { Status = SourceQueryStatus.Success, Release = new RemoteRelease { VersionText = "2.0", Version = VersionValue.Parse("2.0"), SourceUrl = new Uri("https://example.test/" + raw) } };
                }
                return new SourceQueryResult { Status = SourceQueryStatus.Unavailable, Details = "not needed" };
            }
        }
        private sealed class LazyTestProvider : IUpdateSourceProvider
        {
            private readonly bool _indexUnavailable;
            public int IndexCalls; public int DetailCalls;
            public LazyTestProvider(bool indexUnavailable) { _indexUnavailable = indexUnavailable; }
            public string Name { get { return "lazy-test"; } }
            public bool CanHandle(CatalogSource source) { return source.Provider == "totalcmd.net-index" || source.Provider == "totalcmd.net"; }
            public System.Threading.Tasks.Task<SourceQueryResult> QueryAsync(CatalogSource source, System.Threading.CancellationToken token)
            {
                if (source.Provider == "totalcmd.net-index")
                {
                    IndexCalls++;
                    if (_indexUnavailable) return System.Threading.Tasks.Task.FromResult(new SourceQueryResult { Status = SourceQueryStatus.Unavailable });
                    return System.Threading.Tasks.Task.FromResult(new SourceQueryResult { Status = SourceQueryStatus.Success, Release = Release("2.0") });
                }
                DetailCalls++;
                return System.Threading.Tasks.Task.FromResult(new SourceQueryResult { Status = SourceQueryStatus.Success, Release = Release("2.0", RemotePackageArchitecture.Combined) });
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
        private static void SettingsContracts()
        {
            var root = Path.Combine(Path.GetTempPath(), "tu-settings-" + Guid.NewGuid().ToString("N")); var path = Path.Combine(root, "TotalUpdater.ini"); var service = new SettingsService(path);
            Assert(service.Load().Settings.CheckPlugins && service.Load().Settings.FirstRequestTimeoutSeconds == 20, "settings defaults");
            var settings = new AppSettings { IncludePrerelease = true, DownloadDirectory = "%TEMP%\\downloads", FirstRequestTimeoutSeconds = 15, RetryTimeoutSeconds = 45, LastFilter = "Updates", WindowWidth = 1111, WindowHeight = 777, ExcludedCatalogIds = new List<string> { "sample" }, ExcludedUnknownPaths = new List<string> { "c:\\plugins\\unknown.wcx" }, ProxyMode = ProxyMode.Custom, ProxyAddress = "proxy.test", ProxyPort = 8080, ProxyUsername = "user", PostDownloadAction = PostDownloadAction.OfferInstall, CacheMaxAgeDays = 17 }; service.Save(settings); var loaded = service.Load().Settings;
            Assert(loaded.IncludePrerelease && loaded.DownloadDirectory.Contains("%TEMP%") && loaded.FirstRequestTimeoutSeconds == 15 && loaded.RetryTimeoutSeconds == 45 && loaded.LastFilter == "Updates" && loaded.WindowWidth == 1111 && loaded.ExcludedCatalogIds.Single() == "sample", "settings atomic save/load utf8");
            Assert(loaded.ProxyMode == ProxyMode.Custom && loaded.ProxyAddress == "proxy.test" && loaded.ProxyPort == 8080 && loaded.ProxyUsername == "user" && loaded.PostDownloadAction == PostDownloadAction.OfferInstall, "proxy and post-download enum save/load");
            Assert(loaded.CacheMaxAgeDays == 17 && new CachedSourceResponse { FetchedUtc = DateTime.UtcNow.AddDays(-18) }.IsStale(DateTime.UtcNow, loaded.CacheMaxAgeDays), "custom cache age persists and marks stale");
            var cacheValidation = new SettingsValidator().Validate(new AppSettings { CacheMaxAgeDays = 0 }); Assert(cacheValidation.Settings.CacheMaxAgeDays == 180, "cache age validation");
            using (var http = new HttpService("test", new AppSettings { ProxyMode = ProxyMode.System })) { http.Reconfigure(new AppSettings { ProxyMode = ProxyMode.Direct }); Assert(http.CurrentProxyMode == ProxyMode.Direct, "runtime proxy apply"); http.Reconfigure(new AppSettings { ProxyMode = ProxyMode.Custom, ProxyAddress = "proxy.test", ProxyPort = 8080 }); Assert(http.CurrentProxyMode == ProxyMode.Custom, "runtime custom proxy apply"); }
            var selectionVm = new MainViewModel(null, null, null, new CatalogService(Path.Combine(root, "selection-catalog.json")), null, null); var selectionChanges = 0; selectionVm.RestoreSelectedExclusionCommand.CanExecuteChanged += (sender, args) => selectionChanges++; selectionVm.SelectedExclusion = null; Assert(!selectionVm.RestoreSelectedExclusionCommand.CanExecute(null), "null exclusion selection disables restore command"); selectionVm.SelectedExclusion = new SettingsExclusion { Key = "sample", Kind = "Catalog ID" }; Assert(selectionVm.RestoreSelectedExclusionCommand.CanExecute(null) && selectionChanges == 2, "assigned exclusion selection enables and refreshes restore command");
            foreach (var mode in new[] { "System", "Direct", "Custom" }) { selectionVm.ProxyModeName = mode; ProxyMode parsed; Assert(Enum.TryParse(mode, out parsed) && selectionVm.Settings.ProxyMode == parsed, "proxy combo value conversion " + mode); }
            foreach (var actionName in new[] { "Nothing", "OfferInstall" }) { selectionVm.PostDownloadActionName = actionName; PostDownloadAction parsed; Assert(Enum.TryParse(actionName, out parsed) && selectionVm.Settings.PostDownloadAction == parsed, "post-download combo value conversion " + actionName); }
            Assert(MainViewModel.ShouldOfferInstall(new AppSettings { PostDownloadAction = PostDownloadAction.OfferInstall }, 1, "package.zip") && !MainViewModel.ShouldOfferInstall(new AppSettings { PostDownloadAction = PostDownloadAction.Nothing }, 1, "package.zip") && !MainViewModel.ShouldOfferInstall(new AppSettings { PostDownloadAction = PostDownloadAction.OfferInstall }, 2, "package.zip"), "offer install branch");
            var exclusions = SettingsExclusions.List(loaded); SettingsExclusions.Restore(loaded, exclusions.Single(x => x.Kind == "Catalog ID")); Assert(loaded.ExcludedCatalogIds.Count == 0 && loaded.ExcludedUnknownPaths.Count == 1, "restore one exclusion"); SettingsExclusions.RestoreAll(loaded); Assert(loaded.ExcludedCatalogIds.Count == 0 && loaded.ExcludedUnknownPaths.Count == 0, "restore all exclusions");
            var legacyPath = Path.Combine(root, "legacy.ini"); File.WriteAllText(legacyPath, "ExcludedUnknownPaths=Wcx|C:\\plugins\\legacy.wcx\r\n"); var legacy = new SettingsService(legacyPath).Load().Settings; const string migratedKey = "Wcx::C:\\plugins\\legacy.wcx"; Assert(legacy.ExcludedUnknownPaths.Single() == migratedKey && SettingsExclusions.ContainsUnknown(legacy, migratedKey), "legacy unknown exclusion migrates to current exclusion key"); new SettingsService(legacyPath).Save(legacy); Assert(File.ReadAllText(legacyPath).Contains("ExcludedUnknownPaths=b64:") && !File.ReadAllText(legacyPath).Contains("ExcludedUnknownPaths=Wcx|"), "migrated unknown exclusion saves new format");
            var ordinaryPipe = "C:\\plugins\\literal|name.wcx"; var pipeSettings = new AppSettings { ExcludedUnknownPaths = new List<string> { ordinaryPipe } }; pipeSettings = new SettingsValidator().Validate(pipeSettings).Settings; var pipePath = Path.Combine(root, "pipe.ini"); new SettingsService(pipePath).Save(pipeSettings); Assert(new SettingsService(pipePath).Load().Settings.ExcludedUnknownPaths.Single() == ordinaryPipe, "ordinary unknown path with pipe survives new serialization");
            var probeDirectory = Path.Combine(root, "probe"); Directory.CreateDirectory(probeDirectory); var probeBinary = Path.Combine(probeDirectory, "sample.wlx"); File.WriteAllText(probeBinary, "binary"); File.WriteAllText(Path.Combine(probeDirectory, "version.txt"), "version 3.2"); var detection = new AppSettings { UseExtendedVersionDetection = false }; var resolver = new LocalVersionResolver(new IPluginSpecificVersionStrategy[0], new IVersionProbe[] { new TextVersionProbe() }, detection); Assert(!resolver.Resolve(probeBinary).ParsedValue.IsKnown, "extended version detection skips text probe"); detection.UseExtendedVersionDetection = true; Assert(resolver.Resolve(probeBinary).ParsedValue.IsKnown, "extended version detection toggles at runtime");
            File.WriteAllText(path, "bad\0ini"); var broken = service.Load(); Assert(broken.Settings.CheckPlugins && broken.Warnings.Count > 0, "corrupted settings fallback");
            var invalid = new SettingsValidator().Validate(new AppSettings { FirstRequestTimeoutSeconds = 1, RetryTimeoutSeconds = 121 }); Assert(invalid.Settings.FirstRequestTimeoutSeconds == 20 && invalid.Settings.RetryTimeoutSeconds == 30, "settings timeout validation");
            var binary = Path.Combine(root, "sample.wlx"); File.WriteAllText(binary, "one"); var plugin = new InstalledPlugin { Identity = new PluginIdentity { Id = "sample" }, PrimaryPath = binary }; var accepted = new AcceptedVersionService(settings); accepted.Accept(plugin, VersionValue.Parse("2.0"));
            Assert(accepted.IsAccepted(plugin, VersionValue.Parse("2.0")) && accepted.IsAccepted(plugin, VersionValue.Parse("1.9")) && !accepted.IsAccepted(plugin, VersionValue.Parse("2.1")), "accepted version applies only through accepted remote version"); File.WriteAllText(binary, "two"); Assert(!accepted.IsAccepted(plugin, VersionValue.Parse("2.0")), "accepted version invalidated by local sha");
            Directory.Delete(root, true);
        }
        private static void Assert(bool condition, string name) { if (!condition) throw new InvalidOperationException(name); _count++; }
    }
}
