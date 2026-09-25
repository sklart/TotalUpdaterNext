using System;
using System.Collections.Generic;
using System.Linq;

namespace TotalUpdater.Next.Catalog
{
    // Pure catalog-level merge used by the maintenance command and fixtures.
    // It deliberately has no knowledge of aliases, sources or release data:
    // the only mutable catalog member is wcxRegistration.
    public static class WcxRegistrationEvidenceMerger
    {
        public static bool TryApply(IList<PluginCatalogEntry> entries, IEnumerable<WcxRegistrationHarvestFinding> findings, out string error)
        {
            error = null;
            if (entries == null) { error = "Catalog entries are required."; return false; }
            var verified = (findings ?? Enumerable.Empty<WcxRegistrationHarvestFinding>())
                .Where(x => x != null && String.Equals(x.Status, WcxRegistrationHarvest.VerifiedRegistration, StringComparison.Ordinal))
                .ToList();
            if (verified.GroupBy(x => x.Id ?? "", StringComparer.OrdinalIgnoreCase).Any(x => x.Count() != 1)) { error = "Duplicate VerifiedRegistration finding id."; return false; }
            foreach (var finding in verified)
            {
                if (!WcxRegistrationHarvest.IsValidVerifiedFinding(finding)) { error = "Invalid VerifiedRegistration evidence for " + (finding.Id ?? "") + "."; return false; }
                var entry = entries.FirstOrDefault(x => String.Equals(x.Id, finding.Id, StringComparison.OrdinalIgnoreCase));
                if (entry == null || entry.PluginType != Core.PluginType.Wcx) { error = "VerifiedRegistration target is missing or not WCX: " + finding.Id + "."; return false; }
            }
            foreach (var finding in verified)
            {
                var entry = entries.First(x => String.Equals(x.Id, finding.Id, StringComparison.OrdinalIgnoreCase));
                entry.WcxRegistration = Copy(finding.Evidence);
            }
            return true;
        }

        private static WcxRegistrationEvidence Copy(WcxRegistrationEvidence source)
        {
            return new WcxRegistrationEvidence
            {
                PackageSha256 = source.PackageSha256,
                X86BinarySha256 = source.X86BinarySha256,
                X64BinarySha256 = source.X64BinarySha256,
                PackerCaps = source.PackerCaps,
                Extensions = source.Extensions.ToList(),
                VerifiedUtc = source.VerifiedUtc,
                Source = source.Source
            };
        }
    }
}
