using System;
using System.IO;
using System.Runtime.InteropServices;

namespace TotalUpdater.WcxProbe
{
    internal static class Program
    {
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int GetPackerCapsDelegate();
        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)] private static extern IntPtr LoadLibraryW(string fileName);
        [DllImport("kernel32.dll", SetLastError = true)] private static extern IntPtr GetProcAddress(IntPtr module, string name);
        [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool FreeLibrary(IntPtr module);

        private static int Main(string[] args)
        {
            var architecture = Environment.Is64BitProcess ? "x64" : "x86";
            var success = false; var caps = 0; var error = "";
            try
            {
                var path = args != null && args.Length == 2 && args[0].Equals("--wcx-probe", StringComparison.OrdinalIgnoreCase) ? args[1] : null;
                if (String.IsNullOrWhiteSpace(path) || !Path.IsPathRooted(path) || !File.Exists(path)) error = "WCX path must be absolute and existing.";
                else if (!Supported(path, architecture)) error = "ProbeArchitectureMismatch.";
                else
                {
                    var module = LoadLibraryW(path);
                    if (module == IntPtr.Zero) error = "LoadLibrary failed: " + Marshal.GetLastWin32Error();
                    else try
                    {
                        var export = GetProcAddress(module, "GetPackerCaps"); if (export == IntPtr.Zero) export = GetProcAddress(module, "GetPackerCaps@0");
                        if (export == IntPtr.Zero) { success = true; caps = 0; error = "GetPackerCaps export missing; read-only WCX."; }
                        else { caps = ((GetPackerCapsDelegate)Marshal.GetDelegateForFunctionPointer(export, typeof(GetPackerCapsDelegate)))(); if (caps < 0) error = "GetPackerCaps returned negative value."; else { success = true; if (caps == 0) error = "GetPackerCaps returned zero; read-only WCX."; } }
                    }
                    finally { FreeLibrary(module); }
                }
            }
            catch (Exception ex) { error = ex.Message; }
            Console.Out.WriteLine(success + "|" + caps + "|" + architecture + "|" + Safe(error));
            return success ? 0 : 2;
        }

        private static bool Supported(string path, string architecture)
        {
            var extension = Path.GetExtension(path);
            return architecture == "x86" ? extension.Equals(".wcx", StringComparison.OrdinalIgnoreCase) || extension.Equals(".uwcx", StringComparison.OrdinalIgnoreCase) : extension.Equals(".wcx64", StringComparison.OrdinalIgnoreCase);
        }
        private static string Safe(string value) { return (value ?? "").Replace("|", " ").Replace("\r", " ").Replace("\n", " "); }
    }
}
