using System.Windows;
using System.Net;
using System.Linq;
using TotalUpdater.Next.Core.Installation;

namespace TotalUpdater.Next
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            if (e.Args != null && e.Args.Length > 0 && e.Args[0].Equals("--wcx-probe", System.StringComparison.OrdinalIgnoreCase))
            {
                Shutdown(WcxProbeHost.Run(e.Args.Skip(1).ToArray())); return;
            }
            base.OnStartup(e);
        }
    }
}
