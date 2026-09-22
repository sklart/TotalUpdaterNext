using System;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using TotalUpdater.Next.Core;
using TotalUpdater.Next.Resources;

namespace TotalUpdater.Next.UI
{
    public sealed class PluginRowViewModel : INotifyPropertyChanged
    {
        private bool _isChecked; private string _availableVersion = "—"; private string _status = Text.Get("NotChecked"); private UpdateCandidate _candidate;
        public PluginRowViewModel(InstalledPlugin plugin) { Plugin = plugin; if (plugin.HasVersionConflict) _status = Text.Get("VersionConflict"); }
        public InstalledPlugin Plugin { get; private set; }
        public bool IsChecked { get { return _isChecked; } set { _isChecked = value; Changed(); } }
        public string Name { get { return Plugin.DisplayName; } }
        public string Type { get { return Plugin.Type.ToString().ToUpperInvariant(); } }
        public string Group { get { return Plugin.Type == PluginType.TotalCommander ? "Total Commander" : Type; } }
        public string InstalledVersion { get { return Plugin.FileExists && Plugin.LocalVersion.ParsedValue.IsKnown ? Plugin.LocalVersion.RawValue : Plugin.FileExists ? Text.Get("NotDefined") : Text.Get("NotFound"); } }
        public string AvailableVersion { get { return _availableVersion; } private set { _availableVersion = value; Changed(); } }
        public string Status { get { return _status; } private set { _status = value; Changed(); Changed("HasError"); Changed("HasUpdate"); Changed("CanDownload"); } }
        public string Path { get { return Plugin.PrimaryPath; } }
        public string Information
        {
            get
            {
                var lines = new System.Collections.Generic.List<string> { Type + " · " + ArchitectureName(Plugin.Architecture) };
                if (Plugin.HasVersionConflict) { lines.Add(""); lines.Add(Text.Get("VersionConflictWarning")); }
                foreach (var binary in Plugin.Binaries.Where(x => x.Exists))
                {
                    lines.Add(""); lines.Add(BinaryName(binary) + ": " + (binary.LocalVersion.ParsedValue.IsKnown ? binary.LocalVersion.RawValue : Text.Get("NotDefined"))); lines.Add(binary.Path);
                }
                if (_candidate != null)
                {
                    lines.Add(""); lines.Add("Последняя версия: " + (_candidate.AvailableVersion.IsKnown ? _candidate.AvailableVersion.Raw : "—"));
                    lines.Add("Источник версии: " + (_candidate.CanonicalVersionSource == null ? "—" : _candidate.CanonicalVersionSource.ProviderName));
                    lines.Add("Источник загрузки: " + (_candidate.DownloadSource == null ? "—" : _candidate.DownloadSource.ProviderName));
                    if (_candidate.HasSourceDisagreement) { lines.Add("Источники сообщают разные версии"); foreach (var observation in _candidate.Observations.Where(x => x.Release != null && x.Release.Version.IsKnown)) lines.Add(observation.ProviderName + ": " + observation.Release.Version.Raw); }
                }
                return String.Join(Environment.NewLine, lines);
            }
        }
        public UpdateCandidate Candidate { get { return _candidate; } }
        public bool HasUpdate { get { return _candidate != null && _candidate.State == UpdateState.UpdateAvailable; } }
        public bool HasError { get { return _candidate != null && (_candidate.State == UpdateState.Error || _candidate.State == UpdateState.SourceUnavailable); } }
        public bool CanDownload { get { return _candidate != null && _candidate.State == UpdateState.UpdateAvailable && _candidate.DownloadUrl != null && !Plugin.HasVersionConflict; } }

        public void SetChecking() { Status = Plugin.HasVersionConflict ? Text.Get("VersionConflict") : Text.Get("Checking"); }
        public void Apply(UpdateCandidate candidate)
        {
            if (Plugin.HasVersionConflict) candidate.State = UpdateState.LocalVersionConflict;
            _candidate = candidate; AvailableVersion = candidate.AvailableVersion.IsKnown ? candidate.AvailableVersion.Raw : "—";
            Status = ToStatus(candidate.State, candidate.Details);
        }

        private static string ToStatus(UpdateState state, string details)
        {
            if (state == UpdateState.UpdateAvailable && !String.IsNullOrWhiteSpace(details)) return details;
            switch (state)
            {
                case UpdateState.UpToDate: return Text.Get("UpToDate"); case UpdateState.UpdateAvailable: return Text.Get("UpdateAvailable");
                case UpdateState.DevelopmentVersion: return Text.Get("DevelopmentVersion"); case UpdateState.VersionComparisonUnknown: return Text.Get("ComparisonUnknown"); case UpdateState.LocalVersionConflict: return Text.Get("VersionConflict");
                case UpdateState.PluginNotRecognized: return Text.Get("NotRecognized"); case UpdateState.SourceUnavailable: return String.IsNullOrWhiteSpace(details) ? Text.Get("SourceUnavailable") : Text.Get("SourceUnavailable") + ": " + details;
                default: return Text.Get("Error");
            }
        }
        private static string BinaryName(PluginBinary binary)
        {
            if (binary.Architecture == PluginArchitecture.X64) return "x64";
            return binary.Variant == PluginBinaryVariant.Unicode ? "Unicode x86" : "x86";
        }
        private static string ArchitectureName(PluginArchitecture architecture)
        {
            if ((architecture & PluginArchitecture.X86) != 0 && (architecture & PluginArchitecture.X64) != 0) return "x86 + x64";
            if ((architecture & PluginArchitecture.X64) != 0) return "x64";
            if ((architecture & PluginArchitecture.X86) != 0) return "x86";
            return "—";
        }
        public event PropertyChangedEventHandler PropertyChanged;
        private void Changed([CallerMemberName] string name = null) { var handler = PropertyChanged; if (handler != null) handler(this, new PropertyChangedEventArgs(name)); }
    }
}
