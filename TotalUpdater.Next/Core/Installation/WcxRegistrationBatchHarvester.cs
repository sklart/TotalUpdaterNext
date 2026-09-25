using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TotalUpdater.Next.Catalog;

namespace TotalUpdater.Next.Core.Installation
{
    public sealed class WcxRegistrationBatchProgress
    {
        public int Completed { get; set; }
        public int Total { get; set; }
        public int Skipped { get; set; }
        public PluginCatalogEntry Entry { get; set; }
        public WcxRegistrationHarvestFinding Finding { get; set; }
    }

    public sealed class WcxRegistrationBatchResult
    {
        public int Total { get; set; }
        public int Processed { get; set; }
        public int Skipped { get; set; }
        public IList<WcxRegistrationHarvestFinding> Findings { get; set; } = new List<WcxRegistrationHarvestFinding>();
        public int Count(string status) { return Findings.Count(x => String.Equals(x.Status, status, StringComparison.Ordinal)); }
    }

    // Bounded batch runner. It owns no network or probing code: every item
    // still goes through the same WcxRegistrationHarvester pipeline.
    public sealed class WcxRegistrationBatchHarvester
    {
        private readonly Func<PluginCatalogEntry, CancellationToken, Task<WcxRegistrationHarvestFinding>> _harvest;
        private readonly TimeSpan _itemTimeout;
        public WcxRegistrationBatchHarvester(Func<PluginCatalogEntry, CancellationToken, Task<WcxRegistrationHarvestFinding>> harvest, TimeSpan? itemTimeout = null)
        {
            _harvest = harvest ?? throw new ArgumentNullException(nameof(harvest));
            _itemTimeout = itemTimeout ?? TimeSpan.FromMinutes(2);
        }

        public async Task<WcxRegistrationBatchResult> RunAsync(IEnumerable<PluginCatalogEntry> entries, IEnumerable<WcxRegistrationHarvestFinding> existing, bool resume,
            int maximumConcurrency, Action<WcxRegistrationBatchProgress> progress, Action<IList<WcxRegistrationHarvestFinding>> checkpoint, CancellationToken cancellationToken)
        {
            var targets = (entries ?? Enumerable.Empty<PluginCatalogEntry>()).Where(x => x != null && x.PluginType == Core.PluginType.Wcx)
                .OrderBy(x => x.Id, StringComparer.OrdinalIgnoreCase).ToList();
            var byId = (existing ?? Enumerable.Empty<WcxRegistrationHarvestFinding>()).Where(x => x != null && !String.IsNullOrWhiteSpace(x.Id))
                .GroupBy(x => x.Id, StringComparer.OrdinalIgnoreCase).ToDictionary(x => x.Key, x => x.Last(), StringComparer.OrdinalIgnoreCase);
            var result = new WcxRegistrationBatchResult { Total = targets.Count };
            var work = new List<PluginCatalogEntry>();
            foreach (var entry in targets)
            {
                WcxRegistrationHarvestFinding old;
                if (resume && byId.TryGetValue(entry.Id, out old) && IsCurrent(entry, old)) { result.Skipped++; progress?.Invoke(new WcxRegistrationBatchProgress { Completed = result.Skipped, Total = targets.Count, Skipped = result.Skipped, Entry = entry, Finding = old }); }
                else work.Add(entry);
            }
            var gate = new SemaphoreSlim(Math.Min(2, Math.Max(1, maximumConcurrency)));
            var pending = work.Select(entry => HarvestOneAsync(entry, gate, cancellationToken)).ToList();
            while (pending.Count > 0)
            {
                var completed = await Task.WhenAny(pending).ConfigureAwait(false); pending.Remove(completed);
                var item = await completed.ConfigureAwait(false);
                byId[item.Entry.Id] = item.Finding; result.Processed++;
                result.Findings = byId.Values.OrderBy(x => x.Id, StringComparer.OrdinalIgnoreCase).ToList();
                checkpoint?.Invoke(result.Findings);
                progress?.Invoke(new WcxRegistrationBatchProgress { Completed = result.Skipped + result.Processed, Total = targets.Count, Skipped = result.Skipped, Entry = item.Entry, Finding = item.Finding });
            }
            result.Findings = byId.Values.OrderBy(x => x.Id, StringComparer.OrdinalIgnoreCase).ToList();
            return result;
        }

        private async Task<BatchItem> HarvestOneAsync(PluginCatalogEntry entry, SemaphoreSlim gate, CancellationToken token)
        {
            await gate.WaitAsync(token).ConfigureAwait(false);
            try
            {
                using (var item = CancellationTokenSource.CreateLinkedTokenSource(token))
                {
                    var harvest = _harvest(entry, item.Token);
                    var deadline = Task.Delay(_itemTimeout, token);
                    var completed = await Task.WhenAny(harvest, deadline).ConfigureAwait(false);
                    if (completed != harvest)
                    {
                        token.ThrowIfCancellationRequested(); item.Cancel();
                        var ignoredHarvest = harvest.ContinueWith(task => { var ignored = task.Exception; }, TaskContinuationOptions.OnlyOnFaulted);
                        return new BatchItem { Entry = entry, Finding = new WcxRegistrationHarvestFinding { Id = entry.Id, Status = WcxRegistrationHarvest.SourceUnavailable, Detail = "Batch item timeout." } };
                    }
                    return new BatchItem { Entry = entry, Finding = await harvest.ConfigureAwait(false) };
                }
            }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex) { return new BatchItem { Entry = entry, Finding = new WcxRegistrationHarvestFinding { Id = entry.Id, Status = WcxRegistrationHarvest.SourceUnavailable, Detail = ex.Message } }; }
            finally { gate.Release(); }
        }

        public static bool IsCurrent(PluginCatalogEntry entry, WcxRegistrationHarvestFinding finding)
        {
            if (entry == null || !WcxRegistrationHarvest.IsValidVerifiedFinding(finding)) return false;
            var evidence = finding.Evidence;
            if (!String.Equals(evidence.Version, entry.Version, StringComparison.Ordinal) || String.IsNullOrWhiteSpace(evidence.SourceId) || String.IsNullOrWhiteSpace(evidence.SourceFingerprint)) return false;
            return entry.Sources != null && entry.Sources.Any(x => String.Equals(x.Id, evidence.SourceId, StringComparison.OrdinalIgnoreCase) &&
                String.Equals(WcxRegistrationHarvester.SourceFingerprint(x), evidence.SourceFingerprint, StringComparison.OrdinalIgnoreCase));
        }

        private sealed class BatchItem { public PluginCatalogEntry Entry; public WcxRegistrationHarvestFinding Finding; }
    }
}
