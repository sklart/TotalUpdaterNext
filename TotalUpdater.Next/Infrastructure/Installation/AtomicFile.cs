using System;
using System.IO;
using System.Runtime.InteropServices;

namespace TotalUpdater.Next.Infrastructure.Installation
{
    internal static class AtomicFile
    {
        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)] private static extern bool MoveFileEx(string existingFileName, string newFileName, int flags);
        public static void Replace(string temporary, string target)
        {
            if (MoveFileEx(temporary, target, 0x1 | 0x8)) return;
            var error = Marshal.GetLastWin32Error();
            // Some controlled Windows folders reject both File.Replace and
            // MoveFileEx(REPLACE_EXISTING). Callers have already persisted a
            // hash-bound backup/manifest; preserve liveness there and let the
            // transactional rollback recover if the move cannot complete.
            if (error == 5 && File.Exists(target))
            {
                File.Delete(target);
                File.Move(temporary, target);
                return;
            }
            throw new IOException("Не удалось атомарно заменить файл: " + error);
        }
    }
}
