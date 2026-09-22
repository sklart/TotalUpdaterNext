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
            var entry = _catalog.FindByAlias(System.IO.Path.GetFileName(plugin.PrimaryPath));
            if (entry == null) return Candidate(plugin, plugin.HasVersionConflict ? UpdateState.LocalVersionConflict : UpdateState.PluginNotRecognized, null, null, "");
            var provider = _providers.FirstOrDefault(p => p.CanHandle(entry.Source));
            if (provider == null) return Candidate(plugin, plugin.HasVersionConflict ? UpdateState.LocalVersionConflict : UpdateState.SourceUnavailable, null, null, "");
            try
            {
                var release = await provider.GetLatestReleaseAsync(entry.Source, cancellationToken).ConfigureAwait(false);
                if (release == null || !release.Version.IsKnown) return Candidate(plugin, plugin.HasVersionConflict ? UpdateState.LocalVersionConflict : UpdateState.SourceUnavailable, null, null, "");
                if (plugin.HasVersionConflict) return Candidate(plugin, UpdateState.LocalVersionConflict, release, provider.Name, "");
                var comparison = plugin.LocalVersion.ParsedValue.CompareTo(release.Version);
                var state = comparison == VersionComparison.Less ? UpdateState.UpdateAvailable : comparison == VersionComparison.Greater ? UpdateState.DevelopmentVersion :
                    comparison == VersionComparison.Equal ? UpdateState.UpToDate : UpdateState.VersionComparisonUnknown;
                return Candidate(plugin, state, release, provider.Name, "");
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex) { return Candidate(plugin, plugin.HasVersionConflict ? UpdateState.LocalVersionConflict : UpdateState.SourceUnavailable, null, provider.Name, ex.Message); }
        }

        private static UpdateCandidate Candidate(InstalledPlugin plugin, UpdateState state, RemoteRelease release, string source, string details)
        {
            return new UpdateCandidate { Plugin = plugin, InstalledVersion = plugin.LocalVersion.ParsedValue, AvailableVersion = release == null ? VersionValue.Unknown : release.Version,
                State = state, SourceName = source, SourceUrl = release == null ? null : release.SourceUrl, DownloadUrl = release == null ? null : release.DownloadUrl, Details = details };
        }
    }
}
