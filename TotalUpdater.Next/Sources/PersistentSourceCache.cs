using System;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Security.Cryptography;
using System.Text;
using System.Runtime.InteropServices;

namespace TotalUpdater.Next.Sources
{
    public sealed class CachedSourceResponse
    {
        public DateTime FetchedUtc { get; set; }
        public string SourceUrl { get; set; }
        public string Sha256 { get; set; }
        public byte[] Bytes { get; set; }
        public string ContentType { get; set; }
        public string EncodingName { get; set; }
        public bool IsStale(DateTime utcNow, int maxAgeDays = 180) { return FetchedUtc.ToUniversalTime() < utcNow.ToUniversalTime().AddDays(-maxAgeDays); }
    }

    [DataContract]
    internal sealed class PersistentSourceCacheEnvelope
    {
        [DataMember(Name = "sourceUrl")] public string SourceUrl { get; set; }
        [DataMember(Name = "fetchedUtc")] public string FetchedUtc { get; set; }
        [DataMember(Name = "sha256")] public string Sha256 { get; set; }
        [DataMember(Name = "contentType")] public string ContentType { get; set; }
        [DataMember(Name = "encoding")] public string EncodingName { get; set; }
        [DataMember(Name = "responseBytes")] public byte[] Bytes { get; set; }
    }

    public sealed class PersistentSourceCache
    {
        private const int MoveFileReplaceExisting = 0x1;
        private const int MoveFileWriteThrough = 0x8;
        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)] private static extern bool MoveFileEx(string existingFileName, string newFileName, int flags);
        private readonly string _directory;
        public PersistentSourceCache(string directory = null)
        {
            _directory = directory ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TotalUpdaterNext", "cache");
        }

        public void Save(string key, string sourceUrl, byte[] bytes, string contentType = "", string encodingName = "", DateTime? fetchedUtc = null)
        {
            var data = bytes ?? new byte[0];
            var envelope = new PersistentSourceCacheEnvelope
            {
                SourceUrl = sourceUrl ?? "", FetchedUtc = (fetchedUtc ?? DateTime.UtcNow).ToUniversalTime().ToString("o"),
                Sha256 = HashBytes(data), ContentType = contentType ?? "", EncodingName = encodingName ?? "", Bytes = data
            };
            Directory.CreateDirectory(_directory);
            var target = Path.Combine(_directory, HashKey(key) + ".json");
            var temporary = target + ".tmp-" + Guid.NewGuid().ToString("N");
            var backup = target + ".bak";
            try
            {
                using (var stream = File.Create(temporary)) new DataContractJsonSerializer(typeof(PersistentSourceCacheEnvelope)).WriteObject(stream, envelope);
                if (File.Exists(target))
                {
                    if (!MoveFileEx(temporary, target, MoveFileReplaceExisting | MoveFileWriteThrough)) throw new IOException("Не удалось атомарно заменить persistent cache: " + Marshal.GetLastWin32Error());
                }
                else File.Move(temporary, target);
            }
            finally
            {
                if (File.Exists(temporary)) File.Delete(temporary);
                if (File.Exists(backup)) File.Delete(backup);
            }
        }

        public bool TryRead(string key, out CachedSourceResponse result)
        {
            result = null;
            try
            {
                var path = Path.Combine(_directory, HashKey(key) + ".json");
                if (!File.Exists(path)) return false;
                PersistentSourceCacheEnvelope envelope;
                using (var stream = File.OpenRead(path)) envelope = (PersistentSourceCacheEnvelope)new DataContractJsonSerializer(typeof(PersistentSourceCacheEnvelope)).ReadObject(stream);
                DateTime fetchedUtc;
                if (envelope == null || envelope.Bytes == null || String.IsNullOrWhiteSpace(envelope.SourceUrl) ||
                    !DateTime.TryParse(envelope.FetchedUtc, null, System.Globalization.DateTimeStyles.RoundtripKind, out fetchedUtc) ||
                    !String.Equals(envelope.Sha256, HashBytes(envelope.Bytes), StringComparison.OrdinalIgnoreCase)) return false;
                result = new CachedSourceResponse { FetchedUtc = fetchedUtc.ToUniversalTime(), SourceUrl = envelope.SourceUrl, Sha256 = envelope.Sha256,
                    Bytes = envelope.Bytes, ContentType = envelope.ContentType ?? "", EncodingName = envelope.EncodingName ?? "" };
                return true;
            }
            catch { return false; }
        }

        public long GetSizeBytes()
        {
            try { return Directory.Exists(_directory) ? Directory.GetFiles(_directory, "*.json").Sum(x => new FileInfo(x).Length) : 0; }
            catch { return 0; }
        }

        public void Clear()
        {
            try { if (Directory.Exists(_directory)) foreach (var file in Directory.GetFiles(_directory, "*.json")) File.Delete(file); }
            catch { }
        }

        internal string GetPathForTest(string key) { return Path.Combine(_directory, HashKey(key) + ".json"); }
        private static string HashKey(string value) { return HashBytes(Encoding.UTF8.GetBytes(value ?? "")); }
        private static string HashBytes(byte[] value) { using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(value ?? new byte[0])).Replace("-", ""); }
    }
}
