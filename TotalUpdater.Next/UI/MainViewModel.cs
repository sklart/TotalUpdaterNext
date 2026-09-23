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
        private readonly string _backupRoot;
        private readonly CollectionViewSource _itemsView;
        private TotalCommanderConfiguration _configuration;
        private CancellationTokenSource _checkCancellation;
        private int _checkGeneration;
        private string _iniPath = ""; private string _statusText = ""; private string _filter = "All";

        public MainViewModel(TotalCommanderConfigurationResolver resolver, PluginDiscoveryService discovery, UpdateService updates, CatalogService catalog, DownloadService downloads, UserDataPaths paths)
        {
            _resolver = resolver; _discovery = discovery; _updates = updates; _catalog = catalog; _downloads = downloads; _paths = paths;
            _backupRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TotalUpdaterNext", "backups");
            Items = new ObservableCollection<PluginRowViewModel>(); _itemsView = new CollectionViewSource { Source = Items }; _itemsView.GroupDescriptions.Add(new PropertyGroupDescription("Group")); _itemsView.Filter += Filter;
            DiscoverCommand = new RelayCommand(x => Discover()); CheckCommand = new RelayCommand(async x => await CheckAsync()); DownloadCommand = new RelayCommand(async x => await DownloadAsync());
            InstallCommand = new RelayCommand(async x => await InstallAsync()); RollbackCommand = new RelayCommand(x => RollbackLast());
            BrowseIniCommand = new RelayCommand(x => BrowseIni()); OpenUserCatalogCommand = new RelayCommand(x => OpenUserCatalog()); OpenSiteCommand = new RelayCommand(x => OpenSite(x as PluginRowViewModel), x => x is PluginRowViewModel row && row.Candidate != null && row.Candidate.SourceUrl != null);
            OpenPathCommand = new RelayCommand(x => OpenPath(x as PluginRowViewModel), x => x is PluginRowViewModel row && File.Exists(row.Path)); CopyPathCommand = new RelayCommand(x => System.Windows.Clipboard.SetText((x as PluginRowViewModel).Path), x => x is PluginRowViewModel row && !String.IsNullOrWhiteSpace(row.Path)); InfoCommand = new RelayCommand(x => ShowInfo(x as PluginRowViewModel), x => x is PluginRowViewModel);
            UserCatalogEntries = new ObservableCollection<PluginCatalogEntry>(_catalog.LoadUserCatalog());
        }

        public ObservableCollection<PluginRowViewModel> Items { get; private set; }
        public ObservableCollection<PluginCatalogEntry> UserCatalogEntries { get; private set; }
        public ICollectionView ItemsView { get { return _itemsView.View; } }
        public RelayCommand DiscoverCommand { get; private set; } public RelayCommand CheckCommand { get; private set; } public RelayCommand DownloadCommand { get; private set; } public RelayCommand InstallCommand { get; private set; } public RelayCommand RollbackCommand { get; private set; } public RelayCommand BrowseIniCommand { get; private set; } public RelayCommand OpenUserCatalogCommand { get; private set; } public RelayCommand OpenSiteCommand { get; private set; } public RelayCommand OpenPathCommand { get; private set; } public RelayCommand CopyPathCommand { get; private set; } public RelayCommand InfoCommand { get; private set; }
        public string IniPath { get { return _iniPath; } set { _iniPath = value; Changed("IniPath"); } }
        public string InstallDirectory { get { return _configuration == null ? "—" : _configuration.InstallDirectory; } }
        public string DownloadDirectory { get { return _paths.DownloadDirectory; } }
        public string StorageMode { get { return Text.Get(_paths.IsPortable ? "StoragePortable" : "StorageInstalled"); } }
        public string UserCatalogPath { get { return _paths.UserCatalogPath; } }
        public string ApplicationVersion { get { return ApplicationMetadata.Version; } }
        public string StatusText { get { return _statusText; } private set { _statusText = value; Changed("StatusText"); } }
        public string FilterName { get { return _filter; } set { _filter = value; _itemsView.View.Refresh(); Changed("FilterName"); } }

        public void Initialize() { Discover(); RecoverPending(); }
        private void Discover()
        {
            _configuration = _resolver.Resolve(IniPath); IniPath = _configuration == null ? "" : _configuration.IniPath; Items.Clear();
            if (_configuration == null) { StatusText = Text.Get("NoIni"); Changed("InstallDirectory"); return; }
            foreach (var plugin in _discovery.Discover(_configuration)) Items.Add(new PluginRowViewModel(plugin));
            Changed("InstallDirectory"); StatusText = String.Format(Text.Get("FoundCount"), Items.Count);
            var catalogDiagnostics = _catalog.Diagnostics;
            if (catalogDiagnostics.Count > 0) StatusText += " · Каталог: " + String.Join("; ", catalogDiagnostics.Select(x => x.Message));
            if (_configuration.Warnings.Count > 0) StatusText += " · " + String.Join(" · ", _configuration.Warnings);
        }

        private async Task CheckAsync()
        {
            var target = CheckedOrAll(); if (target.Count == 0) return;
            var previous = _checkCancellation; if (previous != null) previous.Cancel();
            var cancellation = new CancellationTokenSource(); _checkCancellation = cancellation; var generation = ++_checkGeneration;
            foreach (var row in target) row.SetChecking();
            var rows = target.ToDictionary(x => x.Plugin); var runner = new UpdateCheckRunner(_updates);
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
                StatusText = String.Format(Text.Get("CheckedCount"), target.Count) + " · Обновлений: " + target.Count(x => x.HasUpdate) + " · Ошибок источников: " + target.Count(x => x.HasError) + " · Не распознано: " + target.Count(x => x.Candidate != null && x.Candidate.State == UpdateState.PluginNotRecognized);
            }
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
                try { var path = await _downloads.DownloadAsync(row.Candidate.DownloadUrl, _paths.DownloadDirectory, CancellationToken.None); DownloadedPackagePolicy.Record(row.Candidate, path); row.Candidate.Details = String.Format(Text.Get("Downloaded"), Path.GetFileName(path)); row.Apply(row.Candidate); done++; }
                catch (Exception ex) { row.Candidate.Details = ex.Message; StatusText = "Скачивание не выполнено: " + ex.Message; }
            }
            StatusText = String.Format(Text.Get("DownloadedCount"), done);
        }

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
                if (packagePath == null) { packagePath = await _downloads.DownloadAsync(row.Candidate.DownloadUrl, _paths.DownloadDirectory, CancellationToken.None); DownloadedPackagePolicy.Record(row.Candidate, packagePath); }
                if (!String.Equals(Path.GetExtension(packagePath), ".zip", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("Этот формат доступен только для скачивания; запуск EXE/MSI/RAR/SFX запрещён.");
                inspected = new PackageInspector().Inspect(packagePath);
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
                var retry = manifest.State == InstallStateMachine.RecoveryConflict || manifest.State == InstallStateMachine.RollbackVerificationFailed || manifest.State == InstallStateMachine.InstallConflict;
                var prompt = (retry ? "Повторить откат " : "Откатить ") + manifest.PluginId + " " + manifest.NewVersion + " → " + manifest.OldVersion + "?" + Environment.NewLine + manifest.TargetDirectory;
                if (System.Windows.MessageBox.Show(System.Windows.Application.Current.MainWindow, prompt, "Откатить последнее обновление", System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Warning) != System.Windows.MessageBoxResult.Yes) return;
                if (System.Diagnostics.Process.GetProcessesByName("TOTALCMD").Any() || System.Diagnostics.Process.GetProcessesByName("TOTALCMD64").Any())
                    throw new InvalidOperationException("Закройте Total Commander перед откатом.");
                new RollbackService().Rollback(manifest, _backupRoot, () => Rediscover(manifest.PluginId, manifest.PluginType, manifest.PrimaryPath)); Discover(); StatusText = "Откат завершён: " + manifest.PluginId;
            }
            catch (Exception ex) { StatusText = "Откат не выполнен: " + ex.Message; }
        }

        private InstalledPlugin Rediscover(string id, string type, string primaryPath)
        {
            return _configuration == null ? null : _discovery.Discover(_configuration).FirstOrDefault(x =>
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
                var reason = manifest.State == InstallStateMachine.RecoveryConflict ? "Конфликт восстановления" :
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
            if (FilterName == "Updates") { e.Accepted = row.HasUpdate; return; }
            if (FilterName == "Unknown") { e.Accepted = row.Candidate == null || row.Candidate.State == UpdateState.PluginNotRecognized || row.Candidate.State == UpdateState.VersionComparisonUnknown; return; }
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
