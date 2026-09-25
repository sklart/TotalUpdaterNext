using System;
using System.Collections.Generic;
using System.Linq;

namespace TotalUpdater.Next.Catalog
{
    public static class WcxRegistrationEvidenceAudit
    {
        public static IList<string> Validate(IEnumerable<PluginCatalogEntry> entries)
        {
            var result = new List<string>();
            var all = (entries ?? Enumerable.Empty<PluginCatalogEntry>()).Where(x => x != null).ToList();
            foreach (var duplicate in all.Where(x => x.WcxRegistration != null).GroupBy(x => x.Id ?? "", StringComparer.OrdinalIgnoreCase).Where(x => x.Count() > 1))
                result.Add("duplicate WCX registration evidence id: " + duplicate.Key);
            foreach (var entry in all.Where(x => x.WcxRegistration != null))
            {
                var evidence = entry.WcxRegistration;
                if (entry.PluginType != Core.PluginType.Wcx) { result.Add(entry.Id + ": wcxRegistration on non-WCX."); continue; }
                if (!evidence.IsComplete || evidence.PackerCaps < 0) result.Add(entry.Id + ": incomplete evidence.");
                if (evidence.Extensions == null || evidence.Extensions.Count == 0 || evidence.Extensions.Any(x => String.IsNullOrWhiteSpace(x) || x != x.Trim().TrimStart('.') || x.IndexOfAny(new[] { '=', ',', '[', ']', '\r', '\n' }) >= 0) || evidence.Extensions.Distinct(StringComparer.OrdinalIgnoreCase).Count() != evidence.Extensions.Count)
                    result.Add(entry.Id + ": non-normalized or duplicate extensions.");
                if (!WcxRegistrationEvidence.IsSha(evidence.PackageSha256) || !WcxRegistrationEvidence.IsSha(evidence.X86BinarySha256) && !WcxRegistrationEvidence.IsSha(evidence.X64BinarySha256)) result.Add(entry.Id + ": invalid SHA-256.");
                if (evidence.VerifiedUtc == default(DateTime) || String.IsNullOrWhiteSpace(evidence.Source)) result.Add(entry.Id + ": missing verification metadata.");
            }
            return result;
        }
    }
}
