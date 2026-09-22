using System;
using System.Collections.Generic;
using TotalUpdater.Next.Core.Versions;

namespace TotalUpdater.Next.Core
{
    public enum PluginType { TotalCommander, Wcx, Wlx, Wfx, Wdx, Other }
    [Flags]
    public enum PluginArchitecture { Unknown = 0, X86 = 1, X64 = 2, AnyCpu = 4 }
    public enum PluginBinaryVariant { Ansi, Unicode, Native64, Other }
    public enum VersionSource { FileVersion, ProductVersion, TextFile, CustomRule, Unknown }
    public enum VersionConfidence { Exact, Probable, Heuristic, Unknown }
    public enum UpdateState { Unknown, NotChecked, Checking, UpToDate, UpdateAvailable, DevelopmentVersion, VersionComparisonUnknown, LocalVersionConflict, SourceUnavailable, PluginNotRecognized, Error }

    public sealed class PluginIdentity
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public PluginType Type { get; set; }
        public string LocalVersionStrategy { get; set; } = "";
    }

    public sealed class LocalVersion
    {
        public static readonly LocalVersion Unknown = new LocalVersion { RawValue = "", ParsedValue = VersionValue.Unknown, Source = VersionSource.Unknown, Confidence = VersionConfidence.Unknown };
        public string RawValue { get; set; } = "";
        public VersionValue ParsedValue { get; set; } = VersionValue.Unknown;
        public VersionSource Source { get; set; }
        public VersionConfidence Confidence { get; set; }
    }

    public sealed class InstalledPlugin
    {
        public PluginIdentity Identity { get; set; } = new PluginIdentity();
        public PluginType Type { get; set; }
        public string DisplayName { get; set; } = "";
        public string PrimaryPath { get; set; } = "";
        public List<PluginBinary> Binaries { get; set; } = new List<PluginBinary>();
        public List<string> RelatedFiles { get; set; } = new List<string>();
        public List<string> ConfigurationKeys { get; set; } = new List<string>();
        public LocalVersion LocalVersion { get; set; } = LocalVersion.Unknown;
        public PluginArchitecture Architecture { get; set; }
        public bool FileExists { get; set; }
        public bool HasVersionConflict { get; set; }
    }

    public sealed class PluginBinary
    {
        public string Path { get; set; } = "";
        public PluginArchitecture Architecture { get; set; }
        public PluginBinaryVariant Variant { get; set; }
        public bool Exists { get; set; }
        public LocalVersion LocalVersion { get; set; } = LocalVersion.Unknown;
    }

    public sealed class UpdateCandidate
    {
        public InstalledPlugin Plugin { get; set; } = new InstalledPlugin();
        public VersionValue InstalledVersion { get; set; } = VersionValue.Unknown;
        public VersionValue AvailableVersion { get; set; } = VersionValue.Unknown;
        public UpdateState State { get; set; }
        public string SourceName { get; set; } = "";
        public Uri SourceUrl { get; set; }
        public Uri DownloadUrl { get; set; }
        public string Details { get; set; } = "";
    }
}
