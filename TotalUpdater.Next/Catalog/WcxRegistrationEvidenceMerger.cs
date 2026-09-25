using System;
using System.Collections.Generic;
using System.Linq;

namespace TotalUpdater.Next.Catalog
{
    public enum WcxRegistrationMergeAction { Add, Update, Unchanged, Reject }
    public sealed class WcxRegistrationMergeDecision { public string Id { get; set; } public WcxRegistrationMergeAction Action { get; set; } public string Detail { get; set; } }
    // Pure catalog-level merge used by the maintenance command and fixtures.
    // It deliberately has no knowledge of aliases, sources or release data:
    // the only mutable catalog member is wcxRegistration.
    public static class WcxRegistrationEvidenceMerger
    {
        public static bool TryApply(IList<PluginCatalogEntry> entries, IEnumerable<WcxRegistrationHarvestFinding> findings, out string error)
        {
            error = null;
            if (entries == null) { error = "Catalog entries are required."; return false; }
            var decisions = Plan(entries, findings);
            var rejected = decisions.FirstOrDefault(x => x.Action == WcxRegistrationMergeAction.Reject);
            if (rejected != null) { error = rejected.Detail; return false; }
            var verified = (findings ?? Enumerable.Empty<WcxRegistrationHarvestFinding>()).Where(x => x != null && String.Equals(x.Status, WcxRegistrationHarvest.VerifiedRegistration, StringComparison.Ordinal)).ToList();
            foreach (var finding in verified.Where(x => decisions.Any(d => String.Equals(d.Id, x.Id, StringComparison.OrdinalIgnoreCase) && (d.Action == WcxRegistrationMergeAction.Add || d.Action == WcxRegistrationMergeAction.Update))))
            {
                var entry = entries.First(x => String.Equals(x.Id, finding.Id, StringComparison.OrdinalIgnoreCase));
                entry.WcxRegistration = Copy(finding.Evidence);
            }
            return true;
        }

        public static IList<WcxRegistrationMergeDecision> Plan(IList<PluginCatalogEntry> entries, IEnumerable<WcxRegistrationHarvestFinding> findings)
        {
            var result = new List<WcxRegistrationMergeDecision>();
            var verified = (findings ?? Enumerable.Empty<WcxRegistrationHarvestFinding>()).Where(x => x != null && String.Equals(x.Status, WcxRegistrationHarvest.VerifiedRegistration, StringComparison.Ordinal)).ToList();
            foreach (var duplicate in verified.GroupBy(x => x.Id ?? "", StringComparer.OrdinalIgnoreCase).Where(x => x.Count() != 1)) result.Add(new WcxRegistrationMergeDecision { Id = duplicate.Key, Action = WcxRegistrationMergeAction.Reject, Detail = "Duplicate VerifiedRegistration finding id." });
            foreach (var finding in verified.Where(x => !result.Any(d => String.Equals(d.Id, x.Id, StringComparison.OrdinalIgnoreCase))))
            {
                var entry = entries == null ? null : entries.FirstOrDefault(x => String.Equals(x.Id, finding.Id, StringComparison.OrdinalIgnoreCase));
                if (!WcxRegistrationHarvest.IsValidVerifiedFinding(finding)) result.Add(new WcxRegistrationMergeDecision { Id = finding.Id, Action = WcxRegistrationMergeAction.Reject, Detail = "Invalid VerifiedRegistration evidence." });
                else if (entry == null || entry.PluginType != Core.PluginType.Wcx) result.Add(new WcxRegistrationMergeDecision { Id = finding.Id, Action = WcxRegistrationMergeAction.Reject, Detail = "VerifiedRegistration target is missing or not WCX." });
                else if (Same(entry.WcxRegistration, finding.Evidence)) result.Add(new WcxRegistrationMergeDecision { Id = finding.Id, Action = WcxRegistrationMergeAction.Unchanged, Detail = "Evidence already matches." });
                else result.Add(new WcxRegistrationMergeDecision { Id = finding.Id, Action = entry.WcxRegistration == null ? WcxRegistrationMergeAction.Add : WcxRegistrationMergeAction.Update, Detail = "Valid hash-bound evidence." });
            }
            return result.OrderBy(x => x.Id ?? "", StringComparer.OrdinalIgnoreCase).ToList();
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
                Source = source.Source,
                Version = source.Version,
                SourceId = source.SourceId,
                PackageUrl = source.PackageUrl,
                SourceFingerprint = source.SourceFingerprint
            };
        }
        private static bool Same(WcxRegistrationEvidence left, WcxRegistrationEvidence right)
        {
            if (left == null || right == null) return left == right;
            return String.Equals(left.PackageSha256, right.PackageSha256, StringComparison.OrdinalIgnoreCase) && String.Equals(left.X86BinarySha256, right.X86BinarySha256, StringComparison.OrdinalIgnoreCase) &&
                String.Equals(left.X64BinarySha256, right.X64BinarySha256, StringComparison.OrdinalIgnoreCase) && left.PackerCaps == right.PackerCaps && WcxRegistrationEvidence.IsSha(left.PackageSha256) &&
                String.Equals(left.Source, right.Source, StringComparison.Ordinal) &&
                left.Extensions.SequenceEqual(right.Extensions, StringComparer.OrdinalIgnoreCase);
        }
    }
}
