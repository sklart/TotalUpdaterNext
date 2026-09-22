using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Data;
using Microsoft.Win32;
using TotalUpdater.Next.Catalog;
using TotalUpdater.Next.Core;
using TotalUpdater.Next.Infrastructure;
using TotalUpdater.Next.Resources;
using TotalUpdater.Next.TotalCommander;

namespace TotalUpdater.Next.UI
{
    public sealed class MainViewModel : INotifyPropertyChanged, IDisposable
    {
        private readonly TotalCommanderConfigurationResolver _resolver;
        private readonly PluginDiscoveryService _discovery;
        private readonly UpdateService _updates;
        private readonly CatalogService _catalog;
        private readonly DownloadService _downloads;
        private readonly UserDataPaths _paths;
        private readonly CollectionViewSource _itemsView;
        private TotalCommanderConfiguration _configuration;
        private string _iniPath = ""; private string _statusText = ""; private string _filter = "All";

        public MainViewModel(TotalCommanderConfigurationResolver resolver, PluginDiscoveryService discovery, UpdateService updates, CatalogService catalog, DownloadService downloads, UserDataPaths paths)
        {
            _resolver = resolver; _discovery = discovery; _updates = updates; _catalog = catalog; _downloads = downloads; _paths = paths;
            Items = new ObservableCollection<PluginRowViewModel>(); _itemsView = new CollectionViewSource { Source = Items }; _itemsView.GroupDescriptions.Add(new PropertyGroupDescription("Group")); _itemsView.Filter += Filter;
            DiscoverCommand = new RelayCommand(x => Discover()); CheckCommand = new RelayCommand(async x => await CheckAsync()); DownloadCommand = new RelayCommand(async x => await DownloadAsync());
            BrowseIniCommand = new RelayCommand(x => BrowseIni()); OpenUserCatalogCommand = new RelayCommand(x => OpenUserCatalog()); OpenSiteCommand = new RelayCommand(x => OpenSite(x as PluginRowViewModel), x => x is PluginRowViewModel row && row.Candidate != null && row.Candidate.SourceUrl != null);
            OpenPathCommand = new RelayCommand(x => OpenPath(x as PluginRowViewModel), x => x is PluginRowViewModel row && File.Exists(row.Path)); CopyPathCommand = new RelayCommand(x => System.Windows.Clipboard.SetText((x as PluginRowViewModel).Path), x => x is PluginRowViewModel row && !String.IsNullOrWhiteSpace(row.Path)); InfoCommand = new RelayCommand(x => ShowInfo(x as PluginRowViewModel), x => x is PluginRowViewModel);
            UserCatalogEntries = new ObservableCollection<PluginCatalogEntry>(_catalog.LoadUserCatalog());
        }

        public ObservableCollection<PluginRowViewModel> Items { get; private set; }
        public ObservableCollection<PluginCatalogEntry> UserCatalogEntries { get; private set; }
        public ICollectionView ItemsView { get { return _itemsView.View; } }
        public RelayCommand DiscoverCommand { get; private set; } public RelayCommand CheckCommand { get; private set; } public RelayCommand DownloadCommand { get; private set; } public RelayCommand BrowseIniCommand { get; private set; } public RelayCommand OpenUserCatalogCommand { get; private set; } public RelayCommand OpenSiteCommand { get; private set; } public RelayCommand OpenPathCommand { get; private set; } public RelayCommand CopyPathCommand { get; private set; } public RelayCommand InfoCommand { get; private set; }
        public string IniPath { get { return _iniPath; } set { _iniPath = value; Changed("IniPath"); } }
        public string InstallDirectory { get { return _configuration == null ? "—" : _configuration.InstallDirectory; } }
        public string DownloadDirectory { get { return _paths.DownloadDirectory; } }
        public string StorageMode { get { return Text.Get(_paths.IsPortable ? "StoragePortable" : "StorageInstalled"); } }
        public string UserCatalogPath { get { return _paths.UserCatalogPath; } }
        public string ApplicationVersion { get { return ApplicationMetadata.Version; } }
        public string StatusText { get { return _statusText; } private set { _statusText = value; Changed("StatusText"); } }
        public string FilterName { get { return _filter; } set { _filter = value; _itemsView.View.Refresh(); Changed("FilterName"); } }

        public void Initialize() { Discover(); }
        private void Discover()
        {
            _configuration = _resolver.Resolve(IniPath); IniPath = _configuration == null ? "" : _configuration.IniPath; Items.Clear();
            if (_configuration == null) { StatusText = Text.Get("NoIni"); Changed("InstallDirectory"); return; }
            foreach (var plugin in _discovery.Discover(_configuration)) Items.Add(new PluginRowViewModel(plugin));
            Changed("InstallDirectory"); StatusText = String.Format(Text.Get("FoundCount"), Items.Count);
            if (_configuration.Warnings.Count > 0) StatusText += " · " + String.Join(" · ", _configuration.Warnings);
        }

        private async Task CheckAsync()
        {
            var target = CheckedOrAll(); if (target.Count == 0) return;
            foreach (var row in target) row.SetChecking();
            foreach (var row in target)
            {
                try { row.Apply(await _updates.CheckAsync(row.Plugin, CancellationToken.None)); }
                catch (Exception ex) { row.Apply(new UpdateCandidate { Plugin = row.Plugin, State = UpdateState.Error, Details = ex.Message }); }
            }
            ItemsView.Refresh(); StatusText = String.Format(Text.Get("CheckedCount"), target.Count);
        }

        private async Task DownloadAsync()
        {
            var target = Items.Where(x => x.IsChecked).ToList();
            if (target.Count == 0) { StatusText = Text.Get("SelectItems"); return; }
            var done = 0;
            foreach (var row in target)
            {
                if (row.Plugin.HasVersionConflict || (row.Candidate != null && row.Candidate.State == UpdateState.LocalVersionConflict))
                {
                    row.Apply(row.Candidate ?? new UpdateCandidate { Plugin = row.Plugin, State = UpdateState.LocalVersionConflict });
                    continue;
                }
                if (row.Candidate == null || row.Candidate.DownloadUrl == null) { row.Apply(new UpdateCandidate { Plugin = row.Plugin, State = UpdateState.Error, Details = Text.Get("NoDownload") }); continue; }
                try { var path = await _downloads.DownloadAsync(row.Candidate.DownloadUrl, _paths.DownloadDirectory, CancellationToken.None); row.Apply(new UpdateCandidate { Plugin = row.Plugin, State = UpdateState.UpdateAvailable, AvailableVersion = row.Candidate.AvailableVersion, SourceUrl = row.Candidate.SourceUrl, DownloadUrl = row.Candidate.DownloadUrl, Details = String.Format(Text.Get("Downloaded"), Path.GetFileName(path)) }); done++; }
                catch (Exception ex) { row.Apply(new UpdateCandidate { Plugin = row.Plugin, State = UpdateState.Error, Details = ex.Message }); }
            }
            StatusText = String.Format(Text.Get("DownloadedCount"), done);
        }

        private System.Collections.Generic.List<PluginRowViewModel> CheckedOrAll() { var checkedRows = Items.Where(x => x.IsChecked).ToList(); return checkedRows.Count > 0 ? checkedRows : Items.ToList(); }
        private void Filter(object sender, FilterEventArgs e)
        {
            var row = e.Item as PluginRowViewModel; if (row == null) { e.Accepted = false; return; }
            if (FilterName == "Updates") { e.Accepted = row.HasUpdate; return; }
            if (FilterName == "Unknown") { e.Accepted = row.Candidate == null || row.Candidate.State == UpdateState.PluginNotRecognized || row.Candidate.State == UpdateState.VersionComparisonUnknown; return; }
            e.Accepted = FilterName != "Errors" || row.HasError;
        }
        private void BrowseIni() { var dialog = new OpenFileDialog { Filter = "wincmd.ini|wincmd.ini;*.ini|Все файлы|*.*", FileName = "wincmd.ini" }; if (dialog.ShowDialog(System.Windows.Application.Current.MainWindow) == true) { IniPath = dialog.FileName; Discover(); } }
        private void OpenUserCatalog() { _catalog.EnsureUserCatalog(); UserCatalogEntries.Clear(); foreach (var entry in _catalog.LoadUserCatalog()) UserCatalogEntries.Add(entry); Process.Start(new ProcessStartInfo("notepad.exe", "\"" + _paths.UserCatalogPath + "\"") { UseShellExecute = true }); }
        private static void OpenSite(PluginRowViewModel row) { Process.Start(new ProcessStartInfo(row.Candidate.SourceUrl.AbsoluteUri) { UseShellExecute = true }); }
        private static void OpenPath(PluginRowViewModel row) { Process.Start(new ProcessStartInfo("explorer.exe", "/select,\"" + row.Path + "\"") { UseShellExecute = true }); }
        private static void ShowInfo(PluginRowViewModel row) { System.Windows.MessageBox.Show(System.Windows.Application.Current.MainWindow, row.Information + Environment.NewLine + row.Status, row.Name, System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information); }
        public void Dispose() { }
        public event PropertyChangedEventHandler PropertyChanged;
        private void Changed(string propertyName) { var handler = PropertyChanged; if (handler != null) handler(this, new PropertyChangedEventArgs(propertyName)); }
    }
}
