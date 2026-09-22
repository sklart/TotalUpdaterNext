using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using TotalUpdater.Next.Catalog;
using TotalUpdater.Next.Core.Versions;

namespace TotalUpdater.Next.Sources
{
    public enum SourceQueryStatus { Success, NotFound, Unavailable, InvalidResponse }
    public enum RemotePackageArchitecture { Unknown, X86, X64, Combined }

    public sealed class RemotePackage
    {
        public RemotePackageArchitecture Architecture { get; set; }
        public Uri Url { get; set; }
        public string FileName { get; set; } = "";
    }

    public sealed class RemoteRelease
    {
        public string VersionText { get; set; } = "";
        public VersionValue Version { get; set; } = VersionValue.Unknown;
        public Uri SourceUrl { get; set; }
        public IList<RemotePackage> Packages { get; set; } = new List<RemotePackage>();
    }

    public sealed class SourceQueryResult
    {
        public SourceQueryStatus Status { get; set; }
        public RemoteRelease Release { get; set; }
        public string Details { get; set; } = "";
    }

    public interface IUpdateSourceProvider
    {
        string Name { get; }
        bool CanHandle(CatalogSource source);
        Task<SourceQueryResult> QueryAsync(CatalogSource source, CancellationToken cancellationToken);
    }

    public class GenericHtmlSourceProvider : IUpdateSourceProvider
    {
        protected readonly HttpService Http;
        public GenericHtmlSourceProvider(HttpService http) { Http = http; }
        public virtual string Name { get { return "HTML"; } }
        public virtual bool CanHandle(CatalogSource source) { return source != null && source.Provider.Equals("generic-html", StringComparison.OrdinalIgnoreCase); }
        public virtual async Task<SourceQueryResult> QueryAsync(CatalogSource source, CancellationToken cancellationToken)
        {
            try
            {
                var html = await Http.GetStringAsync(source.Url, cancellationToken).ConfigureAwait(false);
                var match = Regex.Match(html, source.VersionPattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
                if (!match.Success || match.Groups.Count < 2) return Result(SourceQueryStatus.NotFound, null, "Версия не найдена на странице.");
                var version = VersionValue.Parse(match.Groups[1].Value);
                var packages = new List<RemotePackage>(); Uri download;
                if (!String.IsNullOrWhiteSpace(source.DownloadUrl) && Uri.TryCreate(source.DownloadUrl, UriKind.Absolute, out download)) packages.Add(new RemotePackage { Architecture = RemotePackageArchitecture.Unknown, Url = download, FileName = Path.GetFileName(download.LocalPath) });
                return version.IsKnown ? Result(SourceQueryStatus.Success, new RemoteRelease { VersionText = match.Groups[1].Value, Version = version, SourceUrl = new Uri(source.Url), Packages = packages }, "") : Result(SourceQueryStatus.InvalidResponse, null, "Некорректная версия.");
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex) { return Result(SourceQueryStatus.Unavailable, null, ex.Message); }
        }

        protected static SourceQueryResult Result(SourceQueryStatus status, RemoteRelease release, string details) { return new SourceQueryResult { Status = status, Release = release, Details = details }; }
        protected static IList<RemotePackage> ParseLinks(string html)
        {
            var packages = new List<RemotePackage>();
            foreach (Match match in Regex.Matches(html ?? "", @"<a[^>]*href\s*=\s*[""'](?<url>[^""']+)[""'][^>]*>(?<label>.*?)</a>", RegexOptions.IgnoreCase | RegexOptions.Singleline))
            {
                Uri url; if (!Uri.TryCreate(match.Groups["url"].Value, UriKind.Absolute, out url)) continue;
                var label = Regex.Replace(match.Groups["label"].Value, "<.*?>", "") + " " + url.AbsoluteUri;
                var architecture = DetectPackageArchitecture(label);
                packages.Add(new RemotePackage { Architecture = architecture, Url = url, FileName = Path.GetFileName(url.LocalPath) });
            }
            return packages;
        }
        protected static RemotePackageArchitecture DetectPackageArchitecture(string value)
        {
            var lower = " " + Regex.Replace(value ?? "", @"[^a-z0-9]+", " ").ToLowerInvariant() + " ";
            if (lower.Contains(" arm64 ")) return RemotePackageArchitecture.Unknown;
            var x86 = lower.Contains(" x32 ") || lower.Contains(" x86 ") || lower.Contains(" win32 ") || lower.Contains(" 32bit ") || lower.Contains(" 32 bit "); var x64 = lower.Contains(" x64 ") || lower.Contains(" amd64 ") || lower.Contains(" win64 ") || lower.Contains(" 64bit ") || lower.Contains(" 64 bit ");
            return x86 && x64 ? RemotePackageArchitecture.Combined : x64 ? RemotePackageArchitecture.X64 : x86 ? RemotePackageArchitecture.X86 : RemotePackageArchitecture.Unknown;
        }
        protected static IList<RemotePackage> ParseDownloadLinks(string html, Uri baseUri)
        {
            var result = new List<RemotePackage>();
            foreach (Match match in Regex.Matches(html ?? "", @"<a[^>]*href\s*=\s*[""'](?<url>[^""']+)[""'][^>]*>(?<label>.*?)</a>", RegexOptions.IgnoreCase | RegexOptions.Singleline))
            {
                var label = Regex.Replace(match.Groups["label"].Value, "<.*?>", " "); var text = label + " " + match.Groups["url"].Value;
                if (!Regex.IsMatch(text, @"download|x32|x64|x86|win32|win64|32[ -]?bit|64[ -]?bit", RegexOptions.IgnoreCase) || Regex.IsMatch(text, @"mirror|source|homepage|author|forum|discuss|screen", RegexOptions.IgnoreCase)) continue;
                Uri url; if (!Uri.TryCreate(baseUri, match.Groups["url"].Value, out url)) continue;
                result.Add(new RemotePackage { Architecture = DetectPackageArchitecture(text), Url = url, FileName = url.AbsolutePath.EndsWith("download.php", StringComparison.OrdinalIgnoreCase) ? "" : Path.GetFileName(url.LocalPath) });
            }
            return result;
        }
    }

    public sealed class TotalCmdNetSourceProvider : GenericHtmlSourceProvider
    {
        public TotalCmdNetSourceProvider(HttpService http) : base(http) { }
        public override string Name { get { return "totalcmd.net"; } }
        public override bool CanHandle(CatalogSource source) { return source != null && source.Provider.Equals("totalcmd.net", StringComparison.OrdinalIgnoreCase); }
        public override async Task<SourceQueryResult> QueryAsync(CatalogSource source, CancellationToken cancellationToken)
        {
            try { return Parse(source.Id, await Http.GetStringAsync(CanonicalUrl(source.Id), cancellationToken).ConfigureAwait(false)); }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex) { return Result(SourceQueryStatus.Unavailable, null, ex.Message); }
        }
        public static string CanonicalUrl(string id) { return "https://totalcmd.net/plugring/" + Uri.EscapeDataString(id ?? "") + ".html"; }
        public static SourceQueryResult Parse(string id, string html)
        {
            var match = Regex.Match(html ?? "", @"<h1[^>]*>.*?([0-9]+(?:\.[0-9A-Za-z]+){1,3}).*?</h1>", RegexOptions.IgnoreCase | RegexOptions.Singleline);
            if (!match.Success) match = Regex.Match(html ?? "", @"<title[^>]*>.*?([0-9]+(?:\.[0-9A-Za-z]+){1,3}).*?</title>", RegexOptions.IgnoreCase | RegexOptions.Singleline);
            if (!match.Success) return Result(SourceQueryStatus.NotFound, null, "Версия плагина не найдена.");
            var version = VersionValue.Parse(match.Groups[1].Value);
            if (!version.IsKnown) return Result(SourceQueryStatus.InvalidResponse, null, "Некорректная версия плагина.");
            var packages = ParsePackages(html);
            return Result(SourceQueryStatus.Success, new RemoteRelease { VersionText = match.Groups[1].Value, Version = version, SourceUrl = new Uri(CanonicalUrl(id)), Packages = packages }, "");
        }
        public static IList<RemotePackage> ParsePackages(string html) { return ParseDownloadLinks(html, new Uri("https://totalcmd.net/")); }
    }

    public sealed class GhislerSourceProvider : GenericHtmlSourceProvider
    {
        public GhislerSourceProvider(HttpService http) : base(http) { }
        public override string Name { get { return "Ghisler"; } }
        public override bool CanHandle(CatalogSource source) { return source != null && source.Provider.Equals("ghisler", StringComparison.OrdinalIgnoreCase); }
        public override async Task<SourceQueryResult> QueryAsync(CatalogSource source, CancellationToken cancellationToken)
        {
            const string url = "https://www.ghisler.com/download.htm";
            try { return Parse(await Http.GetStringAsync(url, cancellationToken).ConfigureAwait(false)); }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex) { return Result(SourceQueryStatus.Unavailable, null, ex.Message); }
        }
        public static SourceQueryResult Parse(string html)
        {
            var match = Regex.Match(html ?? "", @"(?:Download\s+version|Total\s+Commander)\s+([0-9]+(?:\.[0-9]+){1,3})", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            if (!match.Success) return Result(SourceQueryStatus.NotFound, null, "Версия Total Commander не найдена.");
            var version = VersionValue.Parse(match.Groups[1].Value);
            return version.IsKnown ? Result(SourceQueryStatus.Success, new RemoteRelease { VersionText = match.Groups[1].Value, Version = version, SourceUrl = new Uri("https://www.ghisler.com/download.htm"), Packages = ParseDownloadLinks(html, new Uri("https://www.ghisler.com/")).Where(x => x.Architecture != RemotePackageArchitecture.Unknown).ToList() }, "") : Result(SourceQueryStatus.InvalidResponse, null, "Некорректная версия Total Commander.");
        }
    }

    public sealed class GitHubReleaseSourceProvider : IUpdateSourceProvider
    {
        private readonly HttpService _http;
        public GitHubReleaseSourceProvider(HttpService http) { _http = http; }
        public string Name { get { return "GitHub Releases"; } }
        public bool CanHandle(CatalogSource source) { return source != null && source.Provider.Equals("github", StringComparison.OrdinalIgnoreCase); }
        public async Task<SourceQueryResult> QueryAsync(CatalogSource source, CancellationToken cancellationToken)
        {
            try
            {
                var json = await _http.GetStringAsync("https://api.github.com/repos/" + source.Repository + "/releases", cancellationToken).ConfigureAwait(false);
                return Parse(json, source);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex) { return Result(SourceQueryStatus.Unavailable, null, ex.Message); }
        }
        public static SourceQueryResult Parse(string json, CatalogSource source)
        {
            try
            {
                var releases = Deserialize(json); var release = releases.FirstOrDefault(x => !x.Draft && (source.IncludePrerelease || !x.Prerelease));
                if (release == null) return Result(SourceQueryStatus.NotFound, null, "Подходящий GitHub release не найден.");
                var version = VersionValue.Parse((release.TagName ?? "").TrimStart('v', 'V'));
                if (!version.IsKnown || !Uri.IsWellFormedUriString(release.HtmlUrl, UriKind.Absolute)) return Result(SourceQueryStatus.InvalidResponse, null, "Некорректный GitHub release.");
                var assets = release.Assets ?? new List<GitHubAsset>();
                if (!String.IsNullOrWhiteSpace(source.AssetPattern)) assets = assets.Where(x => Regex.IsMatch(x.Name ?? "", source.AssetPattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)).ToList();
                if (!String.IsNullOrWhiteSpace(source.AssetPattern) && assets.Count == 0) return Result(SourceQueryStatus.NotFound, null, "В release нет подходящего asset.");
                RemotePackageArchitecture hint; var hasHint = Enum.TryParse(source.PackageArchitecture, true, out hint);
                return Result(SourceQueryStatus.Success, new RemoteRelease { VersionText = release.TagName, Version = version, SourceUrl = new Uri(release.HtmlUrl), Packages = assets.Where(x => Uri.IsWellFormedUriString(x.DownloadUrl, UriKind.Absolute)).Select(x => new RemotePackage { FileName = x.Name ?? "", Url = new Uri(x.DownloadUrl), Architecture = hasHint ? hint : DetectArchitecture(x.Name) }).ToList() }, "");
            }
            catch { return Result(SourceQueryStatus.InvalidResponse, null, "Некорректный GitHub JSON."); }
        }
        private static IList<GitHubRelease> Deserialize(string json)
        {
            using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(json ?? "[]"))) return (IList<GitHubRelease>)new DataContractJsonSerializer(typeof(List<GitHubRelease>)).ReadObject(stream);
        }
        public static RemotePackageArchitecture DetectArchitecture(string name)
        {
            var tokens = Regex.Split((name ?? "").ToLowerInvariant(), @"[^a-z0-9]+");
            if (tokens.Contains("arm64")) return RemotePackageArchitecture.Unknown;
            var x64 = tokens.Any(x => x == "x64" || x == "amd64" || x == "win64" || x == "64bit"); var x86 = tokens.Any(x => x == "x86" || x == "win32" || x == "32bit");
            if (x64 && x86) return RemotePackageArchitecture.Combined;
            if (x64) return RemotePackageArchitecture.X64;
            if (x86) return RemotePackageArchitecture.X86;
            return RemotePackageArchitecture.Unknown;
        }
        [DataContract] private sealed class GitHubRelease { [DataMember(Name = "tag_name")] public string TagName { get; set; } [DataMember(Name = "prerelease")] public bool Prerelease { get; set; } [DataMember(Name = "draft")] public bool Draft { get; set; } [DataMember(Name = "html_url")] public string HtmlUrl { get; set; } [DataMember(Name = "assets")] public List<GitHubAsset> Assets { get; set; } }
        [DataContract] private sealed class GitHubAsset { [DataMember(Name = "name")] public string Name { get; set; } [DataMember(Name = "browser_download_url")] public string DownloadUrl { get; set; } }
        private static SourceQueryResult Result(SourceQueryStatus status, RemoteRelease release, string details) { return new SourceQueryResult { Status = status, Release = release, Details = details }; }
    }
}
