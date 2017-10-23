using Newtonsoft.Json;

namespace GVFS.Common.Http
{
    public class CacheServerInfo
    {
        private const string ObjectsEndpointSuffix = "/gvfs/objects";
        private const string PrefetchEndpointSuffix = "/gvfs/prefetch";

        [JsonConstructor]
        public CacheServerInfo(string url, string name, bool globalDefault = false)
        {
            this.Url = url;
            this.Name = name;
            this.GlobalDefault = globalDefault;

            if (this.Url != null)
            {
                this.ObjectsEndpointUrl = this.Url + ObjectsEndpointSuffix;
                this.PrefetchEndpointUrl = this.Url + PrefetchEndpointSuffix;
            }
        }

        public string Url { get; }
        public string Name { get; }
        public bool GlobalDefault { get; }

        public string ObjectsEndpointUrl { get; }
        public string PrefetchEndpointUrl { get; }

        public override string ToString()
        {
            if (string.IsNullOrWhiteSpace(this.Name))
            {
                return this.Url;
            }

            if (string.IsNullOrWhiteSpace(this.Url))
            {
                return this.Name;
            }

            return string.Format("{0} ({1})", this.Name, this.Url);
        }

        public bool HasResolvedName()
        {
            return
                !this.Name.Equals(ReservedNames.None) &&
                !this.Name.Equals(ReservedNames.Default) &&
                !this.Name.Equals(ReservedNames.UserDefined);
        }

        public static class ReservedNames
        {
            public const string None = "None";
            public const string Default = "Default";
            public const string UserDefined = "User Defined";
        }
    }
}
