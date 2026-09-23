using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using TotalUpdater.Next.Core.Installation;
using TotalUpdater.Next.TotalCommander;

namespace TotalUpdater.Next.Infrastructure.Installation
{
    public sealed class PackageInspector
    {
        private readonly PackageLimits _limits;
        public PackageInspector(PackageLimits limits = null) { _limits = limits ?? new PackageLimits(); }

        public PackageInspection Inspect(string packagePath)
        {
            if (!String.Equals(Path.GetExtension(packagePath), ".zip", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Автоустановка разрешена только для ZIP.");
            var stage = Path.Combine(Path.GetTempPath(), "TotalUpdaterNext", "staging", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(stage);
            try
            {
                var result = new PackageInspection { PackagePath = packagePath, StagingDirectory = stage, PackageSha256 = Hash(packagePath) };
                using (var zip = ZipFile.OpenRead(packagePath))
                {
                    if (zip.Entries.Count > _limits.MaxEntries) throw new InvalidDataException("Слишком много записей ZIP.");
                    var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    long total = 0;
                    foreach (var entry in zip.Entries)
                    {
                        var relative = SafeRelativePath(entry.FullName);
                        if (!seen.Add(relative)) throw new InvalidDataException("Повторяющийся путь в ZIP: " + relative);
                        var unixType = (entry.ExternalAttributes >> 16) & 0xF000;
                        if (unixType != 0 && unixType != 0x8000 && unixType != 0x4000 ||
                            (((entry.ExternalAttributes >> 16) & (int)FileAttributes.ReparsePoint) != 0))
                            throw new InvalidDataException("ZIP содержит ссылку или специальный файл.");
                        var directory = entry.FullName.EndsWith("/", StringComparison.Ordinal);
                        if (directory) continue;
                        if (entry.Length > _limits.MaxFileBytes || entry.Length < 0 || total > _limits.MaxTotalBytes - entry.Length)
                            throw new InvalidDataException("Превышен лимит распакованных данных ZIP.");
                        if (entry.Length > 0 && (entry.CompressedLength == 0 || (double)entry.Length / entry.CompressedLength > _limits.MaxCompressionRatio))
                            throw new InvalidDataException("Подозрительная степень сжатия ZIP.");
                        total += entry.Length;
                        var destination = Path.GetFullPath(Path.Combine(stage, relative));
                        if (!destination.StartsWith(stage + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Выход за staging.");
                        Directory.CreateDirectory(Path.GetDirectoryName(destination));
                        using (var input = entry.Open())
                        using (var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                        {
                            var buffer = new byte[81920]; long written = 0; int count;
                            while ((count = input.Read(buffer, 0, buffer.Length)) > 0)
                            {
                                written += count;
                                if (written > entry.Length || written > _limits.MaxFileBytes) throw new InvalidDataException("Некорректный размер ZIP entry.");
                                output.Write(buffer, 0, count);
                            }
                            if (written != entry.Length) throw new InvalidDataException("Неполная запись ZIP.");
                        }
                        result.Files.Add(new PackageFile { RelativePath = relative, StagedPath = destination, Length = entry.Length, Sha256 = Hash(destination) });
                    }
                }
                var metadata = result.Files.SingleOrDefault(x => String.Equals(x.RelativePath, "pluginst.inf", StringComparison.OrdinalIgnoreCase));
                if (metadata == null) throw new InvalidDataException("В корне ZIP нет pluginst.inf.");
                var section = new IniDocumentReader().Read(metadata.StagedPath).GetSection("plugininstall");
                if (section == null) throw new InvalidDataException("Нет секции [plugininstall].");
                result.Description = section.GetValue("description"); result.Type = section.GetValue("type"); result.File = section.GetValue("file"); result.Version = section.GetValue("version");
                result.DefaultDir = section.GetValue("defaultdir"); result.DefaultExtension = section.GetValue("defaultextension");
                return result;
            }
            catch { Directory.Delete(stage, true); throw; }
        }

        public static string SafeRelativePath(string name)
        {
            if (String.IsNullOrWhiteSpace(name) || name.StartsWith("/", StringComparison.Ordinal) || name.StartsWith("\\", StringComparison.Ordinal) || name.IndexOf(':') >= 0)
                throw new InvalidDataException("Недопустимый путь ZIP: " + name);
            var parts = name.Replace('\\', '/').TrimEnd('/').Split('/');
            if (parts.Any(x => x.Length == 0 || x == "." || x == ".." || x.EndsWith(".", StringComparison.Ordinal) || x.EndsWith(" ", StringComparison.Ordinal)))
                throw new InvalidDataException("Недопустимый компонент пути ZIP: " + name);
            return Path.Combine(parts);
        }

        public static string Hash(string path)
        {
            using (var sha = SHA256.Create()) using (var stream = File.OpenRead(path))
                return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
        }
    }
}
