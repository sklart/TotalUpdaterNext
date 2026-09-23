using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace TotalUpdater.Next.Infrastructure.Installation
{
    public sealed class IniPatchResult
    {
        public byte[] OriginalBytes { get; set; }
        public byte[] NewBytes { get; set; }
        public string OriginalSha256 { get { return IniPatchEngine.Hash(OriginalBytes); } }
        public string InstalledSha256 { get { return IniPatchEngine.Hash(NewBytes); } }
    }

    public sealed class IniPatchEngine
    {
        private readonly Encoding _ansiEncoding;
        public IniPatchEngine(Encoding ansiEncoding = null) { _ansiEncoding = ansiEncoding ?? Encoding.Default; }
        public IniPatchResult Begin(string path)
        {
            if (!File.Exists(path)) throw new FileNotFoundException("INI не найден.", path);
            var bytes = File.ReadAllBytes(path);
            return new IniPatchResult { OriginalBytes = bytes, NewBytes = bytes };
        }
        public void Add(IniPatchResult patch, string section, string key, string value)
        {
            if (patch == null || String.IsNullOrWhiteSpace(section) || String.IsNullOrWhiteSpace(key) ||
                section.IndexOfAny(new[] { '\r', '\n', '[', ']' }) >= 0 ||
                key.IndexOfAny(new[] { '\r', '\n', '[', ']', '=' }) >= 0 ||
                (value ?? "").IndexOfAny(new[] { '\r', '\n' }) >= 0) throw new InvalidDataException("Небезопасная запись INI.");
            var bytes = patch.NewBytes;
            var encoding = DetectEncoding(bytes, out var bom);
            string text;
            try
            {
                text = encoding.GetString(bytes, bom, bytes.Length - bom);
                EnsureRepresentable(encoding, section);
                EnsureRepresentable(encoding, key);
                EnsureRepresentable(encoding, value ?? "");
            }
            catch (EncoderFallbackException ex) { throw new EncodingConflictException("Новая запись не представима в кодировке INI.", ex); }
            catch (DecoderFallbackException ex) { throw new EncodingConflictException("Существующий INI не декодируется без потерь.", ex); }
            var newline = text.Contains("\r\n") ? "\r\n" : text.Contains("\n") ? "\n" : "\r\n";
            var lines = Regex.Matches(text, @"[^\r\n]*(?:\r\n|\n|\r|$)").Cast<Match>().Where(x => x.Length > 0).ToList();
            var sectionStart = -1; var sectionEnd = text.Length;
            foreach (var line in lines)
            {
                var header = Regex.Match(line.Value.Trim(), @"^\[([^\]]+)\]");
                if (!header.Success) continue;
                if (sectionStart >= 0) { sectionEnd = line.Index; break; }
                if (String.Equals(header.Groups[1].Value.Trim(), section, StringComparison.OrdinalIgnoreCase)) sectionStart = line.Index + line.Length;
            }
            string insertion; int position;
            if (sectionStart < 0)
            {
                position = text.Length;
                insertion = (position > 0 && !text.EndsWith("\n", StringComparison.Ordinal) && !text.EndsWith("\r", StringComparison.Ordinal) ? newline : "") +
                    "[" + section + "]" + newline + key + "=" + value + newline;
            }
            else
            {
                foreach (var line in lines.Where(x => x.Index >= sectionStart && x.Index < sectionEnd))
                {
                    var content = line.Value.TrimStart();
                    if (content.StartsWith(";", StringComparison.Ordinal) || content.StartsWith("#", StringComparison.Ordinal)) continue;
                    var equals = content.IndexOf('=');
                    if (equals > 0 && String.Equals(content.Substring(0, equals).Trim(), key, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidDataException("INI key уже занят: [" + section + "] " + key);
                }
                position = sectionEnd;
                insertion = (position > 0 && text[position - 1] != '\n' && text[position - 1] != '\r' ? newline : "") + key + "=" + value + newline;
            }
            var offset = bom + encoding.GetByteCount(text.Substring(0, position));
            byte[] inserted;
            try { inserted = encoding.GetBytes(insertion); }
            catch (EncoderFallbackException ex) { throw new EncodingConflictException("Новая запись не представима в кодировке INI.", ex); }
            var result = new byte[bytes.Length + inserted.Length];
            Buffer.BlockCopy(bytes, 0, result, 0, offset);
            Buffer.BlockCopy(inserted, 0, result, offset, inserted.Length);
            Buffer.BlockCopy(bytes, offset, result, offset + inserted.Length, bytes.Length - offset);
            patch.NewBytes = result;
        }
        public static string Hash(byte[] bytes)
        {
            using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant();
        }
        private Encoding DetectEncoding(byte[] bytes, out int bom)
        {
            bom = 0;
            if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF) { bom = 3; return new UTF8Encoding(false, true); }
            if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE) { bom = 2; return new UnicodeEncoding(false, false, true); }
            if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF) { bom = 2; return new UnicodeEncoding(true, false, true); }
            if (bytes.Any(x => x >= 0x80))
            {
                try { var utf8 = new UTF8Encoding(false, true); utf8.GetString(bytes); return utf8; }
                catch (DecoderFallbackException) { }
            }
            return Encoding.GetEncoding(_ansiEncoding.CodePage, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);
        }
        private static void EnsureRepresentable(Encoding encoding, string value)
        {
            var encoded = encoding.GetBytes(value);
            if (!String.Equals(encoding.GetString(encoded), value, StringComparison.Ordinal))
                throw new EncodingConflictException("Новая запись не представима в кодировке INI.");
        }
    }
    public sealed class EncodingConflictException : IOException
    {
        public EncodingConflictException(string message) : base("EncodingConflict: " + message) { }
        public EncodingConflictException(string message, Exception inner) : base("EncodingConflict: " + message, inner) { }
    }
}
