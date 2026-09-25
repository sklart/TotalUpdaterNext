using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TotalUpdater.Next.Catalog;
using TotalUpdater.Next.Infrastructure.Installation;

namespace TotalUpdater.Next.Core.Installation
{
    // The operational composition is injected so the same full workflow can
    // be exercised with local ZIP fixtures and without network access.
    public sealed class WcxRegistrationHarvester
    {
        private readonly Func<PluginCatalogEntry, CancellationToken, Task<CatalogInstallCandidate>> _resolveCandidate;
        private readonly Func<Uri, CancellationToken, Task<string>> _download;
        private readonly Func<string, WcxProbeResult> _probe;
        private readonly PackageInspector _inspector;

        public WcxRegistrationHarvester(
            Func<PluginCatalogEntry, CancellationToken, Task<CatalogInstallCandidate>> resolveCandidate,
            Func<Uri, CancellationToken, Task<string>> download,
            Func<string, WcxProbeResult> probe = null,
            PackageInspector inspector = null)
        {
            _resolveCandidate = resolveCandidate ?? throw new ArgumentNullException(nameof(resolveCandidate));
            _download = download ?? throw new ArgumentNullException(nameof(download));
            _probe = probe ?? (path => new WcxProbeRunner().Probe(path, TimeSpan.FromSeconds(10)));
            _inspector = inspector ?? new PackageInspector();
        }

        public async Task<WcxRegistrationHarvestFinding> HarvestAsync(PluginCatalogEntry entry, CancellationToken cancellationToken)
        {
            if (entry == null || entry.PluginType != Core.PluginType.Wcx) return new WcxRegistrationHarvestFinding { Id = entry == null ? "" : entry.Id, Status = WcxRegistrationHarvest.MissingPackage };
            CatalogInstallCandidate candidate;
            try { candidate = await _resolveCandidate(entry, cancellationToken).ConfigureAwait(false); }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex) { return Finding(entry, WcxRegistrationHarvest.MissingPackage, ex.Message); }
            if (candidate == null || candidate.DownloadUrl == null) return Finding(entry, WcxRegistrationHarvest.MissingPackage, candidate == null ? "No source candidate." : candidate.Details);

            string packagePath = null;
            PackageInspection package = null;
            try
            {
                packagePath = await _download(candidate.DownloadUrl, cancellationToken).ConfigureAwait(false);
                package = _inspector.Inspect(packagePath);
                var x86 = package.Files.Where(x => WcxProbeRunner.ExpectedArchitecture(x.RelativePath) == "x86").ToList();
                var x64 = package.Files.Where(x => WcxProbeRunner.ExpectedArchitecture(x.RelativePath) == "x64").ToList();
                var x86Result = x86.Count == 1 ? _probe(x86[0].StagedPath) : null;
                var x64Result = x64.Count == 1 ? _probe(x64[0].StagedPath) : null;
                return WcxRegistrationEvidenceBuilder.FromInspectedPackage(entry, package, x86Result, x64Result, candidate.DownloadUrl.ToString());
            }
            catch (OperationCanceledException) { throw; }
            catch (InvalidDataException ex)
            {
                return Finding(entry, ex.Message.IndexOf("pluginst.inf", StringComparison.OrdinalIgnoreCase) >= 0 || ex.Message.IndexOf("[plugininstall]", StringComparison.OrdinalIgnoreCase) >= 0 ? WcxRegistrationHarvest.MissingPluginst : WcxRegistrationHarvest.MissingPackage, ex.Message);
            }
            catch (Exception ex) { return Finding(entry, WcxRegistrationHarvest.MissingPackage, ex.Message); }
            finally
            {
                if (package != null && !String.IsNullOrWhiteSpace(package.StagingDirectory) && Directory.Exists(package.StagingDirectory))
                {
                    try { Directory.Delete(package.StagingDirectory, true); } catch { }
                }
            }
        }

        private static WcxRegistrationHarvestFinding Finding(PluginCatalogEntry entry, string status, string detail)
        {
            return new WcxRegistrationHarvestFinding { Id = entry == null ? "" : entry.Id, Status = status, Detail = detail ?? "" };
        }
    }
}
