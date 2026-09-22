namespace TotalUpdater.Next.Models
{
    public sealed class PluginRecord
    {
        public string Category { get; set; } = "Плагин";
        public string Name { get; set; } = "";
        public string FilePath { get; set; } = "";
        public string LocalVersion { get; set; } = "не определена";
        public string LatestVersion { get; set; } = "—";
        public string Status { get; set; } = "Не проверено";
        public string SourceUrl { get; set; } = "";
        public bool IsSelected { get; set; }
    }
}
