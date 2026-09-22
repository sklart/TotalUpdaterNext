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

namespace TotalUpdater.Next
{
    public partial class MainWindow : Window
    {
        private readonly HttpService _http;
        public MainWindow()
        {
            InitializeComponent();
            LoadApplicationIcon();
            var paths = new UserDataPaths(AppDomain.CurrentDomain.BaseDirectory); _http = new HttpService(ApplicationMetadata.Version);
            var catalog = new CatalogService(paths.UserCatalogPath); var resolver = new TotalCommanderConfigurationResolver();
            var discovery = new PluginDiscoveryService(resolver, new LocalVersionResolver(), catalog);
            var providers = new IUpdateSourceProvider[] { new TotalCmdNetSourceProvider(_http), new GhislerSourceProvider(_http), new GenericHtmlSourceProvider(_http) };
            DataContext = new MainViewModel(resolver, discovery, new UpdateService(catalog, providers), catalog, new DownloadService(_http), paths);
            Loaded += (s, e) => ((MainViewModel)DataContext).Initialize(); Closed += (s, e) => _http.Dispose();
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
