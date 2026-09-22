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
            Directory.CreateDirectory(directory);
            using (var response = await _http.GetAsync(url.ToString(), HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false))
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
                        while ((read = await input.ReadAsync(buffer, 0, buffer.Length, cancellationToken).ConfigureAwait(false)) > 0)
                        {
                            length += read; if (length > MaximumPackageBytes) throw new InvalidOperationException("Package exceeds the 512 MB safety limit.");
                            await output.WriteAsync(buffer, 0, read, cancellationToken).ConfigureAwait(false);
                        }
                    }
                    File.Move(temporary, destination); return destination;
                }
                catch { if (File.Exists(temporary)) File.Delete(temporary); throw; }
            }
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
