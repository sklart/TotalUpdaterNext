using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TotalUpdater.Next.Infrastructure.Installation;

namespace TotalUpdater.Next.Core.Installation
{
    public sealed class ArchitectureMismatchException : InvalidOperationException
    {
        public ArchitectureMismatchException(string message) : base("ArchitectureMismatch: " + message) { }
    }

    public static class NewPluginArchitectureValidator
    {
        public static PluginArchitecture DetectTotalCommander(string installDirectory)
        {
            if (String.IsNullOrWhiteSpace(installDirectory)) return PluginArchitecture.Unknown;
            var result = PluginArchitecture.Unknown;
            if (File.Exists(Path.Combine(installDirectory, "TOTALCMD.EXE"))) result |= PluginArchitecture.X86;
            if (File.Exists(Path.Combine(installDirectory, "TOTALCMD64.EXE"))) result |= PluginArchitecture.X64;
            return result;
        }

        public static PluginArchitecture DetectPackage(PackageInspection package, PluginType type)
        {
            if (package == null) return PluginArchitecture.Unknown;
            var primary = PackageInspector.SafeRelativePath(package.File ?? "");
            var directory = Path.GetDirectoryName(primary) ?? "";
            var stem = Path.GetFileNameWithoutExtension(primary);
            var extension = type == PluginType.Wfx ? ".wfx" : type == PluginType.Wlx ? ".wlx" : type == PluginType.Wdx ? ".wdx" : null;
            if (extension == null) return PluginArchitecture.Unknown;
            var result = PluginArchitecture.Unknown;
            foreach (var item in package.Files)
            {
                if (!String.Equals(Path.GetDirectoryName(item.RelativePath) ?? "", directory, StringComparison.OrdinalIgnoreCase)) continue;
                var name = Path.GetFileName(item.RelativePath);
                if (String.Equals(name, stem + extension, StringComparison.OrdinalIgnoreCase) ||
                    String.Equals(name, stem + ".u" + extension.Substring(1), StringComparison.OrdinalIgnoreCase)) result |= PluginArchitecture.X86;
                else if (String.Equals(name, stem + extension + "64", StringComparison.OrdinalIgnoreCase)) result |= PluginArchitecture.X64;
            }
            return result;
        }

        public static void Validate(string installDirectory, PackageInspection package, PluginType type)
        {
            var tc = DetectTotalCommander(installDirectory);
            var plugin = DetectPackage(package, type);
            if (tc == PluginArchitecture.Unknown)
                throw new ArchitectureMismatchException("В каталоге Total Commander не найден TOTALCMD.EXE или TOTALCMD64.EXE.");
            if ((plugin & tc) != tc)
                throw new ArchitectureMismatchException("Архитектуры TC (" + tc + ") и бинарников ZIP (" + plugin + ") несовместимы.");
        }
    }
}
