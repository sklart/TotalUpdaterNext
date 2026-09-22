using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace TotalUpdater.Next.TotalCommander
{
    public sealed class IniEntry
    {
        public string Key { get; set; }
        public string Value { get; set; }
    }

    public sealed class IniSection
    {
        public IniSection(string name) { Name = name; Entries = new List<IniEntry>(); }
        public string Name { get; private set; }
        public IList<IniEntry> Entries { get; private set; }
        public string GetValue(string key)
        {
            foreach (var entry in Entries)
                if (entry.Key.Equals(key, StringComparison.OrdinalIgnoreCase)) return entry.Value;
            return null;
        }
    }

    public sealed class IniDocument
    {
        private readonly IList<IniSection> _sections = new List<IniSection>();
        public string Path { get; internal set; }
        public IList<IniSection> Sections { get { return _sections; } }
        public IniSection GetSection(string name)
        {
            foreach (var section in _sections)
                if (section.Name.Equals(name, StringComparison.OrdinalIgnoreCase)) return section;
            return null;
        }
        internal IniSection GetOrCreateSection(string name)
        {
            var section = GetSection(name);
            if (section == null) { section = new IniSection(name); _sections.Add(section); }
            return section;
        }
    }

    public interface IIniDocumentReader { IniDocument Read(string path); }

    public sealed class IniDocumentReader : IIniDocumentReader
    {
        public IniDocument Read(string path)
        {
            var document = new IniDocument { Path = System.IO.Path.GetFullPath(path) };
            var text = ReadText(path);
            IniSection current = null;
            using (var reader = new StringReader(text))
            {
                string raw;
                while ((raw = reader.ReadLine()) != null)
                {
                    var line = raw.Trim();
                    if (line.Length == 0 || line.StartsWith(";") || line.StartsWith("#")) continue;
                    if (line.StartsWith("[") && line.EndsWith("]") && line.Length > 2)
                    {
                        current = document.GetOrCreateSection(line.Substring(1, line.Length - 2).Trim());
                        continue;
                    }
                    var equals = line.IndexOf('=');
                    if (current == null || equals <= 0) continue;
                    current.Entries.Add(new IniEntry { Key = line.Substring(0, equals).Trim(), Value = line.Substring(equals + 1).Trim() });
                }
            }
            return document;
        }

        private static string ReadText(string path)
        {
            var bytes = File.ReadAllBytes(path);
            Encoding encoding = Encoding.Default;
            var offset = 0;
            if (bytes.Length >= 2 && bytes[0] == 0xff && bytes[1] == 0xfe) { encoding = Encoding.Unicode; offset = 2; }
            else if (bytes.Length >= 2 && bytes[0] == 0xfe && bytes[1] == 0xff) { encoding = Encoding.BigEndianUnicode; offset = 2; }
            else if (bytes.Length >= 3 && bytes[0] == 0xef && bytes[1] == 0xbb && bytes[2] == 0xbf) { encoding = new UTF8Encoding(false); offset = 3; }
            return encoding.GetString(bytes, offset, bytes.Length - offset);
        }
    }
}
