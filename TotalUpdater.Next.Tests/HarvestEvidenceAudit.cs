using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using TotalUpdater.Next.Catalog;
using TotalUpdater.Next.Core;

namespace TotalUpdater.Next.Tests
{
    [DataContract]
    internal sealed class HarvestEvidence
    {
        [DataMember(Name = "id")] public string Id { get; set; }
        [DataMember(Name = "type")] public string Type { get; set; }
        [DataMember(Name = "aliases")] public List<string> Aliases { get; set; }
        [DataMember(Name = "packageUrl")] public string PackageUrl { get; set; }
        [DataMember(Name = "verifiedUtc")] public string VerifiedUtc { get; set; }
    }

    internal static class HarvestEvidenceAudit
    {
        public static IList<HarvestEvidence> Read(string path)
        {
            using (var stream = File.OpenRead(path))
                return (List<HarvestEvidence>)new DataContractJsonSerializer(typeof(List<HarvestEvidence>)).ReadObject(stream);
        }

        public static IList<string> Validate(IEnumerable<HarvestEvidence> items, DateTime todayUtc)
        {
            var errors = new List<string>();
            var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var aliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var item in items ?? Enumerable.Empty<HarvestEvidence>())
            {
                var id = item == null ? "" : item.Id ?? "";
                if (String.IsNullOrWhiteSpace(id) || !ids.Add(id)) errors.Add(id + ": duplicate/empty id");
                PluginType type;
                if (item == null || !Enum.TryParse(item.Type, true, out type) || type == PluginType.Other || type == PluginType.TotalCommander)
                    errors.Add(id + ": invalid type");
                Uri url;
                if (item == null || !Uri.TryCreate(item.PackageUrl, UriKind.Absolute, out url) ||
                    (url.Scheme != Uri.UriSchemeHttp && url.Scheme != Uri.UriSchemeHttps)) errors.Add(id + ": invalid URL");
                DateTime verified;
                if (item == null || !DateTime.TryParseExact(item.VerifiedUtc, "yyyy-MM-dd", CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out verified) || verified > todayUtc.Date)
                    errors.Add(id + ": malformed verifiedUtc");
                else if (verified < todayUtc.Date.AddDays(-180)) errors.Add(id + ": stale harvest evidence (warning)");
                foreach (var alias in item == null ? new List<string>() : item.Aliases ?? new List<string>())
                {
                    var normalized = (alias ?? "").Trim().Normalize(NormalizationForm.FormC).ToLowerInvariant();
                    string other;
                    if (aliases.TryGetValue(normalized, out other)) errors.Add(id + ": duplicate normalized alias " + alias + " (" + other + ")");
                    else aliases[normalized] = id;
                    if (CatalogAliasAudit.Audit(new[] { new PluginCatalogEntry { Id = id, Type = item.Type, Aliases = new List<string> { alias } } }).Any())
                        errors.Add(id + ": invalid alias " + alias);
                }
            }
            return errors;
        }
    }
}
