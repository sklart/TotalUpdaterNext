using System;
using System.Collections.Generic;

namespace TotalUpdater.Next.Core.Installation
{
    // Persisted transaction states. Hashes remain authoritative after a crash.
    public static class InstallStateMachine
    {
        public const string Prepared = "Prepared", Installing = "Installing", InstallConflict = "InstallConflict", Completed = "Completed";
        public const string RollingBack = "RollingBack", RecoveryConflict = "RecoveryConflict", RollbackVerificationFailed = "RollbackVerificationFailed", RolledBack = "RolledBack";
        public const string ConfigConflict = "ConfigConflict", ConfigRecoveryConflict = "ConfigRecoveryConflict";
        public const string PendingFile = "Pending", InstallingFile = "Installing", InstalledFile = "Installed", RestoredFile = "Restored";
        private static readonly IDictionary<string, string[]> Next = new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            { Prepared, new[] { Installing, RollingBack, InstallConflict, RecoveryConflict, ConfigConflict, ConfigRecoveryConflict } },
            { Installing, new[] { Completed, RollingBack, InstallConflict, RecoveryConflict, ConfigConflict, ConfigRecoveryConflict } },
            { InstallConflict, new[] { RollingBack, RecoveryConflict } },
            { Completed, new[] { RollingBack, RecoveryConflict, ConfigRecoveryConflict } },
            { RollingBack, new[] { RolledBack, RecoveryConflict, ConfigRecoveryConflict, RollbackVerificationFailed } },
            { RecoveryConflict, new[] { RollingBack, RecoveryConflict } },
            { ConfigConflict, new[] { RollingBack, ConfigRecoveryConflict } },
            { ConfigRecoveryConflict, new[] { RollingBack, ConfigRecoveryConflict } },
            { RollbackVerificationFailed, new[] { RollingBack, RecoveryConflict } },
            { RolledBack, new string[0] }
        };
        public static bool IsKnown(string state) { return state != null && Next.ContainsKey(state); }
        public static bool IsKnownFile(string state) { return state == PendingFile || state == InstallingFile || state == InstalledFile || state == RestoredFile; }
        public static void Set(InstallManifest manifest, string next)
        {
            if (manifest == null || !IsKnown(manifest.State) || !IsKnown(next) ||
                (manifest.State != next && Array.IndexOf(Next[manifest.State], next) < 0))
                throw new InvalidOperationException("Недопустимый переход manifest: " + manifest?.State + " -> " + next);
            manifest.State = next;
        }
        public static void SetFile(InstallManifestFile file, string next)
        {
            var current = file.State ?? PendingFile; // 0.8.0/0.8.1 manifests did not persist file state.
            if (!IsKnownFile(next) || !IsKnownFile(current) ||
                (current == PendingFile && next != InstallingFile && next != RestoredFile && next != PendingFile) ||
                (current == InstallingFile && next != InstalledFile && next != RestoredFile && next != InstallingFile) ||
                (current == InstalledFile && next != RestoredFile && next != InstalledFile) ||
                (current == RestoredFile && next != RestoredFile))
                throw new InvalidOperationException("Недопустимый переход файла manifest: " + current + " -> " + next);
            file.State = next;
        }
    }
}
