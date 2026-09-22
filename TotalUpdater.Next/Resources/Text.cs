using System.Globalization;
using System.Reflection;
using System.Resources;

namespace TotalUpdater.Next.Resources
{
    public static class Text
    {
        private static readonly ResourceManager Manager = new ResourceManager("TotalUpdater.Next.Resources.Strings", Assembly.GetExecutingAssembly());
        public static string Get(string key) { return Manager.GetString(key, CultureInfo.CurrentUICulture) ?? key; }
    }
}
