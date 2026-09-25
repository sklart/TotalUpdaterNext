using System;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using TotalUpdater.Next.Sources;

namespace TotalUpdater.Next.Infrastructure
{
    public sealed class DownloadService
    {
        private const long MaximumPackageBytes = 512L * 1024L * 1024L;
        private readonly HttpService _http;
        public DownloadService(HttpService http) { _http = http; }
        public async Task<string> DownloadAsync(Uri url, string directory, CancellationToken cancellationToken)
        {
            Exception last = null;
            for (var attempt = 0; attempt < 2; attempt++)
            {
                try { return await DownloadOnceAsync(url, directory, cancellationToken).ConfigureAwait(false); }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { last = new TimeoutException("Package download timed out."); }
                catch (HttpRequestException ex) { last = ex; }
                catch (IOException ex) { last = ex; }
                if (attempt == 0) continue;
            }
            throw last ?? new IOException("Package download failed.");
        }
        private async Task<string> DownloadOnceAsync(Uri url, string directory, CancellationToken cancellationToken)
        {
            Directory.CreateDirectory(directory);
            using (var request = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
            {
                request.CancelAfter(_http.FirstRequestTimeout + _http.RetryTimeout);
                using (var response = await _http.GetAsync(url.ToString(), HttpCompletionOption.ResponseHeadersRead, request.Token).ConfigureAwait(false))
            {
                response.EnsureSuccessStatusCode();
                if (response.Content.Headers.ContentLength.HasValue && response.Content.Headers.ContentLength.Value > MaximumPackageBytes)
                    throw new InvalidOperationException("Package exceeds the 512 MB safety limit.");
                var destination = FindFreePath(directory, FileName(response, url)); var temporary = destination + ".part"; long length = 0;
                try
                {
                    using (var input = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
                    using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                    {
                        var buffer = new byte[81920]; int read;
                        while ((read = await ReadBoundedAsync(input, buffer, request.Token).ConfigureAwait(false)) > 0)
                        {
                            length += read; if (length > MaximumPackageBytes) throw new InvalidOperationException("Package exceeds the 512 MB safety limit.");
                            await output.WriteAsync(buffer, 0, read, request.Token).ConfigureAwait(false);
                        }
                    }
                    File.Move(temporary, destination); return destination;
                }
                catch { if (File.Exists(temporary)) File.Delete(temporary); throw; }
            }
            }
        }
        private async Task<int> ReadBoundedAsync(Stream input, byte[] buffer, CancellationToken cancellationToken)
        {
            var read = input.ReadAsync(buffer, 0, buffer.Length, CancellationToken.None);
            var deadline = Task.Delay(_http.FirstRequestTimeout + _http.RetryTimeout, cancellationToken);
            var completed = await Task.WhenAny(read, deadline).ConfigureAwait(false);
            if (completed != read)
            {
                cancellationToken.ThrowIfCancellationRequested();
                // Dispose of the response in the caller's finally path; do
                // not await a .NET Framework stream that ignores cancellation.
                var ignoredRead = read.ContinueWith(task => { var ignored = task.Exception; }, TaskContinuationOptions.OnlyOnFaulted);
                throw new TimeoutException("Package stream read timed out.");
            }
            return await read.ConfigureAwait(false);
        }
        private static string FileName(HttpResponseMessage response, Uri fallback)
        {
            var header = response.Content.Headers.ContentDisposition; var name = header == null ? null : header.FileNameStar ?? header.FileName;
            name = Path.GetFileName((name ?? Path.GetFileName(response.RequestMessage.RequestUri.LocalPath) ?? Path.GetFileName(fallback.LocalPath) ?? "plugin-package.zip").Trim('"'));
            return String.IsNullOrWhiteSpace(name) ? "plugin-package.zip" : name;
        }
        private static string FindFreePath(string directory, string name)
        {
            var candidate = Path.Combine(directory, name); if (!File.Exists(candidate)) return candidate;
            var stem = Path.GetFileNameWithoutExtension(name); var extension = Path.GetExtension(name);
            for (var index = 2; ; index++) { candidate = Path.Combine(directory, stem + " (" + index + ")" + extension); if (!File.Exists(candidate)) return candidate; }
        }
    }
}
