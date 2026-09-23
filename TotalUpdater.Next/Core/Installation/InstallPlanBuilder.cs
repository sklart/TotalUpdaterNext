using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TotalUpdater.Next.Core.Versions;
using TotalUpdater.Next.Infrastructure.Installation;

namespace TotalUpdater.Next.Core.Installation
{
    public sealed class InstallPlanBuilder
    {
        public InstallPlan Build(InstalledPlugin plugin, PackageInspection package, VersionValue canonicalVersion, Uri packageUrl, string backupRoot, UpdateCandidate candidate = null)
        {
            if (plugin == null || !plugin.FileExists || plugin.Identity == null || plugin.HasVersionConflict || !plugin.LocalVersion.ParsedValue.IsKnown)
                throw new InvalidOperationException("Нужен установленный плагин с известной согласованной версией.");
            if (plugin.Type != PluginType.Wcx && plugin.Type != PluginType.Wlx && plugin.Type != PluginType.Wfx && plugin.Type != PluginType.Wdx)
                throw new InvalidOperationException("Установка этого типа плагина не поддерживается.");
            if (canonicalVersion == null || !canonicalVersion.IsKnown || packageUrl == null ||
                (packageUrl.Scheme != Uri.UriSchemeHttps && packageUrl.Scheme != Uri.UriSchemeHttp))
                throw new InvalidOperationException("Нет canonical версии и HTTP(S)-адреса пакета.");
            if (package == null || !File.Exists(package.PackagePath) || !String.Equals(PackageInspector.Hash(package.PackagePath), package.PackageSha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("ZIP изменился после проверки.");
            if (String.IsNullOrWhiteSpace(package.Description)) throw new InvalidDataException("Нет description в pluginst.inf.");
            if (!String.Equals(package.Type, plugin.Type.ToString(), StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Тип pluginst.inf не совпадает с установленным плагином.");
            if (!String.IsNullOrWhiteSpace(package.Version) && VersionValue.Parse(package.Version).CompareTo(canonicalVersion) != VersionComparison.Equal)
                throw new InvalidDataException("Версия pluginst.inf не совпадает с canonical release.");
            if (String.IsNullOrWhiteSpace(package.DefaultDir) || package.DefaultDir == "." || package.DefaultDir.Contains("..") ||
                package.DefaultDir.IndexOfAny(new[] { '/', '\\', ':' }) >= 0 || Path.IsPathRooted(package.DefaultDir) ||
                package.DefaultDir.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
                package.DefaultDir.EndsWith(" ", StringComparison.Ordinal) || package.DefaultDir.EndsWith(".", StringComparison.Ordinal))
                throw new InvalidDataException("Некорректный defaultdir в pluginst.inf.");
            if (plugin.Type == PluginType.Wcx && String.IsNullOrWhiteSpace(package.DefaultExtension))
                throw new InvalidDataException("Для WCX отсутствует defaultextension.");
            if (plugin.Type == PluginType.Wcx && package.DefaultExtension.Split(new[] { ',', ' ', ';' }, StringSplitOptions.RemoveEmptyEntries).Any(x => x.IndexOfAny(new[] { '/', '\\', ':', '.', '=' }) >= 0))
                throw new InvalidDataException("Некорректный defaultextension.");
            var file = PackageInspector.SafeRelativePath(package.File ?? "");
            if (!plugin.Binaries.Any(binary => String.Equals(Path.GetFileName(binary.Path), Path.GetFileName(file), StringComparison.OrdinalIgnoreCase)) ||
                !package.Files.Any(x => String.Equals(x.RelativePath, file, StringComparison.OrdinalIgnoreCase)))
                throw new InvalidDataException("file в pluginst.inf не соответствует установленному бинарнику.");
            var target = Path.GetFullPath(Path.GetDirectoryName(plugin.PrimaryPath));
            if (!Directory.Exists(target) || !File.Exists(plugin.PrimaryPath)) throw new InvalidOperationException("Каталог установленного плагина недоступен.");
            var backupDirectory = Path.GetFullPath(Path.Combine(backupRoot, Guid.NewGuid().ToString("N")));
            if (backupDirectory.StartsWith(target + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) || target.StartsWith(backupDirectory + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Backup должен находиться вне каталога плагина.");
            var plan = new InstallPlan { Plugin = plugin, Package = package, TargetDirectory = target, BackupDirectory = backupDirectory,
                OldVersion = plugin.LocalVersion.RawValue, NewVersion = canonicalVersion.Raw, PackageUrl = packageUrl.AbsoluteUri,
                CanonicalSource = candidate?.CanonicalVersionSource?.ProviderName ?? "", DownloadSource = candidate?.DownloadSource?.ProviderName ?? "",
                DownloadAuthority = candidate?.DownloadSource == null ? "" : candidate.DownloadSource.Authority.ToString() };
            foreach (var source in package.Files.Where(x => !String.Equals(x.RelativePath, "pluginst.inf", StringComparison.OrdinalIgnoreCase)))
            {
                // Do not let package-supplied subdirectories escape the installed family directory.
                var destination = Path.GetFullPath(Path.Combine(target, source.RelativePath));
                if (!destination.StartsWith(target + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Путь выходит за каталог плагина.");
                var exists = File.Exists(destination);
                if (String.Equals(Path.GetExtension(destination), ".ini", StringComparison.OrdinalIgnoreCase) && exists) plan.UserConfigRisk = true;
                plan.Files.Add(new InstallFile { Source = source, Destination = destination, ReplacesExisting = exists });
            }
            if (plan.UserConfigRisk) throw new InvalidOperationException("UserConfigRisk: ZIP содержит существующий пользовательский INI; автоматическая установка запрещена.");
            var installed = plugin.Binaries.Where(x => x.Exists).ToList();
            if (installed.Count == 0 || installed.Any(binary => !plan.Files.Any(x => String.Equals(x.Destination, Path.GetFullPath(binary.Path), StringComparison.OrdinalIgnoreCase))))
                throw new InvalidDataException("Пакет не обновляет все существующие варианты x86/Unicode/x64.");
            return plan;
        }
    }
}
