using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using TotalUpdater.Next.Core;
using TotalUpdater.Next.Core.Installation;
using TotalUpdater.Next.Core.Versions;
using TotalUpdater.Next.Infrastructure.Installation;
using TotalUpdater.Next.UI;

namespace TotalUpdater.Next.Tests
{
    internal static class RecoveryTests
    {
        public static void Run(Action<bool, string> check)
        {
            var root = Path.Combine(Path.GetTempPath(), "TotalUpdaterNext", "tests", "recovery-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            var stages = new List<string>();
            try
            {
                var backupRoot = Path.Combine(root, "backups");
                var first = Fixture(root, backupRoot, "first", stages, true);
                var prepared = new BackupService().Create(first.Plan);
                check(prepared.State == "Prepared" && new RecoveryService(backupRoot).FindPending().Any(x => x.TransactionId == prepared.TransactionId), "Prepared manifest detected at startup");
                new RecoveryService(backupRoot).Recover(prepared, first.Old);
                check(prepared.State == "RolledBack" && File.ReadAllText(first.PrimaryPath) == "old", "crash before first replacement recovered");

                var second = Fixture(root, backupRoot, "second", stages, true);
                var interrupted = new BackupService().Create(second.Plan);
                interrupted.State = "Installing"; BackupService.Save(interrupted);
                File.Copy(second.Plan.Files[0].Source.StagedPath, second.PrimaryPath, true);
                check(interrupted.Files[0].State == InstallStateMachine.PendingFile && File.ReadAllText(second.PrimaryPath) == "new", "crash before Installed marker uses hash as truth");
                check(new RecoveryService(backupRoot).FindPending().Any(x => x.TransactionId == interrupted.TransactionId), "Installing manifest detected at startup");
                new RecoveryService(backupRoot).Recover(interrupted, second.Old);
                check(interrupted.State == "RolledBack" && File.ReadAllText(second.PrimaryPath) == "old" && !File.Exists(Path.Combine(second.TargetDirectory, "extra.txt")), "crash after first file rollback");

                var marked = Fixture(root, backupRoot, "marked", stages, true);
                var markedManifest = new BackupService().Create(marked.Plan);
                InstallStateMachine.Set(markedManifest, InstallStateMachine.Installing);
                InstallStateMachine.SetFile(markedManifest.Files[0], InstallStateMachine.InstallingFile);
                File.Copy(marked.Plan.Files[0].Source.StagedPath, marked.PrimaryPath, true);
                InstallStateMachine.SetFile(markedManifest.Files[0], InstallStateMachine.InstalledFile); BackupService.Save(markedManifest);
                new RecoveryService(backupRoot).Recover(markedManifest, marked.Old);
                check(markedManifest.State == InstallStateMachine.RolledBack && markedManifest.Files.All(x => x.State == InstallStateMachine.RestoredFile),
                    "crash with Installed and Pending markers recovers both files");

                var rolling = Fixture(root, backupRoot, "rolling", stages, true);
                var rollingManifest = new TransactionalInstaller(isTotalCommanderRunning: () => false).Install(rolling.Plan, rolling.Current);
                rollingManifest.State = "RollingBack"; BackupService.Save(rollingManifest);
                File.Copy(BackupService.ResolveBackupPath(rollingManifest, rollingManifest.Files[0]), rolling.PrimaryPath, true);
                new RecoveryService(backupRoot).Recover(rollingManifest, rolling.Old);
                check(rollingManifest.State == "RolledBack" && !File.Exists(Path.Combine(rolling.TargetDirectory, "extra.txt")), "interrupted RollingBack resumes safely");

                var conflict = Fixture(root, backupRoot, "conflict", stages, false);
                var conflictManifest = new BackupService().Create(conflict.Plan);
                conflictManifest.State = "Installing"; BackupService.Save(conflictManifest);
                File.WriteAllText(conflict.PrimaryPath, "user edit");
                check(Fails(() => new RecoveryService(backupRoot).Recover(conflictManifest, conflict.Old)) && conflictManifest.State == "RecoveryConflict" && File.ReadAllText(conflict.PrimaryPath) == "user edit", "RecoveryConflict preserves changed replaced file");

                var modified = Fixture(root, backupRoot, "modified", stages, false);
                var modifiedManifest = new TransactionalInstaller(isTotalCommanderRunning: () => false).Install(modified.Plan, modified.Current);
                File.WriteAllText(modified.PrimaryPath, "edited after install");
                check(Fails(() => new RollbackService().Rollback(modifiedManifest, backupRoot, modified.Old)) && modifiedManifest.State == "RecoveryConflict" && File.ReadAllText(modified.PrimaryPath) == "edited after install", "manual rollback blocks modified replaced file");

                var added = Fixture(root, backupRoot, "added", stages, true);
                var addedManifest = new TransactionalInstaller(isTotalCommanderRunning: () => false).Install(added.Plan, added.Current);
                var extra = Path.Combine(added.TargetDirectory, "extra.txt"); File.WriteAllText(extra, "edited extra");
                check(Fails(() => new RollbackService().Rollback(addedManifest, backupRoot, added.Old)) && File.ReadAllText(extra) == "edited extra" && File.ReadAllText(added.PrimaryPath) == "new", "modified added file blocks entire rollback");

                var restored = Fixture(root, backupRoot, "restored", stages, false);
                var restoredManifest = new TransactionalInstaller(isTotalCommanderRunning: () => false).Install(restored.Plan, restored.Current);
                File.Copy(BackupService.ResolveBackupPath(restoredManifest, restoredManifest.Files[0]), restored.PrimaryPath, true);
                new RollbackService().Rollback(restoredManifest, backupRoot, restored.Old);
                check(restoredManifest.State == "RolledBack" && File.ReadAllText(restored.PrimaryPath) == "old", "already restored replaced file is no-op");

                var badVersion = Fixture(root, backupRoot, "badversion", stages, false);
                var badManifest = new TransactionalInstaller(isTotalCommanderRunning: () => false).Install(badVersion.Plan, badVersion.Current);
                check(Fails(() => new RollbackService().Rollback(badManifest, backupRoot, () => badVersion.New())) && badManifest.State == "RollbackVerificationFailed" && File.ReadAllText(badVersion.PrimaryPath) == "old", "rollback old-version verification failure recorded");

                var latest = Fixture(root, backupRoot, "latest", stages, false);
                var sourceCandidate = new UpdateCandidate { CanonicalVersionSource = new RemoteVersionObservation { ProviderName = "official" },
                    DownloadSource = new RemoteVersionObservation { ProviderName = "mirror", Authority = TotalUpdater.Next.Catalog.SourceAuthority.Mirror } };
                var provenancePlan = new InstallPlanBuilder().Build(latest.Plugin, latest.Plan.Package, VersionValue.Parse("2.0"),
                    new Uri("https://example.test/plugin.zip"), backupRoot, sourceCandidate);
                var latestManifest = new TransactionalInstaller(isTotalCommanderRunning: () => false).Install(provenancePlan, latest.Current);
                check(BackupService.FindLatest(backupRoot, latest.Plugin.Identity.Id, latest.PrimaryPath).TransactionId == latestManifest.TransactionId &&
                    BackupService.FindLatest(backupRoot, "modified", modified.PrimaryPath).State == InstallStateMachine.RecoveryConflict, "latest rollback scoped to plugin id and primary path; conflict remains retryable");
                check(latestManifest.ManifestVersion == 3 && latestManifest.CanonicalSource == "official" && latestManifest.DownloadSource == "mirror" &&
                    latestManifest.DownloadAuthority == "Mirror" && latestManifest.PackageSha256 == PackageInspector.Hash(latest.ZipPath), "manifest provenance and actual ZIP SHA stored");
                var sibling = Fixture(root, backupRoot, "sibling", stages, false);
                sibling.Plugin.Identity.Id = latest.Plugin.Identity.Id;
                var siblingManifest = new TransactionalInstaller(isTotalCommanderRunning: () => false).Install(sibling.Plan, sibling.Current);
                check(BackupService.FindLatest(backupRoot, latest.Plugin.Identity.Id, latest.PrimaryPath).TransactionId == latestManifest.TransactionId &&
                    BackupService.FindLatest(backupRoot, sibling.Plugin.Identity.Id, sibling.PrimaryPath).TransactionId == siblingManifest.TransactionId,
                    "same plugin id in different directories has separate rollback history");

                var tamper = Fixture(root, backupRoot, "tamper", stages, false);
                var tamperManifest = new TransactionalInstaller(isTotalCommanderRunning: () => false).Install(tamper.Plan, tamper.Current);
                tamperManifest.Files[0].BackupPath = Path.Combine(root, "unrelated.txt"); File.WriteAllText(tamperManifest.Files[0].BackupPath, "do not touch"); BackupService.Save(tamperManifest);
                var loaded = BackupService.Load(Path.Combine(tamperManifest.BackupDirectory, "manifest.json"), backupRoot);
                check(Fails(() => BackupService.Load(Path.Combine(tamperManifest.BackupDirectory, "manifest.json"), Path.Combine(root, "wrong-root"))), "manifest outside configured backup root rejected");
                new RollbackService().Rollback(loaded, backupRoot, tamper.Old);
                check(File.ReadAllText(tamper.PrimaryPath) == "old" && File.ReadAllText(Path.Combine(root, "unrelated.txt")) == "do not touch", "absolute BackupPath ignored");
                loaded.TargetDirectory = root;
                check(Fails(() => new RollbackService().Rollback(loaded, backupRoot, tamper.Old)), "manifest target/primary mismatch rejected");

                var legacy = Fixture(root, backupRoot, "legacy", stages, false);
                var legacyManifest = new BackupService().Create(legacy.Plan);
                legacyManifest.ManifestVersion = 0; legacyManifest.TransactionId = null; legacyManifest.RequiredBinaryPaths = null; BackupService.Save(legacyManifest);
                var legacyLoaded = BackupService.Load(Path.Combine(legacyManifest.BackupDirectory, "manifest.json"), backupRoot);
                check(legacyLoaded.RequiredBinaryPaths.Single() == legacy.PrimaryPath, "0.8.0 manifest remains readable with primary-binary fallback");

                var candidate = new UpdateCandidate { AvailableVersion = VersionValue.Parse("2.0"), State = UpdateState.UpdateAvailable,
                    DownloadUrl = new Uri("https://example.test/plugin.zip"), CanonicalVersionSource = new RemoteVersionObservation { ProviderName = "official" },
                    DownloadSource = new RemoteVersionObservation { ProviderName = "mirror" }, HasSourceDisagreement = true };
                DownloadedPackagePolicy.Record(candidate, latest.ZipPath);
                var next = new UpdateCandidate { AvailableVersion = VersionValue.Parse("2.0"), State = UpdateState.UpdateAvailable,
                    DownloadUrl = candidate.DownloadUrl, CanonicalVersionSource = candidate.CanonicalVersionSource, DownloadSource = candidate.DownloadSource,
                    Observations = candidate.Observations, HasSourceDisagreement = candidate.HasSourceDisagreement };
                var row = new PluginRowViewModel(latest.Plugin); row.Apply(candidate); row.Apply(next);
                check(row.Candidate.CanonicalVersionSource.ProviderName == "official" && row.Candidate.DownloadSource.ProviderName == "mirror" && row.Candidate.HasSourceDisagreement && row.Candidate.DownloadedPackageSha256 == candidate.DownloadedPackageSha256, "Download to Install keeps provenance through UI Apply");
                check(DownloadedPackagePolicy.Resolve(next) == latest.ZipPath, "downloaded ZIP reused when URL version and SHA match");
                File.AppendAllText(latest.ZipPath, "tampered");
                check(Fails(() => DownloadedPackagePolicy.Resolve(next)), "modified downloaded ZIP rejected");

                var installRace = Fixture(root, backupRoot, "installrace", stages, false);
                check(Fails(() => new TransactionalInstaller(isTotalCommanderRunning: () => false,
                    beforeOperation: (m, f) => File.WriteAllText(installRace.PrimaryPath, "user race")).Install(installRace.Plan, installRace.Current)) &&
                    File.ReadAllText(installRace.PrimaryPath) == "user race", "changed target after backup blocks install and preserves user change");
                var installRaceManifest = BackupService.Load(Path.Combine(installRace.Plan.BackupDirectory, "manifest.json"), backupRoot);
                check(installRaceManifest.State == InstallStateMachine.InstallConflict && installRaceManifest.Files[0].State == InstallStateMachine.InstallingFile &&
                    new RecoveryService(backupRoot).FindPending().Any(x => x.TransactionId == installRaceManifest.TransactionId), "InstallConflict and per-file Installing persisted for startup");
                check(Fails(() => new RecoveryService(backupRoot).Recover(installRaceManifest, installRace.Old)) &&
                    installRaceManifest.State == InstallStateMachine.RecoveryConflict && File.ReadAllText(installRace.PrimaryPath) == "user race", "unresolved RecoveryConflict retry is non-destructive");
                File.WriteAllText(installRace.PrimaryPath, "old");
                new RecoveryService(backupRoot).Recover(installRaceManifest, installRace.Old);
                check(installRaceManifest.State == InstallStateMachine.RolledBack && installRaceManifest.Files[0].State == InstallStateMachine.RestoredFile,
                    "RecoveryConflict fixed then retry reaches RolledBack");

                var newRace = Fixture(root, backupRoot, "newrace", stages, true);
                var appeared = Path.Combine(newRace.TargetDirectory, "extra.txt");
                check(Fails(() => new TransactionalInstaller(isTotalCommanderRunning: () => false,
                    beforeOperation: (m, f) => { if (f.RelativePath == "extra.txt") File.WriteAllText(appeared, "user added"); }).Install(newRace.Plan, newRace.Current)) &&
                    File.ReadAllText(appeared) == "user added" && File.ReadAllText(newRace.PrimaryPath) == "old", "new destination race preserves user file and rolls back prior replacement");
                var newRaceManifest = BackupService.Load(Path.Combine(newRace.Plan.BackupDirectory, "manifest.json"), backupRoot);
                check(newRaceManifest.State == InstallStateMachine.InstallConflict && newRaceManifest.Files[0].State == InstallStateMachine.RestoredFile &&
                    newRaceManifest.Files[1].State == InstallStateMachine.InstallingFile, "partial install rollback records Restored and Installing states");

                var rollbackRace = Fixture(root, backupRoot, "rollbackrace", stages, false);
                var rollbackRaceManifest = new TransactionalInstaller(isTotalCommanderRunning: () => false).Install(rollbackRace.Plan, rollbackRace.Current);
                check(Fails(() => new RollbackService((m, f) => File.WriteAllText(rollbackRace.PrimaryPath, "changed during rollback"))
                    .Rollback(rollbackRaceManifest, backupRoot, rollbackRace.Old)) &&
                    rollbackRaceManifest.State == InstallStateMachine.RecoveryConflict && File.ReadAllText(rollbackRace.PrimaryPath) == "changed during rollback",
                    "replaced target changed after rollback pre-check is preserved");

                var deleteRace = Fixture(root, backupRoot, "deleterace", stages, true);
                var deleteRaceManifest = new TransactionalInstaller(isTotalCommanderRunning: () => false).Install(deleteRace.Plan, deleteRace.Current);
                var addedPath = Path.Combine(deleteRace.TargetDirectory, "extra.txt");
                check(Fails(() => new RollbackService((m, f) => { if (f.RelativePath == "extra.txt") File.WriteAllText(addedPath, "changed before delete"); })
                    .Rollback(deleteRaceManifest, backupRoot, deleteRace.Old)) && File.ReadAllText(addedPath) == "changed before delete" &&
                    File.ReadAllText(deleteRace.PrimaryPath) == "new", "added file changed after rollback pre-check is not deleted");

                var verifyRetry = Fixture(root, backupRoot, "verifyretry", stages, false);
                var verifyManifest = new TransactionalInstaller(isTotalCommanderRunning: () => false).Install(verifyRetry.Plan, verifyRetry.Current);
                check(Fails(() => new RollbackService().Rollback(verifyManifest, backupRoot, verifyRetry.New)) &&
                    verifyManifest.State == InstallStateMachine.RollbackVerificationFailed &&
                    new RecoveryService(backupRoot).FindPending().Any(x => x.TransactionId == verifyManifest.TransactionId), "RollbackVerificationFailed remains discoverable");
                new RecoveryService(backupRoot).Recover(verifyManifest, verifyRetry.Old);
                check(verifyManifest.State == InstallStateMachine.RolledBack, "RollbackVerificationFailed retry succeeds after verifier fixed");

                var state = new InstallManifest { State = InstallStateMachine.Prepared };
                InstallStateMachine.Set(state, InstallStateMachine.Installing); InstallStateMachine.Set(state, InstallStateMachine.InstallConflict);
                check(Fails(() => InstallStateMachine.Set(state, InstallStateMachine.Completed)), "invalid manifest transition rejected");
                var fileState = new InstallManifestFile { State = InstallStateMachine.PendingFile };
                InstallStateMachine.SetFile(fileState, InstallStateMachine.InstallingFile); InstallStateMachine.SetFile(fileState, InstallStateMachine.InstalledFile);
                InstallStateMachine.SetFile(fileState, InstallStateMachine.RestoredFile);
                check(Fails(() => InstallStateMachine.SetFile(fileState, InstallStateMachine.InstalledFile)), "per-file state transitions enforced");
            }
            finally
            {
                var stagingRoot = Path.Combine(Path.GetTempPath(), "TotalUpdaterNext", "staging") + Path.DirectorySeparatorChar;
                foreach (var stage in stages.Where(x => x.StartsWith(stagingRoot, StringComparison.OrdinalIgnoreCase) && Directory.Exists(x))) Directory.Delete(stage, true);
                Directory.Delete(root, true);
            }
        }

        private static TestFixture Fixture(string root, string backupRoot, string id, IList<string> stages, bool extra)
        {
            var directory = Path.Combine(root, id); Directory.CreateDirectory(directory);
            var primary = Path.Combine(directory, "sample.wlx"); File.WriteAllText(primary, "old");
            var zipPath = Path.Combine(root, id + ".zip");
            using (var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create))
            {
                Add(zip, "pluginst.inf", "[plugininstall]\ndescription=Sample\ntype=wlx\nfile=sample.wlx\nversion=2.0\ndefaultdir=sample\n");
                Add(zip, "sample.wlx", "new"); if (extra) Add(zip, "extra.txt", "extra");
            }
            var package = new PackageInspector().Inspect(zipPath); stages.Add(package.StagingDirectory);
            var plugin = new InstalledPlugin { Identity = new PluginIdentity { Id = id }, Type = PluginType.Wlx, PrimaryPath = primary, FileExists = true,
                LocalVersion = Version("1.0"), Binaries = new List<PluginBinary> { new PluginBinary { Path = primary, Exists = true, Architecture = PluginArchitecture.X86 } } };
            var plan = new InstallPlanBuilder().Build(plugin, package, VersionValue.Parse("2.0"), new Uri("https://example.test/plugin.zip"), backupRoot);
            return new TestFixture { Plugin = plugin, Plan = plan, PrimaryPath = primary, TargetDirectory = directory, ZipPath = zipPath };
        }
        private static LocalVersion Version(string value) { return new LocalVersion { RawValue = value, ParsedValue = VersionValue.Parse(value) }; }
        private static void Add(ZipArchive zip, string name, string value)
        {
            using (var writer = new StreamWriter(zip.CreateEntry(name).Open(), Encoding.UTF8)) writer.Write(value);
        }
        private static bool Fails(Action action) { try { action(); return false; } catch { return true; } }
        private sealed class TestFixture
        {
            public InstalledPlugin Plugin; public InstallPlan Plan; public string PrimaryPath; public string TargetDirectory; public string ZipPath;
            public InstalledPlugin Old() { return Detect("1.0"); }
            public InstalledPlugin New() { return Detect("2.0"); }
            public InstalledPlugin Current() { return Detect(File.ReadAllText(PrimaryPath) == "old" ? "1.0" : "2.0"); }
            private InstalledPlugin Detect(string version)
            {
                return new InstalledPlugin { Identity = Plugin.Identity, Type = Plugin.Type, PrimaryPath = PrimaryPath, FileExists = File.Exists(PrimaryPath),
                    LocalVersion = Version(version), Binaries = new List<PluginBinary> { new PluginBinary { Path = PrimaryPath, Exists = File.Exists(PrimaryPath), Architecture = PluginArchitecture.X86 } } };
            }
        }
    }
}
