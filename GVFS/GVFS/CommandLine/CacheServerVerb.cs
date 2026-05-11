using GVFS.Common;
using GVFS.Common.Http;
using GVFS.Common.Tracing;
using System;
using System.Collections.Generic;
using System.Linq;

namespace GVFS.CommandLine
{
    public class CacheServerVerb : GVFSVerb.ForExistingEnlistment
    {
        private const string CacheVerbName = "cache-server";

        public string CacheToSet { get; set; }

        public bool OutputCurrentInfo { get; set; }

        public bool ListCacheServers { get; set; }

        public static System.CommandLine.Command CreateCommand()
        {
            System.CommandLine.Command cmd = new System.CommandLine.Command("cache-server", "Manages the cache server configuration for an existing repo.");

            System.CommandLine.Argument<string> enlistmentArg = GVFSVerb.CreateEnlistmentPathArgument();
            cmd.Add(enlistmentArg);

            System.CommandLine.Option<string> setOption = new System.CommandLine.Option<string>("--set") { Description = "Sets the cache server to the supplied name or url" };
            cmd.Add(setOption);

            System.CommandLine.Option<bool> getOption = new System.CommandLine.Option<bool>("--get") { Description = "Outputs the current cache server information. This is the default." };
            cmd.Add(getOption);

            System.CommandLine.Option<bool> listOption = new System.CommandLine.Option<bool>("--list") { Description = "List available cache servers for the remote repo" };
            cmd.Add(listOption);

            System.CommandLine.Option<string> internalOption = GVFSVerb.CreateInternalParametersOption();
            cmd.Add(internalOption);

            GVFSVerb.SetActionForVerbWithEnlistment<CacheServerVerb>(cmd, enlistmentArg, internalOption, defaultEnlistmentPathToCwd: true,
                (verb, result) =>
                {
                    verb.CacheToSet = result.GetValue(setOption);
                    verb.OutputCurrentInfo = result.GetValue(getOption);
                    verb.ListCacheServers = result.GetValue(listOption);
                });

            return cmd;
        }

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
                CacheServerResolver cacheServerResolver = new CacheServerResolver(tracer, enlistment);
                ServerGVFSConfig serverGVFSConfig = null;
                string error = null;

                // Handle the three operation types: list, set, and get (default)
                if (this.ListCacheServers)
                {
                    // For listing, require config endpoint to succeed (no fallback)
                    if (!this.TryAuthenticateAndQueryGVFSConfig(
                        tracer, enlistment, retryConfig, out serverGVFSConfig, out error))
                    {
                        this.ReportErrorAndExit(tracer, "Unable to query /gvfs/config" + Environment.NewLine + error);
                    }

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
                    if (!this.TryAuthenticateAndQueryGVFSConfig(
                        tracer, enlistment, retryConfig, out serverGVFSConfig, out error,
                        fallbackCacheServer: cacheServer))
                    {
                        this.ReportErrorAndExit(tracer, "Authentication failed: " + error);
                    }

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
                    if (!this.TryAuthenticateAndQueryGVFSConfig(
                        tracer, enlistment, retryConfig, out serverGVFSConfig, out error,
                        fallbackCacheServer: cacheServer))
                    {
                        this.ReportErrorAndExit(tracer, "Authentication failed: " + error);
                    }

                    CacheServerInfo resolvedCacheServer = cacheServerResolver.ResolveNameFromRemote(cacheServer.Url, serverGVFSConfig);

                    this.Output.WriteLine("Using cache server: " + resolvedCacheServer);
                }
            }
        }
    }
}
