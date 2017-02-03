using GVFS.Common.Git;
using System;
using System.IO;

namespace GVFS.Common
{
    public abstract class Enlistment
    {
        private const string ObjectsEndpointSuffix = "/gvfs/objects";
        private const string PrefetchEndpointSuffix = "/gvfs/prefetch";

        private const string DeprecatedObjectsEndpointGitConfigName = "gvfs.objects-endpoint";

        private const string GVFSGitConfigPrefix = "gvfs.";
        private const string CacheEndpointGitConfigSuffix = ".cache-server-url";

        // New enlistment
        protected Enlistment(string enlistmentRoot, string workingDirectoryRoot, string repoUrl, string cacheServerUrl, string gitBinPath, string gvfsHooksRoot)
        {
            if (string.IsNullOrWhiteSpace(gitBinPath))
            {
                throw new ArgumentException("Path to git.exe must be set");
            }

            this.EnlistmentRoot = enlistmentRoot;
            this.WorkingDirectoryRoot = workingDirectoryRoot;
            this.GitBinPath = gitBinPath;
            this.GVFSHooksRoot = gvfsHooksRoot;
            this.RepoUrl = repoUrl;

            this.SetComputedPaths();
            this.SetComputedURLs(cacheServerUrl);            
        }

        // Existing, configured enlistment
        protected Enlistment(string enlistmentRoot, string workingDirectoryRoot, string cacheServerUrl, string gitBinPath, string gvfsHooksRoot)
        {
            if (string.IsNullOrWhiteSpace(gitBinPath))
            {
                throw new ArgumentException("Path to git.exe must be set");
            }

            this.EnlistmentRoot = enlistmentRoot;
            this.WorkingDirectoryRoot = workingDirectoryRoot;
            this.GitBinPath = gitBinPath;
            this.GVFSHooksRoot = gvfsHooksRoot;

            this.SetComputedPaths();

            GitProcess.Result originResult = new GitProcess(this).GetOriginUrl();
            if (originResult.HasErrors)
            {
                throw new InvalidRepoException("Could not get origin url. git error: " + originResult.Errors);
            }

            this.RepoUrl = originResult.Output;
            this.SetComputedURLs(cacheServerUrl);
        }

        public string EnlistmentRoot { get; }
        public string WorkingDirectoryRoot { get; }
        public string DotGitRoot { get; private set; }
        public string GitPackRoot { get; private set; }
        public string RepoUrl { get; }
        public string CacheServerUrl { get; private set; }

        public string ObjectsEndpointUrl { get; private set; }        

        public string PrefetchEndpointUrl { get; private set; }           

        public string GitBinPath { get; }
        public string GVFSHooksRoot { get; }

        public static string StripObjectsEndpointSuffix(string input)
        {
            if (!string.IsNullOrWhiteSpace(input) && input.EndsWith(ObjectsEndpointSuffix))
            {
                input = input.Substring(0, input.Length - ObjectsEndpointSuffix.Length);
            }

            return input;
        }

        protected static string GetCacheConfigSettingName(string repoUrl)
        {
            string sectionUrl = repoUrl.ToLowerInvariant()
                            .Replace("https://", string.Empty)
                            .Replace("http://", string.Empty)
                            .Replace('/', '.');

            return GVFSGitConfigPrefix + sectionUrl + CacheEndpointGitConfigSuffix;
        }
        
        protected string GetCacheServerUrlFromConfig(string repoUrl)
        {
            GitProcess git = new GitProcess(this);
            string cacheConfigName = GetCacheConfigSettingName(repoUrl);

            string cacheServerUrl = git.GetFromConfig(cacheConfigName);
            if (string.IsNullOrWhiteSpace(cacheServerUrl))
            {
                // Try getting from the deprecated setting for compatibility reasons
                cacheServerUrl = StripObjectsEndpointSuffix(git.GetFromConfig(DeprecatedObjectsEndpointGitConfigName));

                // Upgrade for future runs, but not at clone time.
                if (!string.IsNullOrWhiteSpace(cacheServerUrl) && Directory.Exists(this.WorkingDirectoryRoot))
                {
                    git.SetInLocalConfig(cacheConfigName, cacheServerUrl);
                    git.DeleteFromLocalConfig(DeprecatedObjectsEndpointGitConfigName);
                }
            }

            // Default to uncached url
            if (string.IsNullOrWhiteSpace(cacheServerUrl))
            {
                return repoUrl;
            }

            return cacheServerUrl;
        }

        private void SetComputedPaths()
        {
            this.DotGitRoot = Path.Combine(this.WorkingDirectoryRoot, GVFSConstants.DotGit.Root);
            this.GitPackRoot = Path.Combine(this.WorkingDirectoryRoot, GVFSConstants.DotGit.Objects.Pack.Root);
        }

        private void SetComputedURLs(string cacheServerUrl)
        {
            this.CacheServerUrl = !string.IsNullOrWhiteSpace(cacheServerUrl) ? cacheServerUrl : this.GetCacheServerUrlFromConfig(this.RepoUrl);
            this.ObjectsEndpointUrl = this.CacheServerUrl + ObjectsEndpointSuffix;
            this.PrefetchEndpointUrl = this.CacheServerUrl + PrefetchEndpointSuffix;
        }
    }
}