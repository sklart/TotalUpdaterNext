using System.Collections.Generic;
using TotalUpdater.Next.Settings;

namespace TotalUpdater.Next.Sources
{
    public static class UpdateSourceProviderFactory
    {
        public static IList<IUpdateSourceProvider> Create(HttpService http, AppSettings settings = null)
        {
            return new IUpdateSourceProvider[] { new TotalCmdNetSourceProvider(http), new TotalCmdNetIndexProvider(http), new GhislerSourceProvider(http), new GhislerPluginsSourceProvider(http), new GitHubReleaseSourceProvider(http, settings), new GenericHtmlSourceProvider(http) };
        }
    }
}
