using System;
using System.Collections.Generic;
using System.Linq;
using TotalUpdater.Next.Catalog;
using TotalUpdater.Next.Infrastructure.Installation;

namespace TotalUpdater.Next.Core.Installation
{
    public static class WcxRegistrationEvidenceBuilder
    {
        public static WcxRegistrationHarvestFinding FromInspectedPackage(PluginCatalogEntry entry, PackageInspection package, WcxProbeResult x86, WcxProbeResult x64, string source, string version = null, string sourceId = null, string sourceFingerprint = null)
        {
            if (entry == null || entry.PluginType != Core.PluginType.Wcx || package == null) return Finding(entry, WcxRegistrationHarvest.MissingPackage);
            if (!String.Equals(package.Type, "wcx", StringComparison.OrdinalIgnoreCase)) return Finding(entry, WcxRegistrationHarvest.PackageIdentityMismatch);
            if (!PackageIdentityVerifier.ContainsCatalogAlias(package.PackagePath, entry)) return Finding(entry, WcxRegistrationHarvest.PackageIdentityMismatch);
            if (!WcxRegistration.TryNormalizeExtensions(package.DefaultExtension, out var extensions)) return Finding(entry, WcxRegistrationHarvest.MissingDefaultExtension);
            var families = new[] { package.File }.Concat(entry.Aliases ?? Enumerable.Empty<string>())
                .Where(x => !String.IsNullOrWhiteSpace(x)).Select(WcxRegistration.FamilyName).Where(x => !String.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            // pluginst.inf file has priority.  Catalog aliases are a fallback
            // only when it is omitted, never permission to select a random WCX
            // from a nested dependency package.
            if (!String.IsNullOrWhiteSpace(package.File)) families = new List<string> { WcxRegistration.FamilyName(package.File) };
            if (families.Count != 1) return Finding(entry, WcxRegistrationHarvest.AmbiguousBinary);
            var family = families[0];
            var x86Files = package.Files.Where(x => WcxProbeRunner.ExpectedArchitecture(x.RelativePath) == "x86" && String.Equals(WcxRegistration.FamilyName(x.RelativePath), family, StringComparison.OrdinalIgnoreCase)).ToList();
            var x64Files = package.Files.Where(x => WcxProbeRunner.ExpectedArchitecture(x.RelativePath) == "x64" && String.Equals(WcxRegistration.FamilyName(x.RelativePath), family, StringComparison.OrdinalIgnoreCase)).ToList();
            // Selecting an arbitrary binary would turn a harvest finding into
            // untrustworthy installation evidence. A family must identify one
            // binary per architecture.
            if (x86Files.Count > 1 || x64Files.Count > 1 || (x86Files.Count == 0 && x64Files.Count == 0)) return Finding(entry, WcxRegistrationHarvest.AmbiguousBinary);
            var x86File = x86Files.SingleOrDefault();
            var x64File = x64Files.SingleOrDefault();
            var x86Failure = ProbeFailure(x86File, x86, "x86");
            if (x86Failure != null) return Finding(entry, x86Failure);
            var x64Failure = ProbeFailure(x64File, x64, "x64");
            if (x64Failure != null) return Finding(entry, x64Failure);
            if (x86File != null && x64File != null && x86.Caps != x64.Caps) return Finding(entry, WcxRegistrationHarvest.CapsMismatch);
            return new WcxRegistrationHarvestFinding { Id = entry.Id, Status = WcxRegistrationHarvest.VerifiedRegistration, Evidence = new WcxRegistrationEvidence { PackageSha256 = package.PackageSha256, X86BinarySha256 = x86File == null ? null : x86File.Sha256, X64BinarySha256 = x64File == null ? null : x64File.Sha256, PackerCaps = x86File != null ? x86.Caps : x64.Caps, Extensions = extensions.ToList(), VerifiedUtc = DateTime.UtcNow, Source = source ?? "", PackageUrl = source ?? "", Version = version, SourceId = sourceId, SourceFingerprint = sourceFingerprint } };
        }
        private static string ProbeFailure(PackageFile file, WcxProbeResult result, string expectedArchitecture)
        {
            if (file == null) return null;
            if (result != null && result.Success && String.Equals(result.Architecture, expectedArchitecture, StringComparison.Ordinal)) return null;
            var error = result == null ? "" : result.Error ?? "";
            if (error.IndexOf("timeout", StringComparison.OrdinalIgnoreCase) >= 0) return WcxRegistrationHarvest.ProbeTimeout;
            if (error.IndexOf("architecture", StringComparison.OrdinalIgnoreCase) >= 0 || (result != null && !String.IsNullOrWhiteSpace(result.Architecture) && !String.Equals(result.Architecture, expectedArchitecture, StringComparison.Ordinal))) return WcxRegistrationHarvest.ProbeArchitectureMismatch;
            return WcxRegistrationHarvest.ProbeFailed;
        }
        private static WcxRegistrationHarvestFinding Finding(PluginCatalogEntry entry, string status) { return new WcxRegistrationHarvestFinding { Id = entry == null ? "" : entry.Id, Status = status }; }
    }
}
