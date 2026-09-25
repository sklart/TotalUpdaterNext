using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Reflection;
using System.Security.Cryptography;
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
        public WcxProbeRunner(string executable = null) { _executable = executable; }
        public WcxProbeResult Probe(string absoluteWcxPath, TimeSpan timeout)
        {
            if (!Path.IsPathRooted(absoluteWcxPath) || !File.Exists(absoluteWcxPath)) return Failure("WCX path must be an existing absolute path.");
            var expectedArchitecture = ExpectedArchitecture(absoluteWcxPath);
            if (expectedArchitecture == null) return Failure("Unsupported WCX extension for probe.");
            var executable = _executable ?? WcxProbeHelperExtractor.Extract(expectedArchitecture);
            var start = new ProcessStartInfo(executable, "--wcx-probe \"" + absoluteWcxPath + "\"") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
            using (var process = Process.Start(start))
            {
                if (process == null) return Failure("WcxProbe process could not start.");
                using (var output = new ProbeOutputCapture())
                {
                    process.OutputDataReceived += (sender, args) => output.AppendOut(args.Data);
                    process.ErrorDataReceived += (sender, args) => output.AppendError(args.Data);
                    process.BeginOutputReadLine();
                    process.BeginErrorReadLine();
                    if (!process.WaitForExit((int)Math.Min(10000, Math.Max(1, timeout.TotalMilliseconds))))
                    {
                        try { process.Kill(); } catch { }
                        // A malicious helper can leave descendants holding its
                        // inherited pipes.  Bound both the process wait and
                        // asynchronous stream drain so this safety timeout
                        // itself cannot hang the regression suite.
                        try { process.WaitForExit(1000); } catch { }
                        output.WaitForDrain(1000);
                        return Failure("WcxProbe timeout.");
                    }
                    output.WaitForDrain(1000);
                    if (process.ExitCode != 0) return Failure(String.IsNullOrWhiteSpace(output.StandardOutput) ? output.StandardError : output.StandardOutput);
                    var result = ParseOutput(output.StandardOutput);
                    return result.Success && !String.Equals(result.Architecture, expectedArchitecture, StringComparison.Ordinal) ? Failure("ProbeArchitectureMismatch.") : result;
                }
            }
        }
        public static string ExpectedArchitecture(string path)
        {
            var extension = Path.GetExtension(path ?? "");
            if (extension.Equals(".wcx", StringComparison.OrdinalIgnoreCase) || extension.Equals(".uwcx", StringComparison.OrdinalIgnoreCase)) return "x86";
            return extension.Equals(".wcx64", StringComparison.OrdinalIgnoreCase) ? "x64" : null;
        }
        // Kept public so the command-line protocol remains regression-tested
        // without loading an untrusted native module in the test process.
        public static WcxProbeResult ParseOutput(string output)
        {
            // Deliberately strict, one line only: success|caps|x86/x64|error
            var parts = (output ?? "").Split('|'); int caps;
            if (parts.Length != 4 || !Boolean.TryParse(parts[0], out var success) || !Int32.TryParse(parts[1], out caps) || caps < 0 || (parts[2] != "x86" && parts[2] != "x64")) return Failure("Unexpected WcxProbe output.");
            return new WcxProbeResult { Success = success, Caps = caps, Architecture = parts[2], Error = parts[3] };
        }
        private static WcxProbeResult Failure(string error) { return new WcxProbeResult { Success = false, Error = error ?? "WcxProbe failed." }; }

        private sealed class ProbeOutputCapture : IDisposable
        {
            private const int MaximumLength = 16384;
            private readonly StringBuilder _standardOutput = new StringBuilder();
            private readonly StringBuilder _standardError = new StringBuilder();
            private readonly object _gate = new object();
            private readonly ManualResetEvent _standardOutputClosed = new ManualResetEvent(false);
            private readonly ManualResetEvent _standardErrorClosed = new ManualResetEvent(false);
            public string StandardOutput { get { lock (_gate) return _standardOutput.ToString().Trim(); } }
            public string StandardError { get { lock (_gate) return _standardError.ToString().Trim(); } }
            public void AppendOut(string value) { if (value == null) _standardOutputClosed.Set(); else Append(_standardOutput, value); }
            public void AppendError(string value) { if (value == null) _standardErrorClosed.Set(); else Append(_standardError, value); }
            public void WaitForDrain(int milliseconds) { WaitHandle.WaitAll(new WaitHandle[] { _standardOutputClosed, _standardErrorClosed }, milliseconds); }
            public void Dispose() { _standardOutputClosed.Dispose(); _standardErrorClosed.Dispose(); }
            private void Append(StringBuilder destination, string value)
            {
                if (value == null) return;
                lock (_gate)
                {
                    if (destination.Length >= MaximumLength) return;
                    if (destination.Length > 0) destination.AppendLine();
                    destination.Append(value.Length > MaximumLength - destination.Length ? value.Substring(0, MaximumLength - destination.Length) : value);
                }
            }
        }
    }

    internal static class WcxProbeHelperExtractor
    {
        internal static string Extract(string architecture)
        {
            var resource = architecture == "x86" ? "TotalUpdater.Next.WcxProbe.x86.exe" : "TotalUpdater.Next.WcxProbe.x64.exe";
            byte[] bytes;
            using (var input = Assembly.GetExecutingAssembly().GetManifestResourceStream(resource))
            {
                if (input == null) throw new InvalidOperationException("Embedded " + architecture + " WCX probe is missing.");
                using (var output = new MemoryStream()) { input.CopyTo(output); bytes = output.ToArray(); }
            }
            string hash;
            using (var sha = SHA256.Create()) hash = BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant();
            var directory = Path.Combine(Path.GetTempPath(), "TotalUpdaterNext", "WcxProbe", hash);
            var target = Path.Combine(directory, architecture == "x86" ? "WcxProbe.x86.exe" : "WcxProbe.x64.exe");
            Directory.CreateDirectory(directory);
            if (File.Exists(target) && String.Equals(Hash(target), hash, StringComparison.OrdinalIgnoreCase)) return target;
            var temporary = target + "." + Guid.NewGuid().ToString("N") + ".tmp";
            File.WriteAllBytes(temporary, bytes);
            if (File.Exists(target)) File.Delete(target);
            File.Move(temporary, target);
            return target;
        }
        private static string Hash(string path)
        {
            using (var sha = SHA256.Create()) using (var input = File.OpenRead(path)) return BitConverter.ToString(sha.ComputeHash(input)).Replace("-", "").ToLowerInvariant();
        }
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
                if (export == IntPtr.Zero) return new WcxProbeResult { Success = true, Caps = 0, Architecture = Environment.Is64BitProcess ? "x64" : "x86", Error = "GetPackerCaps export missing; read-only WCX." };
                var caps = ((GetPackerCapsDelegate)Marshal.GetDelegateForFunctionPointer(export, typeof(GetPackerCapsDelegate)))();
                return caps < 0 ? Fail("GetPackerCaps returned negative value.") : new WcxProbeResult { Success = true, Caps = caps, Architecture = Environment.Is64BitProcess ? "x64" : "x86", Error = caps == 0 ? "GetPackerCaps returned zero; read-only WCX." : "" };
            }
            finally { FreeLibrary(module); }
        }
        private static WcxProbeResult Fail(string error) { return new WcxProbeResult { Success = false, Caps = 0, Architecture = Environment.Is64BitProcess ? "x64" : "x86", Error = error }; }
        private static string Safe(string value) { return (value ?? "").Replace("|", " ").Replace("\r", " ").Replace("\n", " "); }
    }
}
