using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using TotalUpdater.Next.Infrastructure.Installation;

namespace TotalUpdater.Next.Catalog
{
    [DataContract]
    public sealed class WcxRegistrationHarvestFinding
    {
        [DataMember(Name = "id")] public string Id { get; set; }
        [DataMember(Name = "status")] public string Status { get; set; }
        [DataMember(Name = "detail")] public string Detail { get; set; }
        [DataMember(Name = "evidence")] public WcxRegistrationEvidence Evidence { get; set; }
    }

    public static class WcxRegistrationHarvest
    {
        public const string Downloadable = "Downloadable";
        public const string VerifiedRegistration = "VerifiedRegistration";
        public const string MissingPackage = "MissingPackage";
        public const string MissingPluginst = "MissingPluginst";
        public const string MissingDefaultExtension = "MissingDefaultExtension";
        public const string AmbiguousBinary = "AmbiguousBinary";
        public const string ProbeFailed = "ProbeFailed";
        public const string ProbeTimeout = "ProbeTimeout";
        public const string ProbeArchitectureMismatch = "ProbeArchitectureMismatch";
        public const string CapsMismatch = "CapsMismatch";
        public const string PackageIdentityMismatch = "PackageIdentityMismatch";
        public const string HashMismatch = "HashMismatch";
        public const string SourceUnavailable = "SourceUnavailable";
        public const string UnsupportedArchive = "UnsupportedArchive";

        public static IList<WcxRegistrationHarvestFinding> Read(string path)
        {
            if (String.IsNullOrWhiteSpace(path) || !File.Exists(path)) return new List<WcxRegistrationHarvestFinding>();
            try { using (var stream = File.OpenRead(path)) return (IList<WcxRegistrationHarvestFinding>)new DataContractJsonSerializer(typeof(List<WcxRegistrationHarvestFinding>)).ReadObject(stream); }
            catch { return new List<WcxRegistrationHarvestFinding>(); }
        }

        public static void Write(string path, IEnumerable<WcxRegistrationHarvestFinding> findings)
        {
            if (String.IsNullOrWhiteSpace(path)) throw new ArgumentException("Evidence path is required.", nameof(path));
            var directory = Path.GetDirectoryName(Path.GetFullPath(path));
            Directory.CreateDirectory(directory);
            var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                using (var stream = File.Create(temporary)) new DataContractJsonSerializer(typeof(List<WcxRegistrationHarvestFinding>)).WriteObject(stream,
                    (findings ?? Enumerable.Empty<WcxRegistrationHarvestFinding>()).Where(x => x != null).OrderBy(x => x.Id ?? "", StringComparer.OrdinalIgnoreCase).ToList());
                if (File.Exists(path)) AtomicFile.Replace(temporary, path); else File.Move(temporary, path);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }

        public static bool IsKnownStatus(string status)
        {
            return new[] { Downloadable, VerifiedRegistration, MissingPackage, MissingPluginst, MissingDefaultExtension, AmbiguousBinary, ProbeFailed, ProbeTimeout, ProbeArchitectureMismatch, CapsMismatch, PackageIdentityMismatch, HashMismatch, SourceUnavailable, UnsupportedArchive }.Contains(status ?? "", StringComparer.Ordinal);
        }

        public static bool IsValidVerifiedFinding(WcxRegistrationHarvestFinding finding)
        {
            if (finding == null || !String.Equals(finding.Status, VerifiedRegistration, StringComparison.Ordinal) || String.IsNullOrWhiteSpace(finding.Id) || finding.Evidence == null || !finding.Evidence.IsComplete) return false;
            var extensions = finding.Evidence.Extensions;
            return extensions != null && extensions.Count > 0 && extensions.All(x => !String.IsNullOrWhiteSpace(x) && x == x.Trim().TrimStart('.') && x.IndexOfAny(new[] { '=', ',', '[', ']', '\r', '\n' }) < 0) && extensions.Distinct(StringComparer.OrdinalIgnoreCase).Count() == extensions.Count;
        }
    }
}
