using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TotalUpdater.Next.Catalog;
using TotalUpdater.Next.Core.Versions;
using TotalUpdater.Next.Sources;

namespace TotalUpdater.Next.Core.Installation
{
    public sealed class CatalogInstallCandidate
    {
        public PluginCatalogEntry Entry { get; set; }
        public VersionValue Version { get; set; } = VersionValue.Unknown;
        public Uri DownloadUrl { get; set; }
        public string SourceName { get; set; } = "";
        public RemoteVersionObservation CanonicalSource { get; set; }
        public RemoteVersionObservation DownloadSource { get; set; }
        public IList<RemoteVersionObservation> Observations { get; set; } = new List<RemoteVersionObservation>();
        public bool AuthorityConflict { get; set; }
        public string Details { get; set; } = "";
    }

    public sealed class CatalogInstallService
    {
        private readonly IList<IUpdateSourceProvider> _providers;
        private readonly SourceAuthorityResolver _authority = new SourceAuthorityResolver();
        public CatalogInstallService(IEnumerable<IUpdateSourceProvider> providers) { _providers = providers.ToList(); }
        public async Task<CatalogInstallCandidate> CheckAsync(PluginCatalogEntry entry, CancellationToken cancellationToken)
        {
            if (entry == null) throw new ArgumentNullException(nameof(entry));
            var observations = new List<RemoteVersionObservation>();
            foreach (var source in entry.Sources.OrderByDescending(x => x.Priority))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (source.AuthorityValue == SourceAuthority.ManualOverride)
                {
                    var manual = source.ManualOverride;
                    observations.Add(new RemoteVersionObservation { Source = source, ProviderName = "Manual override", Authority = source.AuthorityValue,
                        Purpose = source.PurposeValue, Status = SourceQueryStatus.Success,
                        Release = new RemoteRelease { VersionText = manual.Version, Version = VersionValue.Parse(manual.Version), SourceUrl = new Uri(manual.EvidenceUrl) } });
                    continue;
                }
                var provider = _providers.FirstOrDefault(x => x.CanHandle(source));
                if (provider == null) { observations.Add(new RemoteVersionObservation { Source = source, Authority = source.AuthorityValue, Purpose = source.PurposeValue, Status = SourceQueryStatus.Unavailable, Details = "Источник не найден" }); continue; }
                try
                {
                    var result = await provider.QueryAsync(source, cancellationToken).ConfigureAwait(false);
                    observations.Add(new RemoteVersionObservation { Source = source, ProviderName = provider.Name, Authority = source.AuthorityValue,
                        Purpose = source.PurposeValue, Status = result == null ? SourceQueryStatus.InvalidResponse : result.Status,
                        Release = result == null ? null : result.Release, Details = result == null ? "Пустой ответ" : result.Details });
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex) { observations.Add(new RemoteVersionObservation { Source = source, ProviderName = provider.Name, Authority = source.AuthorityValue, Purpose = source.PurposeValue, Status = SourceQueryStatus.Unavailable, Details = ex.Message }); }
            }
            var resolution = _authority.Resolve(observations);
            var candidate = new CatalogInstallCandidate { Entry = entry, Observations = observations, CanonicalSource = resolution.Canonical,
                AuthorityConflict = resolution.HasConflict, Version = resolution.Canonical == null ? VersionValue.Unknown : resolution.Canonical.Release.Version,
                SourceName = resolution.Canonical == null ? "" : resolution.Canonical.ProviderName };
            if (resolution.Canonical == null) { candidate.Details = "Версия не найдена."; return candidate; }
            if (resolution.HasConflict) { candidate.Details = "Конфликт источников одинакового доверия."; return candidate; }
            var eligible = observations.Where(x => x.Status == SourceQueryStatus.Success && x.Purpose != SourcePurpose.Metadata && x.Release != null &&
                x.Release.Version.CompareTo(candidate.Version) == VersionComparison.Equal && x.Release.Packages != null)
                .OrderByDescending(x => x == resolution.Canonical).ThenByDescending(x => x.Authority).ThenByDescending(x => x.Source == null ? 0 : x.Source.Priority);
            foreach (var observation in eligible)
            {
                var packages = observation.Release.Packages.Where(x => x.Url != null).ToList();
                var combined = packages.Where(x => x.Architecture == RemotePackageArchitecture.Combined).ToList();
                var selected = combined.Count == 1 ? combined[0] : combined.Count == 0 && packages.Count == 1 ? packages[0] : null;
                if (selected == null) continue;
                candidate.DownloadUrl = selected.Url; candidate.DownloadSource = observation; return candidate;
            }
            candidate.Details = "Нет однозначного пакета для новой установки.";
            return candidate;
        }
    }
}
