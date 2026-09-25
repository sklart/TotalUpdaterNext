using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TotalUpdater.Next.Catalog;
using TotalUpdater.Next.Core.Versions;
using TotalUpdater.Next.Infrastructure.Installation;
using TotalUpdater.Next.TotalCommander;

namespace TotalUpdater.Next.Core.Installation
{
    public sealed class NewPluginInstallPlanBuilder
    {
        private readonly TotalCommanderConfigurationResolver _paths;
        private readonly IIniDocumentReader _reader;
        private readonly IniPatchEngine _patches;
        public NewPluginInstallPlanBuilder(TotalCommanderConfigurationResolver paths = null, IIniDocumentReader reader = null, IniPatchEngine patches = null)
        { _paths = paths ?? new TotalCommanderConfigurationResolver(); _reader = reader ?? new IniDocumentReader(); _patches = patches ?? new IniPatchEngine(); }

        public NewPluginInstallPlan Build(PluginCatalogEntry entry, CatalogInstallCandidate candidate, PackageInspection package,
            TotalCommanderConfiguration configuration, IEnumerable<InstalledPlugin> installed, string backupRoot)
        {
            if (entry == null || candidate == null || candidate.Entry == null || !String.Equals(entry.Id, candidate.Entry.Id, StringComparison.OrdinalIgnoreCase) ||
                configuration == null || String.IsNullOrWhiteSpace(configuration.IniPath) || !File.Exists(configuration.IniPath))
                throw new InvalidOperationException("Нужны каталог, проверенный источник и существующий wincmd.ini.");
            var type = entry.PluginType;
            if (type != PluginType.Wcx && type != PluginType.Wfx && type != PluginType.Wlx && type != PluginType.Wdx)
                throw new InvalidOperationException("Новая установка разрешена только для WCX/WFX/WLX/WDX.");
            if (installed != null && installed.Any(x => x.Identity != null && String.Equals(x.Identity.Id, entry.Id, StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException("Плагин уже установлен; используйте обновление.");
            if (candidate.AuthorityConflict || !candidate.Version.IsKnown || candidate.DownloadUrl == null ||
                (candidate.DownloadUrl.Scheme != Uri.UriSchemeHttps && candidate.DownloadUrl.Scheme != Uri.UriSchemeHttp))
                throw new InvalidOperationException("Нет однозначной версии и безопасного адреса пакета.");
            if (package == null || !File.Exists(package.PackagePath) ||
                !String.Equals(PackageInspector.Hash(package.PackagePath), package.PackageSha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("ZIP изменён после проверки.");
            if (String.IsNullOrWhiteSpace(package.Description) || !String.Equals(package.Type, type.ToString(), StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Некорректные description/type в pluginst.inf.");
            if (!String.IsNullOrWhiteSpace(package.Version) && VersionValue.Parse(package.Version).CompareTo(candidate.Version) != VersionComparison.Equal)
                throw new InvalidDataException("Версия pluginst.inf отличается от проверенной версии.");
            if (String.IsNullOrWhiteSpace(package.DefaultDir) || package.DefaultDir == "." || package.DefaultDir.Contains("..") ||
                package.DefaultDir.IndexOfAny(new[] { '/', '\\', ':' }) >= 0 || Path.IsPathRooted(package.DefaultDir) ||
                package.DefaultDir.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
                package.DefaultDir.EndsWith(" ", StringComparison.Ordinal) || package.DefaultDir.EndsWith(".", StringComparison.Ordinal))
                throw new InvalidDataException("Некорректный defaultdir.");
            var reserved = new[] { "CON", "PRN", "AUX", "NUL", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
                "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9" };
            if (reserved.Contains(package.DefaultDir.Split('.')[0], StringComparer.OrdinalIgnoreCase))
                throw new InvalidDataException("defaultdir зарезервирован Windows.");
            var pluginFile = PackageInspector.SafeRelativePath(package.File ?? "");
            if (!package.Files.Any(x => String.Equals(x.RelativePath, pluginFile, StringComparison.OrdinalIgnoreCase)))
                throw new InvalidDataException("Файл плагина из pluginst.inf отсутствует в ZIP.");
            var ext = type == PluginType.Wcx ? ".wcx" : type == PluginType.Wfx ? ".wfx" : type == PluginType.Wlx ? ".wlx" : ".wdx";
            if (!NormalizeBinaryName(pluginFile).EndsWith(ext, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Тип бинарника не совпадает с pluginst.inf.");
            if (entry.Aliases == null || !entry.Aliases.Any(alias => String.Equals(NormalizeBinaryName(alias), NormalizeBinaryName(Path.GetFileName(pluginFile)), StringComparison.OrdinalIgnoreCase)))
                throw new InvalidDataException("Бинарник ZIP не соответствует alias записи каталога.");
            NewPluginArchitectureValidator.Validate(configuration.InstallDirectory, package, type);
            var wcxExtensions = new List<string>();
            if (type == PluginType.Wcx)
            {
                if (!entry.AllowsAutomaticInstall || !entry.HasVerifiedWcxRegistration)
                    throw new InvalidOperationException("Пакет проверен, но параметры регистрации WCX не подтверждены. Автоматическая установка недоступна.");
                var evidence = entry.WcxRegistration;
                if (!String.Equals(evidence.PackageSha256, package.PackageSha256, StringComparison.OrdinalIgnoreCase) || !WcxEvidenceMatchesInstalledArchitecture(evidence, package, pluginFile, configuration.InstallDirectory))
                    throw new InvalidOperationException("WCX registration evidence outdated: hash пакета или бинарника не соответствует подтверждённым данным.");
                IList<string> normalizedExtensions;
                if (!WcxRegistration.TryNormalizeExtensions(package.DefaultExtension, out normalizedExtensions))
                    throw new InvalidDataException("Некорректный WCX defaultextension.");
                wcxExtensions = normalizedExtensions.ToList();
                if (!WcxRegistration.SameExtensions(wcxExtensions, evidence.Extensions))
                    throw new InvalidDataException("WCX defaultextension отсутствует или не соответствует подтверждённым registration metadata.");
            }
            var baseValue = configuration.Document.GetSection("Configuration")?.GetValue("PluginBaseDir");
            var baseDir = String.IsNullOrWhiteSpace(baseValue) ? Path.Combine(configuration.InstallDirectory, "plugins") : _paths.ExpandPath(baseValue, configuration);
            var target = Path.GetFullPath(Path.Combine(baseDir, type.ToString().ToLowerInvariant(), package.DefaultDir));
            var backup = Path.GetFullPath(Path.Combine(backupRoot, Guid.NewGuid().ToString("N")));
            if (backup.StartsWith(target + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
                target.StartsWith(backup + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Backup не может находиться внутри plugin target.");
            var plan = new NewPluginInstallPlan { PluginId = entry.Id, PluginType = type, TotalCommanderDirectory = configuration.InstallDirectory, Version = candidate.Version.Raw,
                TargetDirectory = target, BackupDirectory = backup, Package = package, PackageUrl = candidate.DownloadUrl.AbsoluteUri };
            foreach (var source in package.Files.Where(x => !String.Equals(x.RelativePath, "pluginst.inf", StringComparison.OrdinalIgnoreCase)))
            {
                var destination = Path.GetFullPath(Path.Combine(target, source.RelativePath));
                if (!destination.StartsWith(target + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Файл выходит за target.");
                if (File.Exists(destination) || Directory.Exists(destination)) throw new InvalidOperationException("Target уже существует: " + destination);
                plan.Files.Add(new InstallFile { Source = source, Destination = destination });
            }
            plan.PrimaryPath = Path.Combine(target, pluginFile);
            var stem = Path.GetFileNameWithoutExtension(pluginFile);
            foreach (var suffix in new[] { ext, ".u" + ext.Substring(1), ext + "64" })
            {
                var companion = Path.Combine(Path.GetDirectoryName(plan.PrimaryPath), stem + suffix);
                if (plan.Files.Any(x => String.Equals(x.Destination, companion, StringComparison.OrdinalIgnoreCase))) plan.RequiredBinaryPaths.Add(companion);
            }
            if (!plan.RequiredBinaryPaths.Contains(plan.PrimaryPath, StringComparer.OrdinalIgnoreCase)) plan.RequiredBinaryPaths.Add(plan.PrimaryPath);
            var section = type == PluginType.Wcx ? "PackerPlugins" : type == PluginType.Wfx ? "FileSystemPlugins" : type == PluginType.Wlx ? "ListerPlugins" : "ContentPlugins";
            var patches = new Dictionary<string, NewPluginConfigPatch>(StringComparer.OrdinalIgnoreCase);
            var registrationPath = ResolveSectionPath(configuration, section);
            var existing = _reader.Read(registrationPath).GetSection(section);
            var hasX64 = plan.RequiredBinaryPaths.Any(x => x.EndsWith(ext + "64", StringComparison.OrdinalIgnoreCase));
            var markerPath = hasX64 ? ResolveSectionPath(configuration, section + "64") : null;
            var markerEntries = hasX64 ? _reader.Read(markerPath).GetSection(section + "64")?.Entries : null;
            string key = null;
            if (type == PluginType.Wcx)
            {
                foreach (var extension in wcxExtensions)
                {
                    var current = existing?.GetValue(extension);
                    if (current != null)
                    {
                        string caps; string registeredPath;
                        if (!TryParsePackerRegistration(current, out caps, out registeredPath)) throw new InvalidOperationException("Malformed registration: " + extension);
                        if (!String.Equals(Path.GetFullPath(registeredPath), Path.GetFullPath(plan.PrimaryPath), StringComparison.OrdinalIgnoreCase))
                            throw new InvalidOperationException("RegistrationConflict: extension занято другим WCX: " + extension);
                        continue;
                    }
                    AddPatch(patches, registrationPath, section, extension, entry.WcxRegistration.PackerCaps.ToString(System.Globalization.CultureInfo.InvariantCulture) + "," + plan.PrimaryPath);
                }
            }
            else if (type == PluginType.Wfx)
            {
                key = stem;
                if (String.IsNullOrWhiteSpace(key) || key.IndexOfAny(new[] { '=', '\r', '\n', '[', ']' }) >= 0)
                    throw new InvalidDataException("Недопустимое имя WFX.");
                if (existing?.GetValue(key) != null) throw new InvalidOperationException("Имя WFX уже занято: " + key);
            }
            else
            {
                var occupied = new HashSet<int>();
                if (existing != null) foreach (var item in existing.Entries) if (Int32.TryParse(item.Key, out var n) && n >= 0) occupied.Add(n);
                if (markerEntries != null) foreach (var item in markerEntries) if (Int32.TryParse(item.Key, out var n) && n >= 0) occupied.Add(n);
                var next = 0; while (occupied.Contains(next)) next++;
                key = next.ToString(System.Globalization.CultureInfo.InvariantCulture);
            }
            if (type != PluginType.Wcx)
            {
                AddPatch(patches, registrationPath, section, key, plan.PrimaryPath);
                if (hasX64) AddPatch(patches, markerPath, section + "64", key, "1");
            }
            plan.ConfigurationFiles.AddRange(patches.Values);
            return plan;
        }

        private string ResolveSectionPath(TotalCommanderConfiguration configuration, string sectionName)
        {
            var section = configuration.Document.GetSection(sectionName);
            var redirect = section?.GetValue("RedirectSection")?.Trim();
            if (String.IsNullOrWhiteSpace(redirect) || redirect == "0") return configuration.IniPath;
            var path = redirect == "1" ? configuration.AlternateUserIni : _paths.ExpandRedirectPath(redirect, configuration);
            if (String.IsNullOrWhiteSpace(path) || !Path.IsPathRooted(path) || !File.Exists(path))
                throw new InvalidDataException("RedirectSection не найден или недопустим: [" + sectionName + "]");
            if (String.Equals(Path.GetFullPath(path), Path.GetFullPath(configuration.IniPath), StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("RedirectSection образует цикл: [" + sectionName + "]");
            var redirected = _reader.Read(path).GetSection(sectionName)?.GetValue("RedirectSection");
            if (!String.IsNullOrWhiteSpace(redirected) && redirected.Trim() != "0")
                throw new InvalidDataException("Рекурсивный RedirectSection не поддерживается: [" + sectionName + "]");
            return Path.GetFullPath(path);
        }

        private void AddPatch(IDictionary<string, NewPluginConfigPatch> patches, string path, string section, string key, string value)
        {
            NewPluginConfigPatch item;
            if (!patches.TryGetValue(path, out item))
            {
                var first = _patches.Begin(path);
                item = new NewPluginConfigPatch { Path = Path.GetFullPath(path), OriginalBytes = first.OriginalBytes, InstalledBytes = first.NewBytes,
                    OriginalSha256 = first.OriginalSha256 };
                patches.Add(path, item);
            }
            var current = new IniPatchResult { OriginalBytes = item.OriginalBytes, NewBytes = item.InstalledBytes };
            _patches.Add(current, section, key, value);
            item.InstalledBytes = current.NewBytes; item.InstalledSha256 = current.InstalledSha256;
            item.Changes.Add(new InstallConfigurationChange { Section = section, Key = key, Value = value });
        }
        private static string NormalizeBinaryName(string value)
        {
            if (String.IsNullOrWhiteSpace(value)) return "";
            var name = Path.GetFileName(value);
            var extension = Path.GetExtension(name);
            if (extension.StartsWith(".uw", StringComparison.OrdinalIgnoreCase)) return Path.GetFileNameWithoutExtension(name) + ".w" + extension.Substring(3);
            if (extension.StartsWith(".w", StringComparison.OrdinalIgnoreCase) && extension.EndsWith("64", StringComparison.OrdinalIgnoreCase))
                return Path.GetFileNameWithoutExtension(name) + extension.Substring(0, extension.Length - 2);
            return name;
        }
        private static bool TryParsePackerRegistration(string value, out string caps, out string path)
        {
            caps = null; path = null; var comma = value == null ? -1 : value.IndexOf(',');
            if (comma <= 0 || comma != value.LastIndexOf(',') || !Int32.TryParse(value.Substring(0, comma).Trim(), out var parsed) || parsed < 0) return false;
            caps = parsed.ToString(System.Globalization.CultureInfo.InvariantCulture); path = value.Substring(comma + 1).Trim(); return !String.IsNullOrWhiteSpace(path) && !path.Contains("\r") && !path.Contains("\n");
        }
        private static bool WcxEvidenceMatchesInstalledArchitecture(WcxRegistrationEvidence evidence, PackageInspection package, string pluginFile, string tcDirectory)
        {
            var tc = NewPluginArchitectureValidator.DetectTotalCommander(tcDirectory); var directory = Path.GetDirectoryName(pluginFile) ?? ""; var stem = Path.GetFileNameWithoutExtension(pluginFile);
            var x86 = package.Files.FirstOrDefault(x => String.Equals(Path.GetDirectoryName(x.RelativePath) ?? "", directory, StringComparison.OrdinalIgnoreCase) && (String.Equals(Path.GetFileName(x.RelativePath), stem + ".wcx", StringComparison.OrdinalIgnoreCase) || String.Equals(Path.GetFileName(x.RelativePath), stem + ".uwcx", StringComparison.OrdinalIgnoreCase)));
            var x64 = package.Files.FirstOrDefault(x => String.Equals(Path.GetDirectoryName(x.RelativePath) ?? "", directory, StringComparison.OrdinalIgnoreCase) && String.Equals(Path.GetFileName(x.RelativePath), stem + ".wcx64", StringComparison.OrdinalIgnoreCase));
            var x86Matches = (tc & PluginArchitecture.X86) == 0 || (x86 != null && String.Equals(evidence.X86BinarySha256, x86.Sha256, StringComparison.OrdinalIgnoreCase));
            var x64Matches = (tc & PluginArchitecture.X64) == 0 || (x64 != null && String.Equals(evidence.X64BinarySha256, x64.Sha256, StringComparison.OrdinalIgnoreCase));
            return x86Matches && x64Matches;
        }
    }
}
