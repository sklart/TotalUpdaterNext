using System;
using System.Collections.Generic;
using System.IO;

namespace TotalUpdater.Next.TotalCommander
{
    public sealed class RedirectedSectionResolver
    {
        private readonly IIniDocumentReader _reader;
        private readonly TotalCommanderConfigurationResolver _paths;
        public RedirectedSectionResolver(IIniDocumentReader reader, TotalCommanderConfigurationResolver paths) { _reader = reader; _paths = paths; }

        public IList<IniEntry> GetEntries(TotalCommanderConfiguration configuration, string sectionName)
        {
            var section = configuration.Document == null ? null : configuration.Document.GetSection(sectionName);
            if (section == null) return new List<IniEntry>();
            var redirect = section.GetValue("RedirectSection");
            if (String.IsNullOrWhiteSpace(redirect) || redirect.Trim() == "0") return WithoutRedirectControl(section.Entries);
            var path = redirect.Trim() == "1" ? configuration.AlternateUserIni : _paths.ExpandRedirectPath(redirect, configuration);
            if (String.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                configuration.Warnings.Add("Не найден файл RedirectSection для [" + sectionName + "]: " + (String.IsNullOrWhiteSpace(path) ? redirect : path));
                return new List<IniEntry>();
            }
            try
            {
                var redirected = _reader.Read(path).GetSection(sectionName);
                return redirected == null ? new List<IniEntry>() : WithoutRedirectControl(redirected.Entries);
            }
            catch (Exception ex)
            {
                configuration.Warnings.Add("Не удалось прочитать RedirectSection для [" + sectionName + "]: " + ex.Message);
                return new List<IniEntry>();
            }
        }
        private static IList<IniEntry> WithoutRedirectControl(IList<IniEntry> entries)
        {
            var result = new List<IniEntry>();
            foreach (var entry in entries) if (!entry.Key.Equals("RedirectSection", StringComparison.OrdinalIgnoreCase)) result.Add(entry);
            return result;
        }
    }
}
