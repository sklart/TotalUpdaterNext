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
using TotalUpdater.Next.Core.Installation;
using TotalUpdater.Next.Infrastructure;
using TotalUpdater.Next.Infrastructure.Installation;
using TotalUpdater.Next.Resources;
using TotalUpdater.Next.Sources;
using TotalUpdater.Next.TotalCommander;
using TotalUpdater.Next.Settings;

namespace TotalUpdater.Next.UI
{
    public sealed class MainViewModel : INotifyPropertyChanged, IDisposable
    {
        private readonly TotalCommanderConfigurationResolver _resolver;
        private readonly PluginDiscoveryService _discovery;
        private readonly UpdateService _updates;
        private readonly CatalogService _catalog;
        private readonly CatalogInstallService _catalogInstall;
        private readonly DownloadService _downloads;
        private readonly UserDataPaths _paths;
        private readonly ISourceDiagnostics _sourceDiagnostics;
        private readonly SettingsService _settingsService;
        private readonly PersistentSourceCache _persistentCache;
        private readonly Action<AppSettings> _networkReconfigure;
        private readonly string _backupRoot;
        private readonly CollectionViewSource _itemsView;
        private TotalCommanderConfiguration _configuration;
        private CancellationTokenSource _checkCancellation;
        private int _checkGeneration;
        private string _iniPath = ""; private string _statusText = ""; private string _filter = "All"; private SettingsExclusion _selectedExclusion;

        public MainViewModel(TotalCommanderConfigurationResolver resolver, PluginDiscoveryService discovery, UpdateService updates, CatalogService catalog, DownloadService downloads, UserDataPaths paths, CatalogInstallService catalogInstall = null, ISourceDiagnostics sourceDiagnostics = null, SettingsService settingsService = null, SettingsValidationResult loadedSettings = null, Action<AppSettings> networkReconfigure = null)
        {
            _resolver = resolver; _discovery = discovery; _updates = updates; _catalog = catalog; _downloads = downloads; _paths = paths; _catalogInstall = catalogInstall; _sourceDiagnostics = sourceDiagnostics; _settingsService = settingsService; _persistentCache = new PersistentSourceCache(); _networkReconfigure = networkReconfigure; Settings = loadedSettings == null ? new AppSettings() : loadedSettings.Settings;
            _backupRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TotalUpdaterNext", "backups");
            Items = new ObservableCollection<PluginRowViewModel>(); ExclusionItems = new ObservableCollection<SettingsExclusion>(); RefreshExclusions(); _itemsView = new CollectionViewSource { Source = Items }; _itemsView.GroupDescriptions.Add(new PropertyGroupDescription("Group")); _itemsView.Filter += Filter;
            DiscoverCommand = new RelayCommand(x => Discover()); CheckCommand = new RelayCommand(async x => await CheckAsync()); DownloadCommand = new RelayCommand(async x => await DownloadAsync());
            InstallCommand = new RelayCommand(async x => await InstallAsync()); RollbackCommand = new RelayCommand(x => RollbackLast());
            CheckCatalogCommand = new RelayCommand(async x => await CheckCatalogAsync()); InstallNewCommand = new RelayCommand(async x => await InstallNewAsync());
            CheckSourceAvailabilityCommand = new RelayCommand(async x => await CheckSourceAvailabilityAsync(), x => _sourceDiagnostics != null);
            ApplySettingsCommand = new RelayCommand(x => ApplySettings()); DefaultSettingsCommand = new RelayCommand(x => ResetSettings()); ClearCacheCommand = new RelayCommand(x => ClearCache());
            ExcludeCommand = new RelayCommand(x => Exclude(x as PluginRowViewModel), x => x is PluginRowViewModel); RestoreExcludedCommand = new RelayCommand(x => RestoreExcluded(x as PluginRowViewModel), x => x is PluginRowViewModel);
            AcceptVersionCommand = new RelayCommand(x => AcceptVersion(x as PluginRowViewModel), x => x is PluginRowViewModel);
            ManageExclusionsCommand = new RelayCommand(x => ManageExclusions()); RestoreSelectedExclusionCommand = new RelayCommand(x => RestoreSelectedExclusion(SelectedExclusion), x => SelectedExclusion != null); RestoreAllExclusionsCommand = new RelayCommand(x => RestoreAllExclusions());
            BrowseIniCommand = new RelayCommand(x => BrowseIni()); OpenUserCatalogCommand = new RelayCommand(x => OpenUserCatalog()); OpenSiteCommand = new RelayCommand(x => OpenSite(x as PluginRowViewModel), x => x is PluginRowViewModel row && row.Candidate != null && row.Candidate.SourceUrl != null);
            OpenPathCommand = new RelayCommand(x => OpenPath(x as PluginRowViewModel), x => x is PluginRowViewModel row && File.Exists(row.Path)); CopyPathCommand = new RelayCommand(x => System.Windows.Clipboard.SetText((x as PluginRowViewModel).Path), x => x is PluginRowViewModel row && !String.IsNullOrWhiteSpace(row.Path)); InfoCommand = new RelayCommand(x => ShowInfo(x as PluginRowViewModel), x => x is PluginRowViewModel);
            UserCatalogEntries = new ObservableCollection<PluginCatalogEntry>(_catalog.LoadUserCatalog());
            CatalogItems = new ObservableCollection<CatalogRowViewModel>();
            SourceDiagnostics = new ObservableCollection<SourceDiagnosticResult>();
            if (loadedSettings != null && loadedSettings.Warnings.Count > 0) _statusText = String.Join(" ", loadedSettings.Warnings);
        }

        public ObservableCollection<PluginRowViewModel> Items { get; private set; }
        public ObservableCollection<SettingsExclusion> ExclusionItems { get; private set; }
        public ObservableCollection<PluginCatalogEntry> UserCatalogEntries { get; private set; }
        public ObservableCollection<CatalogRowViewModel> CatalogItems { get; private set; }
        public ObservableCollection<SourceDiagnosticResult> SourceDiagnostics { get; private set; }
        public AppSettings Settings { get; private set; }
        public ICollectionView ItemsView { get { return _itemsView.View; } }
        public RelayCommand DiscoverCommand { get; private set; } public RelayCommand CheckCommand { get; private set; } public RelayCommand DownloadCommand { get; private set; } public RelayCommand InstallCommand { get; private set; } public RelayCommand RollbackCommand { get; private set; } public RelayCommand BrowseIniCommand { get; private set; } public RelayCommand OpenUserCatalogCommand { get; private set; } public RelayCommand OpenSiteCommand { get; private set; } public RelayCommand OpenPathCommand { get; private set; } public RelayCommand CopyPathCommand { get; private set; } public RelayCommand InfoCommand { get; private set; }
        public RelayCommand CheckCatalogCommand { get; private set; } public RelayCommand InstallNewCommand { get; private set; } public RelayCommand CheckSourceAvailabilityCommand { get; private set; } public RelayCommand ApplySettingsCommand { get; private set; } public RelayCommand DefaultSettingsCommand { get; private set; } public RelayCommand ClearCacheCommand { get; private set; } public RelayCommand ExcludeCommand { get; private set; } public RelayCommand RestoreExcludedCommand { get; private set; } public RelayCommand AcceptVersionCommand { get; private set; } public RelayCommand ManageExclusionsCommand { get; private set; } public RelayCommand RestoreSelectedExclusionCommand { get; private set; } public RelayCommand RestoreAllExclusionsCommand { get; private set; }
        public string IniPath { get { return _iniPath; } set { _iniPath = value; Changed("IniPath"); } }
        public string InstallDirectory { get { return _configuration == null ? "—" : _configuration.InstallDirectory; } }
        public string DownloadDirectory { get { return String.IsNullOrWhiteSpace(Settings.DownloadDirectory) ? _paths.DownloadDirectory : Environment.ExpandEnvironmentVariables(Settings.DownloadDirectory).Replace("%COMMANDER_PATH%", _configuration == null ? "" : _configuration.InstallDirectory); } }
        public string CacheSize { get { return (_persistentCache.GetSizeBytes() / 1024L) + " KB"; } }
        public int ExclusionCount { get { return Settings.ExcludedCatalogIds.Count + Settings.ExcludedUnknownPaths.Count; } }
        public SettingsExclusion SelectedExclusion { get { return _selectedExclusion; } set { _selectedExclusion = value; Changed("SelectedExclusion"); RestoreSelectedExclusionCommand.RaiseCanExecuteChanged(); } }
        public bool ScanUnregisteredDirectories { get { return Settings.DiscoveryMode == DiscoveryMode.RegisteredAndDirectories; } set { Settings.DiscoveryMode = value ? DiscoveryMode.RegisteredAndDirectories : DiscoveryMode.RegisteredOnly; Changed("ScanUnregisteredDirectories"); } }
        public string ProxyModeName { get { return Settings.ProxyMode.ToString(); } set { ProxyMode mode; if (Enum.TryParse(value, true, out mode)) Settings.ProxyMode = mode; Changed("ProxyModeName"); } }
        public string PostDownloadActionName { get { return Settings.PostDownloadAction.ToString(); } set { PostDownloadAction action; if (Enum.TryParse(value, true, out action)) Settings.PostDownloadAction = action; Changed("PostDownloadActionName"); } }
        public string StorageMode { get { return Text.Get(_paths.IsPortable ? "StoragePortable" : "StorageInstalled"); } }
        public string UserCatalogPath { get { return _paths.UserCatalogPath; } }
        public string ApplicationVersion { get { return ApplicationMetadata.Version; } }
        public string StatusText { get { return _statusText; } private set { _statusText = value; Changed("StatusText"); } }
        public string FilterName { get { return _filter; } set { _filter = value; Settings.LastFilter = value; _itemsView.View.Refresh(); Changed("FilterName"); } }

        public void Initialize() { _filter = Settings.LastFilter; Changed("FilterName"); Discover(); RecoverPending(); }

        private void ApplySettings()
        {
            if (_settingsService == null) { StatusText = "Сохранение настроек недоступно."; return; }
            var validated = new SettingsValidator().Validate(Settings); Settings = validated.Settings; _settingsService.Save(Settings); if (_networkReconfigure != null) _networkReconfigure(Settings); RefreshExclusions(); Changed("Settings"); Changed("DownloadDirectory"); Changed("CacheSize"); Changed("ExclusionCount"); Discover(); StatusText = validated.Warnings.Count == 0 ? "Настройки применены." : String.Join(" ", validated.Warnings);
        }
        private void ResetSettings()
        {
            var defaults = new AppSettings();
            foreach (var property in typeof(AppSettings).GetProperties().Where(x => x.CanRead && x.CanWrite)) property.SetValue(Settings, property.GetValue(defaults, null), null);
            RefreshExclusions(); Changed("Settings"); Changed("ScanUnregisteredDirectories"); Changed("DownloadDirectory"); Changed("CacheSize"); Changed("ExclusionCount"); StatusText = "Восстановлены значения по умолчанию. Нажмите «Применить».";
        }
        private void ClearCache() { _persistentCache.Clear(); Changed("CacheSize"); StatusText = "Кэш metadata очищен."; }
        private void AcceptVersion(PluginRowViewModel row) { if (row == null || row.Candidate == null || !row.Candidate.AvailableVersion.IsKnown) { StatusText = "Сначала проверьте доступную версию."; return; } new AcceptedVersionService(Settings).Accept(row.Plugin, row.Candidate.AvailableVersion); ApplySettings(); StatusText = "Установленная версия отмечена как актуальная: " + row.Name; }
        private void ManageExclusions() { RefreshExclusions(); StatusText = ExclusionItems.Count == 0 ? "Исключений нет." : "Выберите исключение в списке настроек или верните все."; }
        private void RefreshExclusions() { if (ExclusionItems == null) return; ExclusionItems.Clear(); foreach (var item in SettingsExclusions.List(Settings)) ExclusionItems.Add(item); }
        private void RestoreSelectedExclusion(SettingsExclusion exclusion) { if (exclusion == null) return; SettingsExclusions.Restore(Settings, exclusion); ApplySettings(); StatusText = "Возвращено в проверку: " + exclusion.Key; }
        private void RestoreAllExclusions() { if (ExclusionItems.Count == 0) return; SettingsExclusions.RestoreAll(Settings); ApplySettings(); StatusText = "Все исключения возвращены в проверку."; }
        private string ExclusionKey(PluginRowViewModel row) { return row.Plugin.CatalogMatchKind == CatalogMatchKind.NotFound ? row.Plugin.Type + "::" + Path.GetFullPath(row.Path).ToLowerInvariant() : row.Plugin.Identity.Id; }
        private void Exclude(PluginRowViewModel row) { if (row == null) return; var key = ExclusionKey(row); var list = row.Plugin.CatalogMatchKind == CatalogMatchKind.NotFound ? Settings.ExcludedUnknownPaths : Settings.ExcludedCatalogIds; if (!list.Contains(key, StringComparer.OrdinalIgnoreCase)) list.Add(key); ApplySettings(); StatusText = "Исключено из проверки: " + row.Name; }
        private void RestoreExcluded(PluginRowViewModel row) { if (row == null) return; var key = ExclusionKey(row); Settings.ExcludedCatalogIds = Settings.ExcludedCatalogIds.Where(x => !x.Equals(key, StringComparison.OrdinalIgnoreCase)).ToList(); Settings.ExcludedUnknownPaths = Settings.ExcludedUnknownPaths.Where(x => !x.Equals(key, StringComparison.OrdinalIgnoreCase)).ToList(); ApplySettings(); StatusText = "Возвращено в проверку: " + row.Name; }
        private bool IsExcluded(PluginRowViewModel row) { var key = ExclusionKey(row); return Settings.ExcludedCatalogIds.Contains(key, StringComparer.OrdinalIgnoreCase) || SettingsExclusions.ContainsUnknown(Settings, key); }
        private void Discover()
        {
            _configuration = _resolver.Resolve(IniPath); IniPath = _configuration == null ? "" : _configuration.IniPath; Items.Clear();
            if (_configuration == null) { RefreshCatalog(); StatusText = Text.Get("NoIni"); Changed("InstallDirectory"); return; }
            foreach (var plugin in _discovery.Discover(_configuration)) Items.Add(new PluginRowViewModel(plugin));
            RefreshCatalog();
            Changed("InstallDirectory"); StatusText = String.Format(Text.Get("FoundCount"), Items.Count);
            var catalogDiagnostics = _catalog.Diagnostics;
            if (catalogDiagnostics.Count > 0) StatusText += " · Каталог: " + String.Join("; ", catalogDiagnostics.Select(x => x.Message));
            if (_configuration.Warnings.Count > 0) StatusText += " · " + String.Join(" · ", _configuration.Warnings);
        }

        private void RefreshCatalog()
        {
            CatalogItems.Clear();
            var installed = new System.Collections.Generic.HashSet<string>(Items.Where(x => x.Plugin.Identity != null).Select(x => x.Plugin.Identity.Id), StringComparer.OrdinalIgnoreCase);
            foreach (var entry in _catalog.Load()) CatalogItems.Add(new CatalogRowViewModel(entry, installed.Contains(entry.Id)));
        }

        private async Task CheckSourceAvailabilityAsync()
        {
            if (_sourceDiagnostics == null) { StatusText = "Диагностика источников недоступна."; return; }
            try
            {
                StatusText = "Проверка доступности источников…";
                var results = await _sourceDiagnostics.CheckAsync(CancellationToken.None);
                SourceDiagnostics.Clear(); foreach (var result in results) SourceDiagnostics.Add(result);
                var failed = results.Where(x => !x.IsAvailable).Select(x => x.Host).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                StatusText = failed.Count == 0 ? "Источники доступны: " + results.Count + " из " + results.Count : "Источники недоступны: " + String.Join(", ", failed);
            }
            catch (Exception ex) { StatusText = "Диагностика источников не выполнена: " + ex.Message; }
        }

        private async Task CheckCatalogAsync()
        {
            var selected = CatalogItems.Where(x => x.IsChecked).ToList();
            if (selected.Count != 1 || selected[0].Installed || _catalogInstall == null)
            { StatusText = "Выберите один неустановленный плагин каталога."; return; }
            if (selected[0].Entry.PluginType != PluginType.Wcx && selected[0].Entry.PluginType != PluginType.Wfx && selected[0].Entry.PluginType != PluginType.Wlx && selected[0].Entry.PluginType != PluginType.Wdx)
            { StatusText = "Новая установка разрешена только для WCX/WFX/WLX/WDX."; return; }
            if (selected[0].Entry.PluginType == PluginType.Wcx && !selected[0].Entry.HasVerifiedWcxRegistration)
            { StatusText = "Пакет проверен, но параметры регистрации WCX не подтверждены. Автоматическая установка недоступна."; return; }
            try
            {
                StatusText = "Проверка источников: " + selected[0].Name;
                var result = await _catalogInstall.CheckAsync(selected[0].Entry, CancellationToken.None);
                selected[0].Apply(result);
                StatusText = result.DownloadUrl == null ? result.Details : "Пакет доступен: " + result.Version.Raw;
            }
            catch (Exception ex) { StatusText = "Проверка каталога не выполнена: " + ex.Message; }
        }

        private async Task InstallNewAsync()
        {
            var selected = CatalogItems.Where(x => x.IsChecked).ToList();
            if (selected.Count != 1 || selected[0].Installed || selected[0].Candidate?.DownloadUrl == null || _configuration == null)
            { StatusText = "Выберите один проверенный неустановленный плагин с доступным ZIP."; return; }
            if (selected[0].Entry.PluginType != PluginType.Wcx && selected[0].Entry.PluginType != PluginType.Wfx && selected[0].Entry.PluginType != PluginType.Wlx && selected[0].Entry.PluginType != PluginType.Wdx)
            { StatusText = "Новая установка разрешена только для WCX/WFX/WLX/WDX."; return; }
            if (selected[0].Entry.PluginType == PluginType.Wcx && !selected[0].Entry.HasVerifiedWcxRegistration)
            { StatusText = "Пакет проверен, но параметры регистрации WCX не подтверждены. Автоматическая установка недоступна."; return; }
            var row = selected[0]; PackageInspection inspected = null;
            try
            {
                var packagePath = await _downloads.DownloadAsync(row.Candidate.DownloadUrl, DownloadDirectory, CancellationToken.None);
                if (!String.Equals(Path.GetExtension(packagePath), ".zip", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("Автоустановка разрешена только для ZIP.");
                inspected = new PackageInspector().Inspect(packagePath);
                var plan = new NewPluginInstallPlanBuilder(_resolver).Build(row.Entry, row.Candidate, inspected, _configuration, Items.Select(x => x.Plugin), _backupRoot);
                NewPluginTransactionalInstaller.Preflight(plan);
                var changes = String.Join(Environment.NewLine, plan.ConfigurationFiles.SelectMany(x => x.Changes.Select(change => x.Path + " [" + change.Section + "] " + change.Key + "=" + change.Value)));
                var wcxEvidence = row.Entry.PluginType == PluginType.Wcx ? Environment.NewLine + "Capability flags: " + row.Entry.WcxRegistration.PackerCaps + Environment.NewLine + "Источник evidence: " + row.Entry.WcxRegistration.Source + Environment.NewLine + "Verified: " + row.Entry.WcxRegistration.VerifiedUtc.ToUniversalTime().ToString("u") + Environment.NewLine + "Package SHA-256: " + row.Entry.WcxRegistration.PackageSha256 : "";
                var prompt = row.Name + " · " + plan.Version + " · " + plan.PluginType + Environment.NewLine +
                    "Каталог: " + plan.TargetDirectory + Environment.NewLine +
                    "Файлы: " + String.Join(", ", plan.Files.Select(x => x.Source.RelativePath)) + Environment.NewLine +
                    "Регистрация:" + Environment.NewLine + changes + Environment.NewLine +
                    "Backup: " + plan.BackupDirectory + wcxEvidence + Environment.NewLine + "Установить новый плагин?";
                if (System.Windows.MessageBox.Show(System.Windows.Application.Current.MainWindow, prompt, "Подтверждение новой установки", System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Warning) != System.Windows.MessageBoxResult.Yes)
                { StatusText = "Установка отменена; ZIP сохранён: " + packagePath; return; }
                var manifest = new NewPluginTransactionalInstaller().Install(plan, () => Rediscover(plan.PluginId, plan.PluginType.ToString(), plan.PrimaryPath));
                if (Settings.DeleteZipAfterInstall && File.Exists(packagePath)) File.Delete(packagePath);
                Discover(); StatusText = "Установлен новый плагин: " + row.Name + ". Backup: " + manifest.BackupDirectory;
            }
            catch (Exception ex) { StatusText = "Новая установка не выполнена: " + ex.Message; }
            finally { if (inspected != null && Directory.Exists(inspected.StagingDirectory)) { try { Directory.Delete(inspected.StagingDirectory, true); } catch { } } }
        }

        private async Task CheckAsync()
        {
            var target = CheckedOrAll().Where(x => (x.Plugin.Type != PluginType.TotalCommander || Settings.CheckTotalCommander) && (x.Plugin.Type == PluginType.TotalCommander || Settings.CheckPlugins)).ToList(); if (target.Count == 0) { StatusText = "Для выбранных настроек нет элементов проверки."; return; }
            var previous = _checkCancellation; if (previous != null) previous.Cancel();
            var cancellation = new CancellationTokenSource(); _checkCancellation = cancellation; var generation = ++_checkGeneration;
            foreach (var row in target) row.SetChecking();
            var rows = target.ToDictionary(x => x.Plugin); var runner = new UpdateCheckRunner(_updates, Settings);
            await runner.RunAsync(target.Select(x => x.Plugin), (plugin, candidate) => System.Windows.Application.Current.Dispatcher.BeginInvoke(new Action(() =>
            {
                PluginRowViewModel row; if (generation == _checkGeneration && !cancellation.IsCancellationRequested && rows.TryGetValue(plugin, out row)) row.Apply(candidate);
            })), (completed, total) => System.Windows.Application.Current.Dispatcher.BeginInvoke(new Action(() =>
            {
                if (generation == _checkGeneration && !cancellation.IsCancellationRequested) StatusText = "Проверено " + completed + " из " + total;
            })), cancellation.Token);
            if (generation == _checkGeneration && !cancellation.IsCancellationRequested)
            {
                ItemsView.Refresh();
                StatusText = BuildCheckSummary(target);
            }
        }

        public static string BuildCheckSummary(System.Collections.Generic.IEnumerable<PluginRowViewModel> rows)
        {
            var target = (rows ?? Enumerable.Empty<PluginRowViewModel>()).ToList();
            var unavailable = target.SelectMany(x => x.Candidate == null ? Enumerable.Empty<RemoteVersionObservation>() : x.Candidate.Observations)
                .Where(x => x.Status == SourceQueryStatus.Unavailable).Select(x => SourceStatusText.FriendlyName(x.Source, x.ProviderName)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            return String.Format(Text.Get("CheckedCount"), target.Count) + " · Обновлений: " + target.Count(x => x.HasUpdate) +
                " · Не распознано: " + target.Count(x => x.Candidate != null && x.Candidate.State == UpdateState.PluginNotRecognized) +
                " · Неоднозначно: " + target.Count(x => x.Candidate != null && x.Candidate.State == UpdateState.CatalogAmbiguous) +
                (unavailable.Count == 0 ? "" : " · Источники недоступны: " + String.Join(", ", unavailable));
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
                if (!row.CanDownload) { if (row.Candidate != null) row.Apply(row.Candidate); else row.Apply(new UpdateCandidate { Plugin = row.Plugin, State = UpdateState.NotChecked, Details = Text.Get("NoDownload") }); continue; }
                try { var path = await _downloads.DownloadAsync(row.Candidate.DownloadUrl, DownloadDirectory, CancellationToken.None);
                    var entry = _catalog.FindById(row.Plugin.Identity.Id);
                    if (entry != null && String.Equals(Path.GetExtension(path), ".zip", StringComparison.OrdinalIgnoreCase))
                    {
                        if (!PackageIdentityVerifier.ContainsCatalogAlias(path, entry))
                        { row.Candidate.PackageAvailability = PackageAvailability.Ambiguous; row.Candidate.DownloadUrl = null;
                            row.Candidate.Details = "Пакет не содержит бинарник текущего плагина; установка запрещена."; row.Apply(row.Candidate); continue; }
                        row.Candidate.PackageAvailability = PackageAvailability.Verified;
                    }
                    DownloadedPackagePolicy.Record(row.Candidate, path); row.Candidate.Details = String.Format(Text.Get("Downloaded"), Path.GetFileName(path)); row.Apply(row.Candidate); done++;
                    if (ShouldOfferInstall(Settings, target.Count, path) &&
                        System.Windows.MessageBox.Show(System.Windows.Application.Current.MainWindow, "ZIP загружен: " + Path.GetFileName(path) + Environment.NewLine + "Установить сейчас?", "Загрузка завершена", System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Question) == System.Windows.MessageBoxResult.Yes)
                        await InstallAsync(); }
                catch (Exception ex) { row.Candidate.Details = ex.Message; StatusText = "Скачивание не выполнено: " + ex.Message; }
            }
            StatusText = String.Format(Text.Get("DownloadedCount"), done);
        }
        public static bool ShouldOfferInstall(AppSettings settings, int selectedCount, string packagePath) { return settings != null && settings.PostDownloadAction == PostDownloadAction.OfferInstall && selectedCount == 1 && String.Equals(Path.GetExtension(packagePath), ".zip", StringComparison.OrdinalIgnoreCase); }

        private async Task InstallAsync()
        {
            var selected = Items.Where(x => x.IsChecked).ToList();
            if (selected.Count != 1) { StatusText = "Для установки отметьте ровно один существующий плагин."; return; }
            var row = selected[0];
            if (!row.CanDownload || row.Plugin.Type == PluginType.TotalCommander || row.Candidate.AuthorityConflict)
            { StatusText = "Автоустановка для этого элемента недоступна."; return; }
            string packagePath = null; PackageInspection inspected = null;
            try
            {
                packagePath = DownloadedPackagePolicy.Resolve(row.Candidate);
                if (packagePath == null) { packagePath = await _downloads.DownloadAsync(row.Candidate.DownloadUrl, DownloadDirectory, CancellationToken.None); DownloadedPackagePolicy.Record(row.Candidate, packagePath); }
                if (!String.Equals(Path.GetExtension(packagePath), ".zip", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("Этот формат доступен только для скачивания; запуск EXE/MSI/RAR/SFX запрещён.");
                inspected = new PackageInspector().Inspect(packagePath);
                var catalogEntry = _catalog.FindById(row.Plugin.Identity.Id);
                if (catalogEntry != null && !PackageIdentityVerifier.ContainsCatalogAlias(packagePath, catalogEntry))
                    throw new InvalidDataException("PackageIdentityMismatch: ZIP не содержит alias установленного плагина; автоматическая установка запрещена.");
                var plan = new InstallPlanBuilder().Build(row.Plugin, inspected, row.Candidate.AvailableVersion, row.Candidate.DownloadUrl, _backupRoot, row.Candidate);
                TransactionalInstaller.Preflight(plan);
                var replacements = plan.Files.Where(x => x.ReplacesExisting).Select(x => x.Source.RelativePath);
                var additions = plan.Files.Where(x => !x.ReplacesExisting).Select(x => x.Source.RelativePath);
                var message = row.Name + " · " + plan.OldVersion + " → " + plan.NewVersion + Environment.NewLine +
                    "Каталог: " + plan.TargetDirectory + Environment.NewLine +
                    "Замена: " + String.Join(", ", replacements) + Environment.NewLine +
                    "Добавление: " + String.Join(", ", additions) + Environment.NewLine +
                    "Backup: " + plan.BackupDirectory + Environment.NewLine +
                    (plan.PackageUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ? "Внимание: загрузка по HTTP без защиты канала." + Environment.NewLine : "") +
                    "SHA-256 проверяет целостность загрузки, но без подписи издателя не удостоверяет происхождение." + Environment.NewLine +
                    "Продолжить установку?";
                if (System.Windows.MessageBox.Show(System.Windows.Application.Current.MainWindow, message, "Подтверждение установки", System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Warning) != System.Windows.MessageBoxResult.Yes)
                { StatusText = "Установка отменена. ZIP сохранён: " + packagePath; return; }
                var manifest = new TransactionalInstaller().Install(plan, () => Rediscover(row.Plugin.Identity.Id, row.Plugin.Type.ToString(), row.Plugin.PrimaryPath));
                if (Settings.DeleteZipAfterInstall && File.Exists(packagePath)) File.Delete(packagePath);
                Discover(); StatusText = "Установлено: " + row.Name + ". Backup: " + manifest.BackupDirectory;
            }
            catch (Exception ex) { StatusText = "Установка не выполнена: " + ex.Message; }
            finally { if (inspected != null && Directory.Exists(inspected.StagingDirectory)) { try { Directory.Delete(inspected.StagingDirectory, true); } catch { } } }
        }

        private void RollbackLast()
        {
            try
            {
                var selected = Items.Where(x => x.IsChecked).ToList();
                if (selected.Count != 1) { StatusText = "Для отката отметьте ровно один плагин."; return; }
                var manifest = BackupService.FindLatest(_backupRoot, selected[0].Plugin.Identity.Id, selected[0].Plugin.PrimaryPath);
                if (manifest == null) { StatusText = "Нет обновления выбранного плагина для отката."; return; }
                var retry = manifest.State == InstallStateMachine.RecoveryConflict || manifest.State == InstallStateMachine.ConfigRecoveryConflict ||
                    manifest.State == InstallStateMachine.ConfigConflict || manifest.State == InstallStateMachine.RollbackVerificationFailed || manifest.State == InstallStateMachine.InstallConflict;
                var prompt = (retry ? "Повторить откат " : "Откатить ") + manifest.PluginId + " " + manifest.NewVersion + " → " + manifest.OldVersion + "?" + Environment.NewLine + manifest.TargetDirectory;
                if (System.Windows.MessageBox.Show(System.Windows.Application.Current.MainWindow, prompt, "Откатить последнее обновление", System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Warning) != System.Windows.MessageBoxResult.Yes) return;
                if (System.Diagnostics.Process.GetProcessesByName("TOTALCMD").Any() || System.Diagnostics.Process.GetProcessesByName("TOTALCMD64").Any())
                    throw new InvalidOperationException("Закройте Total Commander перед откатом.");
                if (manifest.ManifestVersion == 4) new NewPluginRollbackService().Rollback(manifest, _backupRoot);
                else new RollbackService().Rollback(manifest, _backupRoot, () => Rediscover(manifest.PluginId, manifest.PluginType, manifest.PrimaryPath));
                Discover(); StatusText = "Откат завершён: " + manifest.PluginId;
            }
            catch (Exception ex) { StatusText = "Откат не выполнен: " + ex.Message; }
        }

        private InstalledPlugin Rediscover(string id, string type, string primaryPath)
        {
            var fresh = _configuration == null ? null : _resolver.Resolve(_configuration.IniPath);
            return fresh == null ? null : _discovery.Discover(fresh).FirstOrDefault(x =>
                String.Equals(x.Identity.Id, id, StringComparison.OrdinalIgnoreCase) &&
                String.Equals(x.Type.ToString(), type, StringComparison.OrdinalIgnoreCase) &&
                String.Equals(x.PrimaryPath, primaryPath, StringComparison.OrdinalIgnoreCase));
        }

        private void RecoverPending()
        {
            var recovery = new RecoveryService(_backupRoot);
            foreach (var manifest in recovery.FindPending())
            {
                if (_configuration == null)
                {
                    StatusText = "Найден незавершённый backup. Укажите wincmd.ini для безопасного восстановления.";
                    System.Windows.MessageBox.Show(System.Windows.Application.Current.MainWindow, StatusText, "Восстановление после сбоя", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
                    return;
                }
                var reason = manifest.State == InstallStateMachine.RecoveryConflict ? "Файл плагина изменён после установки" :
                    manifest.State == InstallStateMachine.ConfigRecoveryConflict ? "INI изменён после установки" :
                    manifest.State == InstallStateMachine.ConfigConflict ? "INI изменён во время установки" :
                    manifest.State == InstallStateMachine.RollbackVerificationFailed ? "Проверка отката не завершена" :
                    manifest.State == InstallStateMachine.InstallConflict ? "Конфликт установки" : "Незавершённая установка";
                var prompt = reason + ": " + manifest.PluginId + " (" + manifest.State + ")." + Environment.NewLine +
                    manifest.TargetDirectory + Environment.NewLine + "Повторить откат из backup?";
                if (System.Windows.MessageBox.Show(System.Windows.Application.Current.MainWindow, prompt, "Восстановление после сбоя", System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Warning) != System.Windows.MessageBoxResult.Yes) continue;
                try
                {
                    if (Process.GetProcessesByName("TOTALCMD").Any() || Process.GetProcessesByName("TOTALCMD64").Any())
                        throw new InvalidOperationException("Закройте Total Commander перед восстановлением.");
                    recovery.Recover(manifest, () => Rediscover(manifest.PluginId, manifest.PluginType, manifest.PrimaryPath));
                    Discover(); StatusText = "Исходные файлы восстановлены: " + manifest.PluginId;
                }
                catch (Exception ex) { StatusText = "Восстановление остановлено: " + ex.Message; }
            }
        }

        private System.Collections.Generic.List<PluginRowViewModel> CheckedOrAll() { var checkedRows = Items.Where(x => x.IsChecked).ToList(); return checkedRows.Count > 0 ? checkedRows : Items.ToList(); }
        private void Filter(object sender, FilterEventArgs e)
        {
            var row = e.Item as PluginRowViewModel; if (row == null) { e.Accepted = false; return; }
            if (IsExcluded(row)) { e.Accepted = false; return; }
            if (Settings.HideUnknownVersion && !row.Plugin.LocalVersion.ParsedValue.IsKnown) { e.Accepted = false; return; }
            if (FilterName == "Updates") { e.Accepted = row.HasUpdate; return; }
            if (FilterName == "Unknown") { e.Accepted = row.Candidate == null || row.Candidate.State == UpdateState.PluginNotRecognized || row.Candidate.State == UpdateState.CatalogAmbiguous || row.Candidate.State == UpdateState.SourceOutdated || row.Candidate.State == UpdateState.LocalAheadUnknown || row.Candidate.State == UpdateState.VersionComparisonUnknown; return; }
            e.Accepted = FilterName != "Errors" || row.HasError;
        }
        private void BrowseIni() { var dialog = new OpenFileDialog { Filter = "wincmd.ini|wincmd.ini;*.ini|Все файлы|*.*", FileName = "wincmd.ini" }; if (dialog.ShowDialog(System.Windows.Application.Current.MainWindow) == true) { IniPath = dialog.FileName; Discover(); RecoverPending(); } }
        private void OpenUserCatalog() { _catalog.EnsureUserCatalog(); UserCatalogEntries.Clear(); foreach (var entry in _catalog.LoadUserCatalog()) UserCatalogEntries.Add(entry); Process.Start(new ProcessStartInfo("notepad.exe", "\"" + _paths.UserCatalogPath + "\"") { UseShellExecute = true }); }
        private static void OpenSite(PluginRowViewModel row) { Process.Start(new ProcessStartInfo(row.Candidate.SourceUrl.AbsoluteUri) { UseShellExecute = true }); }
        private static void OpenPath(PluginRowViewModel row) { Process.Start(new ProcessStartInfo("explorer.exe", "/select,\"" + row.Path + "\"") { UseShellExecute = true }); }
        private static void ShowInfo(PluginRowViewModel row) { System.Windows.MessageBox.Show(System.Windows.Application.Current.MainWindow, row.Information + Environment.NewLine + row.Status, row.Name, System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information); }
        public void Dispose() { if (_checkCancellation != null) _checkCancellation.Cancel(); }
        public event PropertyChangedEventHandler PropertyChanged;
        private void Changed(string propertyName) { var handler = PropertyChanged; if (handler != null) handler(this, new PropertyChangedEventArgs(propertyName)); }
    }
}
