using System;
using System.Collections.Generic;
using System.Linq;

namespace TotalUpdater.Next.Catalog
{
    public sealed class WcxRegistrationAuditReport
    {
        public int WcxTotal { get; set; }
        public int VerifiedRegistration { get; set; }
        public int MissingPackage { get; set; }
        public int MissingPluginst { get; set; }
        public int MissingDefaultExtension { get; set; }
        public int ProbeFailed { get; set; }
        public int ArchitectureMismatch { get; set; }
        public int HashMismatch { get; set; }
        public int AmbiguousBinary { get; set; }
    }

    public static class WcxRegistrationAudit
    {
        public static WcxRegistrationAuditReport Audit(IEnumerable<PluginCatalogEntry> entries)
        {
            var report = new WcxRegistrationAuditReport();
            foreach (var entry in (entries ?? Enumerable.Empty<PluginCatalogEntry>()).Where(x => x.PluginType == Core.PluginType.Wcx))
            {
                report.WcxTotal++;
                if (entry.HasVerifiedWcxRegistration) { report.VerifiedRegistration++; continue; }
                if (entry.Sources == null || !entry.Sources.Any(x => x != null && x.PurposeValue != SourcePurpose.Metadata && !String.IsNullOrWhiteSpace(x.PackageUrl ?? x.DownloadUrl ?? x.EphemeralVerifiedPackageUrl))) report.MissingPackage++;
                else if (entry.WcxRegistration == null) report.MissingPluginst++;
                else if (entry.WcxRegistration.Extensions == null || entry.WcxRegistration.Extensions.Count == 0) report.MissingDefaultExtension++;
                else if (String.IsNullOrWhiteSpace(entry.WcxRegistration.BinarySha256) || String.IsNullOrWhiteSpace(entry.WcxRegistration.PackageSha256)) report.HashMismatch++;
                else report.ProbeFailed++;
            }
            return report;
        }
    }
}
