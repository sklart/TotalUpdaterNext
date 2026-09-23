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
        public RecoveryService(string backupRoot, RollbackService rollback = null)
        { _backupRoot = backupRoot; _rollback = rollback ?? new RollbackService(); }
        public IList<InstallManifest> FindPending() { return BackupService.FindIncomplete(_backupRoot); }
        public void Recover(InstallManifest manifest, Func<InstalledPlugin> rediscover)
        {
            if (manifest == null || (manifest.State != InstallStateMachine.Prepared && manifest.State != InstallStateMachine.Installing &&
                manifest.State != InstallStateMachine.InstallConflict && manifest.State != InstallStateMachine.RollingBack &&
                manifest.State != InstallStateMachine.RecoveryConflict && manifest.State != InstallStateMachine.RollbackVerificationFailed))
                throw new InvalidOperationException("Этот manifest не ожидает восстановления.");
            _rollback.Rollback(manifest, _backupRoot, rediscover);
        }
    }
}
