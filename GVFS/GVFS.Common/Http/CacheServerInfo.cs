using System;
using System.Text.Json.Serialization;

namespace GVFS.Common.Http
{
    public class CacheServerInfo
    {
        private const string ObjectsEndpointSuffix = "/gvfs/objects";
        private const string PrefetchEndpointSuffix = "/gvfs/prefetch";
        private const string SizesEndpointSuffix = "/gvfs/sizes";

        [JsonConstructor]
        public CacheServerInfo(string url, string name, bool globalDefault = false)
            : this(url, name, globalDefault, null, null, null, null)
        {
        }

        public CacheServerInfo(
            string url,
            string name,
            bool globalDefault,
            string prefetchCacheServerUrl,
            string getCacheServerUrl,
            string postCacheServerUrl,
            string sizesCacheServerUrl)
        {
            this.Url = url;
            this.Name = name;
            this.GlobalDefault = globalDefault;
            this.PrefetchCacheServerUrl = prefetchCacheServerUrl;
            this.GetCacheServerUrl = getCacheServerUrl;
            this.PostCacheServerUrl = postCacheServerUrl;
            this.SizesCacheServerUrl = sizesCacheServerUrl;

            if (this.Url != null)
            {
                this.ObjectsEndpointUrl = this.Url + ObjectsEndpointSuffix;
            }

            this.GlobalPrefetchEndpointUrl = GetEndpointUrl(this.Url, PrefetchEndpointSuffix);
            this.GlobalSizesEndpointUrl = GetEndpointUrl(this.Url, SizesEndpointSuffix);
            this.PrefetchEndpointUrl = GetEndpointUrl(prefetchCacheServerUrl ?? this.Url, PrefetchEndpointSuffix);
            this.ObjectsGetEndpointUrl = GetEndpointUrl(getCacheServerUrl ?? this.Url, ObjectsEndpointSuffix);
            this.ObjectsPostEndpointUrl = GetEndpointUrl(postCacheServerUrl ?? this.Url, ObjectsEndpointSuffix);
            this.SizesEndpointUrl = GetEndpointUrl(sizesCacheServerUrl ?? this.Url, SizesEndpointSuffix);
        }

        public string Url { get; }
        public string Name { get; }
        public bool GlobalDefault { get; }

        [JsonIgnore]
        public string PrefetchCacheServerUrl { get; }

        [JsonIgnore]
        public string GetCacheServerUrl { get; }

        [JsonIgnore]
        public string PostCacheServerUrl { get; }

        [JsonIgnore]
        public string SizesCacheServerUrl { get; }

        public string ObjectsEndpointUrl { get; }
        public string PrefetchEndpointUrl { get; }
        public string SizesEndpointUrl { get; }

        [JsonIgnore]
        public string ObjectsGetEndpointUrl { get; }

        [JsonIgnore]
        public string ObjectsPostEndpointUrl { get; }

        [JsonIgnore]
        public string GlobalPrefetchEndpointUrl { get; }

        [JsonIgnore]
        public string GlobalSizesEndpointUrl { get; }

        public CacheServerInfo WithEndpointOverrides(
            string prefetchCacheServerUrl,
            string getCacheServerUrl,
            string postCacheServerUrl,
            string sizesCacheServerUrl)
        {
            return new CacheServerInfo(
                this.Url,
                this.Name,
                this.GlobalDefault,
                prefetchCacheServerUrl,
                getCacheServerUrl,
                postCacheServerUrl,
                sizesCacheServerUrl);
        }

        public CacheServerInfo WithEndpointOverridesFrom(CacheServerInfo cacheServer)
        {
            return this.WithEndpointOverrides(
                cacheServer.PrefetchCacheServerUrl,
                cacheServer.GetCacheServerUrl,
                cacheServer.PostCacheServerUrl,
                cacheServer.SizesCacheServerUrl);
        }

        public bool HasValidUrl()
        {
            return IsValidUrl(this.Url);
        }

        public static bool IsValidUrl(string url)
        {
            return Uri.IsWellFormedUriString(url, UriKind.Absolute);
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

        private static string GetEndpointUrl(string cacheServerUrl, string endpointSuffix)
        {
            return cacheServerUrl == null ? null : cacheServerUrl + endpointSuffix;
        }
    }
}
