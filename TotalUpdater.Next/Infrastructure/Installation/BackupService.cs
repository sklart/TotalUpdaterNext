using System;
using System.IO;
using System.Linq;
using System.Runtime.Serialization.Json;
using TotalUpdater.Next.Core.Installation;

namespace TotalUpdater.Next.Infrastructure.Installation
{
    public sealed class BackupService
    {
        public static InstallManifest FindLatest(string backupRoot)
        {
            if (!Directory.Exists(backupRoot)) return null;
            return Directory.GetDirectories(backupRoot).Select(path => Path.Combine(path, "manifest.json"))
                .Where(File.Exists).Select(path => { try { return Load(path); } catch { return null; } })
                .Where(x => x != null && x.State == "Completed").OrderByDescending(x => x.CreatedUtc).FirstOrDefault();
        }
        public InstallManifest Create(InstallPlan plan)
        {
            Directory.CreateDirectory(plan.BackupDirectory);
            var manifest = new InstallManifest { PluginId = plan.Plugin.Identity.Id, PluginType = plan.Plugin.Type.ToString(), PrimaryPath = plan.Plugin.PrimaryPath,
                OldVersion = plan.OldVersion, NewVersion = plan.NewVersion, PackageUrl = plan.PackageUrl, PackageSha256 = plan.Package.PackageSha256,
                TargetDirectory = plan.TargetDirectory, BackupDirectory = plan.BackupDirectory, CreatedUtc = DateTime.UtcNow, State = "Prepared" };
            foreach (var item in plan.Files)
            {
                var record = new InstallManifestFile { RelativePath = item.Source.RelativePath, Replaced = item.ReplacesExisting, InstalledSha256 = item.Source.Sha256 };
                if (item.ReplacesExisting)
                {
                    var info = new FileInfo(item.Destination);
                    record.OriginalSha256 = PackageInspector.Hash(item.Destination);
                    record.CreationTimeUtc = info.CreationTimeUtc; record.LastWriteTimeUtc = info.LastWriteTimeUtc; record.Attributes = info.Attributes;
                    record.BackupPath = Path.Combine(plan.BackupDirectory, "files", item.Source.RelativePath);
                    Directory.CreateDirectory(Path.GetDirectoryName(record.BackupPath));
                    File.Copy(item.Destination, record.BackupPath, false);
                    if (PackageInspector.Hash(record.BackupPath) != record.OriginalSha256) throw new IOException("Backup checksum mismatch.");
                }
                manifest.Files.Add(record);
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

        public static InstallManifest Load(string path)
        {
            using (var input = File.OpenRead(path)) return (InstallManifest)new DataContractJsonSerializer(typeof(InstallManifest)).ReadObject(input);
        }
    }
}
