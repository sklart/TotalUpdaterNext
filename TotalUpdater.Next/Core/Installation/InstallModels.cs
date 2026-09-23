using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;

namespace TotalUpdater.Next.Core.Installation
{
    public sealed class PackageLimits
    {
        public int MaxEntries { get; set; } = 4096;
        public long MaxFileBytes { get; set; } = 256L * 1024 * 1024;
        public long MaxTotalBytes { get; set; } = 512L * 1024 * 1024;
        public double MaxCompressionRatio { get; set; } = 1000;
    }
    public sealed class PackageFile
    {
        public string RelativePath { get; set; }
        public string StagedPath { get; set; }
        public long Length { get; set; }
        public string Sha256 { get; set; }
    }
    public sealed class PackageInspection
    {
        public string PackagePath { get; set; }
        public string StagingDirectory { get; set; }
        public string PackageSha256 { get; set; }
        public string Type { get; set; }
        public string File { get; set; }
        public string Version { get; set; }
        public string DefaultDir { get; set; }
        public string DefaultExtension { get; set; }
        public List<PackageFile> Files { get; set; } = new List<PackageFile>();
    }
    public sealed class InstallPlan
    {
        public InstalledPlugin Plugin { get; set; }
        public PackageInspection Package { get; set; }
        public string TargetDirectory { get; set; }
        public string BackupDirectory { get; set; }
        public string OldVersion { get; set; }
        public string NewVersion { get; set; }
        public string PackageUrl { get; set; }
        public bool UserConfigRisk { get; set; }
        public List<InstallFile> Files { get; set; } = new List<InstallFile>();
    }
    public sealed class InstallFile
    {
        public PackageFile Source { get; set; }
        public string Destination { get; set; }
        public bool ReplacesExisting { get; set; }
    }
    [DataContract]
    public sealed class InstallManifest
    {
        [DataMember] public string PluginId { get; set; }
        [DataMember] public string PluginType { get; set; }
        [DataMember] public string PrimaryPath { get; set; }
        [DataMember] public string OldVersion { get; set; }
        [DataMember] public string NewVersion { get; set; }
        [DataMember] public string PackageUrl { get; set; }
        [DataMember] public string PackageSha256 { get; set; }
        [DataMember] public string TargetDirectory { get; set; }
        [DataMember] public string BackupDirectory { get; set; }
        [DataMember] public string State { get; set; }
        [DataMember] public DateTime CreatedUtc { get; set; }
        [DataMember] public List<InstallManifestFile> Files { get; set; } = new List<InstallManifestFile>();
    }
    [DataContract]
    public sealed class InstallManifestFile
    {
        [DataMember] public string RelativePath { get; set; }
        [DataMember] public bool Replaced { get; set; }
        [DataMember] public string OriginalSha256 { get; set; }
        [DataMember] public string InstalledSha256 { get; set; }
        [DataMember] public string BackupPath { get; set; }
        [DataMember] public DateTime? CreationTimeUtc { get; set; }
        [DataMember] public DateTime? LastWriteTimeUtc { get; set; }
        [DataMember] public FileAttributes Attributes { get; set; }
    }
}
