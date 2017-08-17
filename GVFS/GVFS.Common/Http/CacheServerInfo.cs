using GVFS.Common.Git;
using GVFS.Common.Tracing;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;

namespace GVFS.Common.Http
{
    public class CacheServerInfo
    {
        public const string NoneFriendlyName = "None";
        public const string UserDefinedFriendlyName = "User Defined";
        
        private const string CacheServerConfigName = "gvfs.cache-server";
        private const string ObjectsEndpointSuffix = "/gvfs/objects";
        private const string PrefetchEndpointSuffix = "/gvfs/prefetch";

        private const string DeprecatedCacheEndpointGitConfigSuffix = ".cache-server-url";

        [JsonConstructor]
        public CacheServerInfo(string url, string name, bool globalDefault = false)
        {
            this.Url = url;
            this.Name = name;
            this.GlobalDefault = globalDefault;

            this.ObjectsEndpointUrl = this.Url + ObjectsEndpointSuffix;
            this.PrefetchEndpointUrl = this.Url + PrefetchEndpointSuffix;
        }

        public string Url { get; }
        public string Name { get; }
        public bool GlobalDefault { get; }

        public string ObjectsEndpointUrl { get; }
        public string PrefetchEndpointUrl { get; }
        
        public static bool TryDetermineCacheServer(
            string userUrlish, 
            ITracer tracer, 
            Enlistment enlistment, 
            RetryConfig retryConfig, 
            out CacheServerInfo cache, 
            out string error)
        {
            using (ConfigHttpRequestor configRequestor = new ConfigHttpRequestor(tracer, enlistment, retryConfig))
            {
                GVFSConfig config = configRequestor.QueryGVFSConfig();
                if (!CacheServerInfo.TryDetermineCacheServer(userUrlish, enlistment, config.CacheServers, out cache, out error))
                {
                    return false;
                }
            }

            return true;
        }
        
        public static bool TryDetermineCacheServer(
            string userUrlish, 
            Enlistment enlistment, 
            IEnumerable<CacheServerInfo> knownCaches, 
            out CacheServerInfo output, 
            out string error)
        {
            return TryDetermineCacheServer(userUrlish, new GitProcess(enlistment), enlistment, knownCaches, out output, out error);
        }

        public static bool TryDetermineCacheServer(
            string userUrlish, 
            GitProcess gitProcess, 
            Enlistment enlistment, 
            IEnumerable<CacheServerInfo> knownCaches, 
            out CacheServerInfo output,
            out string error)
        {
            output = null;

            // User input overrules everything
            if (!string.IsNullOrWhiteSpace(userUrlish))
            {
                if (!CacheServerInfo.TryParse(userUrlish, enlistment, knownCaches, out output))
                {
                    error = "Failed to determine remote objects endpoint";
                    return false;
                }

                error = null;
                return true;
            }

            // Fallback to git config entry if possible.
            string configCacheServer = GetValueFromConfig(gitProcess, CacheServerConfigName);
            if (!string.IsNullOrWhiteSpace(configCacheServer))
            {
                if (!CacheServerInfo.TryParse(configCacheServer, enlistment, knownCaches, out output))
                {
                    error = string.Format(
                        "Failed to determine remote objects endpoint from '{0}': {1}",
                        CacheServerConfigName,
                        configCacheServer);

                    error = "Failed to determine remote objects endpoint";
                    return false;
                }

                error = null;
                return true;
            }

            // Fallback to deprecated cache git config entry (as upgrade path)
            // TODO 1057500: Someday remove support for encoded-repo-url cache config setting
            string deprecatedConfigSetting = GetDeprecatedCacheConfigSettingName(enlistment.RepoUrl);
            string deprecatedConfigCacheServer = GetValueFromConfig(gitProcess, deprecatedConfigSetting);
            if (!string.IsNullOrWhiteSpace(deprecatedConfigCacheServer))
            {
                if (!CacheServerInfo.TryParse(deprecatedConfigCacheServer, enlistment, knownCaches, out output))
                {
                    error = string.Format(
                        "Failed to determine remote objects endpoint from '{0}': {1}",
                        deprecatedConfigSetting, 
                        deprecatedConfigCacheServer);

                    return false;
                }

                if (!TrySaveToConfig(gitProcess, output, out error))
                {
                    return false;
                }

                error = null;
                return true;
            }

            // Fallback to any known default cache.
            if (knownCaches != null)
            {
                output = knownCaches.FirstOrDefault(cache => cache.GlobalDefault);
            }

            // If there are no known defaults, then default to None
            if (output == null)
            {
                output = new CacheServerInfo(enlistment.RepoUrl, NoneFriendlyName);
            }

            error = null;
            return true;
        }

        public static bool TryParse(string urlish, Enlistment enlistment, IEnumerable<CacheServerInfo> knownCaches, out CacheServerInfo output)
        {
            output = null;
            if (string.IsNullOrWhiteSpace(urlish))
            {
                return false;
            }

            if (urlish.Equals(NoneFriendlyName, StringComparison.OrdinalIgnoreCase) ||
                urlish.Equals(enlistment.RepoUrl, StringComparison.OrdinalIgnoreCase))
            {
                output = new CacheServerInfo(enlistment.RepoUrl, NoneFriendlyName);
                return true;
            }

            if (knownCaches != null)
            {
                output = knownCaches.FirstOrDefault(cache => cache.Url.Equals(urlish, StringComparison.OrdinalIgnoreCase));
                if (output != null)
                {
                    return true;
                }
            }

            Uri uri;
            if (Uri.TryCreate(urlish, UriKind.Absolute, out uri))
            {
                output = new CacheServerInfo(urlish, UserDefinedFriendlyName);
                return true;
            }

            if (knownCaches != null)
            {
                output = knownCaches.FirstOrDefault(cache => cache.Name.Equals(urlish, StringComparison.OrdinalIgnoreCase));
                
                return output != null;
            }

            return false;
        }

        public static bool TrySaveToConfig(GitProcess git, CacheServerInfo cache, out string error)
        {
            string value = 
                cache.Name.Equals(UserDefinedFriendlyName, StringComparison.OrdinalIgnoreCase)
                ? cache.Url
                : cache.Name;
            
            GitProcess.Result result = git.SetInLocalConfig(CacheServerConfigName, value);

            error = result.Errors;
            return !result.HasErrors;
        }

        public static string GetCacheServerValueFromConfig(Enlistment enlistment)
        {
            GitProcess git = new GitProcess(enlistment);

            // TODO 1057500: Someday remove support for encoded-repo-url cache config setting
            return GetValueFromConfig(git, CacheServerConfigName)
                ?? GetValueFromConfig(git, GetDeprecatedCacheConfigSettingName(enlistment.RepoUrl)); 
        }

        public override string ToString()
        {
            return string.Format("{0} ({1})", this.Name, this.Url);
        }

        private static string GetValueFromConfig(GitProcess git, string configName)
        {
            GitProcess.Result result = git.GetFromConfig(configName);

            // Git returns non-zero for non-existent settings and errors.
            if (!result.HasErrors)
            {
                return result.Output.TrimEnd('\n');
            }
            else if (result.Errors.Any())
            {
                throw new InvalidRepoException("Error while reading '" + configName + "' from config: " + result.Errors);
            }

            return null;
        }

        private static string GetDeprecatedCacheConfigSettingName(string repoUrl)
        {
            string sectionUrl =
                repoUrl.ToLowerInvariant()
                .Replace("https://", string.Empty)
                .Replace("http://", string.Empty)
                .Replace('/', '.');

            return GVFSConstants.GitConfig.GVFSPrefix + sectionUrl + DeprecatedCacheEndpointGitConfigSuffix;
        }
    }
}
