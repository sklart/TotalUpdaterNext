using System;
using System.Collections.Concurrent;
using System.Net.Http;
using System.Net.Sockets;
using System.Threading.Tasks;

namespace TotalUpdater.Next.Sources
{
    public enum SourceHealth { Healthy, Timeout, DnsFailure, ConnectionFailure, HttpError }

    public sealed class SourceHealthMonitor
    {
        private readonly ConcurrentDictionary<string, SourceHealth> _hosts = new ConcurrentDictionary<string, SourceHealth>(StringComparer.OrdinalIgnoreCase);
        public SourceHealth Get(string host) { SourceHealth state; return _hosts.TryGetValue(host, out state) ? state : SourceHealth.Healthy; }
        public bool IsUnavailable(string host) { return Get(host) != SourceHealth.Healthy; }
        public bool IsTransient(string host) { var health = Get(host); return health == SourceHealth.Timeout || health == SourceHealth.DnsFailure || health == SourceHealth.ConnectionFailure; }
        public void RecordSuccess(string host) { _hosts[host] = SourceHealth.Healthy; }
        public void RecordFailure(string host, Exception error) { _hosts[host] = Classify(error); }
        public static SourceHealth Classify(Exception error)
        {
            if (error is TaskCanceledException || error is TimeoutException) return SourceHealth.Timeout;
            var socket = error as SocketException; if (socket != null) return socket.SocketErrorCode == SocketError.HostNotFound || socket.SocketErrorCode == SocketError.NoData ? SourceHealth.DnsFailure : SourceHealth.ConnectionFailure;
            var http = error as HttpRequestException; if (http != null)
            {
                var innerSocket = http.InnerException as SocketException;
                if (innerSocket != null) return innerSocket.SocketErrorCode == SocketError.HostNotFound || innerSocket.SocketErrorCode == SocketError.NoData ? SourceHealth.DnsFailure : SourceHealth.ConnectionFailure;
                return SourceHealth.HttpError;
            }
            return SourceHealth.ConnectionFailure;
        }
    }
}
