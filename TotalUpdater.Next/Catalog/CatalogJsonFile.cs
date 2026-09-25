using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization.Json;
using System.Text;
using TotalUpdater.Next.Infrastructure.Installation;

namespace TotalUpdater.Next.Catalog
{
    public static class CatalogJsonFile
    {
        public static IList<PluginCatalogEntry> Read(string path)
        {
            using (var input = File.OpenRead(path)) return (IList<PluginCatalogEntry>)new DataContractJsonSerializer(typeof(List<PluginCatalogEntry>)).ReadObject(input);
        }
        public static void Write(string path, IEnumerable<PluginCatalogEntry> entries)
        {
            var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                using (var output = File.Create(temporary)) new DataContractJsonSerializer(typeof(List<PluginCatalogEntry>)).WriteObject(output, (entries ?? Enumerable.Empty<PluginCatalogEntry>()).ToList());
                AtomicFile.Replace(temporary, path);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }

        // The embedded catalog is intentionally human-readable.  A registration merge must
        // not reserialize unrelated entries just to replace this single member.
        public static void ReplaceWcxRegistrationEvidence(string path, IEnumerable<WcxRegistrationHarvestFinding> findings)
        {
            var replacements = (findings ?? Enumerable.Empty<WcxRegistrationHarvestFinding>())
                .Where(x => x != null && x.Evidence != null && String.Equals(x.Status, WcxRegistrationHarvest.VerifiedRegistration, StringComparison.Ordinal))
                .ToDictionary(x => x.Id, x => Serialize(x.Evidence), StringComparer.OrdinalIgnoreCase);
            if (replacements.Count == 0) return;
            var text = File.ReadAllText(path, Encoding.UTF8);
            foreach (var item in replacements.OrderByDescending(x => IndexOfId(text, x.Key)))
            {
                var idIndex = IndexOfId(text, item.Key);
                if (idIndex < 0) throw new InvalidDataException("Catalog id is missing: " + item.Key);
                var objectStart = text.LastIndexOf('{', idIndex);
                var objectEnd = ValueEnd(text, objectStart);
                var property = text.IndexOf("\"wcxRegistration\"", objectStart, objectEnd - objectStart, StringComparison.Ordinal);
                if (property < 0)
                {
                    var closingLine = text.LastIndexOf('\n', objectEnd - 2);
                    if (closingLine > 0 && text[closingLine - 1] == '\r') closingLine--;
                    if (closingLine < objectStart) throw new InvalidDataException("Invalid catalog entry: " + item.Key);
                    text = text.Substring(0, closingLine) + "," + Environment.NewLine + "    \"wcxRegistration\": " + item.Value + text.Substring(closingLine);
                    continue;
                }
                var colon = text.IndexOf(':', property);
                var valueStart = colon + 1; while (valueStart < text.Length && Char.IsWhiteSpace(text[valueStart])) valueStart++;
                var valueEnd = ValueEnd(text, valueStart);
                text = text.Substring(0, valueStart) + item.Value + text.Substring(valueEnd);
            }
            var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try { File.WriteAllText(temporary, text, new UTF8Encoding(false)); AtomicFile.Replace(temporary, path); }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }

        private static int IndexOfId(string text, string id)
        {
            // Source objects also have an id member; only a top-level catalog entry starts
            // its first member at this indentation level.
            var index = text.IndexOf("\n    \"id\": \"" + id + "\"", StringComparison.OrdinalIgnoreCase);
            return index < 0 ? -1 : index + 5;
        }
        private static string Serialize(WcxRegistrationEvidence evidence)
        {
            using (var stream = new MemoryStream())
            {
                new DataContractJsonSerializer(typeof(WcxRegistrationEvidence)).WriteObject(stream, evidence);
                return Encoding.UTF8.GetString(stream.ToArray());
            }
        }
        private static int ValueEnd(string text, int start)
        {
            if (start < 0 || start >= text.Length) throw new InvalidDataException("Invalid JSON position.");
            if (text[start] != '{' && text[start] != '[') { var index = start; while (index < text.Length && text[index] != ',' && text[index] != '}' && text[index] != ']') index++; return index; }
            var depth = 0; var quoted = false;
            for (var index = start; index < text.Length; index++)
            {
                var c = text[index];
                if (quoted) { if (c == '\\') index++; else if (c == '\"') quoted = false; continue; }
                if (c == '\"') quoted = true;
                else if (c == '{' || c == '[') depth++;
                else if (c == '}' || c == ']') { depth--; if (depth == 0) return index + 1; }
            }
            throw new InvalidDataException("Unterminated JSON value.");
        }
    }
}
