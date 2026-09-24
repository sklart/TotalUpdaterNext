using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace TotalUpdater.Next.Sources
{
    public sealed class SourceDiagnosticResult
    {
        public string Name { get; set; } = "";
        public string Host { get; set; } = "";
        public string Url { get; set; } = "";
        public bool IsAvailable { get; set; }
        public long ElapsedMilliseconds { get; set; }
        public string Details { get; set; } = "";
        public string Status { get { return IsAvailable ? "PASS" : "FAIL"; } }
        public string ResponseTime { get { return ElapsedMilliseconds + " мс"; } }
    }

    public interface ISourceDiagnostics
    {
        Task<IList<SourceDiagnosticResult>> CheckAsync(CancellationToken cancellationToken);
    }

    public sealed class SourceDiagnostics : ISourceDiagnostics
    {
        private readonly HttpService _http;
        public SourceDiagnostics(HttpService http) { _http = http; }

        public async Task<IList<SourceDiagnosticResult>> CheckAsync(CancellationToken cancellationToken)
        {
            var checks = new[]
            {
                CheckAsync("totalcmd.net index", "totalcmd.net", "https://totalcmd.net/get_plugins_list.php", cancellationToken),
                CheckAsync("totalcmd.net pages", "totalcmd.net", "https://totalcmd.net/", cancellationToken),
                CheckAsync("ghisler.com", "ghisler.com", "https://www.ghisler.com/plugins.htm", cancellationToken),
                CheckAsync("GitHub API", "api.github.com", "https://api.github.com/", cancellationToken)
            };
            return await Task.WhenAll(checks).ConfigureAwait(false);
        }

        private async Task<SourceDiagnosticResult> CheckAsync(string name, string host, string url, CancellationToken cancellationToken)
        {
            var timer = Stopwatch.StartNew();
            try
            {
                await _http.GetBytesAsync(url, cancellationToken, _http.FirstRequestTimeout).ConfigureAwait(false);
                return new SourceDiagnosticResult { Name = name, Host = host, Url = url, IsAvailable = true, ElapsedMilliseconds = timer.ElapsedMilliseconds };
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch (Exception ex)
            {
                return new SourceDiagnosticResult { Name = name, Host = host, Url = url, IsAvailable = false, ElapsedMilliseconds = timer.ElapsedMilliseconds, Details = Describe(ex) };
            }
        }

        internal static string Describe(Exception error)
        {
            switch (SourceHealthMonitor.Classify(error))
            {
                case SourceHealth.Timeout: return "timeout";
                case SourceHealth.DnsFailure: return "DNS failure";
                case SourceHealth.HttpError: return "HTTP error";
                default: return "connection failure";
            }
        }
    }
}
