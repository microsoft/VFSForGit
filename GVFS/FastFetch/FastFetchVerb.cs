using CommandLine;
using GVFS.Common;
using GVFS.Common.Git;
using GVFS.Common.Tracing;
using Microsoft.Diagnostics.Tracing;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace FastFetch
{
    [Verb("fastfetch", HelpText = "Fast-fetch a branch")]
    public class FastFetchVerb
    {
        private const string DefaultBranch = "master";

        [Option(
            'c',
            "commit",
            Required = false,
            HelpText = "Commit to fetch")]
        public string Commit { get; set; }

        [Option(
            'b',
            "branch",
            Required = false,
            HelpText = "Branch to fetch")]
        public string Branch { get; set; }

        [Option(
            's',
            "silent",
            Required = false,
            Default = false,
            HelpText = "Disables console logging")]
        public bool Silent { get; set; }

        [Option(
            "cache-server-url",
            Required = false,
            Default = "",
            HelpText = "Defines the url of the cache server")]
        public string CacheServerUrl { get; set; }

        [Option(
            "chunk-size",
            Required = false,
            Default = 4000,
            HelpText = "Sets the number of objects to be downloaded in a single pack")]
        public int ChunkSize { get; set; }

        [Option(
            "search-thread-count",
            Required = false,
            Default = 2,
            HelpText = "Sets the number of threads to use for finding missing blobs. (0 for number of logical cores)")]
        public int SearchThreadCount { get; set; }

        [Option(
            "download-thread-count",
            Required = false,
            Default = 0,
            HelpText = "Sets the number of threads to use for downloading. (0 for number of logical cores)")]
        public int DownloadThreadCount { get; set; }

        [Option(
            "index-thread-count",
            Required = false,
            Default = 0,
            HelpText = "Sets the number of threads to use for indexing. (0 for number of logical cores)")]
        public int IndexThreadCount { get; set; }

        [Option(
            "checkout-thread-count",
            Required = false,
            Default = 0,
            HelpText = "Sets the number of threads to use for indexing. (0 for number of logical cores)")]
        public int CheckoutThreadCount { get; set; }

        [Option(
            'r',
            "max-retries",
            Required = false,
            Default = 10,
            HelpText = "Sets the maximum number of retries for downloading a pack")]

        public int MaxRetries { get; set; }

        [Option(
            "git-path",
            Default = "",
            Required = false,
            HelpText = "Sets the path and filename for git.exe if it isn't expected to be on %PATH%.")]
        public string GitBinPath { get; set; }
        
        [Option(
            "folders",
            Required = false,
            Default = "",
            HelpText = "A semicolon-delimited list of paths to fetch")]
        public string PathWhitelist { get; set; }

        [Option(
            "folders-list",
            Required = false,
            Default = "",
            HelpText = "A file containing line-delimited list of paths to fetch")]
        public string PathWhitelistFile { get; set; }

        public void Execute()
        {
            // CmdParser doesn't strip quotes, and Path.Combine will throw
            this.GitBinPath = this.GitBinPath.Replace("\"", string.Empty);
            if (!GitProcess.GitExists(this.GitBinPath))
            {
                Console.WriteLine(
                    "Could not find git.exe {0}",
                    !string.IsNullOrWhiteSpace(this.GitBinPath) ? "at " + this.GitBinPath : "on %PATH%");
                return;
            }

            if (this.Commit != null && this.Branch != null)
            {
                Console.WriteLine("Cannot specify both a commit sha and a branch name to checkout.");
                return;
            }

            this.CacheServerUrl = Enlistment.StripObjectsEndpointSuffix(this.CacheServerUrl);

            this.SearchThreadCount = this.SearchThreadCount > 0 ? this.SearchThreadCount : Environment.ProcessorCount;
            this.DownloadThreadCount = this.DownloadThreadCount > 0 ? this.DownloadThreadCount : Environment.ProcessorCount;
            this.IndexThreadCount = this.IndexThreadCount > 0 ? this.IndexThreadCount : Environment.ProcessorCount;
            this.CheckoutThreadCount = this.CheckoutThreadCount > 0 ? this.CheckoutThreadCount : Environment.ProcessorCount;

            this.GitBinPath = !string.IsNullOrWhiteSpace(this.GitBinPath) ? this.GitBinPath : GitProcess.GetInstalledGitBinPath();

            Enlistment enlistment = (Enlistment)GVFSEnlistment.CreateFromCurrentDirectory(this.CacheServerUrl, this.GitBinPath)
                ?? GitEnlistment.CreateFromCurrentDirectory(this.CacheServerUrl, this.GitBinPath);

            if (enlistment == null)
            {
                Console.WriteLine("Must be run within a .git repo or GVFS enlistment");
                return;
            }

            string commitish = this.Commit ?? this.Branch ?? DefaultBranch;
            
            EventLevel maxVerbosity = this.Silent ? EventLevel.LogAlways : EventLevel.Informational;
            using (JsonEtwTracer tracer = new JsonEtwTracer("Microsoft.Git.FastFetch", "FastFetch"))
            {
                tracer.AddConsoleEventListener(maxVerbosity, Keywords.Any);
                tracer.WriteStartEvent(
                    enlistment.EnlistmentRoot,
                    enlistment.RepoUrl,
                    enlistment.CacheServerUrl,
                    new EventMetadata
                    {
                        { "TargetCommitish", commitish },
                    });

                FetchHelper fetchHelper = this.GetFetchHelper(tracer, enlistment);
                
                fetchHelper.MaxRetries = this.MaxRetries;

                if (!FetchHelper.TryLoadPathWhitelist(this.PathWhitelist, this.PathWhitelistFile, tracer, fetchHelper.PathWhitelist))
                {
                    Environment.ExitCode = 1;
                    return;
                }

                try
                {
                    bool isBranch = this.Commit == null;
                    fetchHelper.FastFetch(commitish, isBranch);
                    if (fetchHelper.HasFailures)
                    {
                        Environment.ExitCode = 1;
                    }
                }
                catch (AggregateException e)
                {
                    Environment.ExitCode = 1;
                    foreach (Exception ex in e.Flatten().InnerExceptions)
                    {
                        tracer.RelatedError(ex.ToString());
                    }
                }
                catch (Exception e)
                {
                    Environment.ExitCode = 1;
                    tracer.RelatedError(e.ToString());
                }

                EventMetadata stopMetadata = new EventMetadata();
                stopMetadata.Add("Success", Environment.ExitCode == 0);
                tracer.Stop(stopMetadata);
            }

            if (Debugger.IsAttached)
            {
                Console.ReadKey();
            }
        }

        private FetchHelper GetFetchHelper(ITracer tracer, Enlistment enlistment)
        {
            return new FetchHelper(
                tracer,
                enlistment,
                this.ChunkSize,
                this.SearchThreadCount,
                this.DownloadThreadCount,
                this.IndexThreadCount);
        }
    }
}
