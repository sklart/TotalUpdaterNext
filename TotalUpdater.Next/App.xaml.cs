using System.Windows;
using System.Net;

namespace TotalUpdater.Next
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            base.OnStartup(e);
        }
    }
}
