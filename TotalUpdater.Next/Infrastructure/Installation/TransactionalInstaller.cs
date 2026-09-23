using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using TotalUpdater.Next.Core;
using TotalUpdater.Next.Core.Installation;
using TotalUpdater.Next.Core.Versions;

namespace TotalUpdater.Next.Infrastructure.Installation
{
    public sealed class TransactionalInstaller
    {
        private readonly BackupService _backups;
        private readonly RollbackService _rollback;
        private readonly Action<int> _afterFile;
        private readonly Func<bool> _isTotalCommanderRunning;
        public TransactionalInstaller(BackupService backups = null, RollbackService rollback = null, Action<int> afterFile = null, Func<bool> isTotalCommanderRunning = null)
        { _backups = backups ?? new BackupService(); _rollback = rollback ?? new RollbackService(); _afterFile = afterFile; _isTotalCommanderRunning = isTotalCommanderRunning; }

        public InstallManifest Install(InstallPlan plan, Func<InstalledPlugin> rediscover)
        {
            if (plan == null || rediscover == null || plan.UserConfigRisk) throw new InvalidOperationException("Некорректный план установки.");
            Preflight(plan, _isTotalCommanderRunning);
            var manifest = _backups.Create(plan);
            try
            {
                manifest.State = "Installing"; BackupService.Save(manifest);
                var installedCount = 0;
                foreach (var file in plan.Files)
                {
                    if (PackageInspector.Hash(file.Source.StagedPath) != file.Source.Sha256) throw new IOException("Staging file changed.");
                    Directory.CreateDirectory(Path.GetDirectoryName(file.Destination));
                    var temporary = file.Destination + ".tu-new-" + Guid.NewGuid().ToString("N");
                    try
                    {
                        File.Copy(file.Source.StagedPath, temporary, false);
                        if (file.ReplacesExisting) File.Replace(temporary, file.Destination, null);
                        else File.Move(temporary, file.Destination);
                    }
                    finally { if (File.Exists(temporary)) File.Delete(temporary); }
                    _afterFile?.Invoke(++installedCount);
                }
                var detected = rediscover();
                if (detected == null || !detected.FileExists || detected.HasVersionConflict ||
                    !String.Equals(detected.Identity.Id, plan.Plugin.Identity.Id, StringComparison.OrdinalIgnoreCase) || detected.Type != plan.Plugin.Type ||
                    !String.Equals(Path.GetFullPath(detected.PrimaryPath), Path.GetFullPath(plan.Plugin.PrimaryPath), StringComparison.OrdinalIgnoreCase) ||
                    detected.LocalVersion.ParsedValue.CompareTo(VersionValue.Parse(plan.NewVersion)) != VersionComparison.Equal ||
                    plan.Plugin.Binaries.Where(x => x.Exists).Any(old => !detected.Binaries.Any(current => current.Exists && String.Equals(current.Path, old.Path, StringComparison.OrdinalIgnoreCase))))
                    throw new InvalidOperationException("Post-install verification failed: plugin/version/architecture mismatch.");
                manifest.State = "Completed"; BackupService.Save(manifest);
                return manifest;
            }
            catch (Exception original)
            {
                try { _rollback.Rollback(manifest); }
                catch (Exception rollbackError) { throw new AggregateException("Ошибка установки и автоматического отката; backup сохранён: " + manifest.BackupDirectory, original, rollbackError); }
                throw;
            }
        }

        public static void Preflight(InstallPlan plan, Func<bool> isTotalCommanderRunning = null)
        {
            if ((isTotalCommanderRunning ?? (() => Process.GetProcessesByName("TOTALCMD").Any() || Process.GetProcessesByName("TOTALCMD64").Any()))())
                throw new InvalidOperationException("Закройте Total Commander перед установкой (TOTALCMD.EXE / TOTALCMD64.EXE).");
            if (!Directory.Exists(plan.TargetDirectory)) throw new DirectoryNotFoundException(plan.TargetDirectory);
            var targetRoot = Path.GetFullPath(plan.TargetDirectory).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            long required = plan.Files.Sum(x => x.Source.Length);
            long backupRequired = plan.Files.Where(x => x.ReplacesExisting).Sum(x => new FileInfo(x.Destination).Length);
            var drive = new DriveInfo(Path.GetPathRoot(plan.TargetDirectory));
            var backupDrive = new DriveInfo(Path.GetPathRoot(plan.BackupDirectory));
            if (drive.AvailableFreeSpace < required + (String.Equals(drive.Name, backupDrive.Name, StringComparison.OrdinalIgnoreCase) ? backupRequired : 0) ||
                backupDrive.AvailableFreeSpace < backupRequired)
                throw new IOException("Недостаточно свободного места для установки и backup.");
            foreach (var item in plan.Files)
            {
                var path = Path.GetFullPath(item.Destination);
                if (!path.StartsWith(targetRoot, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Путь вне каталога плагина.");
                var cursor = Path.GetDirectoryName(path);
                while (cursor.Length >= plan.TargetDirectory.Length)
                {
                    if (Directory.Exists(cursor) && (File.GetAttributes(cursor) & FileAttributes.ReparsePoint) != 0) throw new IOException("Каталог назначения содержит reparse point.");
                    if (String.Equals(cursor, plan.TargetDirectory, StringComparison.OrdinalIgnoreCase)) break;
                    cursor = Path.GetDirectoryName(cursor);
                }
                if (File.Exists(path))
                {
                    var attributes = File.GetAttributes(path);
                    if ((attributes & (FileAttributes.ReadOnly | FileAttributes.ReparsePoint)) != 0) throw new UnauthorizedAccessException("Файл только для чтения или ссылка: " + path);
                    using (new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) { }
                }
            }
            var probe = Path.Combine(plan.TargetDirectory, ".tu-write-probe-" + Guid.NewGuid().ToString("N"));
            try { using (File.Create(probe)) { } }
            catch (UnauthorizedAccessException) { throw new UnauthorizedAccessException("Установка недоступна — требуются права администратора."); }
            finally { if (File.Exists(probe)) File.Delete(probe); }
        }
    }
}
