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
        public string Status { get { return _status; } private set { _status = value; Changed(); Changed("HasError"); Changed("HasUpdate"); } }
        public string Path { get { return Plugin.PrimaryPath; } }
        public string Information
        {
            get
            {
                var binaries = Plugin.Binaries == null ? "" : String.Join(Environment.NewLine, Plugin.Binaries.Where(x => x.Exists).Select(x => BinaryName(x) + ": " + x.Path + " — " + (x.LocalVersion.ParsedValue.IsKnown ? x.LocalVersion.RawValue : Text.Get("NotDefined"))));
                return Type + " · " + ArchitectureName(Plugin.Architecture) + Environment.NewLine + binaries;
            }
        }
        public UpdateCandidate Candidate { get { return _candidate; } }
        public bool HasUpdate { get { return _candidate != null && _candidate.State == UpdateState.UpdateAvailable; } }
        public bool HasError { get { return _candidate != null && (_candidate.State == UpdateState.Error || _candidate.State == UpdateState.SourceUnavailable); } }

        public void SetChecking() { Status = Text.Get("Checking"); }
        public void Apply(UpdateCandidate candidate)
        {
            _candidate = candidate; AvailableVersion = candidate.AvailableVersion.IsKnown ? candidate.AvailableVersion.Raw : "—";
            Status = ToStatus(candidate.State, candidate.Details);
        }

        private static string ToStatus(UpdateState state, string details)
        {
            if (state == UpdateState.UpdateAvailable && !String.IsNullOrWhiteSpace(details)) return details;
            switch (state)
            {
                case UpdateState.UpToDate: return Text.Get("UpToDate"); case UpdateState.UpdateAvailable: return Text.Get("UpdateAvailable");
                case UpdateState.DevelopmentVersion: return Text.Get("DevelopmentVersion"); case UpdateState.VersionComparisonUnknown: return Text.Get("ComparisonUnknown");
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
