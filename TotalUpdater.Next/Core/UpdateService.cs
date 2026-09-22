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
        public UpdateService(CatalogService catalog, IEnumerable<IUpdateSourceProvider> providers) { _catalog = catalog; _providers = providers.ToList(); }

        public async Task<UpdateCandidate> CheckAsync(InstalledPlugin plugin, CancellationToken cancellationToken)
        {
            var entry = _catalog.FindById(plugin.Identity == null ? null : plugin.Identity.Id);
            if (entry == null) return Candidate(plugin, plugin.HasVersionConflict ? UpdateState.LocalVersionConflict : UpdateState.PluginNotRecognized, null, null, "");
            var details = new List<string>();
            foreach (var source in entry.Sources.OrderByDescending(x => x.Priority))
            {
                var provider = _providers.FirstOrDefault(p => p.CanHandle(source));
                if (provider == null) { details.Add(source.Provider + ": provider не найден"); continue; }
                SourceQueryResult result;
                try { result = await provider.QueryAsync(source, cancellationToken).ConfigureAwait(false); }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex) { details.Add(provider.Name + ": " + ex.Message); continue; }
                if (result == null || result.Status != SourceQueryStatus.Success || result.Release == null || !result.Release.Version.IsKnown)
                {
                    details.Add(provider.Name + ": " + (result == null ? "пустой ответ" : result.Details)); continue;
                }
                if (plugin.HasVersionConflict) return Candidate(plugin, UpdateState.LocalVersionConflict, result.Release, provider.Name, result.Details);
                var comparison = plugin.LocalVersion.ParsedValue.CompareTo(result.Release.Version);
                var state = comparison == VersionComparison.Less ? UpdateState.UpdateAvailable : comparison == VersionComparison.Greater ? UpdateState.DevelopmentVersion : comparison == VersionComparison.Equal ? UpdateState.UpToDate : UpdateState.VersionComparisonUnknown;
                var candidate = Candidate(plugin, state, result.Release, provider.Name, result.Details);
                if (state == UpdateState.UpdateAvailable) { string packageDetails; candidate.DownloadUrl = SelectPackage(plugin, result.Release, out packageDetails); candidate.Details = packageDetails; }
                return candidate;
            }
            return Candidate(plugin, plugin.HasVersionConflict ? UpdateState.LocalVersionConflict : UpdateState.SourceUnavailable, null, null, String.Join(" · ", details));
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
