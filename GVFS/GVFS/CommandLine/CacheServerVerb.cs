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

                ServerGVFSConfig serverGVFSConfig = this.QueryGVFSConfig(tracer, enlistment, retryConfig);

                CacheServerResolver cacheServerResolver = new CacheServerResolver(tracer, enlistment);
                string error = null;

                if (this.CacheToSet != null)
                {
                    CacheServerInfo cacheServer = cacheServerResolver.ParseUrlOrFriendlyName(this.CacheToSet);
                    cacheServer = this.ResolveCacheServer(tracer, cacheServer, cacheServerResolver, serverGVFSConfig);

                    if (!cacheServerResolver.TrySaveUrlToLocalConfig(cacheServer, out error))
                    {
                        this.ReportErrorAndExit("Failed to save cache to config: " + error);
                    }

                    this.Output.WriteLine("You must remount GVFS for this to take effect.");
                }
                else if (this.ListCacheServers)
                {
                    List<CacheServerInfo> cacheServers = serverGVFSConfig.CacheServers.ToList();

                    if (cacheServers != null && cacheServers.Any())
                    {
                        this.Output.WriteLine();
                        this.Output.WriteLine("Available cache servers for: " + enlistment.RepoUrl);
                        foreach (CacheServerInfo cacheServer in cacheServers)
                        {
                            this.Output.WriteLine(cacheServer);
                        }
                    }
                    else
                    {
                        this.Output.WriteLine("There are no available cache servers for: " + enlistment.RepoUrl);
                    }
                }
                else
                {
                    string cacheServerUrl = CacheServerResolver.GetUrlFromConfig(enlistment);
                    CacheServerInfo cacheServer = cacheServerResolver.ResolveNameFromRemote(cacheServerUrl, serverGVFSConfig);

                    this.Output.WriteLine("Using cache server: " + cacheServer);
                }
            }
        }
    }
}
