using System.Collections.Generic;
using System.Runtime.Serialization;
using TotalUpdater.Next.Core;

namespace TotalUpdater.Next.Catalog
{
    [DataContract]
    public sealed class PluginCatalogEntry
    {
        [DataMember(Name = "id")] public string Id { get; set; } = "";
        [DataMember(Name = "name")] public string Name { get; set; } = "";
        [DataMember(Name = "type")] public string Type { get; set; } = "";
        [DataMember(Name = "aliases")] public List<string> Aliases { get; set; } = new List<string>();
        [DataMember(Name = "source")] public CatalogSource Source { get; set; } = new CatalogSource();

        public PluginType PluginType
        {
            get
            {
                PluginType type;
                return System.Enum.TryParse(Type, true, out type) ? type : PluginType.Other;
            }
        }
    }

    [DataContract]
    public sealed class CatalogSource
    {
        [DataMember(Name = "provider")] public string Provider { get; set; } = "";
        [DataMember(Name = "id")] public string Id { get; set; } = "";
        [DataMember(Name = "url")] public string Url { get; set; } = "";
        [DataMember(Name = "versionPattern")] public string VersionPattern { get; set; } = "";
        [DataMember(Name = "downloadUrl")] public string DownloadUrl { get; set; } = "";
    }
}
