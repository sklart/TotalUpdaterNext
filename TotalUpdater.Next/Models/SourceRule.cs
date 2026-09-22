using System.Runtime.Serialization;

namespace TotalUpdater.Next.Models
{
    [DataContract]
    public sealed class SourceRule
    {
        [DataMember(Name = "fileName")]
        public string FileName { get; set; } = "";

        [DataMember(Name = "name")]
        public string Name { get; set; } = "";

        [DataMember(Name = "sourceUrl")]
        public string SourceUrl { get; set; } = "";

        [DataMember(Name = "versionPattern")]
        public string VersionPattern { get; set; } = "";

        [DataMember(Name = "downloadUrl")]
        public string DownloadUrl { get; set; } = "";
    }
}
