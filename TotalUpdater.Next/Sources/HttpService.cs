using System;
using System.Net.Http;
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
            token.ThrowIfCancellationRequested();
            using (var response = await _client.GetAsync(url, token).ConfigureAwait(false))
            {
                response.EnsureSuccessStatusCode();
                token.ThrowIfCancellationRequested();
                return await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            }
        }
        public Task<HttpResponseMessage> GetAsync(string url, HttpCompletionOption completion, CancellationToken token) { return _client.GetAsync(url, completion, token); }
        public void Dispose() { _client.Dispose(); }
    }
}
