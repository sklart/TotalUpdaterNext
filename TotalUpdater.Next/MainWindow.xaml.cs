using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.Win32;
using TotalUpdater.Next.Models;
using TotalUpdater.Next.Services;

namespace TotalUpdater.Next
{
    public partial class MainWindow : Window
    {
        private readonly ObservableCollection<PluginRecord> _plugins = new ObservableCollection<PluginRecord>();
        private string BuiltInRulesPath { get { return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Rules", "plugin-sources.json"); } }
        private string RulesPath { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "TotalUpdaterNext", "plugin-sources.user.json"); } }

        public MainWindow()
        {
            InitializeComponent();
            PluginGrid.ItemsSource = _plugins;
            IniPathBox.Text = FindIniPath() ?? "";
            if (!String.IsNullOrWhiteSpace(IniPathBox.Text)) LoadPlugins();
        }

        private void BrowseIni_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog { Filter = "Конфигурация Total Commander|wincmd.ini;*.ini|Все файлы|*.*", FileName = "wincmd.ini" };
            if (dialog.ShowDialog(this) == true) IniPathBox.Text = dialog.FileName;
        }

        private void FindPlugins_Click(object sender, RoutedEventArgs e) { LoadPlugins(); }

        private void LoadPlugins()
        {
            if (!File.Exists(IniPathBox.Text))
            {
                MessageBox.Show(this, "Выберите существующий файл wincmd.ini.", Title, MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            try
            {
                _plugins.Clear();
                AddTotalCommander(IniPathBox.Text);
                foreach (var item in IniPluginReader.Read(IniPathBox.Text)) _plugins.Add(item);
                SummaryText.Text = "Найдено: " + _plugins.Count + " плагинов";
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "Не удалось прочитать конфигурацию", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void CheckSources_Click(object sender, RoutedEventArgs e)
        {
            var rules = RuleStore.Load(BuiltInRulesPath, RulesPath);
            var linked = 0;
            foreach (var plugin in _plugins)
            {
                SourceRule rule;
                if (rules.TryGetValue(Path.GetFileName(plugin.FilePath), out rule))
                {
                    plugin.Name = rule.Name;
                    plugin.SourceUrl = rule.SourceUrl;
                    plugin.Status = "Проверяется…";
                    linked++;
                }
                else if (plugin.Status != "Файл не найден") plugin.Status = "Нет правила источника";
            }
            PluginGrid.Items.Refresh();
            SummaryText.Text = "Проверяются правила: " + linked + " из " + _plugins.Count;

            foreach (var plugin in _plugins.Where(p => !String.IsNullOrWhiteSpace(p.SourceUrl)).ToList())
            {
                SourceRule rule;
                if (!rules.TryGetValue(Path.GetFileName(plugin.FilePath), out rule)) continue;
                try
                {
                    var latest = await UpdateChecker.GetLatestVersionAsync(rule);
                    if (String.IsNullOrWhiteSpace(latest)) plugin.Status = "Версия не найдена на странице";
                    else
                    {
                        var latestVersion = latest!;
                        plugin.LatestVersion = latestVersion;
                        var comparison = UpdateChecker.CompareVersions(plugin.LocalVersion, latestVersion);
                        plugin.Status = comparison < 0 ? "Доступно обновление" : comparison > 0 ? "Установлена тестовая версия" : "Актуально";
                    }
                }
                catch (Exception ex)
                {
                    plugin.Status = "Ошибка источника: " + ex.Message;
                }
                PluginGrid.Items.Refresh();
            }
            SummaryText.Text = "Проверено: " + linked + " из " + _plugins.Count;
        }

        private void AddTotalCommander(string iniPath)
        {
            var directory = TotalCommanderLocator.GetInstallDirectory(iniPath);
            foreach (var fileName in new[] { "TOTALCMD64.EXE", "TOTALCMD.EXE" })
            {
                var path = Path.Combine(directory, fileName);
                if (!File.Exists(path)) continue;
                _plugins.Add(new PluginRecord
                {
                    Category = "Total Commander",
                    Name = fileName == "TOTALCMD64.EXE" ? "Total Commander (x64)" : "Total Commander",
                    FilePath = path,
                    LocalVersion = FileVersionInfo.GetVersionInfo(path).FileVersion ?? "не определена",
                    Status = "Не проверено"
                });
            }
        }

        private void OpenRules_Click(object sender, RoutedEventArgs e)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(RulesPath));
            if (!File.Exists(RulesPath))
                File.Copy(BuiltInRulesPath, RulesPath);
            Process.Start(new ProcessStartInfo("notepad.exe", "\"" + RulesPath + "\"") { UseShellExecute = true });
        }

        private void PluginGrid_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            var selected = PluginGrid.SelectedItem as PluginRecord;
            if (selected == null || String.IsNullOrWhiteSpace(selected.SourceUrl)) return;
            try { Process.Start(new ProcessStartInfo(selected.SourceUrl) { UseShellExecute = true }); }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, "Не удалось открыть источник", MessageBoxButton.OK, MessageBoxImage.Error); }
        }

        private async void DownloadSelected_Click(object sender, RoutedEventArgs e)
        {
            var selected = PluginGrid.SelectedItems.Cast<PluginRecord>().ToList();
            if (selected.Count == 0)
            {
                MessageBox.Show(this, "Выделите в таблице одну или несколько строк с доступными обновлениями.", Title, MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var rules = RuleStore.Load(BuiltInRulesPath, RulesPath);
            var destination = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads", "TotalUpdaterNext");
            var downloaded = 0;
            foreach (var plugin in selected)
            {
                SourceRule rule;
                if (!rules.TryGetValue(Path.GetFileName(plugin.FilePath), out rule) || String.IsNullOrWhiteSpace(rule.DownloadUrl))
                {
                    plugin.Status = "Нет адреса загрузки в правиле";
                    continue;
                }
                try
                {
                    plugin.Status = "Скачивается…";
                    PluginGrid.Items.Refresh();
                    var path = await DownloadService.DownloadAsync(rule.DownloadUrl, destination);
                    plugin.Status = "Скачано: " + Path.GetFileName(path);
                    downloaded++;
                }
                catch (Exception ex)
                {
                    plugin.Status = "Ошибка загрузки: " + ex.Message;
                }
                PluginGrid.Items.Refresh();
            }
            SummaryText.Text = "Скачано пакетов: " + downloaded + ". Папка: " + destination;
        }

        private static string FindIniPath()
        {
            foreach (var hive in new[] { RegistryHive.CurrentUser, RegistryHive.LocalMachine })
            foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
            {
                try
                {
                    using (var baseKey = RegistryKey.OpenBaseKey(hive, view))
                    using (var key = baseKey.OpenSubKey(@"Software\Ghisler\Total Commander"))
                    {
                        var configured = key == null ? null : key.GetValue("IniFileName") as string;
                        if (!String.IsNullOrWhiteSpace(configured) && File.Exists(configured)) return configured!;
                    }
                }
                catch { }
            }
            return null!;
        }
    }
}
