using System;
using System.Windows;
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
            var paths = new UserDataPaths(AppDomain.CurrentDomain.BaseDirectory); _http = new HttpService(ApplicationMetadata.Version);
            var catalog = new CatalogService(paths.UserCatalogPath); var resolver = new TotalCommanderConfigurationResolver();
            var discovery = new PluginDiscoveryService(resolver, new LocalVersionResolver(), catalog);
            var providers = new IUpdateSourceProvider[] { new TotalCmdNetSourceProvider(_http), new GhislerSourceProvider(_http), new GenericHtmlSourceProvider(_http) };
            DataContext = new MainViewModel(resolver, discovery, new UpdateService(catalog, providers), catalog, new DownloadService(_http), paths);
            Loaded += (s, e) => ((MainViewModel)DataContext).Initialize(); Closed += (s, e) => _http.Dispose();
        }
    }
}
