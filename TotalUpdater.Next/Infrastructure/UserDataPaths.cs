using System;
using System.IO;

namespace TotalUpdater.Next.Infrastructure
{
    public sealed class UserDataPaths
    {
        public UserDataPaths(string executableDirectory)
        {
            var portable = IsWritable(executableDirectory);
            RootDirectory = portable ? executableDirectory : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "TotalUpdaterNext");
            IsPortable = portable;
            SettingsPath = Path.Combine(RootDirectory, "TotalUpdater.ini");
            UserCatalogPath = Path.Combine(RootDirectory, "user-catalog.json");
            DownloadDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads", "TotalUpdaterNext");
        }

        public string RootDirectory { get; private set; }
        public bool IsPortable { get; private set; }
        public string SettingsPath { get; private set; }
        public string UserCatalogPath { get; private set; }
        public string DownloadDirectory { get; private set; }

        private static bool IsWritable(string directory)
        {
            try
            {
                var probe = Path.Combine(directory, ".totalupdater-write-test-" + Guid.NewGuid().ToString("N"));
                using (File.Create(probe)) { }
                File.Delete(probe);
                return true;
            }
            catch { return false; }
        }
    }
}
