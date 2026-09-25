using System;
using System.IO;
using TotalUpdater.Next.Core.Installation;

namespace TotalUpdater.Next.Tests
{
    internal static class WcxProbeTests
    {
        public static void Run(Action<bool, string> check)
        {
            var x86 = WcxProbeRunner.ParseOutput("True|251|x86|");
            var x64 = WcxProbeRunner.ParseOutput("True|251|x64|");
            check(x86.Success && x86.Caps == 251 && x86.Architecture == "x86", "WCX probe x86 successful output");
            check(x64.Success && x64.Caps == 251 && x64.Architecture == "x64", "WCX probe x64 successful output");
            check(WcxProbeRunner.ParseOutput("True|0|x86|GetPackerCaps export missing").Success && WcxProbeRunner.ParseOutput("True|0|x86|GetPackerCaps export missing").Caps == 0, "WCX missing GetPackerCaps is valid read-only caps zero");
            check(!WcxProbeRunner.ParseOutput("True|-1|x86|bad").Success && !WcxProbeRunner.ParseOutput("True|1|arm64|bad").Success && !WcxProbeRunner.ParseOutput("garbage").Success, "WCX probe rejects negative caps, wrong bitness and malformed output");
            check(WcxProbeRunner.ExpectedArchitecture("x.wcx") == "x86" && WcxProbeRunner.ExpectedArchitecture("x.uwcx") == "x86" && WcxProbeRunner.ExpectedArchitecture("x.wcx64") == "x64", "WCX probe architecture routing");
            var root = Path.Combine(Path.GetTempPath(), "TotalUpdaterNext", "tests", "wcx-probe-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
            try
            {
                var binary = Path.Combine(root, "sample.wcx"); File.WriteAllText(binary, "fixture");
                var crash = Path.Combine(root, "crash.cmd"); File.WriteAllText(crash, "@exit /b 7");
                check(!new WcxProbeRunner(crash).Probe(binary, TimeSpan.FromSeconds(1)).Success, "WCX probe helper crash is reported");
                var timeout = Path.Combine(root, "timeout.cmd"); File.WriteAllText(timeout, "@ping 127.0.0.1 -n 30 >nul");
                check(!new WcxProbeRunner(timeout).Probe(binary, TimeSpan.FromMilliseconds(100)).Success, "WCX probe helper timeout is killed and reported");
            }
            finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
        }
    }
}
