using System;
using System.Net.Http;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using TotalUpdater.Next.Settings;

namespace TotalUpdater.Next.Sources
{
    public sealed class HttpService : IDisposable
    {
        private HttpClient _client;
        private AppSettings _settings;
        public string UserAgent { get; private set; }
        public HttpService(string applicationVersion, AppSettings settings = null)
        {
            _settings = settings ?? new AppSettings();
            _client = CreateClient(_settings, applicationVersion);
            UserAgent = "TotalUpdaterNext/" + applicationVersion;
        }
        private static HttpClient CreateClient(AppSettings settings, string applicationVersion)
        {
            var handler = new HttpClientHandler();
            if (settings.ProxyMode == ProxyMode.Direct) handler.UseProxy = false;
            else if (settings.ProxyMode == ProxyMode.Custom)
            {
                handler.Proxy = new WebProxy(settings.ProxyAddress, settings.ProxyPort);
                if (!String.IsNullOrWhiteSpace(settings.ProxyUsername)) handler.Proxy.Credentials = new NetworkCredential(settings.ProxyUsername, "");
            }
            var client = new HttpClient(handler) { Timeout = System.Threading.Timeout.InfiniteTimeSpan }; client.DefaultRequestHeaders.UserAgent.ParseAdd("TotalUpdaterNext/" + applicationVersion); return client;
        }
        public void Reconfigure(AppSettings settings) { var next = CreateClient(settings ?? new AppSettings(), UserAgent.Substring(UserAgent.IndexOf('/') + 1)); var old = _client; _settings = settings ?? new AppSettings(); _client = next; old.Dispose(); }
        public TimeSpan FirstRequestTimeout { get { return TimeSpan.FromSeconds(_settings.FirstRequestTimeoutSeconds); } }
        public TimeSpan RetryTimeout { get { return TimeSpan.FromSeconds(_settings.RetryTimeoutSeconds); } }
        public async Task<string> GetStringAsync(string url, CancellationToken token, TimeSpan? timeout = null)
        {
            RequireHttpUrl(url);
            token.ThrowIfCancellationRequested();
            using (var request = CreateTimeoutToken(token, timeout))
            using (var response = await _client.GetAsync(url, request.Token).ConfigureAwait(false))
            {
                response.EnsureSuccessStatusCode();
                request.Token.ThrowIfCancellationRequested();
                return await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            }
        }
        public async Task<byte[]> GetBytesAsync(string url, CancellationToken token, TimeSpan? timeout = null)
        {
            RequireHttpUrl(url);
            token.ThrowIfCancellationRequested();
            using (var request = CreateTimeoutToken(token, timeout))
            using (var response = await _client.GetAsync(url, request.Token).ConfigureAwait(false))
            {
                response.EnsureSuccessStatusCode();
                request.Token.ThrowIfCancellationRequested();
                return await response.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
            }
        }
        public async Task<string> GetTotalCmdIndexAsync(CancellationToken token)
        {
            return DecodeTotalCmdIndex(await GetTotalCmdIndexBytesAsync(token).ConfigureAwait(false));
        }
        public Task<byte[]> GetTotalCmdIndexBytesAsync(CancellationToken token, TimeSpan? timeout = null) { return GetBytesAsync("https://totalcmd.net/get_plugins_list.php", token, timeout); }
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
        private static CancellationTokenSource CreateTimeoutToken(CancellationToken token, TimeSpan? timeout)
        {
            var linked = CancellationTokenSource.CreateLinkedTokenSource(token);
            linked.CancelAfter(timeout ?? TimeSpan.FromSeconds(20));
            return linked;
        }
        private static void RequireHttpUrl(string value)
        {
            Uri uri;
            if (!Uri.TryCreate(value, UriKind.Absolute, out uri) || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
                throw new ArgumentException("Разрешены только HTTP/HTTPS URL.", nameof(value));
        }
        public void Dispose() { _client.Dispose(); }
    }
}
