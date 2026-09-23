using System;
using System.IO;
using System.Linq;
using TotalUpdater.Next.Core;
using TotalUpdater.Next.Core.Installation;
using TotalUpdater.Next.Core.Versions;

namespace TotalUpdater.Next.Infrastructure.Installation
{
    public sealed class RollbackService
    {
        public void Rollback(InstallManifest manifest, string backupRoot, Func<InstalledPlugin> rediscover)
        {
            if (rediscover == null) throw new ArgumentNullException(nameof(rediscover));
            BackupService.Validate(manifest, Path.Combine(manifest.BackupDirectory, "manifest.json"), backupRoot);
            if (manifest.State == "RolledBack") return;
            // Classify all files before changing any of them. A user edit blocks the whole rollback.
            foreach (var record in manifest.Files)
            {
                var target = Target(manifest, record);
                var current = File.Exists(target) ? PackageInspector.Hash(target) : null;
                if (record.Replaced)
                {
                    var backup = BackupService.ResolveBackupPath(manifest, record);
                    if (!File.Exists(backup) || !Equal(PackageInspector.Hash(backup), record.OriginalSha256))
                        throw new IOException("Backup повреждён: " + record.RelativePath);
                    if (!Equal(current, record.OriginalSha256) && !Equal(current, record.InstalledSha256)) Conflict(manifest, record.RelativePath);
                }
                else if (current != null && !Equal(current, record.InstalledSha256)) Conflict(manifest, record.RelativePath);
            }
            manifest.State = "RollingBack"; BackupService.Save(manifest);
            foreach (var record in manifest.Files.AsEnumerable().Reverse())
            {
                var target = Target(manifest, record);
                if (record.Replaced)
                {
                    if (!Equal(PackageInspector.Hash(target), record.OriginalSha256))
                    {
                        var temporary = target + ".tu-restore-" + Guid.NewGuid().ToString("N");
                        File.Copy(BackupService.ResolveBackupPath(manifest, record), temporary);
                        try { File.Replace(temporary, target, null); }
                        finally { if (File.Exists(temporary)) File.Delete(temporary); }
                    }
                    if (record.CreationTimeUtc.HasValue) File.SetCreationTimeUtc(target, record.CreationTimeUtc.Value);
                    if (record.LastWriteTimeUtc.HasValue) File.SetLastWriteTimeUtc(target, record.LastWriteTimeUtc.Value);
                    File.SetAttributes(target, record.Attributes);
                }
                else if (File.Exists(target)) File.Delete(target);
            }
            var found = rediscover();
            if (found == null || !found.FileExists || found.HasVersionConflict ||
                !String.Equals(found.Identity.Id, manifest.PluginId, StringComparison.OrdinalIgnoreCase) ||
                !String.Equals(found.Type.ToString(), manifest.PluginType, StringComparison.OrdinalIgnoreCase) ||
                !String.Equals(Path.GetFullPath(found.PrimaryPath), Path.GetFullPath(manifest.PrimaryPath), StringComparison.OrdinalIgnoreCase) ||
                found.LocalVersion.ParsedValue.CompareTo(VersionValue.Parse(manifest.OldVersion)) != VersionComparison.Equal ||
                manifest.RequiredBinaryPaths.Any(required => !found.Binaries.Any(binary => binary.Exists && String.Equals(Path.GetFullPath(binary.Path), Path.GetFullPath(required), StringComparison.OrdinalIgnoreCase))))
            {
                manifest.State = "RollbackVerificationFailed"; BackupService.Save(manifest);
                throw new InvalidOperationException("Файлы восстановлены, но проверка исходной версии или архитектур не прошла.");
            }
            manifest.State = "RolledBack"; BackupService.Save(manifest);
        }
        private static string Target(InstallManifest manifest, InstallManifestFile record)
        {
            var target = Path.GetFullPath(Path.Combine(manifest.TargetDirectory, PackageInspector.SafeRelativePath(record.RelativePath)));
            if (File.Exists(target) && (File.GetAttributes(target) & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("Reparse point в target file.");
            var cursor = Path.GetDirectoryName(target);
            var root = Path.GetFullPath(manifest.TargetDirectory);
            while (cursor.Length >= root.Length)
            {
                if (Directory.Exists(cursor) && (File.GetAttributes(cursor) & FileAttributes.ReparsePoint) != 0)
                    throw new InvalidDataException("Reparse point в target path.");
                if (String.Equals(cursor, root, StringComparison.OrdinalIgnoreCase)) break;
                cursor = Path.GetDirectoryName(cursor);
                if (cursor == null) break;
            }
            return target;
        }
        private static bool Equal(string left, string right) { return left != null && String.Equals(left, right, StringComparison.OrdinalIgnoreCase); }
        private static void Conflict(InstallManifest manifest, string file)
        {
            manifest.State = "RecoveryConflict"; BackupService.Save(manifest);
            throw new IOException("RecoveryConflict: файл изменён после установки: " + file);
        }
    }
}
