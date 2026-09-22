using System;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;

namespace TotalUpdater.Next.Services
{
    public static class DownloadService
    {
        private const long MaximumPackageBytes = 512L * 1024L * 1024L;

        public static async Task<string> DownloadAsync(string url, string destinationDirectory)
        {
            Directory.CreateDirectory(destinationDirectory);
            using (var client = new HttpClient())
            using (var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false))
            {
                response.EnsureSuccessStatusCode();
                if (response.Content.Headers.ContentLength.HasValue && response.Content.Headers.ContentLength.Value > MaximumPackageBytes)
                    throw new InvalidOperationException("Размер пакета превышает безопасный предел 512 МБ.");

                var name = GetFileName(response, url);
                var target = GetAvailablePath(destinationDirectory, name);
                var temporary = target + ".part";
                long total = 0;
                try
                {
                    using (var input = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
                    using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                    {
                        var buffer = new byte[81920];
                        int read;
                        while ((read = await input.ReadAsync(buffer, 0, buffer.Length).ConfigureAwait(false)) > 0)
                        {
                            total += read;
                            if (total > MaximumPackageBytes) throw new InvalidOperationException("Размер пакета превышает безопасный предел 512 МБ.");
                            await output.WriteAsync(buffer, 0, read).ConfigureAwait(false);
                        }
                    }
                    File.Move(temporary, target);
                    return target;
                }
                catch
                {
                    if (File.Exists(temporary)) File.Delete(temporary);
                    throw;
                }
            }
        }

        private static string GetFileName(HttpResponseMessage response, string fallbackUrl)
        {
            var disposition = response.Content.Headers.ContentDisposition;
            var name = disposition == null ? null : disposition.FileNameStar ?? disposition.FileName;
            if (String.IsNullOrWhiteSpace(name)) name = Path.GetFileName(response.RequestMessage.RequestUri.LocalPath);
            if (String.IsNullOrWhiteSpace(name)) name = Path.GetFileName(new Uri(fallbackUrl).LocalPath);
            name = Path.GetFileName((name ?? "plugin-package.zip").Trim('"'));
            return String.IsNullOrWhiteSpace(name) ? "plugin-package.zip" : name;
        }

        private static string GetAvailablePath(string directory, string fileName)
        {
            var path = Path.Combine(directory, fileName);
            if (!File.Exists(path)) return path;
            var stem = Path.GetFileNameWithoutExtension(fileName);
            var extension = Path.GetExtension(fileName);
            for (var index = 2; ; index++)
            {
                path = Path.Combine(directory, stem + " (" + index + ")" + extension);
                if (!File.Exists(path)) return path;
            }
        }
    }
}
