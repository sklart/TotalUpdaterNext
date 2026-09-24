using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Globalization;
using System.Runtime.Serialization.Json;
using System.Text.RegularExpressions;
using TotalUpdater.Next.Core;
using TotalUpdater.Next.TotalCommander;
using TotalUpdater.Next.Sources;

namespace TotalUpdater.Next.Catalog
{
    public sealed class CatalogService
    {
        private const string EmbeddedResourceName = "TotalUpdater.Next.Catalog.plugin-catalog.json";
        private readonly string _userCatalogPath;

        public CatalogService(string userCatalogPath)
        {
            _userCatalogPath = userCatalogPath;
        }

        public IList<CatalogDiagnostic> Diagnostics { get; private set; } = new List<CatalogDiagnostic>();

        public IList<PluginCatalogEntry> Load()
        {
            return LoadWithDiagnostics().Entries;
        }

        public CatalogLoadResult LoadWithDiagnostics()
        {
            var diagnostics = new List<CatalogDiagnostic>();
            var entries = new Dictionary<string, PluginCatalogEntry>(StringComparer.OrdinalIgnoreCase);
            AddEntries(ReadEmbedded(), entries, diagnostics, false);
            string userError; AddEntries(ReadFileSafe(_userCatalogPath, out userError), entries, diagnostics, true);
            if (!String.IsNullOrWhiteSpace(userError)) AddDiagnostic(diagnostics, "", "Пользовательский каталог проигнорирован: " + userError);
            Diagnostics = diagnostics;
            return new CatalogLoadResult { Entries = entries.Values.OrderBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase).ToList(), Diagnostics = diagnostics };
        }

        public PluginCatalogEntry FindByAlias(string fileName)
        {
            var entries = Load();
            var entry = entries.FirstOrDefault(x => x.Aliases.Any(alias => alias.Equals(fileName, StringComparison.OrdinalIgnoreCase)));
            return entry ?? entries.FirstOrDefault(x => x.Aliases.Any(alias => alias.Equals(NormalizeCompanionAlias(fileName), StringComparison.OrdinalIgnoreCase)));
        }

        public PluginCatalogEntry FindById(string id)
        {
            if (String.IsNullOrWhiteSpace(id)) return null;
            return Load().FirstOrDefault(x => x.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
        }

        private static string NormalizeCompanionAlias(string fileName)
        {
            var extension = Path.GetExtension(fileName);
            if (extension.Length < 4 || (!extension.StartsWith(".w", StringComparison.OrdinalIgnoreCase) && !extension.StartsWith(".uw", StringComparison.OrdinalIgnoreCase))) return fileName;
            if (extension.StartsWith(".uw", StringComparison.OrdinalIgnoreCase)) return Path.GetFileNameWithoutExtension(fileName) + ".w" + extension.Substring(3);
            if (extension.EndsWith("64", StringComparison.OrdinalIgnoreCase)) return Path.GetFileNameWithoutExtension(fileName) + extension.Substring(0, extension.Length - 2);
            return fileName;
        }

        public void EnsureUserCatalog()
        {
            if (File.Exists(_userCatalogPath)) return;
            Directory.CreateDirectory(Path.GetDirectoryName(_userCatalogPath));
            File.WriteAllText(_userCatalogPath, "[]");
        }

        public IList<PluginCatalogEntry> LoadUserCatalog()
        {
            string ignored; return ReadFileSafe(_userCatalogPath, out ignored);
        }

        private static void AddEntries(IEnumerable<PluginCatalogEntry> candidates, IDictionary<string, PluginCatalogEntry> entries, ICollection<CatalogDiagnostic> diagnostics, bool userCatalog)
        {
            var seenIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var entry in candidates)
            {
                var id = entry == null ? "" : entry.Id;
                if (!seenIds.Add(id ?? "")) { AddDiagnostic(diagnostics, id, "Повторяющийся id в каталоге."); continue; }
                string error;
                if (!IsValid(entry, out error)) { AddDiagnostic(diagnostics, id, error); continue; }
                foreach (var source in entry.Sources.Where(x => x.AuthorityValue == SourceAuthority.ManualOverride))
                {
                    DateTime verified; if (DateTime.TryParseExact(source.ManualOverride.VerifiedAt, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out verified) && verified < DateTime.UtcNow.AddDays(-180)) diagnostics.Add(new CatalogDiagnostic { Severity = CatalogDiagnosticSeverity.Warning, EntryId = id, Message = "ManualOverride старше 180 дней." });
                    if (!source.Provider.Equals("manual", StringComparison.OrdinalIgnoreCase)) diagnostics.Add(new CatalogDiagnostic { Severity = CatalogDiagnosticSeverity.Warning, EntryId = id, Message = "Устаревший provider для ManualOverride; используйте manual." });
                }
                var others = entries.Where(x => !x.Key.Equals(entry.Id, StringComparison.OrdinalIgnoreCase)).Select(x => x.Value).ToList();
                var conflict = FindAliasConflict(entry, others);
                if (conflict != null) { AddDiagnostic(diagnostics, entry.Id, "Alias конфликтует с записью '" + conflict.Id + "'."); continue; }
                if (!userCatalog && entries.ContainsKey(entry.Id)) { AddDiagnostic(diagnostics, entry.Id, "Повторяющийся id во встроенном каталоге."); continue; }
                entries[entry.Id] = entry;
            }
        }

        private static PluginCatalogEntry FindAliasConflict(PluginCatalogEntry entry, IEnumerable<PluginCatalogEntry> others)
        {
            var aliases = new HashSet<string>(entry.Aliases.Select(NormalizeCompanionAlias), StringComparer.OrdinalIgnoreCase);
            return others.FirstOrDefault(x => x.Aliases.Select(NormalizeCompanionAlias).Any(aliases.Contains));
        }

        private static bool IsValid(PluginCatalogEntry entry, out string error)
        {
            error = "";
            if (entry == null || String.IsNullOrWhiteSpace(entry.Id)) { error = "Пустой id."; return false; }
            if (String.IsNullOrWhiteSpace(entry.Name)) { error = "Пустое name."; return false; }
            PluginType type;
            if (!Enum.TryParse(entry.Type, true, out type) || type == PluginType.Other) { error = "Неизвестный PluginType."; return false; }
            if (entry.Aliases == null || entry.Aliases.Any(String.IsNullOrWhiteSpace)) { error = "Некорректный aliases."; return false; }
            IdentityEvidence evidence; if (!Enum.TryParse(entry.IdentityEvidenceName ?? "MetadataOnly", true, out evidence)) { error = "Неизвестный identityEvidence."; return false; }
            if ((evidence == IdentityEvidence.VerifiedBinary || evidence == IdentityEvidence.VerifiedPackage) && entry.Aliases.Count == 0) { error = "Подтверждённый identity требует aliases."; return false; }
            if (!LocalVersionStrategyRegistry.Default.Contains(entry.LocalVersionStrategy)) { error = "Неизвестная localVersionStrategy."; return false; }
            LocalAheadPolicy aheadPolicy;
            if (!Enum.TryParse(entry.LocalAheadPolicyName ?? "Unknown", true, out aheadPolicy)) { error = "Неизвестная localAheadPolicy."; return false; }
            if (entry.Sources == null || entry.Sources.Count == 0) { error = "Пустой sources."; return false; }
            if (entry.Sources.Any(x => x != null && x.AuthorityValue == SourceAuthority.Mirror && x.PurposeValue != SourcePurpose.Download) &&
                !entry.Sources.Any(x => x != null && x.AuthorityValue > SourceAuthority.Mirror && x.PurposeValue != SourcePurpose.Download))
            { error = "Mirror metadata требует более доверенный metadata источник."; return false; }
            foreach (var source in entry.Sources)
            {
                if (source == null || !IsKnownProvider(source.Provider)) { error = "Неизвестный provider."; return false; }
                SourceAuthority authority; if (!String.IsNullOrWhiteSpace(source.Authority) && !Enum.TryParse(source.Authority, true, out authority)) { error = "Неизвестный authority."; return false; }
                SourcePurpose purpose; if (!String.IsNullOrWhiteSpace(source.Purpose) && !Enum.TryParse(source.Purpose, true, out purpose)) { error = "Неизвестный purpose."; return false; }
                if (source.Provider.Equals("ghisler-plugins", StringComparison.OrdinalIgnoreCase) && String.IsNullOrWhiteSpace(source.Id)) { error = "Пустой id источника ghisler-plugins."; return false; }
                if (source.AuthorityValue == SourceAuthority.ManualOverride)
                {
                    var manual = source.ManualOverride; DateTime verified;
                    if (manual == null || !Core.Versions.VersionValue.Parse(manual.Version).IsKnown || !IsHttpUrl(manual.EvidenceUrl) || String.IsNullOrWhiteSpace(manual.Reason) || !DateTime.TryParseExact(manual.VerifiedAt, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out verified)) { error = "Некорректный ManualOverride."; return false; }
                    if (source.PurposeValue != SourcePurpose.Metadata) { error = "ManualOverride не может предоставлять download."; return false; }
                }
                else if (source.Provider.Equals("manual", StringComparison.OrdinalIgnoreCase)) { error = "Provider manual требует authority ManualOverride."; return false; }
                if (source.Priority <= 0) { error = "Некорректный priority."; return false; }
                if (!String.IsNullOrWhiteSpace(source.PackageArchitecture) && !Enum.GetNames(typeof(RemotePackageArchitecture)).Any(x => x.Equals(source.PackageArchitecture, StringComparison.OrdinalIgnoreCase))) { error = "Некорректный packageArchitecture."; return false; }
                if (!String.IsNullOrWhiteSpace(source.VersionTransform) && !String.Equals(source.VersionTransform, "Total7zipPe", StringComparison.OrdinalIgnoreCase)) { error = "Некорректный versionTransform."; return false; }
                if ((source.Provider.Equals("totalcmd.net", StringComparison.OrdinalIgnoreCase) || source.Provider.Equals("totalcmd.net-index", StringComparison.OrdinalIgnoreCase)) && String.IsNullOrWhiteSpace(source.Id)) { error = "Пустой id источника totalcmd.net."; return false; }
                if (source.Provider.Equals("github", StringComparison.OrdinalIgnoreCase) && !Regex.IsMatch(source.Repository ?? "", @"^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$")) { error = "Некорректный GitHub repository."; return false; }
                if (source.Provider.Equals("generic-html", StringComparison.OrdinalIgnoreCase))
                {
                    if (!IsHttpUrl(source.Url) || String.IsNullOrWhiteSpace(source.VersionPattern)) { error = "Некорректный URL GenericHtml."; return false; }
                    try { new Regex(source.VersionPattern ?? "", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(200)); } catch { error = "Некорректный GenericHtml regex."; return false; }
                }
                else if (!String.IsNullOrWhiteSpace(source.VersionPattern) || !String.IsNullOrWhiteSpace(source.DownloadUrl)) { error = "versionPattern/downloadUrl разрешены только для generic-html."; return false; }
                else if (!String.IsNullOrWhiteSpace(source.Url) && !IsHttpUrl(source.Url)) { error = "Некорректный URL."; return false; }
                if (!String.IsNullOrWhiteSpace(source.DownloadUrl) && !IsHttpUrl(source.DownloadUrl)) { error = "Некорректный downloadUrl."; return false; }
                if (!String.IsNullOrWhiteSpace(source.AssetPattern)) try { new Regex(source.AssetPattern, RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(200)); } catch { error = "Некорректный assetPattern."; return false; }
            }
            return true;
        }

        private static bool IsKnownProvider(string provider)
        {
            return "manual".Equals(provider, StringComparison.OrdinalIgnoreCase) || "totalcmd.net".Equals(provider, StringComparison.OrdinalIgnoreCase) || "totalcmd.net-index".Equals(provider, StringComparison.OrdinalIgnoreCase) || "ghisler".Equals(provider, StringComparison.OrdinalIgnoreCase) || "ghisler-plugins".Equals(provider, StringComparison.OrdinalIgnoreCase) || "github".Equals(provider, StringComparison.OrdinalIgnoreCase) || "generic-html".Equals(provider, StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsHttpUrl(string value)
        {
            Uri uri; return Uri.TryCreate(value, UriKind.Absolute, out uri) && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
        }

        private static void AddDiagnostic(ICollection<CatalogDiagnostic> diagnostics, string id, string message)
        {
            diagnostics.Add(new CatalogDiagnostic { Severity = CatalogDiagnosticSeverity.Error, EntryId = id ?? "", Message = message });
        }

        private static IList<PluginCatalogEntry> ReadEmbedded()
        {
            var assembly = Assembly.GetExecutingAssembly();
            using (var stream = assembly.GetManifestResourceStream(EmbeddedResourceName))
            {
                if (stream == null) throw new InvalidOperationException("Встроенный каталог не найден.");
                return Read(stream);
            }
        }

        private static IList<PluginCatalogEntry> ReadFile(string path)
        {
            if (!File.Exists(path)) return new List<PluginCatalogEntry>();
            using (var stream = File.OpenRead(path)) return Read(stream);
        }

        private static IList<PluginCatalogEntry> ReadFileSafe(string path, out string error)
        {
            error = ""; if (!File.Exists(path)) return new List<PluginCatalogEntry>();
            try { if (new FileInfo(path).Length == 0) { error = "пустой файл"; return new List<PluginCatalogEntry>(); } return ReadFile(path); }
            catch (Exception ex) { error = ex.GetType().Name; return new List<PluginCatalogEntry>(); }
        }

        private static IList<PluginCatalogEntry> Read(Stream stream)
        {
            var serializer = new DataContractJsonSerializer(typeof(List<PluginCatalogEntry>));
            return (serializer.ReadObject(stream) as List<PluginCatalogEntry>) ?? new List<PluginCatalogEntry>();
        }
    }
}
