using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TotalUpdater.Next.Catalog;
using TotalUpdater.Next.Core.Versions;
using TotalUpdater.Next.Sources;
using TotalUpdater.Next.TotalCommander;
using TotalUpdater.Next.Settings;

namespace TotalUpdater.Next.Core
{
    public enum SourceQueryMode { Normal, AuditAllSources }

    public sealed class UpdateService
    {
        private readonly CatalogService _catalog;
        private readonly IList<IUpdateSourceProvider> _providers;
        private readonly IRemoteCatalogLookup _remoteLookup;
        private readonly SourceAuthorityResolver _authority = new SourceAuthorityResolver();
        private readonly AcceptedVersionService _accepted;
        public UpdateService(CatalogService catalog, IEnumerable<IUpdateSourceProvider> providers, IRemoteCatalogLookup remoteLookup = null, AcceptedVersionService accepted = null) { _catalog = catalog; _providers = providers.ToList(); _remoteLookup = remoteLookup; _accepted = accepted; }

        public Task<UpdateCandidate> CheckAsync(InstalledPlugin plugin, CancellationToken cancellationToken)
        {
            return CheckAsync(plugin, cancellationToken, null, SourceQueryMode.Normal);
        }

        public Task<UpdateCandidate> CheckAsync(InstalledPlugin plugin, CancellationToken cancellationToken, SourceResponseCache sourceCache)
        {
            return CheckAsync(plugin, cancellationToken, sourceCache, SourceQueryMode.Normal);
        }

        public async Task<UpdateCandidate> CheckAsync(InstalledPlugin plugin, CancellationToken cancellationToken, SourceResponseCache sourceCache, SourceQueryMode mode)
        {
            var entry = _catalog.FindById(plugin.Identity == null ? null : plugin.Identity.Id);
            if (entry == null && _remoteLookup != null)
            {
                var match = await _remoteLookup.FindAsync(plugin, sourceCache, cancellationToken).ConfigureAwait(false);
                plugin.CatalogMatchKind = match.Kind;
                if (match.Kind == CatalogMatchKind.Ambiguous)
                    return Candidate(plugin, plugin.HasVersionConflict ? UpdateState.LocalVersionConflict : UpdateState.CatalogAmbiguous,
                        null, null, "Найдено несколько возможных правил каталога");
                entry = match.Entry;
            }
            if (entry == null) return Candidate(plugin, plugin.HasVersionConflict ? UpdateState.LocalVersionConflict : UpdateState.PluginNotRecognized, null, null, "");
            var details = new List<string>(); var observations = new List<RemoteVersionObservation>();
            var ordered = entry.Sources.OrderByDescending(x => x.Priority).ToList();
            var metadata = mode == SourceQueryMode.AuditAllSources ? ordered : ordered.Where(x => !IsDetailSource(x)).ToList();
            foreach (var source in metadata)
                await QuerySourceAsync(source, sourceCache, cancellationToken, details, observations).ConfigureAwait(false);
            if (mode == SourceQueryMode.Normal)
            {
                var preliminary = _authority.Resolve(observations);
                var needsDetail = preliminary.Canonical == null || (!plugin.HasVersionConflict &&
                    plugin.LocalVersion.ParsedValue.CompareTo(preliminary.Canonical.Release.Version) == VersionComparison.Less &&
                    !preliminary.HasConflict && !HasSelectablePackage(plugin, observations, preliminary.Canonical));
                if (needsDetail)
                    foreach (var source in ordered.Where(IsDetailSource))
                        await QuerySourceAsync(source, sourceCache, cancellationToken, details, observations).ConfigureAwait(false);
            }
            var resolution = _authority.Resolve(observations); var canonical = resolution.Canonical;
            if (canonical == null) { var unavailable = Candidate(plugin, plugin.HasVersionConflict ? UpdateState.LocalVersionConflict : UpdateState.SourceUnavailable, null, null, String.Join(" · ", details)); unavailable.Observations = observations; return unavailable; }
            if (plugin.HasVersionConflict) { var conflict = Candidate(plugin, UpdateState.LocalVersionConflict, canonical.Release, canonical.ProviderName, canonical.Details); ApplyProvenance(conflict, observations, resolution); return conflict; }
            var comparison = plugin.LocalVersion.ParsedValue.CompareTo(canonical.Release.Version);
            var state = comparison == VersionComparison.Less ? UpdateState.UpdateAvailable : comparison == VersionComparison.Greater ?
                (entry.LocalAheadPolicy == LocalAheadPolicy.SourceMayLag ? UpdateState.SourceOutdated : entry.LocalAheadPolicy == LocalAheadPolicy.Development ? UpdateState.DevelopmentVersion : UpdateState.LocalAheadUnknown) : comparison == VersionComparison.Equal ? UpdateState.UpToDate : UpdateState.VersionComparisonUnknown;
            var candidate = Candidate(plugin, state, canonical.Release, canonical.ProviderName, canonical.Details); ApplyProvenance(candidate, observations, resolution);
            if (state == UpdateState.UpdateAvailable && _accepted != null && _accepted.IsAccepted(plugin, canonical.Release.Version)) { candidate.State = UpdateState.UpToDate; candidate.Details = "Установленная версия отмечена как актуальная."; return candidate; }
            if (state == UpdateState.UpdateAvailable && resolution.HasConflict)
            { candidate.PackageAvailability = PackageAvailability.Ambiguous; candidate.Details = "Конфликт источников одного уровня доверия; загрузка отключена."; }
            else if (state == UpdateState.UpdateAvailable)
            {
                if (!entry.AllowsAutomaticInstall)
                {
                    candidate.PackageAvailability = PackageAvailability.MetadataOnly;
                    candidate.Details = entry.IdentityEvidence == IdentityEvidence.OfficialRegistrationName ? "Подтверждено только имя регистрации; автоматическая установка отключена." : "Версия известна, пакет не найден; автоматическая установка отключена.";
                    return candidate;
                }
                var download = SelectDownloadObservation(plugin, observations, canonical, out var packageUrl, out var packageDetails);
                if (download != null) { candidate.DownloadUrl = packageUrl; candidate.DownloadSource = download; candidate.PackageAvailability =
                    IsVerifiedPackageEvidence(download.Source, packageUrl) ? PackageAvailability.Verified : PackageAvailability.Unverified; candidate.Details = packageDetails; }
                else { candidate.PackageAvailability = PackageAvailability.MetadataOnly; candidate.Details = String.IsNullOrWhiteSpace(packageDetails) ? "Версия известна, пакет не найден" : packageDetails; }
            }
            return candidate;
        }

        private static bool IsDetailSource(CatalogSource source)
        {
            return source.Provider.Equals("totalcmd.net", StringComparison.OrdinalIgnoreCase);
        }

        private static bool HasSelectablePackage(InstalledPlugin plugin, IList<RemoteVersionObservation> observations, RemoteVersionObservation canonical)
        {
            Uri url; string details;
            return SelectDownloadObservation(plugin, observations, canonical, out url, out details) != null;
        }

        private async Task QuerySourceAsync(CatalogSource source, SourceResponseCache sourceCache, CancellationToken cancellationToken, IList<string> details, IList<RemoteVersionObservation> observations)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (source.AuthorityValue == SourceAuthority.ManualOverride)
            {
                var manual = source.ManualOverride;
                var manualVersion = manual == null ? VersionValue.Unknown : VersionValue.Parse(manual.Version);
                Uri evidence;
                if (manualVersion.IsKnown && manual != null && Uri.TryCreate(manual.EvidenceUrl, UriKind.Absolute, out evidence))
                    observations.Add(new RemoteVersionObservation { Source = source, ProviderName = "Manual override", Authority = SourceAuthority.ManualOverride, Purpose = SourcePurpose.Metadata, Status = SourceQueryStatus.Success, Release = new RemoteRelease { VersionText = manual.Version, Version = manualVersion, SourceUrl = evidence, Packages = new List<RemotePackage>() } });
                else
                    observations.Add(new RemoteVersionObservation { Source = source, ProviderName = "Manual override", Authority = SourceAuthority.ManualOverride, Purpose = SourcePurpose.Metadata, Status = SourceQueryStatus.InvalidResponse, Details = "Некорректный ManualOverride." });
                return;
            }
            var provider = _providers.FirstOrDefault(p => p.CanHandle(source));
            if (provider == null) { details.Add(source.Provider + ": provider не найден"); observations.Add(new RemoteVersionObservation { Source = source, Authority = source.AuthorityValue, Purpose = source.PurposeValue, Status = SourceQueryStatus.Unavailable, Details = "provider не найден" }); return; }
            SourceQueryResult result;
            try
            {
                var cachedProvider = provider as ICachedUpdateSourceProvider;
                result = cachedProvider == null || sourceCache == null
                    ? await provider.QueryAsync(source, cancellationToken).ConfigureAwait(false)
                    : await cachedProvider.QueryAsync(source, sourceCache, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { if (cancellationToken.IsCancellationRequested) throw; details.Add(provider.Name + ": timeout"); observations.Add(new RemoteVersionObservation { Source = source, ProviderName = provider.Name, Authority = source.AuthorityValue, Purpose = source.PurposeValue, Status = SourceQueryStatus.Unavailable, Details = "Превышено время ожидания источника." }); return; }
            catch (Exception ex) { details.Add(provider.Name + ": " + ex.Message); observations.Add(new RemoteVersionObservation { Source = source, ProviderName = provider.Name, Authority = source.AuthorityValue, Purpose = source.PurposeValue, Status = SourceQueryStatus.Unavailable, Details = ex.Message }); return; }
            if (result != null && result.Release != null && String.Equals(source.VersionTransform, "Total7zipPe", StringComparison.OrdinalIgnoreCase))
            {
                var normalized = Total7zipVersionStrategy.FromPeFileVersion(result.Release.VersionText).ParsedValue;
                if (normalized.IsKnown) result.Release.Version = normalized;
            }
            observations.Add(new RemoteVersionObservation { Source = source, ProviderName = provider.Name, Authority = source.AuthorityValue, Purpose = source.PurposeValue, Status = result == null ? SourceQueryStatus.InvalidResponse : result.Status, Release = result == null ? null : result.Release, Details = result == null ? "пустой ответ" : result.Details,
                IsCached = result != null && result.IsCached, CachedAt = result == null ? null : result.CachedAt, IsStale = result != null && result.IsStale });
            if (result == null || result.Status != SourceQueryStatus.Success || result.Release == null || !result.Release.Version.IsKnown)
                details.Add(provider.Name + ": " + (result == null ? "пустой ответ" : result.Details));
        }

        private static void ApplyProvenance(UpdateCandidate candidate, IList<RemoteVersionObservation> observations, AuthorityResolution resolution)
        { candidate.Observations = observations; candidate.CanonicalVersionSource = resolution.Canonical; candidate.HasSourceDisagreement = resolution.HasDisagreement; candidate.AuthorityConflict = resolution.HasConflict;
            if (resolution.Canonical != null) { candidate.IsCached = resolution.Canonical.IsCached; candidate.CachedAt = resolution.Canonical.CachedAt; candidate.IsStale = resolution.Canonical.IsStale; } }

        private static RemoteVersionObservation SelectDownloadObservation(InstalledPlugin plugin, IList<RemoteVersionObservation> observations, RemoteVersionObservation canonical, out Uri packageUrl, out string details)
        {
            packageUrl = null; details = "";
            var eligible = observations.Where(x => !x.IsCached && x.Release != null && x.Purpose != SourcePurpose.Metadata && x.Release.Version.CompareTo(canonical.Release.Version) == VersionComparison.Equal && x.Release.Packages != null && x.Release.Packages.Count > 0)
                .OrderByDescending(x => x.Authority).ThenByDescending(x => x.Source == null ? 0 : x.Source.Priority).ToList();
            // Canonical observation has precedence when it can provide a safe package.
            if (!canonical.IsCached && canonical.Release.Packages != null && canonical.Release.Packages.Count > 0 && canonical.Purpose != SourcePurpose.Metadata)
                eligible.Remove(canonical);
            else
                canonical = null;
            if (canonical != null)
            {
                packageUrl = SelectPackage(plugin, canonical.Release, out details);
                if (packageUrl != null) return canonical;
            }
            foreach (var item in eligible)
            {
                packageUrl = SelectPackage(plugin, item.Release, out details);
                if (packageUrl != null) return item;
            }
            if (eligible.Count > 0 && String.IsNullOrWhiteSpace(details)) details = "Нет безопасно выбираемого пакета загрузки.";
            return null;
        }

        private static bool IsVerifiedPackageEvidence(CatalogSource source, Uri packageUrl)
        {
            if (source == null || packageUrl == null || source.PurposeValue != SourcePurpose.MetadataAndDownload) return false;
            return String.Equals(source.EphemeralVerifiedPackageUrl, packageUrl.AbsoluteUri, StringComparison.OrdinalIgnoreCase) ||
                String.Equals(source.PackageUrl, packageUrl.AbsoluteUri, StringComparison.OrdinalIgnoreCase);
        }

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
