using System;
using TotalUpdater.Next.Catalog;

namespace TotalUpdater.Next.UI
{
    internal static class SourceStatusText
    {
        public static string FriendlyName(CatalogSource source, string providerName)
        {
            var provider = (source == null ? providerName : source.Provider) ?? "";
            if (provider.Equals("totalcmd.net", StringComparison.OrdinalIgnoreCase) || provider.Equals("totalcmd.net-index", StringComparison.OrdinalIgnoreCase)) return "totalcmd.net";
            if (provider.Equals("ghisler", StringComparison.OrdinalIgnoreCase) || provider.Equals("ghisler-plugins", StringComparison.OrdinalIgnoreCase)) return "ghisler.com";
            if (provider.Equals("github", StringComparison.OrdinalIgnoreCase)) return "GitHub API";
            return String.IsNullOrWhiteSpace(providerName) ? "источником" : providerName;
        }

        public static string Unavailable(CatalogSource source, string providerName)
        {
            return "Не удалось связаться с " + FriendlyName(source, providerName);
        }
    }
}
