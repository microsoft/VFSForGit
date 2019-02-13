using CommandLine;
using GVFS.Common;
using GVFS.Common.Git;
using GVFS.Common.Http;
using GVFS.Common.Prefetch;
using GVFS.Common.Tracing;
using System;

namespace FastFetch
{
    [Verb("fastfetch", HelpText = "Fast-fetch a branch")]
    public class FastFetchVerb
    {
        // Testing has shown that more than 16 download threads does not improve
        // performance even with 56 core machines with 40G NICs. More threads does
        // create more load on the servers as they have to handle extra connections.
        private const int MaxDefaultDownloadThreads = 16;

        private const int ExitFailure = 1;
        private const int ExitSuccess = 0;

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
            "checkout",
            Required = false,
            Default = false,
            HelpText = "Checkout the target commit into the working directory after fetching")]
        public bool Checkout { get; set; }

        [Option(
            "force-checkout",
            Required = false,
            Default = false,
            HelpText = "Force FastFetch to checkout content as if the current repo had just been initialized." +
                       "This allows you to include more folders from the repo that were not originally checked out." +
                       "Can only be used with the --checkout option.")]
        public bool ForceCheckout { get; set; }

        [Option(
            "search-thread-count",
            Required = false,
            Default = 0,
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
            HelpText = "Sets the number of threads to use for checkout. (0 for number of logical cores)")]
        public int CheckoutThreadCount { get; set; }

        [Option(
            'r',
            "max-retries",
            Required = false,
            Default = 10,
            HelpText = "Sets the maximum number of attempts for downloading a pack")]

        public int MaxAttempts { get; set; }

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
            HelpText = "A semicolon-delimited list of folders to fetch")]
        public string FolderList { get; set; }

        [Option(
            "folders-list",
            Required = false,
            Default = "",
            HelpText = "A file containing line-delimited list of folders to fetch")]
        public string FolderListFile { get; set; }

        [Option(
            "Allow-index-metadata-update-from-working-tree",
            Required = false,
            Default = false,
            HelpText = "When specified, index metadata (file times and sizes) is updated from disk if not already in the index.  " +
                       "This flag should only be used when the working tree is known to be in a good state.  " +
                       "Do not use this flag if the working tree is not 100% known to be good as it would cause 'git status' to misreport.")]
        public bool AllowIndexMetadataUpdateFromWorkingTree { get; set; }

        [Option(
            "verbose",
            Required = false,
            Default = false,
            HelpText = "Show all outputs on the console in addition to writing them to a log file")]
        public bool Verbose { get; set; }

        [Option(
            "parent-activity-id",
            Required = false,
            Default = "",
            HelpText = "The GUID of the caller - used for telemetry purposes.")]
        public string ParentActivityId { get; set; }

        public void Execute()
        {
            Environment.ExitCode = this.ExecuteWithExitCode();
        }

        private int ExecuteWithExitCode()
        {
            // CmdParser doesn't strip quotes, and Path.Combine will throw
            this.GitBinPath = this.GitBinPath.Replace("\"", string.Empty);
            if (!GVFSPlatform.Instance.GitInstallation.GitExists(this.GitBinPath))
            {
                Console.WriteLine(
                    "Could not find git.exe {0}",
                    !string.IsNullOrWhiteSpace(this.GitBinPath) ? "at " + this.GitBinPath : "on %PATH%");
                return ExitFailure;
            }

            if (this.Commit != null && this.Branch != null)
            {
                Console.WriteLine("Cannot specify both a commit sha and a branch name.");
                return ExitFailure;
            }

            if (this.ForceCheckout && !this.Checkout)
            {
                Console.WriteLine("Cannot use --force-checkout option without --checkout option.");
                return ExitFailure;
            }

            this.SearchThreadCount = this.SearchThreadCount > 0 ? this.SearchThreadCount : Environment.ProcessorCount;
            this.DownloadThreadCount = this.DownloadThreadCount > 0 ? this.DownloadThreadCount : Math.Min(Environment.ProcessorCount, MaxDefaultDownloadThreads);
            this.IndexThreadCount = this.IndexThreadCount > 0 ? this.IndexThreadCount : Environment.ProcessorCount;
            this.CheckoutThreadCount = this.CheckoutThreadCount > 0 ? this.CheckoutThreadCount : Environment.ProcessorCount;

            this.GitBinPath = !string.IsNullOrWhiteSpace(this.GitBinPath) ? this.GitBinPath : GVFSPlatform.Instance.GitInstallation.GetInstalledGitBinPath();

            GitEnlistment enlistment = GitEnlistment.CreateFromCurrentDirectory(this.GitBinPath);
            if (enlistment == null)
            {
                Console.WriteLine("Must be run within a git repo");
                return ExitFailure;
            }

            string commitish = this.Commit ?? this.Branch;
            if (string.IsNullOrWhiteSpace(commitish))
            {
                GitProcess.Result result = new GitProcess(enlistment).GetCurrentBranchName();
                if (result.ExitCodeIsFailure || string.IsNullOrWhiteSpace(result.Output))
                {
                    Console.WriteLine("Could not retrieve current branch name: " + result.Errors);
                    return ExitFailure;
                }

                commitish = result.Output.Trim();
            }

            Guid parentActivityId = Guid.Empty;
            if (!string.IsNullOrWhiteSpace(this.ParentActivityId) && !Guid.TryParse(this.ParentActivityId, out parentActivityId))
            {
                Console.WriteLine("The ParentActivityId provided (" + this.ParentActivityId + ") is not a valid GUID.");
            }

            using (JsonTracer tracer = new JsonTracer("Microsoft.Git.FastFetch", parentActivityId, "FastFetch", enlistmentId: null, mountId: null, disableTelemetry: true))
            {
                if (this.Verbose)
                {
                    tracer.AddDiagnosticConsoleEventListener(EventLevel.Informational, Keywords.Any);
                }
                else
                {
                    tracer.AddPrettyConsoleEventListener(EventLevel.Error, Keywords.Any);
                }

                string fastfetchLogFile = Enlistment.GetNewLogFileName(enlistment.FastFetchLogRoot, "fastfetch");
                tracer.AddLogFileEventListener(fastfetchLogFile, EventLevel.Informational, Keywords.Any);

                CacheServerInfo cacheServer = new CacheServerInfo(this.GetRemoteUrl(enlistment), null);

                tracer.WriteStartEvent(
                    enlistment.EnlistmentRoot,
                    enlistment.RepoUrl,
                    cacheServer.Url,
                    new EventMetadata
                    {
                        { "TargetCommitish", commitish },
                        { "Checkout", this.Checkout },
                    });

                string error;
                if (!enlistment.Authentication.TryInitialize(tracer, enlistment, out error))
                {
                    tracer.RelatedError(error);
                    Console.WriteLine(error);
                    return ExitFailure;
                }

                RetryConfig retryConfig = new RetryConfig(this.MaxAttempts, TimeSpan.FromMinutes(RetryConfig.FetchAndCloneTimeoutMinutes));
                BlobPrefetcher prefetcher = this.GetFolderPrefetcher(tracer, enlistment, cacheServer, retryConfig);
                if (!BlobPrefetcher.TryLoadFolderList(enlistment, this.FolderList, this.FolderListFile, prefetcher.FolderList, readListFromStdIn: false, error: out error))
                {
                    tracer.RelatedError(error);
                    Console.WriteLine(error);
                    return ExitFailure;
                }

                bool isSuccess;

                try
                {
                    Func<bool> doPrefetch =
                        () =>
                        {
                            try
                            {
                                bool isBranch = this.Commit == null;
                                prefetcher.Prefetch(commitish, isBranch);
                                return !prefetcher.HasFailures;
                            }
                            catch (BlobPrefetcher.FetchException e)
                            {
                                tracer.RelatedError(e.Message);
                                return false;
                            }
                        };
                    if (this.Verbose)
                    {
                        isSuccess = doPrefetch();
                    }
                    else
                    {
                        isSuccess = ConsoleHelper.ShowStatusWhileRunning(
                            doPrefetch,
                            "Fetching",
                            output: Console.Out,
                            showSpinner: !Console.IsOutputRedirected,
                            gvfsLogEnlistmentRoot: null);

                        Console.WriteLine();
                        Console.WriteLine("See the full log at " + fastfetchLogFile);
                    }

                    isSuccess &= !prefetcher.HasFailures;
                }
                catch (AggregateException e)
                {
                    isSuccess = false;
                    foreach (Exception ex in e.Flatten().InnerExceptions)
                    {
                        tracer.RelatedError(ex.ToString());
                    }
                }
                catch (Exception e)
                {
                    isSuccess = false;
                    tracer.RelatedError(e.ToString());
                }

                EventMetadata stopMetadata = new EventMetadata();
                stopMetadata.Add("Success", isSuccess);
                tracer.Stop(stopMetadata);

                return isSuccess ? ExitSuccess : ExitFailure;
            }
        }

        private string GetRemoteUrl(Enlistment enlistment)
        {
            if (!string.IsNullOrWhiteSpace(this.CacheServerUrl))
            {
                return this.CacheServerUrl;
            }

            string configuredUrl = CacheServerResolver.GetUrlFromConfig(enlistment);
            if (!string.IsNullOrWhiteSpace(configuredUrl))
            {
                return configuredUrl;
            }

            return enlistment.RepoUrl;
        }

        private BlobPrefetcher GetFolderPrefetcher(ITracer tracer, Enlistment enlistment, CacheServerInfo cacheServer, RetryConfig retryConfig)
        {
            GitObjectsHttpRequestor objectRequestor = new GitObjectsHttpRequestor(tracer, enlistment, cacheServer, retryConfig);

            if (this.Checkout)
            {
                return new CheckoutPrefetcher(
                    tracer,
                    enlistment,
                    objectRequestor,
                    this.ChunkSize,
                    this.SearchThreadCount,
                    this.DownloadThreadCount,
                    this.IndexThreadCount,
                    this.CheckoutThreadCount,
                    this.AllowIndexMetadataUpdateFromWorkingTree,
                    this.ForceCheckout);
            }
            else
            {
                return new BlobPrefetcher(
                    tracer,
                    enlistment,
                    objectRequestor,
                    this.ChunkSize,
                    this.SearchThreadCount,
                    this.DownloadThreadCount,
                    this.IndexThreadCount);
            }
        }
    }
}
