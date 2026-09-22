using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization.Json;
using TotalUpdater.Next.Models;

namespace TotalUpdater.Next.Services
{
    public static class RuleStore
    {
        public static IDictionary<string, SourceRule> Load(params string[] paths)
        {
            var indexed = new Dictionary<string, SourceRule>(StringComparer.OrdinalIgnoreCase);
            foreach (var path in paths)
            {
                if (!File.Exists(path)) continue;
                using (var stream = File.OpenRead(path))
                {
                    var serializer = new DataContractJsonSerializer(typeof(List<SourceRule>));
                    var rules = serializer.ReadObject(stream) as List<SourceRule>;
                    if (rules == null) continue;
                    foreach (var rule in rules)
                        if (!String.IsNullOrWhiteSpace(rule.FileName)) indexed[rule.FileName] = rule;
                }
            }
            return indexed;
        }
    }
}
