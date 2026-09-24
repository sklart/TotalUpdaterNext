using System;
using System.ComponentModel;
using TotalUpdater.Next.Catalog;
using TotalUpdater.Next.Core.Installation;

namespace TotalUpdater.Next.UI
{
    public sealed class CatalogRowViewModel : INotifyPropertyChanged
    {
        public CatalogRowViewModel(PluginCatalogEntry entry, bool installed) { Entry = entry; Installed = installed; }
        public PluginCatalogEntry Entry { get; private set; }
        public bool Installed { get; private set; }
        public bool IsChecked { get; set; }
        public string Name { get { return Entry.Name; } }
        public string Type { get { return Entry.Type; } }
        public string InstallStatus { get { return Installed ? "Установлен" : "Не установлен"; } }
        public string AvailableVersion { get { return Candidate == null || !Candidate.Version.IsKnown ? "—" : Candidate.Version.Raw; } }
        public string Source { get { return Candidate == null ? "—" : Candidate.SourceName; } }
        public string Status { get { if (Entry.PluginType == Core.PluginType.Wcx && !Entry.HasVerifiedWcxRegistration) return "Требуется проверка регистрации"; return Candidate == null ? "Не проверен" : Candidate.DownloadUrl == null ? Candidate.Details : Entry.PluginType == Core.PluginType.Wcx ? "Можно установить" : "Доступен ZIP для установки"; } }
        public CatalogInstallCandidate Candidate { get; private set; }
        public void Apply(CatalogInstallCandidate candidate)
        {
            Candidate = candidate;
            foreach (var name in new[] { "AvailableVersion", "Source", "Status" }) PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
        public event PropertyChangedEventHandler PropertyChanged;
    }
}
