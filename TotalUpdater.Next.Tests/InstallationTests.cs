using System;
using System.IO;
using System.IO.Compression;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using TotalUpdater.Next.Core;
using TotalUpdater.Next.Core.Installation;
using TotalUpdater.Next.Core.Versions;
using TotalUpdater.Next.Infrastructure.Installation;

namespace TotalUpdater.Next.Tests
{
    internal static class InstallationTests
    {
        private static readonly Uri Url = new Uri("https://example.test/plugin.zip");
        private static readonly List<string> StagingDirectories = new List<string>();
        public static void Run(Action<bool, string> check)
        {
            var root = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "tu-install-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            try
            {
                var target = Path.Combine(root, "plugin"); Directory.CreateDirectory(target);
                var primary = Path.Combine(target, "sample.wlx"); File.WriteAllText(primary, "old");
                var plugin = Plugin(primary);
                var zip = Path.Combine(root, "valid.zip"); CreateZip(zip, Metadata(), "sample.wlx", "new");
                var inspection = Inspect(zip);
                check(inspection.Type == "wlx" && inspection.File == "sample.wlx" && inspection.Version == "2.0" && inspection.DefaultDir == "sample", "pluginst.inf fields parsed");
                var plan = Build(plugin, inspection, root);
                check(plan.Files.Count == 1 && plan.Files[0].ReplacesExisting && plan.TargetDirectory == target, "valid ZIP install plan");
                check(Fails(() => TransactionalInstaller.Preflight(plan, () => true)), "running TC blocks install");
                var manifest = new TransactionalInstaller(isTotalCommanderRunning: () => false).Install(plan, () => Updated(plugin));
                check(File.ReadAllText(primary) == "new" && manifest.State == "Completed", "successful transactional install");
                check(manifest.Files[0].OriginalSha256 == PackageInspector.Hash(manifest.Files[0].BackupPath) && File.Exists(Path.Combine(plan.BackupDirectory, "manifest.json")), "backup manifest and original hash");
                new RollbackService().Rollback(manifest, Path.Combine(root, "backups"), () => plugin);
                new RollbackService().Rollback(manifest, Path.Combine(root, "backups"), () => plugin);
                check(File.ReadAllText(primary) == "old" && manifest.State == "RolledBack", "manual idempotent rollback");

                var mismatch = Path.Combine(root, "mismatch.zip"); CreateZip(mismatch, Metadata().Replace("type=wlx", "type=wfx"), "sample.wlx", "new");
                check(Fails(() => Build(plugin, Inspect(mismatch), root)), "pluginst.inf type mismatch");
                var wrongVersion = Path.Combine(root, "version.zip"); CreateZip(wrongVersion, Metadata().Replace("version=2.0", "version=3.0"), "sample.wlx", "new");
                check(Fails(() => Build(plugin, Inspect(wrongVersion), root)), "pluginst.inf canonical version mismatch");
                var noVersion = Path.Combine(root, "noversion.zip"); CreateZip(noVersion, Metadata().Replace("version=2.0\n", ""), "sample.wlx", "new");
                check(Build(plugin, Inspect(noVersion), root).Files.Count == 1, "pluginst.inf version optional");
                check(Fails(() => new TransactionalInstaller(isTotalCommanderRunning: () => false).Install(Build(plugin, Inspect(noVersion), root), () => plugin)) && File.ReadAllText(primary) == "old", "versionless pluginst.inf still requires post-install version verification");
                var noDescription = Path.Combine(root, "nodescription.zip"); CreateZip(noDescription, Metadata().Replace("description=Sample plugin\n", ""), "sample.wlx", "new");
                check(Fails(() => Build(plugin, Inspect(noDescription), root)), "pluginst.inf description required");
                var wrongFile = Path.Combine(root, "file.zip"); CreateZip(wrongFile, Metadata().Replace("file=sample.wlx", "file=other.wlx"), "other.wlx", "new");
                check(Fails(() => Build(plugin, Inspect(wrongFile), root)), "pluginst.inf file mismatch");
                var badDir = Path.Combine(root, "defaultdir.zip"); CreateZip(badDir, Metadata().Replace("defaultdir=sample", "defaultdir=..\\escape"), "sample.wlx", "new");
                check(Fails(() => Build(plugin, Inspect(badDir), root)), "pluginst.inf defaultdir validation");
                var nestedDir = Path.Combine(root, "nesteddir.zip"); CreateZip(nestedDir, Metadata().Replace("defaultdir=sample", "defaultdir=plugins\\sample"), "sample.wlx", "new");
                check(Fails(() => Build(plugin, Inspect(nestedDir), root)), "pluginst.inf nested defaultdir rejected");
                foreach (var unsafeName in new[] { "../evil", "/absolute", "\\\\server\\share", "C:\\drive", "sample.ini:ads" })
                {
                    var bad = Path.Combine(root, Guid.NewGuid().ToString("N") + ".zip"); CreateZip(bad, Metadata(), "sample.wlx", "new", unsafeName, "evil");
                    check(Fails(() => Inspect(bad)), "unsafe ZIP path " + unsafeName);
                }
                var duplicate = Path.Combine(root, "duplicate.zip"); CreateZip(duplicate, Metadata(), "sample.wlx", "new", "SAMPLE.WLX", "new");
                check(Fails(() => Inspect(duplicate)), "case-insensitive ZIP duplicate");
                var symlink = Path.Combine(root, "symlink.zip"); CreateZip(symlink, Metadata(), "sample.wlx", "new", "link", "target", true);
                check(Fails(() => Inspect(symlink)), "ZIP symlink rejected");
                check(Fails(() => new PackageInspector(new PackageLimits { MaxEntries = 1 }).Inspect(zip)), "ZIP entry count limit");
                check(Fails(() => new PackageInspector(new PackageLimits { MaxFileBytes = 2 }).Inspect(zip)), "ZIP file size limit");
                check(Fails(() => new PackageInspector(new PackageLimits { MaxTotalBytes = 4 }).Inspect(zip)), "ZIP total size limit");
                check(Fails(() => new PackageInspector(new PackageLimits { MaxCompressionRatio = 0.1 }).Inspect(zip)), "ZIP ratio limit");
                var dual = Plugin(primary); var x64 = Path.Combine(target, "sample.wlx64"); File.WriteAllText(x64, "old64");
                dual.Binaries.Add(new PluginBinary { Path = x64, Exists = true, Architecture = PluginArchitecture.X64 });
                check(Fails(() => Build(dual, inspection, root)), "dual architecture requires both binaries");
                var dualZip = Path.Combine(root, "dual.zip"); CreateZip(dualZip, Metadata(), "sample.wlx", "new", "sample.wlx64", "new64");
                check(Build(dual, Inspect(dualZip), root).Files.Count == 2, "dual architecture complete package accepted");
                using (new FileStream(primary, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                    check(Fails(() => TransactionalInstaller.Preflight(plan, () => false)), "locked target preflight");
                var multi = Path.Combine(root, "multi.zip"); CreateZip(multi, Metadata(), "sample.wlx", "new", "extra.txt", "extra");
                var multiPlan = Build(plugin, Inspect(multi), root);
                check(Fails(() => new TransactionalInstaller(afterFile: index => { if (index == 1) throw new IOException("simulated failure"); }, isTotalCommanderRunning: () => false).Install(multiPlan, () => File.ReadAllText(primary) == "old" ? plugin : Updated(plugin))) &&
                    File.ReadAllText(primary) == "old" && !File.Exists(Path.Combine(target, "extra.txt")), "mid-install failure auto rollback");
                File.WriteAllText(Path.Combine(target, "unrelated.txt"), "keep");
                var multiManifest = new TransactionalInstaller(isTotalCommanderRunning: () => false).Install(Build(plugin, Inspect(multi), root), () => Updated(plugin));
                check(File.Exists(Path.Combine(target, "extra.txt")), "new package file installed");
                new RollbackService().Rollback(multiManifest, Path.Combine(root, "backups"), () => plugin);
                check(!File.Exists(Path.Combine(target, "extra.txt")) && File.ReadAllText(primary) == "old" && File.ReadAllText(Path.Combine(target, "unrelated.txt")) == "keep", "rollback removes only transaction-added file");
                var mismatchPlan = Build(plugin, Inspect(zip), root);
                check(Fails(() => new TransactionalInstaller(isTotalCommanderRunning: () => false).Install(mismatchPlan, () => plugin)) && File.ReadAllText(primary) == "old", "post-install mismatch auto rollback");
                File.WriteAllText(Path.Combine(target, "settings.ini"), "user");
                var ini = Path.Combine(root, "ini.zip"); CreateZip(ini, Metadata(), "sample.wlx", "new", "settings.ini", "vendor");
                check(Fails(() => Build(plugin, Inspect(ini), root)) && File.ReadAllText(Path.Combine(target, "settings.ini")) == "user", "user INI protection");
                check(!File.Exists(Path.Combine(target, "pluginst.inf")), "pluginst.inf not copied");
                check(Fails(() => Inspect(Path.Combine(root, "not.zip.exe"))), "non-ZIP download only");
            }
            finally
            {
                foreach (var stage in StagingDirectories)
                    if (stage.StartsWith(Path.Combine(Path.GetTempPath(), "TotalUpdaterNext", "staging") + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) && Directory.Exists(stage))
                        Directory.Delete(stage, true);
                StagingDirectories.Clear();
                Directory.Delete(root, true);
            }
        }

        private static PackageInspection Inspect(string path)
        {
            var result = new PackageInspector().Inspect(path);
            StagingDirectories.Add(result.StagingDirectory);
            return result;
        }

        private static string Metadata() { return "[plugininstall]\ndescription=Sample plugin\ntype=wlx\nfile=sample.wlx\nversion=2.0\ndefaultdir=sample\n"; }
        private static InstalledPlugin Plugin(string path)
        {
            return new InstalledPlugin { Identity = new PluginIdentity { Id = "sample", Type = PluginType.Wlx }, Type = PluginType.Wlx, DisplayName = "Sample", PrimaryPath = path,
                FileExists = true, LocalVersion = new LocalVersion { RawValue = "1.0", ParsedValue = VersionValue.Parse("1.0") },
                Binaries = new System.Collections.Generic.List<PluginBinary> { new PluginBinary { Path = path, Exists = true, Architecture = PluginArchitecture.X86 } } };
        }
        private static InstalledPlugin Updated(InstalledPlugin original)
        {
            var result = Plugin(original.PrimaryPath); result.LocalVersion = new LocalVersion { RawValue = "2.0", ParsedValue = VersionValue.Parse("2.0") }; return result;
        }
        private static InstallPlan Build(InstalledPlugin plugin, PackageInspection inspection, string root)
        { return new InstallPlanBuilder().Build(plugin, inspection, VersionValue.Parse("2.0"), Url, Path.Combine(root, "backups")); }
        private static bool Fails(Action action) { try { action(); return false; } catch { return true; } }
        private static void CreateZip(string path, string metadata, string name, string content, string extraName = null, string extraContent = null, bool symlink = false)
        {
            using (var zip = ZipFile.Open(path, ZipArchiveMode.Create))
            {
                Add(zip, "pluginst.inf", metadata); Add(zip, name, content);
                if (extraName != null) { var entry = Add(zip, extraName, extraContent); if (symlink) entry.ExternalAttributes = (0xA000 | 0x1FF) << 16; }
            }
        }
        private static ZipArchiveEntry Add(ZipArchive zip, string name, string text)
        {
            var entry = zip.CreateEntry(name, CompressionLevel.Optimal);
            using (var writer = new StreamWriter(entry.Open(), Encoding.UTF8)) writer.Write(text);
            return entry;
        }
    }
}
