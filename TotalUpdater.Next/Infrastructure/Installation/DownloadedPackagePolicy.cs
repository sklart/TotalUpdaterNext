using System;
using System.IO;
using TotalUpdater.Next.Core;
using TotalUpdater.Next.Core.Versions;

namespace TotalUpdater.Next.Infrastructure.Installation
{
    public static class DownloadedPackagePolicy
    {
        public static void Record(UpdateCandidate candidate, string path)
        {
            if (candidate == null || candidate.DownloadUrl == null || !candidate.AvailableVersion.IsKnown || !File.Exists(path))
                throw new InvalidOperationException("Нельзя привязать файл к неподтверждённому обновлению.");
            candidate.DownloadedPackagePath = Path.GetFullPath(path);
            candidate.DownloadedPackageSha256 = PackageInspector.Hash(path);
            candidate.DownloadedPackageUrl = candidate.DownloadUrl.AbsoluteUri;
            candidate.DownloadedCanonicalVersion = candidate.AvailableVersion.Raw;
        }
        public static void CarryForward(UpdateCandidate previous, UpdateCandidate current)
        {
            if (previous == null || current == null || String.IsNullOrWhiteSpace(previous.DownloadedPackagePath)) return;
            if (SameRelease(previous, current))
            {
                current.DownloadedPackagePath = previous.DownloadedPackagePath;
                current.DownloadedPackageSha256 = previous.DownloadedPackageSha256;
                current.DownloadedPackageUrl = previous.DownloadedPackageUrl;
                current.DownloadedCanonicalVersion = previous.DownloadedCanonicalVersion;
            }
        }
        public static string Resolve(UpdateCandidate candidate)
        {
            if (candidate == null || String.IsNullOrWhiteSpace(candidate.DownloadedPackagePath)) return null;
            if (candidate.DownloadUrl == null || candidate.DownloadedPackageUrl != candidate.DownloadUrl.AbsoluteUri ||
                !String.Equals(candidate.DownloadedCanonicalVersion, candidate.AvailableVersion.Raw, StringComparison.Ordinal) ||
                !File.Exists(candidate.DownloadedPackagePath) ||
                !String.Equals(PackageInspector.Hash(candidate.DownloadedPackagePath), candidate.DownloadedPackageSha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Скачанный ZIP изменён или относится к другой версии/URL. Установка заблокирована; скачайте пакет заново.");
            return candidate.DownloadedPackagePath;
        }
        private static bool SameRelease(UpdateCandidate previous, UpdateCandidate current)
        {
            return current.DownloadUrl != null && previous.DownloadUrl != null &&
                previous.DownloadUrl.AbsoluteUri == current.DownloadUrl.AbsoluteUri &&
                previous.AvailableVersion.CompareTo(current.AvailableVersion) == VersionComparison.Equal &&
                previous.DownloadedPackageUrl == current.DownloadUrl.AbsoluteUri &&
                String.Equals(previous.DownloadedCanonicalVersion, current.AvailableVersion.Raw, StringComparison.Ordinal) &&
                current.State == UpdateState.UpdateAvailable && !current.AuthorityConflict;
        }
    }
}
