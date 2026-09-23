using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using TotalUpdater.Next.Core;
using TotalUpdater.Next.Sources;

namespace TotalUpdater.Next.Catalog
{
    public interface IRemoteCatalogLookup
    {
        Task<CatalogMatchResult> FindAsync(InstalledPlugin plugin, SourceResponseCache cache, CancellationToken token);
    }

    public sealed class RemoteCatalogLookup : IRemoteCatalogLookup
    {
        private readonly HttpService _http;
        public RemoteCatalogLookup(HttpService http) { _http = http; }

        public async Task<CatalogMatchResult> FindAsync(InstalledPlugin plugin, SourceResponseCache cache, CancellationToken token)
        {
            if (plugin == null || plugin.Type == PluginType.TotalCommander) return new CatalogMatchResult { Kind = CatalogMatchKind.NotFound };
            var fileName = Path.GetFileName(plugin.PrimaryPath ?? "");
            var stem = Path.GetFileNameWithoutExtension(fileName);
            var candidates = new List<RemoteIndexCandidate>();
            try
            {
                var index = cache == null ? await _http.GetTotalCmdIndexAsync(token).ConfigureAwait(false) :
                    await cache.GetOrAdd("totalcmd.net:index", () => _http.GetTotalCmdIndexAsync(token)).ConfigureAwait(false);
                candidates.AddRange(ParseTotalCmdIndex(index, plugin.Type).Where(x => MatchesName(stem, plugin.DisplayName, x)));
            }
            catch (OperationCanceledException) { if (token.IsCancellationRequested) throw; }
            catch { /* A failed index does not authorize a guessed match. */ }
            try
            {
                const string url = "https://www.ghisler.com/plugins.htm";
                var html = cache == null ? await _http.GetStringAsync(url, token).ConfigureAwait(false) :
                    await cache.GetOrAdd("ghisler:plugins", () => _http.GetStringAsync(url, token)).ConfigureAwait(false);
                candidates.AddRange(ParseGhislerIndex(html, plugin.Type).Where(x => MatchesName(stem, plugin.DisplayName, x)));
            }
            catch (OperationCanceledException) { if (token.IsCancellationRequested) throw; }
            catch { /* Keep the other index available. */ }
            var verified = new List<RemoteIndexCandidate>();
            foreach (var item in candidates)
            {
                token.ThrowIfCancellationRequested();
                try
                {
                    var bytes = cache == null ? await ReadArchive(item.PackageUrl, token).ConfigureAwait(false) :
                        await cache.GetOrAddBytes("catalog:archive:" + item.PackageUrl, () => ReadArchive(item.PackageUrl, token)).ConfigureAwait(false);
                    if (ArchiveAliases(bytes, plugin.Type).Any(x => x.Equals(fileName, StringComparison.OrdinalIgnoreCase))) verified.Add(item);
                }
                catch (OperationCanceledException) { if (token.IsCancellationRequested) throw; }
                catch { /* Unknown archives never become automatic matches. */ }
            }
            var groups = verified.GroupBy(x => x.Type + "|" + x.Name, StringComparer.OrdinalIgnoreCase).ToList();
            var entries = groups.Select(group => BuildVerifiedEntry(group, fileName)).ToList();
            return CatalogMatcher.Match(plugin.Type, fileName, entries, true);
        }

        public static PluginCatalogEntry BuildVerifiedEntry(IEnumerable<RemoteIndexCandidate> verifiedGroup, string fileName)
        {
            var group = verifiedGroup.ToList();
            return new PluginCatalogEntry
            {
                Id = group[0].Id, Name = group[0].Name, Type = group[0].Type.ToString(), Aliases = new List<string> { fileName },
                Sources = group.GroupBy(x => (x.Official ? "ghisler:" : "totalcmd:") + x.Id, StringComparer.OrdinalIgnoreCase).Select(x => x.First())
                    .Select(x => new CatalogSource { Provider = x.Official ? "ghisler-plugins" : "totalcmd.net", Id = x.Id,
                        Authority = x.Official ? "OfficialTotalCommander" : "CommunityCatalog", Purpose = "MetadataAndDownload",
                        Priority = x.Official ? 200 : 100, EphemeralVerifiedPackageUrl = x.Official ? x.PackageUrl : "" }).ToList()
            };
        }

        public static IList<RemoteIndexCandidate> ParseTotalCmdIndex(string text, PluginType wantedType)
        {
            var map = new Dictionary<string, PluginType>(StringComparer.OrdinalIgnoreCase) { { "packer", PluginType.Wcx }, { "lister", PluginType.Wlx }, { "fsplugin", PluginType.Wfx }, { "content", PluginType.Wdx } };
            var result = new List<RemoteIndexCandidate>();
            foreach (var line in (text ?? "").TrimStart('\uFEFF').Split('\n'))
            {
                var fields = line.TrimEnd('\r').Split('|').Select(x => x.Trim()).ToArray();
                PluginType type;
                if (fields.Length < 5 || !map.TryGetValue(fields[4], out type) || type != wantedType || String.IsNullOrWhiteSpace(fields[0])) continue;
                result.Add(new RemoteIndexCandidate { Id = fields[0], Name = fields[1], Type = type,
                    PackageUrl = "https://totalcmd.net/download.php?id=" + Uri.EscapeDataString(fields[0]) });
            }
            return result;
        }

        public static IList<RemoteIndexCandidate> ParseGhislerIndex(string html, PluginType wantedType)
        {
            var result = new List<RemoteIndexCandidate>();
            foreach (Match row in Regex.Matches(html ?? "", @"(?is)<tr\b[^>]*>.*?</tr>", RegexOptions.CultureInvariant))
            {
                var name = Regex.Match(row.Value, @"(?is)<strong\b[^>]*>\s*([^<]+)\s*</strong>");
                if (!name.Success) continue;
                foreach (Match link in Regex.Matches(row.Value, @"(?is)href\s*=\s*[""']([^""']+\.zip(?:\?[^""']*)?)[""']"))
                {
                    var raw = System.Net.WebUtility.HtmlDecode(link.Groups[1].Value);
                    var type = raw.IndexOf("/pkplugins/", StringComparison.OrdinalIgnoreCase) >= 0 ? PluginType.Wcx :
                        raw.IndexOf("/lsplugins/", StringComparison.OrdinalIgnoreCase) >= 0 ? PluginType.Wlx :
                        raw.IndexOf("/fsplugins/", StringComparison.OrdinalIgnoreCase) >= 0 ? PluginType.Wfx :
                        raw.IndexOf("/cnplugins/", StringComparison.OrdinalIgnoreCase) >= 0 ? PluginType.Wdx : PluginType.Other;
                    if (type != wantedType) continue;
                    Uri url;
                    if (!Uri.TryCreate(new Uri("https://www.ghisler.com/plugins.htm"), raw, out url) || url.Scheme != Uri.UriSchemeHttps) continue;
                    result.Add(new RemoteIndexCandidate { Id = name.Groups[1].Value.Trim(), Name = name.Groups[1].Value.Trim(), Type = type, Official = true, PackageUrl = url.AbsoluteUri });
                }
            }
            return result;
        }

        public static bool MatchesName(string fileStem, string displayName, RemoteIndexCandidate candidate)
        {
            if (candidate == null) return false;
            // Display name can narrow candidates, but cannot replace exact binary verification.
            return String.Equals(fileStem, candidate.Id, StringComparison.OrdinalIgnoreCase) ||
                String.Equals(fileStem, candidate.Name, StringComparison.OrdinalIgnoreCase) ||
                String.Equals(displayName, candidate.Name, StringComparison.OrdinalIgnoreCase);
        }

        public static IList<string> ArchiveAliases(byte[] bytes, PluginType type)
        {
            var suffix = type == PluginType.Wcx ? "cx" : type == PluginType.Wlx ? "lx" : type == PluginType.Wfx ? "fx" : type == PluginType.Wdx ? "dx" : "";
            if (suffix.Length == 0) return new List<string>();
            using (var stream = new MemoryStream(bytes ?? new byte[0]))
            using (var zip = new ZipArchive(stream, ZipArchiveMode.Read))
                return zip.Entries.Select(x => Path.GetFileName(x.FullName)).Where(x => Regex.IsMatch(x, @"(?i)\.u?w" + suffix + "(?:64)?$")).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }

        private async Task<byte[]> ReadArchive(string url, CancellationToken token)
        {
            using (var response = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false))
            {
                response.EnsureSuccessStatusCode();
                if (response.Content.Headers.ContentLength > 8000000) throw new InvalidDataException("Архив слишком большой для lookup.");
                using (var source = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
                using (var target = new MemoryStream())
                {
                    var buffer = new byte[16384]; int read;
                    while ((read = await source.ReadAsync(buffer, 0, buffer.Length, token).ConfigureAwait(false)) > 0)
                    { if (target.Length + read > 8000000) throw new InvalidDataException("Архив слишком большой для lookup."); target.Write(buffer, 0, read); }
                    return target.ToArray();
                }
            }
        }
    }

    public sealed class RemoteIndexCandidate
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public PluginType Type { get; set; }
        public bool Official { get; set; }
        public string PackageUrl { get; set; } = "";
    }
}
