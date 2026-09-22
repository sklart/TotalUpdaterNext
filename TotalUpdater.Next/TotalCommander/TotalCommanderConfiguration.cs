using System.Collections.Generic;

namespace TotalUpdater.Next.TotalCommander
{
    public sealed class TotalCommanderConfiguration
    {
        public string IniPath { get; set; } = "";
        public string InstallDirectory { get; set; } = "";
        public string AlternateUserIni { get; set; } = "";
        public IniDocument Document { get; set; }
        public IList<string> Warnings { get; private set; } = new List<string>();
    }
}
