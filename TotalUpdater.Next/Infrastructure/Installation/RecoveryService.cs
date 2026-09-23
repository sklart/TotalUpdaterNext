using System;
using System.Collections.Generic;
using TotalUpdater.Next.Core;
using TotalUpdater.Next.Core.Installation;

namespace TotalUpdater.Next.Infrastructure.Installation
{
    public sealed class RecoveryService
    {
        private readonly string _backupRoot;
        private readonly RollbackService _rollback;
        private readonly NewPluginRollbackService _newPluginRollback;
        public RecoveryService(string backupRoot, RollbackService rollback = null, NewPluginRollbackService newPluginRollback = null)
        { _backupRoot = backupRoot; _rollback = rollback ?? new RollbackService(); _newPluginRollback = newPluginRollback ?? new NewPluginRollbackService(); }
        public IList<InstallManifest> FindPending() { return BackupService.FindIncomplete(_backupRoot); }
        public void Recover(InstallManifest manifest, Func<InstalledPlugin> rediscover)
        {
            if (manifest == null || (manifest.State != InstallStateMachine.Prepared && manifest.State != InstallStateMachine.Installing &&
                manifest.State != InstallStateMachine.InstallConflict && manifest.State != InstallStateMachine.RollingBack &&
                manifest.State != InstallStateMachine.RecoveryConflict && manifest.State != InstallStateMachine.ConfigRecoveryConflict &&
                manifest.State != InstallStateMachine.ConfigConflict && manifest.State != InstallStateMachine.RollbackVerificationFailed))
                throw new InvalidOperationException("Этот manifest не ожидает восстановления.");
            if (manifest.ManifestVersion == 4)
            {
                if (manifest.State == InstallStateMachine.ConfigConflict) _newPluginRollback.RollbackBeforeConfigWrite(manifest, _backupRoot);
                else _newPluginRollback.Rollback(manifest, _backupRoot);
                return;
            }
            _rollback.Rollback(manifest, _backupRoot, rediscover);
        }
    }
}
