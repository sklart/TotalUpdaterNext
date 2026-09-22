using System;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using TotalUpdater.Next.Models;

namespace TotalUpdater.Next.Services
{
    public static class UpdateChecker
    {
        public static async Task<string?> GetLatestVersionAsync(SourceRule rule)
        {
            if (String.IsNullOrWhiteSpace(rule.SourceUrl) || String.IsNullOrWhiteSpace(rule.VersionPattern))
                return null;

            using (var client = new HttpClient())
            {
                client.Timeout = TimeSpan.FromSeconds(15);
                client.DefaultRequestHeaders.UserAgent.ParseAdd("TotalUpdaterNext/0.1");
                var html = await client.GetStringAsync(rule.SourceUrl).ConfigureAwait(false);
                var match = Regex.Match(html, rule.VersionPattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
                return match.Success && match.Groups.Count > 1 ? match.Groups[1].Value.Trim() : null;
            }
        }

        public static int CompareVersions(string installed, string latest)
        {
            Version installedVersion;
            Version latestVersion;
            if (!Version.TryParse(Normalize(installed), out installedVersion) || !Version.TryParse(Normalize(latest), out latestVersion)) return 0;
            return installedVersion.CompareTo(latestVersion);
        }

        private static string Normalize(string value)
        {
            var match = Regex.Match(value ?? "", "\\d+(?:\\.\\d+){1,3}");
            return match.Success ? match.Value : "";
        }
    }
}
