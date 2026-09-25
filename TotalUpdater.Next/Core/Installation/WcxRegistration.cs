using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;

namespace TotalUpdater.Next.Core.Installation
{
    public static class WcxRegistration
    {
        public static IList<string> NormalizeExtensions(string raw)
        {
            IList<string> result; return TryNormalizeExtensions(raw, out result) ? result : new List<string>();
        }
        public static bool TryNormalizeExtensions(string raw, out IList<string> result)
        {
            result = new List<string>();
            if (String.IsNullOrWhiteSpace(raw)) return false;
            foreach (var item in raw.Split(','))
            {
                var token = (item ?? "").Trim().TrimStart('.');
                if (String.IsNullOrWhiteSpace(token) || token.IndexOfAny(new[] { '=', '\r', '\n', '[', ']', ',' }) >= 0) return false;
                if (!result.Contains(token, StringComparer.OrdinalIgnoreCase)) result.Add(token);
            }
            return result.Count > 0;
        }
        public static bool SameExtensions(IEnumerable<string> left, IEnumerable<string> right)
        {
            var a = NormalizeExtensions(String.Join(",", left ?? Enumerable.Empty<string>())); var b = NormalizeExtensions(String.Join(",", right ?? Enumerable.Empty<string>()));
            return a.Count > 0 && a.Count == b.Count && a.All(x => b.Contains(x, StringComparer.OrdinalIgnoreCase));
        }
        public static string FamilyName(string path)
        {
            var name = Path.GetFileName(path ?? "");
            if (name.EndsWith(".wcx64", StringComparison.OrdinalIgnoreCase)) return name.Substring(0, name.Length - 6);
            if (name.EndsWith(".uwcx", StringComparison.OrdinalIgnoreCase)) return name.Substring(0, name.Length - 5);
            if (name.EndsWith(".wcx", StringComparison.OrdinalIgnoreCase)) return name.Substring(0, name.Length - 4);
            return Path.GetFileNameWithoutExtension(name);
        }
    }
}
