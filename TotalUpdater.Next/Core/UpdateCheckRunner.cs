using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TotalUpdater.Next.Sources;
using TotalUpdater.Next.Settings;

namespace TotalUpdater.Next.Core
{
    public sealed class UpdateCheckRunResult { public int Completed; public int Errors; public bool WasCancelled; }

    // UI-neutral bounded executor: its behavior is covered by regression tests.
    public sealed class UpdateCheckRunner
    {
        public const int MaximumParallelism = 4;
        private readonly UpdateService _updates;
        private readonly AppSettings _settings;
        public UpdateCheckRunner(UpdateService updates, AppSettings settings = null) { _updates = updates; _settings = settings; }
        public async Task<UpdateCheckRunResult> RunAsync(IEnumerable<InstalledPlugin> plugins, Action<InstalledPlugin, UpdateCandidate> completed, Action<int, int> progress, CancellationToken token)
        {
            var target = (plugins ?? Enumerable.Empty<InstalledPlugin>()).ToList(); var result = new UpdateCheckRunResult(); var cache = new SourceResponseCache(null, _settings);
            using (var gate = new SemaphoreSlim(MaximumParallelism))
            {
                var tasks = target.Select(async plugin =>
                {
                    try
                    {
                        await gate.WaitAsync(token).ConfigureAwait(false);
                        try { var candidate = await _updates.CheckAsync(plugin, token, cache).ConfigureAwait(false); if (!token.IsCancellationRequested) completed(plugin, candidate); }
                        finally { gate.Release(); }
                    }
                    catch (OperationCanceledException) { result.WasCancelled = true; }
                    catch (Exception ex) { Interlocked.Increment(ref result.Errors); if (!token.IsCancellationRequested) completed(plugin, new UpdateCandidate { Plugin = plugin, State = UpdateState.Error, Details = ex.Message }); }
                    finally { var count = Interlocked.Increment(ref result.Completed); if (!token.IsCancellationRequested) progress(count, target.Count); }
                }).ToArray();
                await Task.WhenAll(tasks).ConfigureAwait(false);
            }
            return result;
        }
    }
}
