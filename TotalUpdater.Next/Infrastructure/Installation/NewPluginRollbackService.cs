using System;
using System.IO;
using System.Linq;
using TotalUpdater.Next.Core.Installation;

namespace TotalUpdater.Next.Infrastructure.Installation
{
    public sealed class NewPluginRollbackService
    {
        private readonly Action<string> _beforeRestore;
        public NewPluginRollbackService(Action<string> beforeRestore = null) { _beforeRestore = beforeRestore; }

        public void Rollback(InstallManifest manifest, string backupRoot)
        {
            if (manifest == null || manifest.ManifestVersion != 4) throw new InvalidOperationException("Нужен manifest новой установки v4.");
            BackupService.Validate(manifest, Path.Combine(manifest.BackupDirectory, "manifest.json"), backupRoot);
            if (manifest.State == InstallStateMachine.RolledBack) return;
            // Complete classification first. Do not partly unwind a transaction if a user edited an INI.
            foreach (var record in manifest.ConfigurationFiles) EnsureConfigSafe(manifest, record);
            foreach (var record in manifest.Files) EnsureFileSafe(manifest, record);
            InstallStateMachine.Set(manifest, InstallStateMachine.RollingBack); BackupService.Save(manifest);
            foreach (var record in manifest.ConfigurationFiles.AsEnumerable().Reverse())
            {
                _beforeRestore?.Invoke(record.ConfigFile);
                EnsureConfigSafe(manifest, record);
                if (Equal(PackageInspector.Hash(record.ConfigFile), record.InstalledSha256))
                {
                    var temporary = record.ConfigFile + ".tu-restore-" + Guid.NewGuid().ToString("N");
                    try
                    {
                        File.Copy(record.BackupPath, temporary, false);
                        EnsureConfigSafe(manifest, record);
                        File.Replace(temporary, record.ConfigFile, null);
                    }
                    finally { if (File.Exists(temporary)) File.Delete(temporary); }
                }
                record.State = InstallStateMachine.RestoredFile; BackupService.Save(manifest);
            }
            foreach (var record in manifest.Files.AsEnumerable().Reverse())
            {
                var path = Target(manifest, record);
                _beforeRestore?.Invoke(path);
                EnsureFileSafe(manifest, record);
                if (File.Exists(path)) File.Delete(path);
                InstallStateMachine.SetFile(record, InstallStateMachine.RestoredFile); BackupService.Save(manifest);
            }
            foreach (var directory in manifest.CreatedDirectories.OrderByDescending(x => x.Length))
                if (Directory.Exists(directory) && !Directory.EnumerateFileSystemEntries(directory).Any()) Directory.Delete(directory);
            InstallStateMachine.Set(manifest, InstallStateMachine.RolledBack); BackupService.Save(manifest);
        }

        // A pending INI changed by another process is not ours to restore.
        // Already installed INIs can still be restored from their backups.
        public void RollbackBeforeConfigWrite(InstallManifest manifest, string backupRoot)
        {
            BackupService.Validate(manifest, Path.Combine(manifest.BackupDirectory, "manifest.json"), backupRoot);
            if (manifest.State != InstallStateMachine.ConfigConflict ||
                manifest.ConfigurationFiles.Any(x => x.State != InstallStateMachine.PendingFile && x.State != InstallStateMachine.InstalledFile))
                throw new InvalidOperationException("INI write may have started; manual recovery required.");
            foreach (var record in manifest.ConfigurationFiles.Where(x => x.State == InstallStateMachine.InstalledFile)) EnsureConfigSafe(manifest, record);
            foreach (var record in manifest.Files) EnsureFileSafe(manifest, record);
            InstallStateMachine.Set(manifest, InstallStateMachine.RollingBack); BackupService.Save(manifest);
            foreach (var record in manifest.ConfigurationFiles.Where(x => x.State == InstallStateMachine.InstalledFile).Reverse())
            {
                EnsureConfigSafe(manifest, record);
                if (Equal(PackageInspector.Hash(record.ConfigFile), record.InstalledSha256))
                {
                    var temporary = record.ConfigFile + ".tu-restore-" + Guid.NewGuid().ToString("N");
                    try { File.Copy(record.BackupPath, temporary, false); EnsureConfigSafe(manifest, record); File.Replace(temporary, record.ConfigFile, null); }
                    finally { if (File.Exists(temporary)) File.Delete(temporary); }
                }
                record.State = InstallStateMachine.RestoredFile; BackupService.Save(manifest);
            }
            foreach (var record in manifest.Files.AsEnumerable().Reverse())
            {
                var path = Target(manifest, record);
                EnsureFileSafe(manifest, record);
                if (File.Exists(path)) File.Delete(path);
                InstallStateMachine.SetFile(record, InstallStateMachine.RestoredFile); BackupService.Save(manifest);
            }
            foreach (var directory in manifest.CreatedDirectories.OrderByDescending(x => x.Length))
                if (Directory.Exists(directory) && !Directory.EnumerateFileSystemEntries(directory).Any()) Directory.Delete(directory);
            InstallStateMachine.Set(manifest, InstallStateMachine.RolledBack); BackupService.Save(manifest);
        }

        private static void EnsureConfigSafe(InstallManifest manifest, InstallManifestConfiguration record)
        {
            if (!File.Exists(record.ConfigFile) || (File.GetAttributes(record.ConfigFile) & FileAttributes.ReparsePoint) != 0 ||
                !File.Exists(record.BackupPath) || !Equal(PackageInspector.Hash(record.BackupPath), record.OriginalSha256))
                Conflict(manifest, record.ConfigFile);
            var current = PackageInspector.Hash(record.ConfigFile);
            if (!Equal(current, record.OriginalSha256) && !Equal(current, record.InstalledSha256)) Conflict(manifest, record.ConfigFile);
        }
        private static void EnsureFileSafe(InstallManifest manifest, InstallManifestFile record)
        {
            var path = Target(manifest, record);
            if (File.Exists(path) && !Equal(PackageInspector.Hash(path), record.InstalledSha256))
            {
                InstallStateMachine.Set(manifest, InstallStateMachine.RecoveryConflict); BackupService.Save(manifest);
                throw new IOException("RecoveryConflict: Файл плагина изменён после установки: " + path);
            }
        }
        private static string Target(InstallManifest manifest, InstallManifestFile record)
        {
            var path = Path.GetFullPath(Path.Combine(manifest.TargetDirectory, PackageInspector.SafeRelativePath(record.RelativePath)));
            if (!path.StartsWith(Path.GetFullPath(manifest.TargetDirectory).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Target вне каталога плагина.");
            if (File.Exists(path) && (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("Reparse point в файле плагина.");
            for (var cursor = Path.GetDirectoryName(path); cursor != null; cursor = Path.GetDirectoryName(cursor))
            {
                if (Directory.Exists(cursor) && (File.GetAttributes(cursor) & FileAttributes.ReparsePoint) != 0)
                    throw new InvalidDataException("Reparse point в target path.");
                if (String.Equals(cursor, manifest.TargetDirectory, StringComparison.OrdinalIgnoreCase)) break;
            }
            return path;
        }
        private static bool Equal(string left, string right) { return String.Equals(left, right, StringComparison.OrdinalIgnoreCase); }
        private static void Conflict(InstallManifest manifest, string path)
        {
            InstallStateMachine.Set(manifest, InstallStateMachine.ConfigRecoveryConflict); BackupService.Save(manifest);
            throw new IOException("ConfigRecoveryConflict: INI изменён после установки: " + path);
        }
    }
}
