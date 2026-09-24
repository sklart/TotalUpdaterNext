using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using TotalUpdater.Next.Catalog;
using TotalUpdater.Next.Core;
using TotalUpdater.Next.Core.Versions;
using TotalUpdater.Next.Infrastructure;
using TotalUpdater.Next.Sources;
using TotalUpdater.Next.TotalCommander;
using TotalUpdater.Next.UI;

namespace TotalUpdater.Next.Tests
{
    internal static partial class Program
    {
        private static string HarvestEvidencePath()
        {
            return Path.Combine(Environment.CurrentDirectory, "TotalUpdater.Next", "Catalog", "catalog-harvest-evidence.json");
        }

        private static int AuditFullCatalog()
        {
            var catalog = new CatalogService(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".json")).LoadWithDiagnostics();
            var entries = catalog.Entries; var aliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase); var collisions = 0;
            foreach (var entry in entries) foreach (var alias in entry.Aliases ?? new List<string>()) { string owner; if (aliases.TryGetValue(alias, out owner) && !owner.Equals(entry.Id, StringComparison.OrdinalIgnoreCase)) collisions++; else aliases[alias] = entry.Id; }
            Console.WriteLine("Total entries: " + entries.Count);
            foreach (var type in new[] { PluginType.Wcx, PluginType.Wlx, PluginType.Wfx, PluginType.Wdx }) Console.WriteLine(type + ": " + entries.Count(x => x.PluginType == type));
            foreach (var evidence in new[] { IdentityEvidence.VerifiedBinary, IdentityEvidence.VerifiedPackage, IdentityEvidence.MetadataOnly }) Console.WriteLine(evidence + ": " + entries.Count(x => x.IdentityEvidence == evidence));
            Console.WriteLine("Missing source: " + entries.Count(x => x.Sources == null || x.Sources.Count == 0)); Console.WriteLine("Alias collisions: " + collisions);
            return catalog.Diagnostics.Count(x => x.Severity == CatalogDiagnosticSeverity.Error) == 0 && collisions == 0 ? 0 : 1;
        }
        private static int AuditWcxRegistration()
        {
            var catalog = new CatalogService(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".json")).Load(); var report = WcxRegistrationAudit.Audit(catalog);
            Console.WriteLine("WCX total=" + report.WcxTotal + "; VerifiedRegistration=" + report.VerifiedRegistration + "; MissingPackage=" + report.MissingPackage + "; MissingPluginst=" + report.MissingPluginst + "; MissingDefaultExtension=" + report.MissingDefaultExtension + "; ProbeFailed=" + report.ProbeFailed + "; ArchitectureMismatch=" + report.ArchitectureMismatch + "; HashMismatch=" + report.HashMismatch + "; AmbiguousBinary=" + report.AmbiguousBinary);
            return 0;
        }
        private static int ValidateHarvestEvidence()
        {
            var path = HarvestEvidencePath();
            if (!File.Exists(path)) { Console.Error.WriteLine("Evidence file not found: " + path); return 1; }
            var items = HarvestEvidenceAudit.Read(path);
            var diagnostics = HarvestEvidenceAudit.Validate(items, DateTime.UtcNow);
            foreach (var diagnostic in diagnostics) Console.WriteLine(diagnostic);
            var errors = diagnostics.Count(x => !x.EndsWith("(warning)", StringComparison.Ordinal));
            Console.WriteLine("Harvest evidence: entries=" + items.Count + "; errors=" + errors + "; warnings=" + (diagnostics.Count - errors));
            return errors == 0 ? 0 : 1;
        }

        private static void SourceFidelityRegressions()
        {
            var catalog = new CatalogService(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".json"));
            var unknown = new InstalledPlugin { Identity = new PluginIdentity { Id = "chmdir" }, Architecture = PluginArchitecture.X86,
                LocalVersion = FileVersionProbe.Create("99.0", VersionSource.FileVersion, VersionConfidence.Exact) };
            var service = new UpdateService(catalog, new IUpdateSourceProvider[] { new FixedRemoteProvider("1.0") });
            Assert(service.CheckAsync(unknown, CancellationToken.None).GetAwaiter().GetResult().State == UpdateState.LocalAheadUnknown, "unknown local-ahead policy never claims development");
            var total7zip = Total7zipVersionStrategy.FromPeFileVersion("0, 8, 5, 6");
            Assert(total7zip.ParsedValue.CompareTo(VersionValue.Parse("8.56")) == VersionComparison.Equal && total7zip.Source == VersionSource.CustomRule,
                "Total7zip PE 0,8,5,6 normalizes to public 8.56");
            var normalizedPlugin = new InstalledPlugin { Identity = new PluginIdentity { Id = "total7zip" }, Architecture = PluginArchitecture.X86,
                LocalVersion = total7zip };
            var normalizedResult = new UpdateService(catalog, new IUpdateSourceProvider[] { new FidelityVersionProvider() })
                .CheckAsync(normalizedPlugin, CancellationToken.None, null, SourceQueryMode.AuditAllSources).GetAwaiter().GetResult();
            Assert(normalizedResult.State == UpdateState.UpToDate && !normalizedResult.HasSourceDisagreement,
                "Total7zip official and PE-format community versions agree after normalization");
            var imagine = catalog.FindById("imagine");
            var author = imagine.Sources.First(x => x.AuthorityValue == SourceAuthority.OfficialAuthor);
            var html = "<strong data-lang-id=\"main_program\">Main Program</strong> <span class=\"label\"><span class=\"version\">v2.7.0</span>";
            Assert(GenericHtmlSourceProvider.Parse(author, html).Release.Version.Raw == "2.7.0", "Imagine real author markup version");
            var lagging = new InstalledPlugin { Identity = new PluginIdentity { Id = "ampview" }, Architecture = PluginArchitecture.X86,
                LocalVersion = FileVersionProbe.Create("99.0", VersionSource.FileVersion, VersionConfidence.Exact) };
            var lag = service.CheckAsync(lagging, CancellationToken.None).GetAwaiter().GetResult();
            Assert(lag.State == UpdateState.SourceOutdated && lag.DownloadUrl == null, "audited lag policy prevents downgrade");
            var old = new InstalledPlugin { Identity = new PluginIdentity { Id = "ampview" }, Architecture = PluginArchitecture.X86,
                LocalVersion = FileVersionProbe.Create("0.1", VersionSource.FileVersion, VersionConfidence.Exact) };
            var metadata = service.CheckAsync(old, CancellationToken.None).GetAwaiter().GetResult();
            Assert(metadata.State == UpdateState.UpdateAvailable && metadata.PackageAvailability == PackageAvailability.MetadataOnly && metadata.DownloadUrl == null && metadata.Details.Contains("Версия известна"), "metadata-only is distinct from unavailable source");
            var metadataRow = new PluginRowViewModel(old); metadataRow.Apply(metadata);
            Assert(metadataRow.Status.Contains("Метаданные доступны") && !metadataRow.CanDownload, "metadata-only UI status has no install action");
            var unavailableRow = new PluginRowViewModel(old); unavailableRow.Apply(new UpdateCandidate { Plugin = old, State = UpdateState.SourceUnavailable,
                Observations = new List<RemoteVersionObservation> { new RemoteVersionObservation { Source = new CatalogSource { Provider = "totalcmd.net-index" }, ProviderName = "TotalCmd index", Status = SourceQueryStatus.Unavailable } } });
            Assert(unavailableRow.Status == "Не удалось связаться с totalcmd.net", "unavailable source UI status is host-level and distinct");
            var notFoundRow = new PluginRowViewModel(old); notFoundRow.Apply(new UpdateCandidate { Plugin = old, State = UpdateState.PluginNotRecognized });
            var unknownVersionRow = new PluginRowViewModel(old); unknownVersionRow.Apply(new UpdateCandidate { Plugin = old, State = UpdateState.VersionComparisonUnknown });
            Assert(notFoundRow.Status == "Запись отсутствует в каталоге" && unknownVersionRow.Status == "Версия не определена", "CatalogNotFound and VersionUnknown stay distinct in UI");
            var diagnostic = new SourceDiagnosticResult { Name = "totalcmd.net index", IsAvailable = false, ElapsedMilliseconds = 123, Details = "timeout after 20 s" };
            Assert(diagnostic.Status == "FAIL" && diagnostic.ResponseTime == "123 мс", "source diagnostic exposes status and response time");
            var secondUnavailable = new PluginRowViewModel(old); secondUnavailable.Apply(new UpdateCandidate { Plugin = old, State = UpdateState.SourceUnavailable,
                Observations = new List<RemoteVersionObservation> { new RemoteVersionObservation { Source = new CatalogSource { Provider = "totalcmd.net" }, ProviderName = "TotalCmd page", Status = SourceQueryStatus.Unavailable } } });
            var summary = MainViewModel.BuildCheckSummary(new[] { unavailableRow, secondUnavailable, metadataRow });
            Assert(summary.Contains("Проверено: 3") && summary.Contains("Обновлений: 1") && summary.Contains("Источники недоступны: totalcmd.net") && summary.IndexOf("totalcmd.net", StringComparison.OrdinalIgnoreCase) == summary.LastIndexOf("totalcmd.net", StringComparison.OrdinalIgnoreCase), "global check summary deduplicates unavailable source hosts");
            Assert(HarvestEvidenceAudit.Validate(new[] { new HarvestEvidence { Id = "sample", Type = "Wlx", Aliases = new List<string> { "sample.wlx" }, PackageUrl = "https://example.test/sample.zip", VerifiedUtc = "2020-01-01" } }, DateTime.UtcNow).Any(x => x.Contains("stale harvest evidence")), "stale evidence warning");
            Assert(HarvestEvidenceAudit.Validate(new[] {
                new HarvestEvidence { Id = "x", Type = "Wlx", Aliases = new List<string> { "X.wlx" }, PackageUrl = "https://example.test/x.zip", VerifiedUtc = "2026-09-23" },
                new HarvestEvidence { Id = "X", Type = "Wlx", Aliases = new List<string> { "x.WLX" }, PackageUrl = "bad", VerifiedUtc = "bad" }
            }, new DateTime(2026, 9, 23)).Count(x => !x.EndsWith("(warning)", StringComparison.Ordinal)) >= 4, "harvest duplicate id/alias, URL and date validation");
            Assert(HarvestEvidenceAudit.Validate(new[] { new HarvestEvidence { Id = "bad-type", Type = "Unknown", Aliases = new List<string> { "bad.wlx" },
                PackageUrl = "https://example.test/bad.zip", VerifiedUtc = "2026-09-23" } }, new DateTime(2026, 9, 23)).Any(x => x.Contains("invalid type")),
                "harvest invalid type validation");
            var wcxAudit = WcxRegistrationAudit.Audit(new[] { new PluginCatalogEntry { Id = "verified", Name = "Verified", Type = "Wcx", IdentityEvidenceName = "VerifiedPackage", WcxRegistration = new WcxRegistrationEvidence { Extensions = new List<string> { "7z" }, PackerCaps = 1, PackageSha256 = new string('a', 64), BinarySha256 = new string('b', 64), VerifiedUtc = DateTime.UtcNow, Source = "test" } }, new PluginCatalogEntry { Id = "missing", Name = "Missing", Type = "Wcx", Sources = new List<CatalogSource>() } });
            Assert(wcxAudit.WcxTotal == 2 && wcxAudit.VerifiedRegistration == 1 && wcxAudit.MissingPackage == 1, "WCX registration audit distinguishes verified and missing package evidence");
            var remoteEntry = RemoteCatalogLookup.BuildVerifiedEntry(new[] { new RemoteIndexCandidate { Id = "Sample", Name = "Sample", Type = PluginType.Wlx,
                Official = true, PackageUrl = "https://plugins.ghisler.com/lsplugins/sample.zip" } }, "sample.wlx");
            Assert(remoteEntry.Sources.Single().EphemeralVerifiedPackageUrl.EndsWith("sample.zip") && remoteEntry.Sources.Single().PurposeValue == SourcePurpose.MetadataAndDownload,
                "RemoteExact retains inspected Ghisler ZIP as transient package evidence");
            using (var stream = new MemoryStream())
            {
                using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, true)) zip.CreateEntry("other/wrong.wlx");
                stream.Position = 0;
                Assert(!PackageIdentityVerifier.ContainsCatalogAlias(stream, new PluginCatalogEntry { Id = "sample", Type = "Wlx", Aliases = new List<string> { "sample.wlx" } }),
                    "package identity mismatch blocks verified status");
            }
        }

        private static void PersistentSourceCacheContracts()
        {
            var root = NewRoot();
            try
            {
                const string key = "totalcmd.net:index";
                const string url = "https://totalcmd.net/get_plugins_list.php";
                var bytes = Encoding.UTF8.GetBytes("sample|x|2.0|x|x|x|x\n");
                var persistent = new PersistentSourceCache(root);
                var cache = new SourceResponseCache(persistent);
                var live = cache.GetSharedMetadataAsync("totalcmd.net", key, url, () => Task.FromResult(bytes), Encoding.UTF8.GetString, "text/plain", "utf-8").GetAwaiter().GetResult();
                CachedSourceResponse stored;
                Assert(!live.IsCached && persistent.TryRead(key, out stored) && stored.SourceUrl == url && stored.Sha256.Length == 64 && stored.Bytes.SequenceEqual(bytes), "live success writes persistent raw response cache");

                var firstAttempts = 0; var retryAttempts = 0;
                var retry = new SourceResponseCache(new PersistentSourceCache(Path.Combine(root, "retry"))).GetSharedMetadataAsync("totalcmd.net", key, url,
                    () => { firstAttempts++; return Task.FromException<byte[]>(new TimeoutException("timeout")); }, () => { retryAttempts++; return Task.FromResult(bytes); },
                    Encoding.UTF8.GetString, "text/plain", "utf-8").GetAwaiter().GetResult();
                Assert(firstAttempts == 1 && retryAttempts == 1 && !retry.IsCached, "timeout uses exactly one distinct retry attempt and accepts live success");

                var fallbackPersistent = new PersistentSourceCache(Path.Combine(root, "fallback"));
                fallbackPersistent.Save(key, url, bytes, "text/plain", "utf-8"); var fallbackCalls = 0;
                var fallback = new SourceResponseCache(fallbackPersistent).GetSharedMetadataAsync("totalcmd.net", key, url,
                    () => { fallbackCalls++; return Task.FromException<byte[]>(new TimeoutException("timeout")); }, Encoding.UTF8.GetString, "text/plain", "utf-8").GetAwaiter().GetResult();
                Assert(fallbackCalls == 2 && fallback.IsCached && fallback.CachedAt.HasValue && !fallback.IsStale, "two timeouts use last-known-good cache with provenance");

                var perRun = new SourceResponseCache(new PersistentSourceCache(Path.Combine(root, "per-run"))); var perRunCalls = 0;
                try { perRun.GetOrAdd("faulted", () => { perRunCalls++; return Task.FromException<string>(new TimeoutException()); }).GetAwaiter().GetResult(); } catch (TimeoutException) { }
                var recovered = perRun.GetOrAdd("faulted", () => { perRunCalls++; return Task.FromResult("ok"); }).GetAwaiter().GetResult();
                Assert(perRunCalls == 2 && recovered == "ok", "faulted per-run source entry is removed and can retry");

                var sharedPersistent = new PersistentSourceCache(Path.Combine(root, "shared")); sharedPersistent.Save(key, url, bytes); var sharedCalls = 0;
                var shared = new SourceResponseCache(sharedPersistent);
                Task.WaitAll(Enumerable.Range(0, 40).Select(_ => shared.GetSharedMetadataAsync("totalcmd.net", key, url,
                    () => { Interlocked.Increment(ref sharedCalls); return Task.FromException<byte[]>(new TimeoutException()); }, Encoding.UTF8.GetString, "text/plain", "utf-8")).ToArray());
                Assert(sharedCalls == 2, "forty plugins make no more than two live shared-index attempts");

                var isolated = new SourceResponseCache(new PersistentSourceCache(Path.Combine(root, "isolated")));
                try { isolated.GetSharedMetadataAsync("totalcmd.net", key, url, () => Task.FromException<byte[]>(new TimeoutException()), Encoding.UTF8.GetString, "text/plain", "utf-8").GetAwaiter().GetResult(); }
                catch (InvalidOperationException) { }
                var otherHost = isolated.GetSharedMetadataAsync("ghisler.com", "ghisler:plugins", "https://www.ghisler.com/plugins.htm", () => Task.FromResult(bytes), Encoding.UTF8.GetString, "text/html", "utf-8").GetAwaiter().GetResult();
                Assert(!otherHost.IsCached && isolated.Health.IsUnavailable("totalcmd.net") && !isolated.Health.IsUnavailable("ghisler.com"), "one failed host does not block another shared source");

                var stalePersistent = new PersistentSourceCache(Path.Combine(root, "stale")); stalePersistent.Save(key, url, bytes, fetchedUtc: DateTime.UtcNow.AddDays(-181));
                var stale = new SourceResponseCache(stalePersistent).GetSharedMetadataAsync("totalcmd.net", key, url,
                    () => Task.FromException<byte[]>(new TimeoutException()), Encoding.UTF8.GetString, "text/plain", "utf-8").GetAwaiter().GetResult();
                Assert(stale.IsCached && stale.IsStale, "cache older than 180 days is marked stale");

                var corruptPersistent = new PersistentSourceCache(Path.Combine(root, "corrupt")); corruptPersistent.Save(key, url, bytes);
                File.WriteAllText(Directory.GetFiles(Path.Combine(root, "corrupt")).Single(), "not a cache envelope");
                CachedSourceResponse corrupt; Assert(!corruptPersistent.TryRead(key, out corrupt), "corrupted cache is ignored");

                var replacePersistent = new PersistentSourceCache(Path.Combine(root, "replace")); replacePersistent.Save(key, url, Encoding.UTF8.GetBytes("old"));
                new SourceResponseCache(replacePersistent).GetSharedMetadataAsync("totalcmd.net", key, url, () => Task.FromResult(Encoding.UTF8.GetBytes("new")), Encoding.UTF8.GetString, "text/plain", "utf-8").GetAwaiter().GetResult();
                CachedSourceResponse replaced; Assert(replacePersistent.TryRead(key, out replaced) && Encoding.UTF8.GetString(replaced.Bytes) == "new", "live result atomically replaces old cache");

                var user = Path.Combine(root, "cached-package.json");
                File.WriteAllText(user, "[{\"id\":\"cached\",\"name\":\"Cached\",\"type\":\"Wlx\",\"aliases\":[\"cached.wlx\"],\"identityEvidence\":\"VerifiedPackage\",\"sources\":[{\"provider\":\"generic-html\",\"url\":\"https://example.test/meta\",\"versionPattern\":\"([0-9.]+)\",\"authority\":\"OfficialAuthor\",\"purpose\":\"MetadataAndDownload\",\"priority\":100}]}]");
                var cachedCandidate = new UpdateService(new CatalogService(user), new IUpdateSourceProvider[] { new CachedPackageProvider() }).CheckAsync(
                    new InstalledPlugin { Identity = new PluginIdentity { Id = "cached" }, Architecture = PluginArchitecture.X86, LocalVersion = FileVersionProbe.Create("1.0", VersionSource.FileVersion, VersionConfidence.Exact) }, CancellationToken.None).GetAwaiter().GetResult();
                Assert(cachedCandidate.State == UpdateState.UpdateAvailable && cachedCandidate.IsCached && cachedCandidate.DownloadUrl == null && cachedCandidate.PackageAvailability == PackageAvailability.MetadataOnly, "cached metadata can compare version but cannot enable install: " + cachedCandidate.State + "/" + cachedCandidate.IsCached + "/" + cachedCandidate.PackageAvailability + "/" + cachedCandidate.Details);
            }
            finally { Directory.Delete(root, true); }
        }

        private sealed class CachedPackageProvider : IUpdateSourceProvider
        {
            public string Name { get { return "cached package fixture"; } }
            public bool CanHandle(CatalogSource source) { return source != null && source.Provider == "generic-html"; }
            public Task<SourceQueryResult> QueryAsync(CatalogSource source, CancellationToken token)
            {
                return Task.FromResult(new SourceQueryResult { Status = SourceQueryStatus.Success, IsCached = true, CachedAt = DateTime.UtcNow,
                    Release = new RemoteRelease { VersionText = "2.0", Version = VersionValue.Parse("2.0"), SourceUrl = new Uri("https://example.test/meta"),
                        Packages = new List<RemotePackage> { new RemotePackage { Architecture = RemotePackageArchitecture.X86, Url = new Uri("https://example.test/package.zip") } } } });
            }
        }

        private sealed class FidelityVersionProvider : IUpdateSourceProvider
        {
            public string Name { get { return "fidelity fixture"; } }
            public bool CanHandle(CatalogSource source) { return true; }
            public Task<SourceQueryResult> QueryAsync(CatalogSource source, CancellationToken token)
            {
                var raw = source.AuthorityValue == SourceAuthority.OfficialTotalCommander ? "8.56" : "0.8.5.6";
                return Task.FromResult(new SourceQueryResult { Status = SourceQueryStatus.Success,
                    Release = new RemoteRelease { VersionText = raw, Version = VersionValue.Parse(raw), Packages = new List<RemotePackage>() } });
            }
        }

        private static int AuditCatalogVersionFidelity()
        {
            var catalog = new CatalogService(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".json"));
            var entries = catalog.Load();
            var installed = new Dictionary<string, InstalledPlugin>(StringComparer.OrdinalIgnoreCase);
            try
            {
                var config = new TotalCommanderConfigurationResolver().Resolve(null);
                if (config != null && File.Exists(config.IniPath))
                    foreach (var plugin in new PluginDiscoveryService(new TotalCommanderConfigurationResolver(), new LocalVersionResolver(), catalog).Discover(config))
                        if (plugin.Identity != null && !installed.ContainsKey(plugin.Identity.Id)) installed.Add(plugin.Identity.Id, plugin);
            }
            catch (Exception ex) { Console.WriteLine("Installed discovery: " + ex.Message); }
            var output = new ConcurrentBag<string>();
            var categories = new ConcurrentDictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            using (var http = new HttpService(ApplicationMetadata.Version))
            using (var gate = new SemaphoreSlim(4))
            {
                var providers = new IUpdateSourceProvider[] { new TotalCmdNetSourceProvider(http), new TotalCmdNetIndexProvider(http), new GhislerSourceProvider(http), new GhislerPluginsSourceProvider(http), new GitHubReleaseSourceProvider(http), new GenericHtmlSourceProvider(http) };
                var service = new UpdateService(catalog, providers); var cache = new SourceResponseCache();
                var tasks = entries.Select(async entry =>
                {
                    await gate.WaitAsync().ConfigureAwait(false);
                    try
                    {
                        InstalledPlugin plugin;
                        if (!installed.TryGetValue(entry.Id, out plugin)) plugin = new InstalledPlugin { Identity = new PluginIdentity { Id = entry.Id }, Type = entry.PluginType, LocalVersion = LocalVersion.Unknown };
                        var candidate = await service.CheckAsync(plugin, CancellationToken.None, cache, SourceQueryMode.AuditAllSources).ConfigureAwait(false);
                        var packages = candidate.Observations.Where(x => x.Release != null && x.Release.Packages != null &&
                            x.Release.Version.CompareTo(candidate.AvailableVersion) == VersionComparison.Equal)
                            .SelectMany(x => x.Release.Packages).Where(x => x.Url != null).GroupBy(x => x.Url.AbsoluteUri).Select(x => x.First()).ToList();
                        var verified = new HashSet<string>(StringComparer.OrdinalIgnoreCase); var mismatched = false; var reachable = false;
                        var packageFindings = new List<string>();
                        foreach (var package in packages)
                        {
                            try
                            {
                                if (entry.PluginType == PluginType.TotalCommander)
                                { packageFindings.Add(package.Url.AbsoluteUri + "=installer/non-ZIP"); reachable = true; continue; }
                                var bytes = await cache.GetOrAddBytes("fidelity:package:" + package.Url.AbsoluteUri,
                                    () => ReadBoundedArchiveAsync(http, package.Url)).ConfigureAwait(false);
                                IList<string> names;
                                using (var zip = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read))
                                    names = zip.Entries.Select(x => Path.GetFileName(x.FullName)).Where(IsPluginBinary).ToList();
                                reachable = true;
                                var matches = entry.Aliases.Where(alias => names.Contains(alias, StringComparer.OrdinalIgnoreCase)).ToList();
                                foreach (var alias in matches) verified.Add(alias);
                                if (matches.Count == 0) mismatched = true;
                                packageFindings.Add(package.Url.AbsoluteUri + "=" + (matches.Count == 0 ? "identity mismatch" : String.Join(",", matches)));
                            }
                            catch (Exception ex) { packageFindings.Add(package.Url.AbsoluteUri + "=unavailable (" + ex.GetType().Name + ")"); }
                        }
                        var status = candidate.CanonicalVersionSource == null ? "SourceUnavailable" :
                            candidate.AuthorityConflict || candidate.HasSourceDisagreement ? "VersionDisagreement" :
                            candidate.State == UpdateState.SourceOutdated ? "SourceLagging" :
                            mismatched ? "PackageIdentityMismatch" :
                            packages.Count == 0 || !reachable ? "PackageMissing" :
                            candidate.CanonicalVersionSource.Authority <= SourceAuthority.CommunityCatalog ? "NeedsOfficialSource" : "OK";
                        categories.AddOrUpdate(status, 1, (key, count) => count + 1);
                        var rawPe = plugin.Binaries.Count == 0 ? "—" : String.Join(",", plugin.Binaries.Where(x => x.Exists).Select(x =>
                        {
                            try { return System.Diagnostics.FileVersionInfo.GetVersionInfo(x.Path).FileVersion ?? "—"; }
                            catch { return "—"; }
                        }));
                        output.Add(entry.Id + " | " + status + " | source=" + (candidate.CanonicalVersionSource == null ? "—" : candidate.CanonicalVersionSource.ProviderName) +
                            " | authority=" + (candidate.CanonicalVersionSource == null ? "—" : candidate.CanonicalVersionSource.Authority.ToString()) +
                            " | reported=" + (candidate.AvailableVersion.IsKnown ? candidate.AvailableVersion.Raw : "—") +
                            " | source statuses=" + String.Join(",", candidate.Observations.Select(x => (x.Source == null ? x.ProviderName : x.Source.Provider) + ":" + x.Status)) +
                            " | package=" + (mismatched ? "Ambiguous" : verified.Count > 0 || entry.PluginType == PluginType.TotalCommander && reachable ? "Verified" : packages.Count > 0 ? "Unavailable" : "MetadataOnly") +
                            " | verified aliases=" + String.Join(",", verified) + " | raw/local=" + rawPe + "/" + plugin.LocalVersion.RawValue +
                            " | packages=" + String.Join(";", packageFindings));
                    }
                    catch (Exception ex) { categories.AddOrUpdate("SourceUnavailable", 1, (key, count) => count + 1); output.Add(entry.Id + " | SourceUnavailable | " + ex.Message); }
                    finally { gate.Release(); }
                }).ToArray();
                Task.WaitAll(tasks);
            }
            foreach (var line in output.OrderBy(x => x, StringComparer.OrdinalIgnoreCase)) Console.WriteLine(line);
            if (File.Exists(HarvestEvidencePath()))
                foreach (var warning in HarvestEvidenceAudit.Validate(HarvestEvidenceAudit.Read(HarvestEvidencePath()), DateTime.UtcNow)
                    .Where(x => x.EndsWith("(warning)", StringComparison.Ordinal))) Console.WriteLine("Evidence freshness: " + warning);
            Console.WriteLine("Entries=" + entries.Count + "; " + String.Join("; ", categories.OrderBy(x => x.Key).Select(x => x.Key + "=" + x.Value)));
            return 0; // Findings are maintenance data; only offline schema/contract failures gate CI.
        }

        private static async Task<byte[]> ReadBoundedArchiveAsync(HttpService http, Uri url)
        {
            using (var response = await http.GetAsync(url.AbsoluteUri, HttpCompletionOption.ResponseHeadersRead, CancellationToken.None).ConfigureAwait(false))
            {
                response.EnsureSuccessStatusCode();
                if (response.Content.Headers.ContentLength > 32 * 1024 * 1024) throw new InvalidDataException("Package exceeds audit limit.");
                using (var source = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
                using (var target = new MemoryStream())
                {
                    var buffer = new byte[32768]; int read;
                    while ((read = await source.ReadAsync(buffer, 0, buffer.Length).ConfigureAwait(false)) > 0)
                    { if (target.Length + read > 32 * 1024 * 1024) throw new InvalidDataException("Package exceeds audit limit."); target.Write(buffer, 0, read); }
                    return target.ToArray();
                }
            }
        }
    }
}
