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
using TotalUpdater.Next.Core.Installation;
using TotalUpdater.Next.Core.Versions;
using TotalUpdater.Next.Infrastructure;
using TotalUpdater.Next.Infrastructure.Installation;
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
        private static int AuditWcxRegistration(string[] args)
        {
            var catalog = new CatalogService(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".json")).Load();
            var input = CommandValue(args, "--input");
            var findings = String.IsNullOrWhiteSpace(input)
                ? catalog.Where(x => x.PluginType == PluginType.Wcx && x.WcxRegistration != null).Select(x => new WcxRegistrationHarvestFinding { Id = x.Id, Status = WcxRegistrationHarvest.VerifiedRegistration, Evidence = x.WcxRegistration }).ToList()
                : WcxRegistrationHarvest.Read(input);
            PrintWcxRegistrationAudit(WcxRegistrationAudit.Audit(catalog, findings));
            return 0;
        }
        private static void PrintWcxRegistrationAudit(WcxRegistrationAuditReport report)
        {
            Console.WriteLine("WCX total=" + report.WcxTotal + "; Downloadable=" + report.Downloadable + "; VerifiedRegistration=" + report.VerifiedRegistration + "; MissingPackage=" + report.MissingPackage + "; MissingPluginst=" + report.MissingPluginst + "; MissingDefaultExtension=" + report.MissingDefaultExtension + "; UnsupportedArchive=" + report.UnsupportedArchive + "; AmbiguousBinary=" + report.AmbiguousBinary + "; ProbeFailed=" + report.ProbeFailed + "; ProbeTimeout=" + report.ProbeTimeout + "; ProbeArchitectureMismatch=" + report.ProbeArchitectureMismatch + "; CapsMismatch=" + report.CapsMismatch + "; PackageIdentityMismatch=" + report.PackageIdentityMismatch + "; HashMismatch=" + report.HashMismatch + "; SourceUnavailable=" + report.SourceUnavailable + "; NotHarvested=" + report.NotHarvested);
        }
        private static int ValidateWcxRegistrationEvidence()
        {
            var catalog = new CatalogService(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".json")).Load();
            var diagnostics = WcxRegistrationEvidenceAudit.Validate(catalog);
            foreach (var diagnostic in diagnostics) Console.WriteLine(diagnostic);
            Console.WriteLine("WCX registration evidence: entries=" + catalog.Count(x => x.WcxRegistration != null) + "; errors=" + diagnostics.Count);
            return diagnostics.Count == 0 ? 0 : 1;
        }
        // Maintenance-only live verification. It never uses a discovered/user INI: every
        // install is performed into a fresh temporary TC layout and rolled back immediately.
        private static int VerifyWcxInstallE2E(string[] args)
        {
            var rawIds = CommandValue(args, "--ids");
            var ids = (rawIds ?? "").Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim()).Where(x => x.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (ids.Count == 0) { Console.Error.WriteLine("Использование: --verify-wcx-install-e2e --ids <id1,id2,...>"); return 1; }
            var root = Path.Combine(Path.GetTempPath(), "TotalUpdaterNext", "wcx-e2e", Guid.NewGuid().ToString("N"));
            var failures = 0;
            Directory.CreateDirectory(root);
            try
            {
                var catalog = new CatalogService(Path.Combine(root, "user-catalog.json")).Load();
                using (var http = new HttpService(ApplicationMetadata.Version))
                {
                    var candidates = new CatalogInstallService(UpdateSourceProviderFactory.Create(http));
                    var downloads = new DownloadService(http);
                    foreach (var id in ids)
                    {
                        PackageInspection package = null;
                        try
                        {
                            var entry = catalog.FirstOrDefault(x => String.Equals(x.Id, id, StringComparison.OrdinalIgnoreCase));
                            if (entry == null || entry.PluginType != PluginType.Wcx || !entry.HasVerifiedWcxRegistration) throw new InvalidOperationException("Нет verified WCX evidence.");
                            var candidate = candidates.CheckAsync(entry, CancellationToken.None).GetAwaiter().GetResult();
                            if (candidate.DownloadUrl == null || !String.Equals(candidate.DownloadUrl.AbsoluteUri, entry.WcxRegistration.PackageUrl, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Live download source не совпадает с hash-bound evidence.");
                            var packagePath = downloads.DownloadAsync(candidate.DownloadUrl, Path.Combine(root, "downloads"), CancellationToken.None).GetAwaiter().GetResult();
                            package = new PackageInspector().Inspect(packagePath);
                            if (!String.Equals(package.PackageSha256, entry.WcxRegistration.PackageSha256, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Live package SHA-256 не совпадает с evidence.");
                            var tc = Path.Combine(root, id, "tc"); Directory.CreateDirectory(tc); File.WriteAllBytes(Path.Combine(tc, "TOTALCMD.EXE"), new byte[] { 0 });
                            var ini = Path.Combine(tc, "wincmd.ini"); File.WriteAllText(ini, "[PackerPlugins]" + Environment.NewLine);
                            var configuration = new TotalCommanderConfiguration { IniPath = ini, InstallDirectory = tc, Document = new IniDocumentReader().Read(ini) };
                            var plan = new NewPluginInstallPlanBuilder().Build(entry, candidate, package, configuration, new InstalledPlugin[0], Path.Combine(root, id, "backups"));
                            var originalIni = File.ReadAllBytes(ini);
                            var manifest = new NewPluginTransactionalInstaller(isTotalCommanderRunning: () => false).Install(plan, () => E2ERediscover(entry, plan, ini));
                            var registered = new IniDocumentReader().Read(ini).GetSection("PackerPlugins");
                            if (manifest.State != InstallStateMachine.Completed || !plan.ConfigurationFiles.SelectMany(x => x.Changes).All(x => registered.GetValue(x.Key) == x.Value)) throw new InvalidOperationException("PackerPlugins registration не подтверждена.");
                            new NewPluginRollbackService().Rollback(manifest, Path.Combine(root, id, "backups"));
                            if (!originalIni.SequenceEqual(File.ReadAllBytes(ini)) || File.Exists(plan.PrimaryPath)) throw new InvalidOperationException("Rollback не восстановил тестовую TC-конфигурацию.");
                            Console.WriteLine("PASS " + id + " package/evidence/plan/PackerPlugins/install/rediscovery/rollback");
                        }
                        catch (Exception ex) { failures++; Console.WriteLine("FAIL " + id + " " + ex.Message); }
                        finally { if (package != null && Directory.Exists(package.StagingDirectory)) { try { Directory.Delete(package.StagingDirectory, true); } catch { } } }
                    }
                }
            }
            finally { if (Directory.Exists(root)) { try { Directory.Delete(root, true); } catch { } } }
            return failures == 0 ? 0 : 1;
        }
        private static InstalledPlugin E2ERediscover(PluginCatalogEntry entry, NewPluginInstallPlan plan, string ini)
        {
            var section = new IniDocumentReader().Read(ini).GetSection("PackerPlugins");
            if (section == null || !plan.ConfigurationFiles.SelectMany(x => x.Changes).All(x => String.Equals(section.GetValue(x.Key), x.Value, StringComparison.OrdinalIgnoreCase))) return null;
            return new InstalledPlugin { Identity = new PluginIdentity { Id = entry.Id }, Type = PluginType.Wcx, PrimaryPath = plan.PrimaryPath, FileExists = File.Exists(plan.PrimaryPath),
                Binaries = plan.RequiredBinaryPaths.Select(x => new PluginBinary { Path = x, Exists = File.Exists(x), Architecture = x.EndsWith("64", StringComparison.OrdinalIgnoreCase) ? PluginArchitecture.X64 : PluginArchitecture.X86, LocalVersion = FileVersionProbe.Create(plan.Version, VersionSource.FileVersion, VersionConfidence.Exact) }).ToList(),
                LocalVersion = FileVersionProbe.Create(plan.Version, VersionSource.FileVersion, VersionConfidence.Exact) };
        }
        private static int MergeWcxRegistrationEvidence(string[] args)
        {
            var input = CommandValue(args, "--input");
            var dryRun = args != null && args.Any(x => String.Equals(x, "--dry-run", StringComparison.OrdinalIgnoreCase));
            if (String.IsNullOrWhiteSpace(input) || !File.Exists(input)) { Console.Error.WriteLine("Использование: --merge-wcx-registration-evidence --input <evidence.json> [--dry-run]"); return 1; }
            var catalogPath = Path.Combine(Environment.CurrentDirectory, "TotalUpdater.Next", "Catalog", "plugin-catalog.json");
            var entries = CatalogJsonFile.Read(catalogPath);
            var findings = WcxRegistrationHarvest.Read(input);
            var plan = WcxRegistrationEvidenceMerger.Plan(entries, findings);
            foreach (var item in plan) Console.WriteLine(item.Action.ToString().ToUpperInvariant() + " " + item.Id + (String.IsNullOrWhiteSpace(item.Detail) ? "" : " " + item.Detail));
            if (plan.Any(x => x.Action == WcxRegistrationMergeAction.Reject)) return 1;
            if (dryRun) { Console.WriteLine("Dry run: no catalog changes."); return 0; }
            string error;
            if (!WcxRegistrationEvidenceMerger.TryApply(entries, findings, out error)) { Console.Error.WriteLine(error); return 1; }
            var catalogDiagnostics = CatalogService.Validate(entries);
            if (catalogDiagnostics.Any(x => x.Severity == CatalogDiagnosticSeverity.Error)) { foreach (var item in catalogDiagnostics) Console.Error.WriteLine(item.EntryId + ": " + item.Message); return 1; }
            var aliasIssues = CatalogAliasAudit.Audit(entries);
            if (aliasIssues.Count != 0) { foreach (var item in aliasIssues) Console.Error.WriteLine(item.Kind + " | " + item.Alias + " | " + item.Entries); return 1; }
            var audit = WcxRegistrationEvidenceAudit.Validate(entries);
            if (audit.Count != 0) { foreach (var item in audit) Console.Error.WriteLine(item); return 1; }
            CatalogJsonFile.ReplaceWcxRegistrationEvidence(catalogPath, findings.Where(x => plan.Any(d => String.Equals(d.Id, x.Id, StringComparison.OrdinalIgnoreCase) && (d.Action == WcxRegistrationMergeAction.Add || d.Action == WcxRegistrationMergeAction.Update))));
            Console.WriteLine("Merged VerifiedRegistration=" + plan.Count(x => x.Action == WcxRegistrationMergeAction.Add || x.Action == WcxRegistrationMergeAction.Update));
            Console.WriteLine("Catalog validation: entries=" + entries.Count + "; errors=0");
            Console.WriteLine("Alias audit: entries=" + entries.Count + "; issues=0");
            PrintWcxRegistrationAudit(WcxRegistrationAudit.Audit(entries, findings));
            return 0;
        }
        private static int HarvestWcxRegistration(string[] args)
        {
            var id = CommandValue(args, "--id");
            var ids = CommandValue(args, "--ids");
            var output = CommandValue(args, "--output");
            var authorityName = CommandValue(args, "--authority");
            var all = args != null && args.Any(x => String.Equals(x, "--all", StringComparison.OrdinalIgnoreCase));
            var resume = args != null && args.Any(x => String.Equals(x, "--resume", StringComparison.OrdinalIgnoreCase));
            var selectorCount = (String.IsNullOrWhiteSpace(id) ? 0 : 1) + (String.IsNullOrWhiteSpace(ids) ? 0 : 1) + (all ? 1 : 0);
            if (selectorCount != 1 || String.IsNullOrWhiteSpace(output))
            {
                Console.Error.WriteLine("Использование: --harvest-wcx-registration (--all | --id <catalog-id> | --ids <id1,id2>) --output <evidence.json> [--resume]");
                return 1;
            }
            var catalog = new CatalogService(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".json"));
            var requestedIds = String.IsNullOrWhiteSpace(ids) ? new string[0] : ids.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim()).Where(x => x.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            var entries = all ? catalog.Load().Where(x => x.PluginType == PluginType.Wcx).ToList() : new[] { id }.Concat(requestedIds).Select(catalog.FindById).ToList();
            if (entries.Any(x => x == null || x.PluginType != PluginType.Wcx)) { Console.Error.WriteLine("Одна или несколько WCX-записей каталога не найдены."); return 1; }
            if (!String.IsNullOrWhiteSpace(authorityName))
            {
                SourceAuthority authority;
                if (!Enum.TryParse(authorityName, true, out authority)) { Console.Error.WriteLine("Неизвестный authority: " + authorityName); return 1; }
                entries = entries.Where(x => x.Sources != null && x.Sources.Any(s => s.AuthorityValue == authority)).ToList();
                if (entries.Count == 0) { Console.Error.WriteLine("Для authority нет WCX-записей: " + authorityName); return 1; }
            }
            var existing = resume ? WcxRegistrationHarvest.Read(output) : new List<WcxRegistrationHarvestFinding>();
            var root = Path.Combine(Path.GetTempPath(), "TotalUpdaterNext", "wcx-harvest", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            try
            {
                using (var http = new HttpService(ApplicationMetadata.Version))
                {
                    var providers = UpdateSourceProviderFactory.Create(http);
                    var candidates = new CatalogInstallService(providers);
                    var downloads = new DownloadService(http);
                    var harvester = new WcxRegistrationHarvester(
                        (candidateEntry, token) => candidates.CheckAsync(candidateEntry, token),
                        (url, token) => downloads.DownloadAsync(url, root, token));
                    var batch = new WcxRegistrationBatchHarvester((entry, token) => harvester.HarvestAsync(entry, token));
                    var result = batch.RunAsync(entries, existing, resume, 2,
                        step => Console.WriteLine("[" + step.Completed + "/" + step.Total + "] " + step.Entry.Name + " -> " + step.Finding.Status),
                        findings => WcxRegistrationHarvest.Write(output, findings), CancellationToken.None).GetAwaiter().GetResult();
                    if (result.Processed == 0 && !File.Exists(output)) WcxRegistrationHarvest.Write(output, result.Findings);
                    PrintWcxHarvestSummary(result);
                    return result.Count(WcxRegistrationHarvest.VerifiedRegistration) > 0 ? 0 : 2;
                }
            }
            finally
            {
                if (Directory.Exists(root)) { try { Directory.Delete(root, true); } catch { } }
            }
        }
        private static void PrintWcxHarvestSummary(WcxRegistrationBatchResult result)
        {
            Console.WriteLine("WCX total=" + result.Total + "; processed=" + result.Processed + "; skipped/resumed=" + result.Skipped +
                "; VerifiedRegistration=" + result.Count(WcxRegistrationHarvest.VerifiedRegistration) + "; MissingPackage=" + result.Count(WcxRegistrationHarvest.MissingPackage) +
                "; MissingPluginst=" + result.Count(WcxRegistrationHarvest.MissingPluginst) + "; MissingDefaultExtension=" + result.Count(WcxRegistrationHarvest.MissingDefaultExtension) +
                "; UnsupportedArchive=" + result.Count(WcxRegistrationHarvest.UnsupportedArchive) + "; AmbiguousBinary=" + result.Count(WcxRegistrationHarvest.AmbiguousBinary) +
                "; PackageIdentityMismatch=" + result.Count(WcxRegistrationHarvest.PackageIdentityMismatch) + "; ProbeFailed=" + result.Count(WcxRegistrationHarvest.ProbeFailed) +
                "; ProbeTimeout=" + result.Count(WcxRegistrationHarvest.ProbeTimeout) + "; ProbeArchitectureMismatch=" + result.Count(WcxRegistrationHarvest.ProbeArchitectureMismatch) +
                "; CapsMismatch=" + result.Count(WcxRegistrationHarvest.CapsMismatch) + "; HashMismatch=" + result.Count(WcxRegistrationHarvest.HashMismatch) +
                "; SourceUnavailable=" + result.Count(WcxRegistrationHarvest.SourceUnavailable));
        }
        private static string CommandValue(string[] args, string name)
        {
            if (args == null) return null;
            for (var index = 0; index + 1 < args.Length; index++) if (String.Equals(args[index], name, StringComparison.OrdinalIgnoreCase)) return args[index + 1];
            return null;
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
            var wcxAudit = WcxRegistrationAudit.Audit(new[] { new PluginCatalogEntry { Id = "verified", Name = "Verified", Type = "Wcx", IdentityEvidenceName = "VerifiedPackage", WcxRegistration = new WcxRegistrationEvidence { Extensions = new List<string> { "7z" }, PackerCaps = 0, PackageSha256 = new string('a', 64), X86BinarySha256 = new string('b', 64), VerifiedUtc = DateTime.UtcNow, Source = "test" } }, new PluginCatalogEntry { Id = "missing", Name = "Missing", Type = "Wcx", Sources = new List<CatalogSource>() } }, new[] { new WcxRegistrationHarvestFinding { Id = "verified", Status = WcxRegistrationHarvest.VerifiedRegistration }, new WcxRegistrationHarvestFinding { Id = "missing", Status = WcxRegistrationHarvest.MissingPackage } });
            Assert(wcxAudit.WcxTotal == 2 && wcxAudit.VerifiedRegistration == 1 && wcxAudit.MissingPackage == 1, "WCX registration audit distinguishes verified and missing package evidence");
            var mergeFinding = new WcxRegistrationHarvestFinding { Id = "verified", Status = WcxRegistrationHarvest.VerifiedRegistration, Evidence = new WcxRegistrationEvidence { PackageSha256 = new string('a', 64), X86BinarySha256 = new string('b', 64), PackerCaps = 0, Extensions = new List<string> { "7z" }, VerifiedUtc = DateTime.UtcNow, Source = "fixture" } };
            Assert(WcxRegistrationHarvest.IsValidVerifiedFinding(mergeFinding), "WCX merge fixture accepts valid caps-zero evidence");
            mergeFinding.Evidence.Extensions.Add("7Z"); Assert(!WcxRegistrationHarvest.IsValidVerifiedFinding(mergeFinding), "WCX merge fixture rejects duplicate extensions");
            var mergeEntry = new PluginCatalogEntry { Id = "merge-wcx", Name = "Merge WCX", Type = "Wcx", Version = "1.0", Aliases = new List<string> { "merge.wcx" }, Sources = new List<CatalogSource> { new CatalogSource { Provider = "fixture", Url = "https://example.test/merge.zip" } } };
            var originalName = mergeEntry.Name; var originalVersion = mergeEntry.Version; var originalAliases = String.Join("|", mergeEntry.Aliases); var originalSource = mergeEntry.Sources[0].Url;
            var validMerge = new WcxRegistrationHarvestFinding { Id = "merge-wcx", Status = WcxRegistrationHarvest.VerifiedRegistration, Evidence = new WcxRegistrationEvidence { PackageSha256 = new string('c', 64), X86BinarySha256 = new string('d', 64), PackerCaps = 0, Extensions = new List<string> { "zip" }, VerifiedUtc = DateTime.UtcNow, Source = "fixture" } };
            string mergeError;
            Assert(WcxRegistrationEvidenceMerger.TryApply(new List<PluginCatalogEntry> { mergeEntry }, new[] { validMerge }, out mergeError) && mergeEntry.WcxRegistration != validMerge.Evidence && mergeEntry.Name == originalName && mergeEntry.Version == originalVersion && String.Join("|", mergeEntry.Aliases) == originalAliases && mergeEntry.Sources[0].Url == originalSource, "WCX merge updates only registration evidence");
            Assert(!WcxRegistrationEvidenceMerger.TryApply(new List<PluginCatalogEntry> { mergeEntry }, new[] { validMerge, validMerge }, out mergeError), "WCX merge rejects duplicate verified findings atomically");
            var mergePlan = WcxRegistrationEvidenceMerger.Plan(new List<PluginCatalogEntry> { mergeEntry }, new[] { validMerge });
            Assert(mergePlan.Single().Action == WcxRegistrationMergeAction.Unchanged && WcxRegistrationEvidenceMerger.Plan(new List<PluginCatalogEntry> { new PluginCatalogEntry { Id = "merge-wcx", Type = "Wcx" } }, new[] { validMerge }).Single().Action == WcxRegistrationMergeAction.Add,
                "WCX dry-run merge reports UNCHANGED and ADD without mutating catalog");
            var updateEntry = new PluginCatalogEntry { Id = "update-wcx", Name = "Update WCX", Type = "Wcx", WcxRegistration = new WcxRegistrationEvidence { PackageSha256 = new string('1', 64), X86BinarySha256 = new string('2', 64), PackerCaps = 0, Extensions = new List<string> { "zip" }, VerifiedUtc = DateTime.UtcNow, Source = "fixture" } };
            var updateFinding = new WcxRegistrationHarvestFinding { Id = "update-wcx", Status = WcxRegistrationHarvest.VerifiedRegistration, Evidence = new WcxRegistrationEvidence { PackageSha256 = new string('3', 64), X86BinarySha256 = new string('4', 64), PackerCaps = 0, Extensions = new List<string> { "zip" }, VerifiedUtc = DateTime.UtcNow, Source = "fixture" } };
            var invalidEntry = new PluginCatalogEntry { Id = "invalid-wcx", Name = "Invalid WCX", Type = "Wcx" };
            var invalidFinding = new WcxRegistrationHarvestFinding { Id = "invalid-wcx", Status = WcxRegistrationHarvest.VerifiedRegistration, Evidence = new WcxRegistrationEvidence { PackageSha256 = new string('5', 64), X86BinarySha256 = new string('6', 64), PackerCaps = -1, Extensions = new List<string> { "zip" }, VerifiedUtc = DateTime.UtcNow, Source = "fixture" } };
            var updatePlan = WcxRegistrationEvidenceMerger.Plan(new List<PluginCatalogEntry> { updateEntry, invalidEntry }, new[] { updateFinding, invalidFinding });
            Assert(updatePlan.Single(x => x.Id == "update-wcx").Action == WcxRegistrationMergeAction.Update && updatePlan.Single(x => x.Id == "invalid-wcx").Action == WcxRegistrationMergeAction.Reject &&
                !WcxRegistrationEvidenceMerger.TryApply(new List<PluginCatalogEntry> { invalidEntry }, new[] { invalidFinding }, out mergeError) && invalidEntry.WcxRegistration == null,
                "WCX merge reports UPDATE and REJECT, while invalid evidence leaves catalog unchanged");
            Assert(WcxRegistrationEvidenceAudit.Validate(new[] { new PluginCatalogEntry { Id = "audit-wcx", Type = "Wcx", WcxRegistration = validMerge.Evidence } }).Count == 0 &&
                WcxRegistrationEvidenceAudit.Validate(new[] { new PluginCatalogEntry { Id = "audit-non-wcx", Type = "Wlx", WcxRegistration = validMerge.Evidence } }).Count == 1,
                "WCX embedded evidence audit accepts valid WCX and rejects non-WCX evidence");
            var evidencePath = Path.Combine(NewRoot(), "renamed-evidence.json");
            WcxRegistrationHarvest.Write(evidencePath, new[] { validMerge });
            var roundTrip = WcxRegistrationHarvest.Read(evidencePath).Single();
            Assert(roundTrip.Id == "merge-wcx" && roundTrip.Evidence.PackerCaps == 0 && roundTrip.Evidence.X86BinarySha256 == validMerge.Evidence.X86BinarySha256 && roundTrip.Evidence.VerifiedUtc != default(DateTime), "WCX evidence JSON round-trip preserves caps-zero hashes and verification UTC");
            var catalogRoundTripPath = Path.Combine(NewRoot(), "catalog-roundtrip.json");
            var catalogRoundTripEntries = new List<PluginCatalogEntry> { mergeEntry, new PluginCatalogEntry { Id = "plain-wlx", Name = "Plain WLX", Type = "Wlx", Version = "2.0", Aliases = new List<string> { "plain.wlx" } } };
            CatalogJsonFile.Write(catalogRoundTripPath, catalogRoundTripEntries);
            var catalogRoundTrip = CatalogJsonFile.Read(catalogRoundTripPath);
            Assert(catalogRoundTrip.Count == 2 && catalogRoundTrip.Single(x => x.Id == "plain-wlx").Aliases.Single() == "plain.wlx" &&
                WcxRegistrationEvidenceMerger.TryApply(catalogRoundTrip, new[] { validMerge }, out mergeError) && catalogRoundTrip.Single(x => x.Id == "merge-wcx").WcxRegistration != null,
                "WCX atomic catalog merge round-trip preserves unrelated entries and applies only registration evidence");
            CatalogJsonFile.Write(catalogRoundTripPath, catalogRoundTrip);
            Assert(CatalogJsonFile.Read(catalogRoundTripPath).Count == 2, "WCX atomic catalog writer replaces existing catalog without truncation");
            var embeddedCatalogPath = Path.Combine(Environment.CurrentDirectory, "TotalUpdater.Next", "Catalog", "plugin-catalog.json");
            var embeddedCatalogEntries = CatalogJsonFile.Read(embeddedCatalogPath);
            var embeddedCatalogCopy = Path.Combine(NewRoot(), "embedded-catalog-copy.json");
            CatalogJsonFile.Write(embeddedCatalogCopy, embeddedCatalogEntries);
            var embeddedCatalogCopyEntries = CatalogJsonFile.Read(embeddedCatalogCopy);
            Assert(embeddedCatalogCopyEntries.Count == embeddedCatalogEntries.Count && embeddedCatalogCopyEntries.Select(x => x.Id).OrderBy(x => x).SequenceEqual(embeddedCatalogEntries.Select(x => x.Id).OrderBy(x => x)) &&
                embeddedCatalogCopyEntries.Count(x => x.WcxRegistration != null) == embeddedCatalogEntries.Count(x => x.WcxRegistration != null),
                "WCX catalog writer round-trips the complete embedded catalog without entry or evidence loss");
            var textualMergePath = Path.Combine(NewRoot(), "textual-merge.json");
            File.WriteAllText(textualMergePath, "[\n  {\n    \"id\": \"merge-wcx\",\n    \"name\": \"Merge WCX\",\n    \"wcxRegistration\": null\n  }\n]\n");
            CatalogJsonFile.ReplaceWcxRegistrationEvidence(textualMergePath, new[] { validMerge });
            var textualMerge = File.ReadAllText(textualMergePath);
            Assert(textualMerge.Contains("\n    \"name\": \"Merge WCX\",") && textualMerge.Contains("\"wcxRegistration\": {") && !textualMerge.Contains("\"wcxRegistration\": null"),
                "WCX textual merge replaces only registration value without reformatting unrelated catalog fields");
            var textualAddPath = Path.Combine(NewRoot(), "textual-add.json");
            File.WriteAllText(textualAddPath, "[\n  {\n    \"id\": \"merge-wcx\",\n    \"name\": \"Merge WCX\"\n  }\n]\n");
            CatalogJsonFile.ReplaceWcxRegistrationEvidence(textualAddPath, new[] { validMerge });
            Assert(File.ReadAllText(textualAddPath).Contains("\"name\": \"Merge WCX\",") && File.ReadAllText(textualAddPath).Contains("\"wcxRegistration\": {"),
                "WCX textual merge adds absent registration member without rewriting entry");
            var batchSource = new CatalogSource { Id = "batch-source", Provider = "totalcmd.net", Priority = 1, Authority = "CommunityCatalog", Purpose = "MetadataAndDownload" };
            var resumedEntry = new PluginCatalogEntry { Id = "batch-resume", Name = "Batch resume", Type = "Wcx", Version = "1.0", Sources = new List<CatalogSource> { batchSource } };
            var failedEntry = new PluginCatalogEntry { Id = "batch-failure", Name = "Batch failure", Type = "Wcx", Version = "1.0", Sources = new List<CatalogSource> { batchSource } };
            var resumed = new WcxRegistrationHarvestFinding { Id = resumedEntry.Id, Status = WcxRegistrationHarvest.VerifiedRegistration, Evidence = new WcxRegistrationEvidence { PackageSha256 = new string('e', 64), X86BinarySha256 = new string('f', 64), PackerCaps = 0, Extensions = new List<string> { "zip" }, VerifiedUtc = DateTime.UtcNow, Source = "https://example.test/resume.zip", PackageUrl = "https://example.test/resume.zip", Version = "1.0", SourceId = "batch-source", SourceFingerprint = WcxRegistrationHarvester.SourceFingerprint(batchSource) } };
            var checkpoints = 0;
            var batch = new WcxRegistrationBatchHarvester((entry, token) => entry.Id == failedEntry.Id ? Task.FromException<WcxRegistrationHarvestFinding>(new TimeoutException("fixture timeout")) : Task.FromResult(resumed));
            var batchResult = batch.RunAsync(new[] { failedEntry, resumedEntry }, new[] { resumed }, true, 10, null, findings => checkpoints++, CancellationToken.None).GetAwaiter().GetResult();
            Assert(batchResult.Skipped == 1 && batchResult.Processed == 1 && batchResult.Count(WcxRegistrationHarvest.SourceUnavailable) == 1 && checkpoints == 1, "WCX batch resumes valid evidence and continues after source failure with concurrency cap");
            var deterministic = Path.Combine(NewRoot(), "batch.json"); WcxRegistrationHarvest.Write(deterministic, batchResult.Findings); var firstJson = File.ReadAllText(deterministic); WcxRegistrationHarvest.Write(deterministic, batchResult.Findings.Reverse());
            Assert(firstJson == File.ReadAllText(deterministic) && WcxRegistrationHarvest.Read(deterministic).Select(x => x.Id).SequenceEqual(WcxRegistrationHarvest.Read(deterministic).Select(x => x.Id).OrderBy(x => x, StringComparer.OrdinalIgnoreCase)), "WCX batch evidence output is deterministic and atomically replaceable");
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
