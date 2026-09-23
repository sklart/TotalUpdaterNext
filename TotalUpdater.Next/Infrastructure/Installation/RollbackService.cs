using System;
using System.IO;
using System.Linq;
using TotalUpdater.Next.Core.Installation;

namespace TotalUpdater.Next.Infrastructure.Installation
{
    public sealed class RollbackService
    {
        public void Rollback(InstallManifest manifest)
        {
            if (manifest == null || manifest.State == "RolledBack") return;
            var root = Path.GetFullPath(manifest.TargetDirectory).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            foreach (var record in manifest.Files.AsEnumerable().Reverse())
            {
                var safe = PackageInspector.SafeRelativePath(record.RelativePath);
                var target = Path.GetFullPath(Path.Combine(manifest.TargetDirectory, safe));
                if (!target.StartsWith(root, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Недопустимый путь в manifest.");
                if (record.Replaced)
                {
                    if (!File.Exists(record.BackupPath) || PackageInspector.Hash(record.BackupPath) != record.OriginalSha256)
                        throw new IOException("Backup повреждён: " + safe);
                    if (!File.Exists(target) || PackageInspector.Hash(target) != record.OriginalSha256)
                    {
                        var temporary = target + ".tu-restore-" + Guid.NewGuid().ToString("N");
                        File.Copy(record.BackupPath, temporary);
                        if (File.Exists(target)) File.Replace(temporary, target, null); else File.Move(temporary, target);
                    }
                    File.SetAttributes(target, record.Attributes);
                    if (record.CreationTimeUtc.HasValue) File.SetCreationTimeUtc(target, record.CreationTimeUtc.Value);
                    if (record.LastWriteTimeUtc.HasValue) File.SetLastWriteTimeUtc(target, record.LastWriteTimeUtc.Value);
                }
                else if (File.Exists(target))
                {
                    if (PackageInspector.Hash(target) != record.InstalledSha256)
                        throw new IOException("Новый файл изменён после установки; удаление запрещено: " + safe);
                    File.Delete(target);
                }
            }
            manifest.State = "RolledBack"; BackupService.Save(manifest);
        }
    }
}
