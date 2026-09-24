using System;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace TotalUpdater.Next.Sources
{
    public sealed class HttpService : IDisposable
    {
        private readonly HttpClient _client;
        public string UserAgent { get; private set; }
        public HttpService(string applicationVersion)
        {
            _client = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
            UserAgent = "TotalUpdaterNext/" + applicationVersion;
            _client.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent);
        }
        public async Task<string> GetStringAsync(string url, CancellationToken token)
        {
            RequireHttpUrl(url);
            token.ThrowIfCancellationRequested();
            using (var response = await _client.GetAsync(url, token).ConfigureAwait(false))
            {
                response.EnsureSuccessStatusCode();
                token.ThrowIfCancellationRequested();
                return await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            }
        }
        public async Task<byte[]> GetBytesAsync(string url, CancellationToken token)
        {
            RequireHttpUrl(url);
            token.ThrowIfCancellationRequested();
            using (var response = await _client.GetAsync(url, token).ConfigureAwait(false))
            {
                response.EnsureSuccessStatusCode();
                token.ThrowIfCancellationRequested();
                return await response.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
            }
        }
        public async Task<string> GetTotalCmdIndexAsync(CancellationToken token)
        {
            return DecodeTotalCmdIndex(await GetTotalCmdIndexBytesAsync(token).ConfigureAwait(false));
        }
        public Task<byte[]> GetTotalCmdIndexBytesAsync(CancellationToken token) { return GetBytesAsync("https://totalcmd.net/get_plugins_list.php", token); }
        public static string DecodeTotalCmdIndex(byte[] bytes)
        {
            if (bytes == null) return "";
            // The live endpoint has no charset header and currently serves Windows-1251.
            if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
                return Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);
            if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
                return Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2);
            if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
                return Encoding.BigEndianUnicode.GetString(bytes, 2, bytes.Length - 2);
            return Encoding.GetEncoding(1251).GetString(bytes);
        }
        public Task<HttpResponseMessage> GetAsync(string url, HttpCompletionOption completion, CancellationToken token) { RequireHttpUrl(url); return _client.GetAsync(url, completion, token); }
        private static void RequireHttpUrl(string value)
        {
            Uri uri;
            if (!Uri.TryCreate(value, UriKind.Absolute, out uri) || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
                throw new ArgumentException("Разрешены только HTTP/HTTPS URL.", nameof(value));
        }
        public void Dispose() { _client.Dispose(); }
    }
}
