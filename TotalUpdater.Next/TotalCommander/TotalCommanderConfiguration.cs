using System.Collections.Generic;

namespace TotalUpdater.Next.TotalCommander
{
    public enum InstallDirectorySource { CommanderPath, Registry, Configuration, Fallback }

    public sealed class TotalCommanderConfiguration
    {
        public string IniPath { get; set; } = "";
        public string InstallDirectory { get; set; } = "";
        public InstallDirectorySource InstallDirectorySource { get; set; }
        public string AlternateUserIni { get; set; } = "";
        public IniDocument Document { get; set; }
        public IList<string> Warnings { get; private set; } = new List<string>();
    }
}
