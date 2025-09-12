using CommandLine;
using GVFS.Common;
using GVFS.Common.Http;
using GVFS.Common.Tracing;
using System;
using System.Collections.Generic;
using System.Linq;

namespace GVFS.CommandLine
{
    [Verb(CacheVerbName, HelpText = "Manages the cache server configuration for an existing repo.")]
    public class CacheServerVerb : GVFSVerb.ForExistingEnlistment
    {
        private const string CacheVerbName = "cache-server";

        [Option(
            "set",
            Default = null,
            Required = false,
            HelpText = "Sets the cache server to the supplied name or url")]
        public string CacheToSet { get; set; }

        [Option("get", Required = false, HelpText = "Outputs the current cache server information. This is the default.")]
        public bool OutputCurrentInfo { get; set; }

        [Option(
            "list",
            Required = false,
            HelpText = "List available cache servers for the remote repo")]
        public bool ListCacheServers { get; set; }

        protected override string VerbName
        {
            get { return CacheVerbName; }
        }

        protected override void Execute(GVFSEnlistment enlistment)
        {
            this.BlockEmptyCacheServerUrl(this.CacheToSet);

            RetryConfig retryConfig = new RetryConfig(RetryConfig.DefaultMaxRetries, TimeSpan.FromMinutes(RetryConfig.FetchAndCloneTimeoutMinutes));

            using (ITracer tracer = new JsonTracer(GVFSConstants.GVFSEtwProviderName, "CacheVerb"))
            {
                string authErrorMessage;
                if (!this.TryAuthenticate(tracer, enlistment, out authErrorMessage))
                {
                    this.ReportErrorAndExit(tracer, "Authentication failed: " + authErrorMessage);
                }

                CacheServerResolver cacheServerResolver = new CacheServerResolver(tracer, enlistment);
                ServerGVFSConfig serverGVFSConfig = null;
                string error = null;

                // Handle the three operation types: list, set, and get (default)
                if (this.ListCacheServers)
                {
                    // For listing, require config endpoint to succeed
                    serverGVFSConfig = this.QueryGVFSConfig(tracer, enlistment, retryConfig);

                    List<CacheServerInfo> cacheServers = serverGVFSConfig.CacheServers.ToList();

                    if (cacheServers != null && cacheServers.Any())
                    {
                        this.Output.WriteLine();
                        this.Output.WriteLine("Available cache servers for: " + enlistment.RepoUrl);
                        foreach (CacheServerInfo cacheServerInfo in cacheServers)
                        {
                            this.Output.WriteLine(cacheServerInfo);
                        }
                    }
                    else
                    {
                        this.Output.WriteLine("There are no available cache servers for: " + enlistment.RepoUrl);
                    }
                }
                else if (this.CacheToSet != null)
                {
                    // Setting a new cache server
                    CacheServerInfo cacheServer = cacheServerResolver.ParseUrlOrFriendlyName(this.CacheToSet);

                    // For set operation, allow fallback if config endpoint fails but cache server URL is valid
                    serverGVFSConfig = this.QueryGVFSConfigWithFallbackCacheServer(
                        tracer,
                        enlistment,
                        retryConfig,
                        cacheServer);

                    cacheServer = this.ResolveCacheServer(tracer, cacheServer, cacheServerResolver, serverGVFSConfig);

                    if (!cacheServerResolver.TrySaveUrlToLocalConfig(cacheServer, out error))
                    {
                        this.ReportErrorAndExit("Failed to save cache to config: " + error);
                    }

                    this.Output.WriteLine("You must remount GVFS for this to take effect.");
                }
                else
                {
                    // Default operation: get current cache server info
                    CacheServerInfo cacheServer = CacheServerResolver.GetCacheServerFromConfig(enlistment);

                    // For get operation, allow fallback if config endpoint fails but cache server URL is valid
                    serverGVFSConfig =this.QueryGVFSConfigWithFallbackCacheServer(
                        tracer,
                        enlistment,
                        retryConfig,
                        cacheServer);

                    CacheServerInfo resolvedCacheServer = cacheServerResolver.ResolveNameFromRemote(cacheServer.Url, serverGVFSConfig);

                    this.Output.WriteLine("Using cache server: " + resolvedCacheServer);
                }
            }
        }
    }
}
