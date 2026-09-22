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
        [DataMember(Name = "localVersionStrategy")] public string LocalVersionStrategy { get; set; } = "";
        [DataMember(Name = "sources")] public List<CatalogSource> Sources { get; set; } = new List<CatalogSource>();

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
        [DataMember(Name = "priority")] public int Priority { get; set; }
        [DataMember(Name = "url")] public string Url { get; set; } = "";
        [DataMember(Name = "versionPattern")] public string VersionPattern { get; set; } = "";
        [DataMember(Name = "downloadUrl")] public string DownloadUrl { get; set; } = "";
        [DataMember(Name = "repository")] public string Repository { get; set; } = "";
        [DataMember(Name = "assetPattern")] public string AssetPattern { get; set; } = "";
        [DataMember(Name = "includePrerelease")] public bool IncludePrerelease { get; set; }
    }

    public enum CatalogDiagnosticSeverity { Warning, Error }
    public sealed class CatalogDiagnostic
    {
        public CatalogDiagnosticSeverity Severity { get; set; }
        public string EntryId { get; set; } = "";
        public string Message { get; set; } = "";
    }

    public sealed class CatalogLoadResult
    {
        public IList<PluginCatalogEntry> Entries { get; set; } = new List<PluginCatalogEntry>();
        public IList<CatalogDiagnostic> Diagnostics { get; set; } = new List<CatalogDiagnostic>();
    }
}
