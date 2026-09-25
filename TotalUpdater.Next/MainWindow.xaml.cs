using System;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using TotalUpdater.Next.Catalog;
using TotalUpdater.Next.Core;
using TotalUpdater.Next.Infrastructure;
using TotalUpdater.Next.Sources;
using TotalUpdater.Next.TotalCommander;
using TotalUpdater.Next.UI;
using TotalUpdater.Next.Settings;

namespace TotalUpdater.Next
{
    public partial class MainWindow : Window
    {
        private readonly HttpService _http;
        private readonly SettingsService _settingsService;
        public MainWindow()
        {
            InitializeComponent();
            LoadApplicationIcon();
            var paths = new UserDataPaths(AppDomain.CurrentDomain.BaseDirectory); _settingsService = new SettingsService(paths.SettingsPath); var loadedSettings = _settingsService.Load(); _http = new HttpService(ApplicationMetadata.Version, loadedSettings.Settings);
            if (loadedSettings.Settings.WindowWidth >= MinWidth) Width = loadedSettings.Settings.WindowWidth;
            if (loadedSettings.Settings.WindowHeight >= MinHeight) Height = loadedSettings.Settings.WindowHeight;
            if (!Double.IsNaN(loadedSettings.Settings.WindowLeft) && !Double.IsNaN(loadedSettings.Settings.WindowTop)) { Left = loadedSettings.Settings.WindowLeft; Top = loadedSettings.Settings.WindowTop; WindowStartupLocation = WindowStartupLocation.Manual; }
            var catalog = new CatalogService(paths.UserCatalogPath); var resolver = new TotalCommanderConfigurationResolver();
            var discovery = new PluginDiscoveryService(resolver, new LocalVersionResolver(loadedSettings.Settings), catalog, null, loadedSettings.Settings);
            var providers = UpdateSourceProviderFactory.Create(_http, loadedSettings.Settings);
            DataContext = new MainViewModel(resolver, discovery, new UpdateService(catalog, providers, new RemoteCatalogLookup(_http), new AcceptedVersionService(loadedSettings.Settings)), catalog, new DownloadService(_http), paths, new TotalUpdater.Next.Core.Installation.CatalogInstallService(providers), new SourceDiagnostics(_http), _settingsService, loadedSettings, s => _http.Reconfigure(s));
            Loaded += (s, e) => ((MainViewModel)DataContext).Initialize(); Closing += (s, e) => SaveWindowState(); Closed += (s, e) => _http.Dispose();
        }

        private void SaveWindowState()
        {
            var viewModel = DataContext as MainViewModel; if (viewModel == null) return;
            viewModel.Settings.WindowLeft = Left; viewModel.Settings.WindowTop = Top; viewModel.Settings.WindowWidth = Width; viewModel.Settings.WindowHeight = Height;
            try { _settingsService.Save(viewModel.Settings); } catch { }
        }

        private void LoadApplicationIcon()
        {
            try
            {
                using (var applicationIcon = System.Drawing.Icon.ExtractAssociatedIcon(typeof(MainWindow).Assembly.Location))
                {
                    if (applicationIcon != null) Icon = Imaging.CreateBitmapSourceFromHIcon(applicationIcon.Handle, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                }
            }
            catch { }
        }
    }
}
