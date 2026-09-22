using System;
using System.Collections.Generic;
using System.Linq;
using TotalUpdater.Next.Catalog;
using TotalUpdater.Next.Core.Versions;
using TotalUpdater.Next.Sources;

namespace TotalUpdater.Next.Core
{
    public sealed class RemoteVersionObservation
    {
        public CatalogSource Source { get; set; }
        public string ProviderName { get; set; } = "";
        public SourceAuthority Authority { get; set; }
        public SourcePurpose Purpose { get; set; }
        public SourceQueryStatus Status { get; set; }
        public RemoteRelease Release { get; set; }
        public string Details { get; set; } = "";
    }
    public sealed class AuthorityResolution
    {
        public RemoteVersionObservation Canonical { get; set; }
        public bool HasDisagreement { get; set; }
        public bool HasConflict { get; set; }
    }
    public sealed class SourceAuthorityResolver
    {
        public AuthorityResolution Resolve(IEnumerable<RemoteVersionObservation> observations)
        {
            var valid = (observations ?? Enumerable.Empty<RemoteVersionObservation>()).Where(x => x.Purpose != SourcePurpose.Download && x.Status == SourceQueryStatus.Success && x.Release != null && x.Release.Version.IsKnown).ToList();
            if (valid.Count == 0) return new AuthorityResolution();
            var tier = valid.Max(x => (int)x.Authority); var top = valid.Where(x => (int)x.Authority == tier).ToList();
            var canonical = top[0];
            foreach (var item in top.Skip(1))
            {
                var comparison = canonical.Release.Version.CompareTo(item.Release.Version);
                if (comparison == VersionComparison.Unknown) return new AuthorityResolution { Canonical = canonical, HasDisagreement = true, HasConflict = true };
                if (comparison == VersionComparison.Less) canonical = item;
            }
            var disagreement = valid.Any(x => x.Release.Version.CompareTo(canonical.Release.Version) != VersionComparison.Equal);
            return new AuthorityResolution { Canonical = canonical, HasDisagreement = disagreement };
        }
    }
}
