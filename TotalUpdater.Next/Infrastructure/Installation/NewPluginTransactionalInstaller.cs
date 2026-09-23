using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using TotalUpdater.Next.Core;
using TotalUpdater.Next.Core.Installation;
using TotalUpdater.Next.Core.Versions;

namespace TotalUpdater.Next.Infrastructure.Installation
{
    public sealed class NewPluginTransactionalInstaller
    {
        private readonly BackupService _backups;
        private readonly NewPluginRollbackService _rollback;
        private readonly Action<string> _checkpoint;
        private readonly Func<bool> _isTotalCommanderRunning;
        public NewPluginTransactionalInstaller(BackupService backups = null, NewPluginRollbackService rollback = null,
            Action<string> checkpoint = null, Func<bool> isTotalCommanderRunning = null)
        { _backups = backups ?? new BackupService(); _rollback = rollback ?? new NewPluginRollbackService();
            _checkpoint = checkpoint; _isTotalCommanderRunning = isTotalCommanderRunning; }

        public InstallManifest Install(NewPluginInstallPlan plan, Func<InstalledPlugin> rediscover)
        {
            if (plan == null || rediscover == null) throw new ArgumentNullException(plan == null ? nameof(plan) : nameof(rediscover));
            Preflight(plan, _isTotalCommanderRunning);
            var manifest = _backups.CreateNew(plan);
            try
            {
                InstallStateMachine.Set(manifest, InstallStateMachine.Installing); BackupService.Save(manifest);
                foreach (var file in plan.Files)
                {
                    if (!String.Equals(PackageInspector.Hash(file.Source.StagedPath), file.Source.Sha256, StringComparison.OrdinalIgnoreCase))
                        throw new IOException("Staging file changed.");
                    var record = manifest.Files.Single(x => String.Equals(x.RelativePath, file.Source.RelativePath, StringComparison.OrdinalIgnoreCase));
                    CreateDirectories(manifest, Path.GetDirectoryName(file.Destination));
                    if (File.Exists(file.Destination) || Directory.Exists(file.Destination)) throw new IOException("InstallConflict: target появился: " + file.Destination);
                    InstallStateMachine.SetFile(record, InstallStateMachine.InstallingFile); BackupService.Save(manifest);
                    var temporary = file.Destination + ".tu-new-" + Guid.NewGuid().ToString("N");
                    try
                    {
                        File.Copy(file.Source.StagedPath, temporary, false);
                        if (File.Exists(file.Destination) || Directory.Exists(file.Destination)) throw new IOException("InstallConflict: target появился: " + file.Destination);
                        File.Move(temporary, file.Destination);
                    }
                    finally { if (File.Exists(temporary)) File.Delete(temporary); }
                    InstallStateMachine.SetFile(record, InstallStateMachine.InstalledFile); BackupService.Save(manifest);
                }
                _checkpoint?.Invoke("AfterFiles");
                foreach (var patch in plan.ConfigurationFiles)
                {
                    var record = manifest.ConfigurationFiles.Single(x => String.Equals(x.ConfigFile, patch.Path, StringComparison.OrdinalIgnoreCase));
                    if (!String.Equals(PackageInspector.Hash(patch.Path), patch.OriginalSha256, StringComparison.OrdinalIgnoreCase))
                        throw new ConfigConflictException("ConfigConflict: INI изменён после backup: " + patch.Path);
                    record.State = InstallStateMachine.InstallingFile; BackupService.Save(manifest);
                    var temporary = patch.Path + ".tu-new-" + Guid.NewGuid().ToString("N");
                    try
                    {
                        using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                        { output.Write(patch.InstalledBytes, 0, patch.InstalledBytes.Length); output.Flush(true); }
                        if (!String.Equals(PackageInspector.Hash(patch.Path), patch.OriginalSha256, StringComparison.OrdinalIgnoreCase))
                        {
                            record.State = InstallStateMachine.PendingFile; BackupService.Save(manifest);
                            throw new ConfigConflictException("ConfigConflict: INI изменён перед replace: " + patch.Path);
                        }
                        File.Replace(temporary, patch.Path, null);
                    }
                    finally { if (File.Exists(temporary)) File.Delete(temporary); }
                    record.State = InstallStateMachine.InstalledFile; BackupService.Save(manifest);
                }
                _checkpoint?.Invoke("AfterIni");
                Verify(plan, rediscover());
                InstallStateMachine.Set(manifest, InstallStateMachine.Completed); BackupService.Save(manifest);
                return manifest;
            }
            catch (ConfigConflictException)
            {
                InstallStateMachine.Set(manifest, InstallStateMachine.ConfigConflict); BackupService.Save(manifest);
                // Before the first INI write, files can be removed without touching the foreign INI edit.
                if (manifest.ConfigurationFiles.All(x => x.State == InstallStateMachine.PendingFile || x.State == InstallStateMachine.InstalledFile))
                    _rollback.RollbackBeforeConfigWrite(manifest, Path.GetDirectoryName(manifest.BackupDirectory));
                throw;
            }
            catch (Exception original)
            {
                try { _rollback.Rollback(manifest, Path.GetDirectoryName(manifest.BackupDirectory)); }
                catch (Exception rollbackError) { throw new AggregateException("Ошибка новой установки и автоматического отката: " + manifest.BackupDirectory, original, rollbackError); }
                throw;
            }
        }

        private static void Verify(NewPluginInstallPlan plan, InstalledPlugin plugin)
        {
            if (plugin == null || plugin.Identity == null || !plugin.FileExists || plugin.HasVersionConflict ||
                !String.Equals(plugin.Identity.Id, plan.PluginId, StringComparison.OrdinalIgnoreCase) || plugin.Type != plan.PluginType ||
                plugin.LocalVersion.ParsedValue.CompareTo(VersionValue.Parse(plan.Version)) != VersionComparison.Equal ||
                !plan.RequiredBinaryPaths.All(path => plugin.Binaries.Any(binary => binary.Exists &&
                    String.Equals(Path.GetFullPath(binary.Path), Path.GetFullPath(path), StringComparison.OrdinalIgnoreCase))))
                throw new InvalidOperationException("Post-install verification failed: identity/type/version/binaries.");
            foreach (var patch in plan.ConfigurationFiles)
            {
                if (!String.Equals(PackageInspector.Hash(patch.Path), patch.InstalledSha256, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("Post-install verification failed: registration INI changed.");
                var document = new TotalCommander.IniDocumentReader().Read(patch.Path);
                if (patch.Changes.Any(change => !String.Equals(document.GetSection(change.Section)?.GetValue(change.Key), change.Value, StringComparison.OrdinalIgnoreCase)))
                    throw new InvalidOperationException("Post-install verification failed: registration key/path.");
            }
        }

        public static void Preflight(NewPluginInstallPlan plan, Func<bool> running = null)
        {
            NewPluginArchitectureValidator.Validate(plan.TotalCommanderDirectory, plan.Package, plan.PluginType);
            if (!File.Exists(plan.Package.PackagePath) || !String.Equals(PackageInspector.Hash(plan.Package.PackagePath), plan.Package.PackageSha256, StringComparison.OrdinalIgnoreCase))
                throw new IOException("ZIP изменён после inspection.");
            if ((running ?? (() => Process.GetProcessesByName("TOTALCMD").Any() || Process.GetProcessesByName("TOTALCMD64").Any()))())
                throw new InvalidOperationException("Закройте Total Commander перед установкой.");
            foreach (var file in plan.Files)
            {
                if (File.Exists(file.Destination) || Directory.Exists(file.Destination)) throw new IOException("InstallConflict: target существует: " + file.Destination);
                RejectReparseParents(file.Destination);
            }
            foreach (var patch in plan.ConfigurationFiles)
            {
                if (!File.Exists(patch.Path) || (File.GetAttributes(patch.Path) & (FileAttributes.ReadOnly | FileAttributes.ReparsePoint)) != 0)
                    throw new UnauthorizedAccessException("Установка недоступна — требуются права администратора или INI защищён: " + patch.Path);
                RejectReparseParents(patch.Path);
                try { using (new FileStream(patch.Path, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) { } }
                catch (UnauthorizedAccessException) { throw new UnauthorizedAccessException("Установка недоступна — требуются права администратора."); }
                if (!String.Equals(PackageInspector.Hash(patch.Path), patch.OriginalSha256, StringComparison.OrdinalIgnoreCase))
                    throw new ConfigConflictException("ConfigConflict: INI изменён после составления плана: " + patch.Path);
                var probeDirectory = Path.GetDirectoryName(patch.Path);
                var configProbe = Path.Combine(probeDirectory, ".tu-probe-" + Guid.NewGuid().ToString("N"));
                try { using (File.Create(configProbe)) { } }
                catch (UnauthorizedAccessException) { throw new UnauthorizedAccessException("Установка недоступна — требуются права администратора."); }
                finally { if (File.Exists(configProbe)) File.Delete(configProbe); }
            }
            var cursor = plan.TargetDirectory;
            while (!Directory.Exists(cursor)) cursor = Path.GetDirectoryName(cursor) ?? throw new DirectoryNotFoundException(plan.TargetDirectory);
            var probe = Path.Combine(cursor, ".tu-probe-" + Guid.NewGuid().ToString("N"));
            try { using (File.Create(probe)) { } }
            catch (UnauthorizedAccessException) { throw new UnauthorizedAccessException("Установка недоступна — требуются права администратора."); }
            finally { if (File.Exists(probe)) File.Delete(probe); }
        }
        private static void RejectReparseParents(string path)
        {
            for (var cursor = Path.GetDirectoryName(Path.GetFullPath(path)); cursor != null; cursor = Path.GetDirectoryName(cursor))
                if (Directory.Exists(cursor) && (File.GetAttributes(cursor) & FileAttributes.ReparsePoint) != 0)
                    throw new InvalidDataException("Reparse point в пути установки: " + cursor);
        }
        private static void CreateDirectories(InstallManifest manifest, string path)
        {
            var pending = new System.Collections.Generic.Stack<string>();
            for (var cursor = path; !Directory.Exists(cursor); cursor = Path.GetDirectoryName(cursor))
            {
                if (cursor == null) throw new DirectoryNotFoundException(path);
                pending.Push(cursor);
            }
            while (pending.Count > 0)
            {
                var next = pending.Pop(); Directory.CreateDirectory(next);
                manifest.CreatedDirectories.Add(next); BackupService.Save(manifest);
            }
        }
    }
    public sealed class ConfigConflictException : IOException
    { public ConfigConflictException(string message) : base(message) { } }
}
