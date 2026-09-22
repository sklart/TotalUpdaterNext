using System;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using TotalUpdater.Next.Catalog;
using TotalUpdater.Next.Core.Versions;

namespace TotalUpdater.Next.Sources
{
    public sealed class RemoteRelease
    {
        public string VersionText { get; set; } = "";
        public VersionValue Version { get; set; } = VersionValue.Unknown;
        public Uri SourceUrl { get; set; }
        public Uri DownloadUrl { get; set; }
    }

    public interface IUpdateSourceProvider
    {
        string Name { get; }
        bool CanHandle(CatalogSource source);
        Task<RemoteRelease> GetLatestReleaseAsync(CatalogSource source, CancellationToken cancellationToken);
    }

    public class GenericHtmlSourceProvider : IUpdateSourceProvider
    {
        protected readonly HttpService Http;
        public GenericHtmlSourceProvider(HttpService http) { Http = http; }
        public virtual string Name { get { return "HTML"; } }
        public virtual bool CanHandle(CatalogSource source) { return !String.IsNullOrWhiteSpace(source.Url) && !String.IsNullOrWhiteSpace(source.VersionPattern); }
        public virtual async Task<RemoteRelease> GetLatestReleaseAsync(CatalogSource source, CancellationToken cancellationToken)
        {
            var html = await Http.GetStringAsync(source.Url, cancellationToken).ConfigureAwait(false);
            var match = Regex.Match(html, source.VersionPattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            if (!match.Success || match.Groups.Count < 2) return null;
            var version = VersionValue.Parse(match.Groups[1].Value);
            return new RemoteRelease
            {
                VersionText = match.Groups[1].Value, Version = version, SourceUrl = new Uri(source.Url),
                DownloadUrl = String.IsNullOrWhiteSpace(source.DownloadUrl) ? null : new Uri(source.DownloadUrl)
            };
        }
    }

    public sealed class TotalCmdNetSourceProvider : GenericHtmlSourceProvider
    {
        public TotalCmdNetSourceProvider(HttpService http) : base(http) { }
        public override string Name { get { return "totalcmd.net"; } }
        public override bool CanHandle(CatalogSource source) { return source.Provider.Equals("totalcmd.net", StringComparison.OrdinalIgnoreCase) && base.CanHandle(source); }
    }

    public sealed class GhislerSourceProvider : GenericHtmlSourceProvider
    {
        public GhislerSourceProvider(HttpService http) : base(http) { }
        public override string Name { get { return "Ghisler"; } }
        public override bool CanHandle(CatalogSource source) { return source.Provider.Equals("ghisler", StringComparison.OrdinalIgnoreCase) && base.CanHandle(source); }
    }
}
