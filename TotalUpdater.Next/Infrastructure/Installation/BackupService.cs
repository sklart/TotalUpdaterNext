using System;
using System.IO;
using System.Linq;
using System.Runtime.Serialization.Json;
using System.Collections.Generic;
using TotalUpdater.Next.Core.Installation;

namespace TotalUpdater.Next.Infrastructure.Installation
{
    public sealed class BackupService
    {
        public static InstallManifest FindLatest(string backupRoot, string pluginId, string primaryPath)
        {
            return Enumerate(backupRoot).Where(x => (x.State == InstallStateMachine.Completed || x.State == InstallStateMachine.InstallConflict ||
                x.State == InstallStateMachine.RecoveryConflict || x.State == InstallStateMachine.ConfigRecoveryConflict ||
                x.State == InstallStateMachine.ConfigConflict || x.State == InstallStateMachine.RollbackVerificationFailed) &&
                String.Equals(x.PluginId, pluginId, StringComparison.OrdinalIgnoreCase) &&
                SamePath(x.PrimaryPath, primaryPath)).OrderByDescending(x => x.CreatedUtc).FirstOrDefault();
        }
        public static IList<InstallManifest> FindIncomplete(string backupRoot)
        {
            return Enumerate(backupRoot).Where(x => x.State == InstallStateMachine.Prepared || x.State == InstallStateMachine.Installing ||
                x.State == InstallStateMachine.InstallConflict || x.State == InstallStateMachine.RollingBack ||
                x.State == InstallStateMachine.RecoveryConflict || x.State == InstallStateMachine.ConfigRecoveryConflict ||
                x.State == InstallStateMachine.ConfigConflict || x.State == InstallStateMachine.RollbackVerificationFailed)
                .OrderBy(x => x.CreatedUtc).ToList();
        }
        private static IEnumerable<InstallManifest> Enumerate(string backupRoot)
        {
            if (!Directory.Exists(backupRoot)) yield break;
            foreach (var directory in Directory.GetDirectories(backupRoot))
            {
                var path = Path.Combine(directory, "manifest.json");
                if (!File.Exists(path)) continue;
                InstallManifest manifest = null;
                try { manifest = Load(path, backupRoot); } catch (Exception) { }
                if (manifest != null) yield return manifest;
            }
        }
        public InstallManifest Create(InstallPlan plan)
        {
            Directory.CreateDirectory(plan.BackupDirectory);
            var manifest = new InstallManifest { ManifestVersion = 3, TransactionId = Path.GetFileName(plan.BackupDirectory),
                PluginId = plan.Plugin.Identity.Id, PluginType = plan.Plugin.Type.ToString(), PrimaryPath = plan.Plugin.PrimaryPath,
                OldVersion = plan.OldVersion, NewVersion = plan.NewVersion, PackageUrl = plan.PackageUrl, PackageSha256 = plan.Package.PackageSha256,
                CanonicalSource = plan.CanonicalSource, DownloadSource = plan.DownloadSource, DownloadAuthority = plan.DownloadAuthority,
                RequiredBinaryPaths = plan.Plugin.Binaries.Where(x => x.Exists).Select(x => Path.GetFullPath(x.Path)).ToList(),
                TargetDirectory = plan.TargetDirectory, BackupDirectory = plan.BackupDirectory, CreatedUtc = DateTime.UtcNow, State = "Prepared" };
            foreach (var item in plan.Files)
            {
                var record = new InstallManifestFile { State = InstallStateMachine.PendingFile, RelativePath = item.Source.RelativePath, Replaced = item.ReplacesExisting, InstalledSha256 = item.Source.Sha256 };
                if (item.ReplacesExisting)
                {
                    var info = new FileInfo(item.Destination);
                    record.OriginalSha256 = PackageInspector.Hash(item.Destination);
                    record.CreationTimeUtc = info.CreationTimeUtc; record.LastWriteTimeUtc = info.LastWriteTimeUtc; record.Attributes = info.Attributes;
                    record.BackupPath = ResolveBackupPath(manifest, record);
                    Directory.CreateDirectory(Path.GetDirectoryName(record.BackupPath));
                    File.Copy(item.Destination, record.BackupPath, false);
                    if (PackageInspector.Hash(record.BackupPath) != record.OriginalSha256) throw new IOException("Backup checksum mismatch.");
                }
                manifest.Files.Add(record);
            }
            Save(manifest);
            return manifest;
        }
        public InstallManifest CreateNew(NewPluginInstallPlan plan)
        {
            Directory.CreateDirectory(plan.BackupDirectory);
            var manifest = new InstallManifest { ManifestVersion = 4, TransactionId = Path.GetFileName(plan.BackupDirectory),
                PluginId = plan.PluginId, PluginType = plan.PluginType.ToString(), PrimaryPath = plan.PrimaryPath,
                NewVersion = plan.Version, PackageUrl = plan.PackageUrl, PackageSha256 = plan.Package.PackageSha256,
                RequiredBinaryPaths = plan.RequiredBinaryPaths.ToList(), TargetDirectory = plan.TargetDirectory,
                BackupDirectory = plan.BackupDirectory, CreatedUtc = DateTime.UtcNow, State = InstallStateMachine.Prepared };
            foreach (var item in plan.Files)
                manifest.Files.Add(new InstallManifestFile { State = InstallStateMachine.PendingFile, RelativePath = item.Source.RelativePath,
                    Replaced = false, InstalledSha256 = item.Source.Sha256 });
            for (var index = 0; index < plan.ConfigurationFiles.Count; index++)
            {
                var patch = plan.ConfigurationFiles[index];
                if (!File.Exists(patch.Path) || !String.Equals(PackageInspector.Hash(patch.Path), patch.OriginalSha256, StringComparison.OrdinalIgnoreCase))
                    throw new IOException("ConfigConflict: INI изменён до создания backup: " + patch.Path);
                var record = new InstallManifestConfiguration { ConfigFile = patch.Path, OriginalSha256 = patch.OriginalSha256,
                    InstalledSha256 = patch.InstalledSha256, State = InstallStateMachine.PendingFile, Changes = patch.Changes };
                record.BackupPath = ResolveConfigurationBackupPath(manifest, index);
                Directory.CreateDirectory(Path.GetDirectoryName(record.BackupPath));
                File.WriteAllBytes(record.BackupPath, patch.OriginalBytes);
                if (!String.Equals(PackageInspector.Hash(record.BackupPath), record.OriginalSha256, StringComparison.OrdinalIgnoreCase))
                    throw new IOException("Backup checksum mismatch: " + patch.Path);
                manifest.ConfigurationFiles.Add(record);
            }
            Save(manifest);
            return manifest;
        }

        public static void Save(InstallManifest manifest)
        {
            var path = Path.Combine(manifest.BackupDirectory, "manifest.json");
            var temporary = path + ".tmp";
            using (var output = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
                new DataContractJsonSerializer(typeof(InstallManifest)).WriteObject(output, manifest);
            if (File.Exists(path)) File.Replace(temporary, path, null);
            else File.Move(temporary, path);
        }

        public static InstallManifest Load(string path, string backupRoot)
        {
            InstallManifest manifest;
            using (var input = File.OpenRead(path)) manifest = (InstallManifest)new DataContractJsonSerializer(typeof(InstallManifest)).ReadObject(input);
            Validate(manifest, path, backupRoot);
            return manifest;
        }
        public static void Validate(InstallManifest manifest, string path, string backupRoot)
        {
            if (manifest == null || String.IsNullOrWhiteSpace(backupRoot) || String.IsNullOrWhiteSpace(manifest.BackupDirectory) ||
                String.IsNullOrWhiteSpace(manifest.PrimaryPath) || String.IsNullOrWhiteSpace(manifest.TargetDirectory) || String.IsNullOrWhiteSpace(manifest.PluginId) ||
                manifest.Files == null || manifest.Files.Count == 0) throw new InvalidDataException("Неполный manifest.");
            if (!Path.IsPathRooted(backupRoot) || !Path.IsPathRooted(manifest.BackupDirectory) || !Path.IsPathRooted(manifest.PrimaryPath) || !Path.IsPathRooted(manifest.TargetDirectory))
                throw new InvalidDataException("Manifest содержит не абсолютные пути.");
            var directory = Path.GetFullPath(Path.GetDirectoryName(path));
            var root = Path.GetFullPath(backupRoot).TrimEnd(Path.DirectorySeparatorChar);
            if (!SamePath(Path.GetDirectoryName(directory), root) || !SamePath(directory, manifest.BackupDirectory) ||
                !SamePath(Path.Combine(directory, "manifest.json"), path) ||
                (manifest.ManifestVersion != 4 && !SamePath(Path.GetDirectoryName(Path.GetFullPath(manifest.PrimaryPath)), manifest.TargetDirectory)))
                throw new InvalidDataException("Manifest находится вне backup root или target не соответствует primary path.");
            if (manifest.ManifestVersion != 0 && manifest.ManifestVersion != 2 && manifest.ManifestVersion != 3 && manifest.ManifestVersion != 4) throw new InvalidDataException("Неизвестная версия manifest.");
            if (manifest.ManifestVersion == 4 && !Path.GetFullPath(manifest.PrimaryPath).StartsWith(Path.GetFullPath(manifest.TargetDirectory).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Primary path вне target.");
            Guid transactionId;
            if (manifest.ManifestVersion >= 2 && (!Guid.TryParseExact(manifest.TransactionId, "N", out transactionId) ||
                !String.Equals(manifest.TransactionId, Path.GetFileName(directory), StringComparison.OrdinalIgnoreCase)))
                throw new InvalidDataException("TransactionId не совпадает с каталогом backup.");
            if (!InstallStateMachine.IsKnown(manifest.State))
                throw new InvalidDataException("Неизвестное состояние manifest.");
            RejectReparse(root, directory);
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var record in manifest.Files)
            {
                var safe = PackageInspector.SafeRelativePath(record.RelativePath);
                if (!seen.Add(safe) || String.Equals(safe, "pluginst.inf", StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Повтор или недопустимый файл в manifest.");
                var target = Path.GetFullPath(Path.Combine(manifest.TargetDirectory, safe));
                if (!target.StartsWith(Path.GetFullPath(manifest.TargetDirectory).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Файл manifest вне target.");
                if (record.Replaced && String.IsNullOrWhiteSpace(record.OriginalSha256)) throw new InvalidDataException("Отсутствует original hash.");
                if (String.IsNullOrWhiteSpace(record.InstalledSha256)) throw new InvalidDataException("Отсутствует installed hash.");
                if (manifest.ManifestVersion >= 3 && !InstallStateMachine.IsKnownFile(record.State)) throw new InvalidDataException("Неизвестное состояние файла manifest.");
                if (manifest.ManifestVersion == 4 && record.Replaced) throw new InvalidDataException("Новая установка не заменяет существующие файлы.");
            }
            if (manifest.ManifestVersion >= 2 && (manifest.RequiredBinaryPaths == null || manifest.RequiredBinaryPaths.Count == 0))
                throw new InvalidDataException("Нет списка required binaries.");
            if (manifest.RequiredBinaryPaths == null || manifest.RequiredBinaryPaths.Count == 0)
                manifest.RequiredBinaryPaths = new List<string> { manifest.PrimaryPath };
            if (manifest.RequiredBinaryPaths.Any(x => !Path.IsPathRooted(x) || !Path.GetFullPath(x).StartsWith(Path.GetFullPath(manifest.TargetDirectory).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)))
                throw new InvalidDataException("Путь required binary вне target.");
            if (manifest.ManifestVersion == 4)
            {
                var creationRoot = Path.GetDirectoryName(Path.GetDirectoryName(Path.GetFullPath(manifest.TargetDirectory)));
                if (manifest.CreatedDirectories == null || manifest.CreatedDirectories.Any(x => !Path.IsPathRooted(x) ||
                    (!SamePath(x, creationRoot) && !Path.GetFullPath(x).StartsWith(creationRoot.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))))
                    throw new InvalidDataException("Created directory вне target.");
                if (manifest.ConfigurationFiles == null || manifest.ConfigurationFiles.Count == 0) throw new InvalidDataException("Нет configuration records.");
                var configPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                for (var index = 0; index < manifest.ConfigurationFiles.Count; index++)
                {
                    var config = manifest.ConfigurationFiles[index];
                    if (config == null || !Path.IsPathRooted(config.ConfigFile) || !configPaths.Add(Path.GetFullPath(config.ConfigFile)) ||
                        !SamePath(config.BackupPath, ResolveConfigurationBackupPath(manifest, index)) ||
                        String.IsNullOrWhiteSpace(config.OriginalSha256) || String.IsNullOrWhiteSpace(config.InstalledSha256) ||
                        !InstallStateMachine.IsKnownFile(config.State) || config.Changes == null || config.Changes.Count == 0)
                        throw new InvalidDataException("Недопустимый configuration record.");
                }
            }
        }
        public static string ResolveConfigurationBackupPath(InstallManifest manifest, int index)
        {
            if (index < 0) throw new ArgumentOutOfRangeException(nameof(index));
            var root = Path.GetFullPath(Path.Combine(manifest.BackupDirectory, "config"));
            var path = Path.Combine(root, index.ToString(System.Globalization.CultureInfo.InvariantCulture) + ".ini");
            RejectReparse(manifest.BackupDirectory, root);
            return path;
        }
        public static string ResolveBackupPath(InstallManifest manifest, InstallManifestFile record)
        {
            var root = Path.GetFullPath(Path.Combine(manifest.BackupDirectory, "files"));
            var path = Path.GetFullPath(Path.Combine(root, PackageInspector.SafeRelativePath(record.RelativePath)));
            if (!path.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Backup path вне backup directory.");
            RejectReparse(manifest.BackupDirectory, Path.GetDirectoryName(path));
            if (File.Exists(path) && (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("Reparse point в backup file.");
            return path;
        }
        private static void RejectReparse(string root, string endpoint)
        {
            var cursor = Path.GetFullPath(endpoint);
            var boundary = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar);
            while (cursor.Length >= boundary.Length)
            {
                if (Directory.Exists(cursor) && (File.GetAttributes(cursor) & FileAttributes.ReparsePoint) != 0)
                    throw new InvalidDataException("Reparse point в backup path.");
                if (SamePath(cursor, boundary)) break;
                cursor = Path.GetDirectoryName(cursor);
                if (cursor == null) break;
            }
        }
        private static bool SamePath(string left, string right)
        {
            return String.Equals(Path.GetFullPath(left).TrimEnd(Path.DirectorySeparatorChar), Path.GetFullPath(right).TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase);
        }
    }
}
