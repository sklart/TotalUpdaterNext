using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TotalUpdater.Next.Catalog;
using TotalUpdater.Next.Core.Versions;
using TotalUpdater.Next.Sources;

namespace TotalUpdater.Next.Core
{
    public sealed class UpdateService
    {
        private readonly CatalogService _catalog;
        private readonly IList<IUpdateSourceProvider> _providers;
        private readonly SourceAuthorityResolver _authority = new SourceAuthorityResolver();
        public UpdateService(CatalogService catalog, IEnumerable<IUpdateSourceProvider> providers) { _catalog = catalog; _providers = providers.ToList(); }

        public Task<UpdateCandidate> CheckAsync(InstalledPlugin plugin, CancellationToken cancellationToken)
        {
            return CheckAsync(plugin, cancellationToken, null);
        }

        public async Task<UpdateCandidate> CheckAsync(InstalledPlugin plugin, CancellationToken cancellationToken, SourceResponseCache sourceCache)
        {
            var entry = _catalog.FindById(plugin.Identity == null ? null : plugin.Identity.Id);
            if (entry == null) return Candidate(plugin, plugin.HasVersionConflict ? UpdateState.LocalVersionConflict : UpdateState.PluginNotRecognized, null, null, "");
            var details = new List<string>(); var observations = new List<RemoteVersionObservation>();
            foreach (var source in entry.Sources.OrderByDescending(x => x.Priority))
            {
                var provider = _providers.FirstOrDefault(p => p.CanHandle(source));
                if (provider == null) { details.Add(source.Provider + ": provider не найден"); observations.Add(new RemoteVersionObservation { Source = source, Authority = source.AuthorityValue, Purpose = source.PurposeValue, Status = SourceQueryStatus.Unavailable, Details = "provider не найден" }); continue; }
                SourceQueryResult result;
                try
                {
                    var cachedProvider = provider as ICachedUpdateSourceProvider;
                    result = cachedProvider == null || sourceCache == null
                        ? await provider.QueryAsync(source, cancellationToken).ConfigureAwait(false)
                        : await cachedProvider.QueryAsync(source, sourceCache, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex) { details.Add(provider.Name + ": " + ex.Message); observations.Add(new RemoteVersionObservation { Source = source, ProviderName = provider.Name, Authority = source.AuthorityValue, Purpose = source.PurposeValue, Status = SourceQueryStatus.Unavailable, Details = ex.Message }); continue; }
                observations.Add(new RemoteVersionObservation { Source = source, ProviderName = provider.Name, Authority = source.AuthorityValue, Purpose = source.PurposeValue, Status = result == null ? SourceQueryStatus.InvalidResponse : result.Status, Release = result == null ? null : result.Release, Details = result == null ? "пустой ответ" : result.Details });
                if (result == null || result.Status != SourceQueryStatus.Success || result.Release == null || !result.Release.Version.IsKnown)
                {
                    details.Add(provider.Name + ": " + (result == null ? "пустой ответ" : result.Details)); continue;
                }
            }
            var resolution = _authority.Resolve(observations); var canonical = resolution.Canonical;
            if (canonical == null) { var unavailable = Candidate(plugin, plugin.HasVersionConflict ? UpdateState.LocalVersionConflict : UpdateState.SourceUnavailable, null, null, String.Join(" · ", details)); unavailable.Observations = observations; return unavailable; }
            if (plugin.HasVersionConflict) { var conflict = Candidate(plugin, UpdateState.LocalVersionConflict, canonical.Release, canonical.ProviderName, canonical.Details); ApplyProvenance(conflict, observations, resolution); return conflict; }
            var comparison = plugin.LocalVersion.ParsedValue.CompareTo(canonical.Release.Version);
            var state = comparison == VersionComparison.Less ? UpdateState.UpdateAvailable : comparison == VersionComparison.Greater ? UpdateState.DevelopmentVersion : comparison == VersionComparison.Equal ? UpdateState.UpToDate : UpdateState.VersionComparisonUnknown;
            var candidate = Candidate(plugin, state, canonical.Release, canonical.ProviderName, canonical.Details); ApplyProvenance(candidate, observations, resolution);
            if (state == UpdateState.UpdateAvailable && !resolution.HasConflict)
            {
                var download = observations.FirstOrDefault(x => x.Release != null && x.Purpose != SourcePurpose.Metadata && x.Release.Version.CompareTo(canonical.Release.Version) == VersionComparison.Equal && x.Release.Packages != null && x.Release.Packages.Count > 0);
                if (download != null) { string packageDetails; candidate.DownloadUrl = SelectPackage(plugin, download.Release, out packageDetails); candidate.DownloadSource = download; candidate.Details = packageDetails; }
            }
            return candidate;
        }

        private static void ApplyProvenance(UpdateCandidate candidate, IList<RemoteVersionObservation> observations, AuthorityResolution resolution)
        { candidate.Observations = observations; candidate.CanonicalVersionSource = resolution.Canonical; candidate.HasSourceDisagreement = resolution.HasDisagreement; candidate.AuthorityConflict = resolution.HasConflict; }

        private static Uri SelectPackage(InstalledPlugin plugin, RemoteRelease release, out string details)
        {
            details = ""; var packages = release.Packages ?? new List<RemotePackage>();
            if (packages.Count == 0) { details = "В источнике нет пакета загрузки."; return null; }
            var hasX86 = (plugin.Architecture & PluginArchitecture.X86) != 0; var hasX64 = (plugin.Architecture & PluginArchitecture.X64) != 0;
            var suitable = packages.Where(x => x.Url != null && (x.Architecture == RemotePackageArchitecture.Combined || x.Architecture == RemotePackageArchitecture.Unknown || (x.Architecture == RemotePackageArchitecture.X86 && hasX86) || (x.Architecture == RemotePackageArchitecture.X64 && hasX64))).ToList();
            if (hasX86 && hasX64)
            {
                var combined = suitable.Where(x => x.Architecture == RemotePackageArchitecture.Combined).ToList();
                if (combined.Count == 1) return combined[0].Url;
                details = "Для x86+x64 установки требуется ровно один Combined-пакет; автоматический выбор отключён."; return null;
            }
            if (hasX86 && !hasX64 && suitable.Count(x => x.Architecture == RemotePackageArchitecture.X86) == 1) return suitable.Single(x => x.Architecture == RemotePackageArchitecture.X86).Url;
            if (hasX64 && !hasX86 && suitable.Count(x => x.Architecture == RemotePackageArchitecture.X64) == 1) return suitable.Single(x => x.Architecture == RemotePackageArchitecture.X64).Url;
            if (!hasX86 || !hasX64) { var combined = suitable.Where(x => x.Architecture == RemotePackageArchitecture.Combined).ToList(); if (combined.Count == 1) return combined[0].Url; }
            if (suitable.Count == 1) return suitable[0].Url;
            details = suitable.Count == 0 ? "Нет подходящего пакета." : "Выбор пакета неоднозначен."; return null;
        }

        private static UpdateCandidate Candidate(InstalledPlugin plugin, UpdateState state, RemoteRelease release, string source, string details)
        {
            return new UpdateCandidate { Plugin = plugin, InstalledVersion = plugin.LocalVersion.ParsedValue, AvailableVersion = release == null ? VersionValue.Unknown : release.Version,
                State = state, SourceName = source, SourceUrl = release == null ? null : release.SourceUrl, Details = details };
        }
    }
}
