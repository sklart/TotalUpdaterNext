using System;
using System.Collections.Generic;
using System.Linq;

namespace TotalUpdater.Next.Catalog
{
    public sealed class WcxRegistrationAuditReport
    {
        public int WcxTotal { get; set; }
        public int Downloadable { get; set; }
        public int VerifiedRegistration { get; set; }
        public int MissingPackage { get; set; }
        public int MissingPluginst { get; set; }
        public int MissingDefaultExtension { get; set; }
        public int ProbeFailed { get; set; }
        public int ProbeTimeout { get; set; }
        public int ProbeArchitectureMismatch { get; set; }
        public int CapsMismatch { get; set; }
        public int PackageIdentityMismatch { get; set; }
        public int HashMismatch { get; set; }
        public int SourceUnavailable { get; set; }
        public int UnsupportedArchive { get; set; }
        public int AmbiguousBinary { get; set; }
        public int NotHarvested { get; set; }
    }

    public static class WcxRegistrationAudit
    {
        public static WcxRegistrationAuditReport Audit(IEnumerable<PluginCatalogEntry> entries, IEnumerable<WcxRegistrationHarvestFinding> findings = null)
        {
            var report = new WcxRegistrationAuditReport();
            var byId = (findings ?? Enumerable.Empty<WcxRegistrationHarvestFinding>()).Where(x => x != null && !String.IsNullOrWhiteSpace(x.Id)).GroupBy(x => x.Id, StringComparer.OrdinalIgnoreCase).ToDictionary(x => x.Key, x => x.Last(), StringComparer.OrdinalIgnoreCase);
            foreach (var entry in (entries ?? Enumerable.Empty<PluginCatalogEntry>()).Where(x => x.PluginType == Core.PluginType.Wcx))
            {
                report.WcxTotal++;
                WcxRegistrationHarvestFinding finding;
                if (!byId.TryGetValue(entry.Id, out finding) || !WcxRegistrationHarvest.IsKnownStatus(finding.Status)) { report.NotHarvested++; continue; }
                if (finding.Status == WcxRegistrationHarvest.Downloadable) report.Downloadable++;
                else if (finding.Status == WcxRegistrationHarvest.VerifiedRegistration) { report.Downloadable++; report.VerifiedRegistration++; }
                else if (finding.Status == WcxRegistrationHarvest.MissingPackage) report.MissingPackage++;
                else if (finding.Status == WcxRegistrationHarvest.MissingPluginst) report.MissingPluginst++;
                else if (finding.Status == WcxRegistrationHarvest.MissingDefaultExtension) report.MissingDefaultExtension++;
                else if (finding.Status == WcxRegistrationHarvest.AmbiguousBinary) report.AmbiguousBinary++;
                else if (finding.Status == WcxRegistrationHarvest.ProbeTimeout) report.ProbeTimeout++;
                else if (finding.Status == WcxRegistrationHarvest.ProbeArchitectureMismatch) report.ProbeArchitectureMismatch++;
                else if (finding.Status == WcxRegistrationHarvest.CapsMismatch) report.CapsMismatch++;
                else if (finding.Status == WcxRegistrationHarvest.PackageIdentityMismatch) report.PackageIdentityMismatch++;
                else if (finding.Status == WcxRegistrationHarvest.HashMismatch) report.HashMismatch++;
                else if (finding.Status == WcxRegistrationHarvest.SourceUnavailable) report.SourceUnavailable++;
                else if (finding.Status == WcxRegistrationHarvest.UnsupportedArchive) report.UnsupportedArchive++;
                else report.ProbeFailed++;
            }
            return report;
        }
    }
}
