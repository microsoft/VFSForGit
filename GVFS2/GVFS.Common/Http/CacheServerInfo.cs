using Newtonsoft.Json;
using System;

namespace GVFS.Common.Http
{
    public class CacheServerInfo
    {
        private const string ObjectsEndpointSuffix = "/gvfs/objects";
        private const string PrefetchEndpointSuffix = "/gvfs/prefetch";
        private const string SizesEndpointSuffix = "/gvfs/sizes";

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
                this.SizesEndpointUrl = this.Url + SizesEndpointSuffix;
            }
        }

        public string Url { get; }
        public string Name { get; }
        public bool GlobalDefault { get; }

        public string ObjectsEndpointUrl { get; }
        public string PrefetchEndpointUrl { get; }
        public string SizesEndpointUrl { get; }

        public bool HasValidUrl()
        {
            return Uri.IsWellFormedUriString(this.Url, UriKind.Absolute);
        }

        public bool IsNone(string repoUrl)
        {
            return ReservedNames.None.Equals(this.Name, StringComparison.OrdinalIgnoreCase)
                || this.Url?.StartsWith(repoUrl, StringComparison.OrdinalIgnoreCase) == true;
        }

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

        public static class ReservedNames
        {
            public const string None = "None";
            public const string Default = "Default";
            public const string UserDefined = "User Defined";
        }
    }
}
