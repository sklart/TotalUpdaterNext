using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace TotalUpdater.Next.Core.Installation
{
    public sealed class WcxProbeResult
    {
        public bool Success { get; set; }
        public int Caps { get; set; }
        public string Architecture { get; set; }
        public string Error { get; set; }
    }

    // The same single-file release starts this narrow command in a separate
    // process. The UI process never loads a WCX binary.
    public sealed class WcxProbeRunner
    {
        private readonly string _executable;
        public WcxProbeRunner(string executable = null) { _executable = executable ?? typeof(WcxProbeRunner).Assembly.Location; }
        public WcxProbeResult Probe(string absoluteWcxPath, TimeSpan timeout)
        {
            if (!Path.IsPathRooted(absoluteWcxPath) || !File.Exists(absoluteWcxPath)) return Failure("WCX path must be an existing absolute path.");
            var start = new ProcessStartInfo(_executable, "--wcx-probe \"" + absoluteWcxPath + "\"") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
            using (var process = Process.Start(start))
            {
                if (!process.WaitForExit((int)Math.Max(1, timeout.TotalMilliseconds))) { try { process.Kill(); } catch { } return Failure("WcxProbe timeout."); }
                var output = process.StandardOutput.ReadToEnd().Trim();
                if (process.ExitCode != 0) return Failure(String.IsNullOrWhiteSpace(output) ? process.StandardError.ReadToEnd().Trim() : output);
                return Parse(output);
            }
        }
        private static WcxProbeResult Parse(string output)
        {
            // Deliberately strict, one line only: success|caps|x86/x64|error
            var parts = (output ?? "").Split('|'); int caps;
            if (parts.Length != 4 || !Boolean.TryParse(parts[0], out var success) || !Int32.TryParse(parts[1], out caps) || (parts[2] != "x86" && parts[2] != "x64")) return Failure("Unexpected WcxProbe output.");
            return new WcxProbeResult { Success = success, Caps = caps, Architecture = parts[2], Error = parts[3] };
        }
        private static WcxProbeResult Failure(string error) { return new WcxProbeResult { Success = false, Error = error ?? "WcxProbe failed." }; }
    }

    public static class WcxProbeHost
    {
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int GetPackerCapsDelegate();
        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)] private static extern IntPtr LoadLibraryW(string fileName);
        [DllImport("kernel32.dll", SetLastError = true)] private static extern IntPtr GetProcAddress(IntPtr module, string name);
        [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool FreeLibrary(IntPtr module);
        public static int Run(string[] args)
        {
            WcxProbeResult result;
            try { result = Probe(args == null || args.Length != 1 ? null : args[0]); }
            catch (Exception ex) { result = new WcxProbeResult { Success = false, Architecture = Environment.Is64BitProcess ? "x64" : "x86", Error = Safe(ex.Message) }; }
            Console.Out.WriteLine(result.Success + "|" + result.Caps + "|" + (result.Architecture ?? (Environment.Is64BitProcess ? "x64" : "x86")) + "|" + Safe(result.Error)); return result.Success ? 0 : 2;
        }
        private static WcxProbeResult Probe(string path)
        {
            if (String.IsNullOrWhiteSpace(path) || !Path.IsPathRooted(path) || !File.Exists(path)) return Fail("WCX path must be absolute and existing.");
            var extension = Path.GetExtension(path); if (!extension.Equals(".wcx", StringComparison.OrdinalIgnoreCase) && !extension.Equals(".uwcx", StringComparison.OrdinalIgnoreCase) && !extension.Equals(".wcx64", StringComparison.OrdinalIgnoreCase)) return Fail("Not a WCX binary.");
            var module = LoadLibraryW(path); if (module == IntPtr.Zero) return Fail("LoadLibrary failed: " + Marshal.GetLastWin32Error());
            try
            {
                var export = GetProcAddress(module, "GetPackerCaps"); if (export == IntPtr.Zero) export = GetProcAddress(module, "GetPackerCaps@0");
                if (export == IntPtr.Zero) return Fail("GetPackerCaps export missing.");
                var caps = ((GetPackerCapsDelegate)Marshal.GetDelegateForFunctionPointer(export, typeof(GetPackerCapsDelegate)))();
                return new WcxProbeResult { Success = caps > 0, Caps = caps, Architecture = Environment.Is64BitProcess ? "x64" : "x86", Error = caps > 0 ? "" : "GetPackerCaps returned zero." };
            }
            finally { FreeLibrary(module); }
        }
        private static WcxProbeResult Fail(string error) { return new WcxProbeResult { Success = false, Caps = 0, Architecture = Environment.Is64BitProcess ? "x64" : "x86", Error = error }; }
        private static string Safe(string value) { return (value ?? "").Replace("|", " ").Replace("\r", " ").Replace("\n", " "); }
    }
}
