using CommandLine;
using GVFS.Common;
using GVFS.Common.Git;
using GVFS.Common.Http;
using GVFS.Common.Tracing;
using System.Linq;

namespace GVFS.CommandLine
{
    [Verb(CacheVerbName, HelpText = "Manages the cache server configuration for an existing repo.")]
    public class CacheServerVerb : GVFSVerb.ForExistingEnlistment
    {
        private const string CacheVerbName = "cache-server";
        
        [Option(
            "set",
            Required = false,
            HelpText = "Sets the current cache server to the supplied name or url")]
        public string CacheToSet { get; set; }

        [Option("get", Required = false, HelpText = "Outputs the current cache server information. This is the default.")]
        public bool OutputCurrentInfo { get; set; }
        
        [Option(
            "list",
            Required = false,
            HelpText = "List available cache servers for the current GVFS enlistment")]
        public bool ListCacheServers { get; set; }

        protected override string VerbName
        {
            get { return CacheVerbName; }
        }
        
        protected override void Execute(GVFSEnlistment enlistment)
        {
            using (ITracer tracer = new JsonEtwTracer(GVFSConstants.GVFSEtwProviderName, "CacheVerb"))
            {
                RetryConfig retryConfig;
                string error;
                if (!RetryConfig.TryLoadFromGitConfig(tracer, enlistment, out retryConfig, out error))
                {
                    this.ReportErrorAndExit("Failed to determine GVFS timeout and max retries: " + error);
                }

                GVFSConfig config;
                using (ConfigHttpRequestor configRequestor = new ConfigHttpRequestor(tracer, enlistment, retryConfig))
                {
                    config = configRequestor.QueryGVFSConfig();
                    if (config == null)
                    {
                        this.ReportErrorAndExit("Could not query for available cache servers.");
                    }
                }

                CacheServerInfo cache;
                if (!string.IsNullOrWhiteSpace(this.CacheToSet))
                {
                    if (CacheServerInfo.TryParse(this.CacheToSet, enlistment, config.CacheServers, out cache))
                    {
                        if (!CacheServerInfo.TrySaveToConfig(new GitProcess(enlistment), cache, out error))
                        {
                            this.ReportErrorAndExit("Failed to save cache to config: " + error);
                        }
                    }
                    else
                    {
                        this.ReportErrorAndExit("Unrecognized or invalid cache name or url: " + this.CacheToSet);
                    }

                    this.OutputCacheInfo(cache);
                    this.Output.WriteLine("You must remount GVFS for this to take effect.");
                }
                else if (this.ListCacheServers)
                {
                    if (config.CacheServers.Any())
                    {
                        this.Output.WriteLine("Available cache servers for: " + enlistment.RepoUrl);
                        foreach (CacheServerInfo cacheServer in config.CacheServers)
                        {
                            this.Output.WriteLine("{0, -25} ({1})", cacheServer.Name, cacheServer.Url);
                        }
                    }
                    else
                    {
                        this.Output.WriteLine("There are no available cache servers for: " + enlistment.RepoUrl);
                    }
                }
                else
                {
                    if (!CacheServerInfo.TryDetermineCacheServer(null, enlistment, config.CacheServers, out cache, out error))
                    {
                        this.ReportErrorAndExit(error);
                    }

                    this.OutputCacheInfo(cache);
                }
            }
        }

        private void OutputCacheInfo(CacheServerInfo cache)
        {
            this.Output.WriteLine("Current Cache Server:\t" + cache);
        }
    }
}
