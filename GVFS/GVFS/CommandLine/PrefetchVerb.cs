using GVFS.Common;
using GVFS.Common.FileSystem;
using GVFS.Common.Git;
using GVFS.Common.Http;
using GVFS.Common.Maintenance;
using GVFS.Common.NamedPipes;
using GVFS.Common.Prefetch;
using GVFS.Common.Tracing;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace GVFS.CommandLine
{
    public class PrefetchVerb : GVFSVerb.ForExistingEnlistment
    {
        private const string PrefetchVerbName = "prefetch";

        private const int LockWaitTimeMs = 100;
        private const int WaitingOnLockLogThreshold = 50;
        private const int IoFailureRetryDelayMS = 50;
        private const string PrefetchCommitsAndTreesLock = "prefetch-commits-trees.lock";

        private const int ChunkSize = 4000;
        private static readonly int SearchThreadCount = Environment.ProcessorCount;
        private static readonly int DownloadThreadCount = Environment.ProcessorCount;
        private static readonly int IndexThreadCount = Environment.ProcessorCount;

        public string Files { get; set; }

        public string Folders { get; set; }

        public string FoldersListFile { get; set; }

        public bool FilesFromStdIn { get; set; }

        public bool FoldersFromStdIn { get; set; }

        public string FilesListFile { get; set; }

        public bool HydrateFiles { get; set; }

        public bool Commits { get; set; }

        public bool Verbose { get; set; }

        public static System.CommandLine.Command CreateCommand()
        {
            System.CommandLine.Command cmd = new System.CommandLine.Command("prefetch", "Prefetch remote objects for the current head");

            System.CommandLine.Argument<string> enlistmentArg = GVFSVerb.CreateEnlistmentPathArgument();
            cmd.Add(enlistmentArg);

            System.CommandLine.Option<string> filesOption = new System.CommandLine.Option<string>("--files")
            {
                Description = "A semicolon-delimited list of files to fetch. Simple prefix wildcards, e.g. *.txt, are supported.",
                DefaultValueFactory = (_) => "",
            };
            cmd.Add(filesOption);

            System.CommandLine.Option<string> foldersOption = new System.CommandLine.Option<string>("--folders")
            {
                Description = "A semicolon-delimited list of folders to fetch. Wildcards are not supported.",
                DefaultValueFactory = (_) => "",
            };
            cmd.Add(foldersOption);

            System.CommandLine.Option<string> foldersListOption = new System.CommandLine.Option<string>("--folders-list")
            {
                Description = "A file containing line-delimited list of folders to fetch. Wildcards are not supported.",
                DefaultValueFactory = (_) => "",
            };
            cmd.Add(foldersListOption);

            System.CommandLine.Option<bool> stdinFilesOption = new System.CommandLine.Option<bool>("--stdin-files-list") { Description = "Specify this flag to load file list from stdin. Same format as when loading from file." };
            cmd.Add(stdinFilesOption);

            System.CommandLine.Option<bool> stdinFoldersOption = new System.CommandLine.Option<bool>("--stdin-folders-list") { Description = "Specify this flag to load folder list from stdin. Same format as when loading from file." };
            cmd.Add(stdinFoldersOption);

            System.CommandLine.Option<string> filesListOption = new System.CommandLine.Option<string>("--files-list")
            {
                Description = "A file containing line-delimited list of files to fetch. Wildcards are supported.",
                DefaultValueFactory = (_) => "",
            };
            cmd.Add(filesListOption);

            System.CommandLine.Option<bool> hydrateOption = new System.CommandLine.Option<bool>("--hydrate") { Description = "Specify this flag to also hydrate files in the working directory." };
            cmd.Add(hydrateOption);

            System.CommandLine.Option<bool> commitsOption = new System.CommandLine.Option<bool>("--commits", new[] { "-c" }) { Description = "Fetch the latest set of commit and tree packs. This option cannot be used with any of the file- or folder-related options." };
            cmd.Add(commitsOption);

            System.CommandLine.Option<bool> verboseOption = new System.CommandLine.Option<bool>("--verbose") { Description = "Show all outputs on the console in addition to writing them to a log file." };
            cmd.Add(verboseOption);

            System.CommandLine.Option<string> internalOption = GVFSVerb.CreateInternalParametersOption();
            cmd.Add(internalOption);

            GVFSVerb.SetActionForVerbWithEnlistment<PrefetchVerb>(cmd, enlistmentArg, internalOption, defaultEnlistmentPathToCwd: true,
                (verb, result) =>
                {
                    verb.Files = result.GetValue(filesOption) ?? "";
                    verb.Folders = result.GetValue(foldersOption) ?? "";
                    verb.FoldersListFile = result.GetValue(foldersListOption) ?? "";
                    verb.FilesFromStdIn = result.GetValue(stdinFilesOption);
                    verb.FoldersFromStdIn = result.GetValue(stdinFoldersOption);
                    verb.FilesListFile = result.GetValue(filesListOption) ?? "";
                    verb.HydrateFiles = result.GetValue(hydrateOption);
                    verb.Commits = result.GetValue(commitsOption);
                    verb.Verbose = result.GetValue(verboseOption);
                });

            return cmd;
        }

        public bool SkipVersionCheck { get; set; }
        public CacheServerInfo ResolvedCacheServer { get; set; }
        public ServerGVFSConfig ServerGVFSConfig { get; set; }

        protected override string VerbName
        {
            get { return PrefetchVerbName; }
        }

        protected override void Execute(GVFSEnlistment enlistment)
        {
            using (JsonTracer tracer = new JsonTracer(GVFSConstants.GVFSEtwProviderName, "Prefetch"))
            {
                if (this.Verbose)
                {
                    tracer.AddDiagnosticConsoleEventListener(EventLevel.Informational, Keywords.Any);
                }

                var cacheServerFromConfig = CacheServerResolver.GetCacheServerFromConfig(enlistment);

                tracer.AddLogFileEventListener(
                    GVFSEnlistment.GetNewGVFSLogFileName(enlistment.GVFSLogsRoot, GVFSConstants.LogFileTypes.Prefetch),
                    EventLevel.Informational,
                    Keywords.Any);
                tracer.WriteStartEvent(
                    enlistment.PrimaryEnlistmentRoot,
                    enlistment.RepoUrl,
                    cacheServerFromConfig.Url);

                try
                {
                    EventMetadata metadata = new EventMetadata();
                    metadata.Add("Commits", this.Commits);
                    metadata.Add("Files", this.Files);
                    metadata.Add("Folders", this.Folders);
                    metadata.Add("FileListFile", this.FilesListFile);
                    metadata.Add("FoldersListFile", this.FoldersListFile);
                    metadata.Add("FilesFromStdIn", this.FilesFromStdIn);
                    metadata.Add("FoldersFromStdIn", this.FoldersFromStdIn);
                    metadata.Add("HydrateFiles", this.HydrateFiles);
                    tracer.RelatedEvent(EventLevel.Informational, "PerformPrefetch", metadata);

                    if (this.Commits)
                    {
                        if (!string.IsNullOrWhiteSpace(this.Files) ||
                            !string.IsNullOrWhiteSpace(this.Folders) ||
                            !string.IsNullOrWhiteSpace(this.FoldersListFile) ||
                            !string.IsNullOrWhiteSpace(this.FilesListFile) ||
                            this.FilesFromStdIn ||
                            this.FoldersFromStdIn)
                        {
                            this.ReportErrorAndExit(tracer, "You cannot prefetch commits and blobs at the same time.");
                        }

                        if (this.HydrateFiles)
                        {
                            this.ReportErrorAndExit(tracer, "You can only specify --hydrate with --files or --folders");
                        }

                        // Try offload silently — if mount isn't available this returns
                        // false quickly and we fall through to the direct-auth path which
                        // has its own spinner. We don't wrap this in ShowStatusWhileRunning
                        // because a false return (mount unavailable) would print "Failed"
                        // to the console, which is misleading for an expected fallback.
                        bool offloadSucceeded = this.TryPrefetchCommitsViaMountProcess(tracer, enlistment);

                        if (!offloadSucceeded)
                        {
                            GitObjectsHttpRequestor objectRequestor;
                            CacheServerInfo resolvedCacheServer;
                            this.InitializeServerConnection(
                                tracer,
                                enlistment,
                                cacheServerFromConfig,
                                out objectRequestor,
                                out resolvedCacheServer);
                            this.PrefetchCommits(tracer, enlistment, objectRequestor, resolvedCacheServer);
                        }
                    }
                    else
                    {
                        string headCommitId;
                        List<string> filesList;
                        List<string> foldersList;
                        FileBasedDictionary<string, string> prefetchCache;
                        int prefetchCacheSize;

                        this.LoadBlobPrefetchArgs(tracer, enlistment, out headCommitId, out filesList, out foldersList, out prefetchCache, out prefetchCacheSize);

                        if (BlobPrefetcher.IsNoopPrefetch(tracer, prefetchCache, headCommitId, filesList, foldersList, this.HydrateFiles))
                        {
                            Console.WriteLine("All requested files are already available. Nothing new to prefetch.");
                        }
                        else if (filesList.Count == 0 && foldersList.Count == 0)
                        {
                            this.ReportErrorAndExit(tracer, "Did you mean to fetch all blobs? If so, specify `--files '*'` to confirm.");
                        }
                        else if (this.HydrateFiles)
                        {
                            // For --hydrate, try offloading the download phase to the mount
                            // (without hydration), then hydrate locally in the verb process.
                            // This avoids the mount process writing to ProjFS-virtualized files
                            // (self-callback risk) while still benefiting from warm auth.
                            if (!this.TryPrefetchBlobsViaMountProcess(tracer, enlistment, filesList, foldersList, headCommitId))
                            {
                                // Mount unavailable — fall back to direct auth for download
                                GitObjectsHttpRequestor objectRequestor;
                                CacheServerInfo resolvedCacheServer;
                                this.InitializeServerConnection(
                                    tracer,
                                    enlistment,
                                    cacheServerFromConfig,
                                    out objectRequestor,
                                    out resolvedCacheServer);
                                this.PrefetchBlobs(tracer, enlistment, headCommitId, filesList, foldersList, prefetchCache, prefetchCacheSize, objectRequestor, resolvedCacheServer);
                            }
                            else
                            {
                                // Mount handled download — hydrate locally, then update noop
                                // cache. Cache update is after hydration so a hydration failure
                                // doesn't suppress the retry on the next run.
                                this.HydrateMatchingFiles(tracer, enlistment, filesList, foldersList);
                                BlobPrefetcher.UpdateNoopCache(prefetchCache, prefetchCacheSize, headCommitId, filesList, foldersList, this.HydrateFiles);
                            }
                        }
                        else if (!this.TryPrefetchBlobsViaMountProcess(tracer, enlistment, filesList, foldersList, headCommitId))
                        {
                            GitObjectsHttpRequestor objectRequestor;
                            CacheServerInfo resolvedCacheServer;
                            this.InitializeServerConnection(
                                tracer,
                                enlistment,
                                cacheServerFromConfig,
                                out objectRequestor,
                                out resolvedCacheServer);
                            this.PrefetchBlobs(tracer, enlistment, headCommitId, filesList, foldersList, prefetchCache, prefetchCacheSize, objectRequestor, resolvedCacheServer);
                        }
                        else
                        {
                            // Mount handled download — update noop cache so repeat runs are skipped
                            BlobPrefetcher.UpdateNoopCache(prefetchCache, prefetchCacheSize, headCommitId, filesList, foldersList, hydrate: false);
                        }
                    }
                }
                catch (VerbAbortedException)
                {
                    throw;
                }
                catch (AggregateException aggregateException)
                {
                    this.Output.WriteLine(
                        "Cannot prefetch {0}. " + ConsoleHelper.GetGVFSLogMessage(enlistment.WorkingDirectoryRoot),
                        enlistment.WorkingDirectoryRoot);
                    foreach (Exception innerException in aggregateException.Flatten().InnerExceptions)
                    {
                        tracer.RelatedError(
                            new EventMetadata
                            {
                                { "Verb", typeof(PrefetchVerb).Name },
                                { "Exception", innerException.ToString() }
                            },
                            $"Unhandled {innerException.GetType().Name}: {innerException.Message}");
                    }

                    Environment.ExitCode = (int)ReturnCode.GenericError;
                }
                catch (Exception e)
                {
                    this.Output.WriteLine(
                        "Cannot prefetch {0}. " + ConsoleHelper.GetGVFSLogMessage(enlistment.WorkingDirectoryRoot),
                        enlistment.WorkingDirectoryRoot);
                    tracer.RelatedError(
                        new EventMetadata
                        {
                            { "Verb", typeof(PrefetchVerb).Name },
                            { "Exception", e.ToString() }
                        },
                        $"Unhandled {e.GetType().Name}: {e.Message}");

                    Environment.ExitCode = (int)ReturnCode.GenericError;
                }
            }
        }

        private void InitializeServerConnection(
            ITracer tracer,
            GVFSEnlistment enlistment,
            CacheServerInfo cacheServerFromConfig,
            out GitObjectsHttpRequestor objectRequestor,
            out CacheServerInfo resolvedCacheServer)
        {
            RetryConfig retryConfig = this.GetRetryConfig(tracer, enlistment, TimeSpan.FromMinutes(RetryConfig.FetchAndCloneTimeoutMinutes));

            // These this.* arguments are set if this is a follow-on operation from clone or mount.
            resolvedCacheServer = this.ResolvedCacheServer;
            ServerGVFSConfig serverGVFSConfig = this.ServerGVFSConfig;

            // If ResolvedCacheServer is set, then we have already tried querying the server config and checking versions.
            if (resolvedCacheServer == null)
            {
                if (serverGVFSConfig == null)
                {
                    string authErrorMessage;
                    if (!this.TryAuthenticateAndQueryGVFSConfig(
                        tracer,
                        enlistment,
                        retryConfig,
                        out serverGVFSConfig,
                        out authErrorMessage,
                        fallbackCacheServer: cacheServerFromConfig))
                    {
                        this.ReportErrorAndExit(tracer, "Unable to prefetch because authentication failed: " + authErrorMessage);
                    }
                }

                CacheServerResolver cacheServerResolver = new CacheServerResolver(tracer, enlistment);

                resolvedCacheServer = cacheServerResolver.ResolveNameFromRemote(cacheServerFromConfig.Url, serverGVFSConfig);

                if (!this.SkipVersionCheck)
                {
                    this.ValidateClientVersions(tracer, enlistment, serverGVFSConfig, showWarnings: false);
                }

                this.Output.WriteLine("Configured cache server: " + resolvedCacheServer);
            }

            this.InitializeLocalCacheAndObjectsPaths(tracer, enlistment, retryConfig, serverGVFSConfig, resolvedCacheServer);
            objectRequestor = new GitObjectsHttpRequestor(tracer, enlistment, resolvedCacheServer, retryConfig);
        }

        /// <summary>
        /// Attempts to offload the commit prefetch to a running mount process,
        /// which already has warm authentication. Returns true if the mount
        /// handled the request (success or failure); returns false if offload
        /// is unavailable and the caller should fall back to direct auth.
        /// </summary>
        private bool TryPrefetchCommitsViaMountProcess(ITracer tracer, GVFSEnlistment enlistment)
        {
            using (NamedPipeClient pipeClient = new NamedPipeClient(enlistment.NamedPipeName))
            {
                if (!pipeClient.Connect())
                {
                    tracer.RelatedInfo("TryPrefetchCommitsViaMountProcess: Mount not running, falling back to direct prefetch");
                    return false;
                }

                NamedPipeMessages.Message request = new NamedPipeMessages.Message(NamedPipeMessages.PrefetchCommits.Request, null);
                if (!pipeClient.TrySendRequest(request))
                {
                    tracer.RelatedWarning("TryPrefetchCommitsViaMountProcess: Failed to send request, falling back to direct prefetch");
                    return false;
                }

                NamedPipeMessages.Message response;
                if (!pipeClient.TryReadResponse(out response))
                {
                    tracer.RelatedWarning("TryPrefetchCommitsViaMountProcess: Failed to read response, falling back to direct prefetch");
                    return false;
                }

                switch (response.Header)
                {
                    case NamedPipeMessages.PrefetchCommits.CompleteResult:
                        NamedPipeMessages.PrefetchCommits.Response prefetchResponse =
                            NamedPipeMessages.PrefetchCommits.Response.FromMessage(response);

                        if (prefetchResponse.Success)
                        {
                            tracer.RelatedInfo("TryPrefetchCommitsViaMountProcess: Mount completed prefetch successfully");
                            return true;
                        }

                        this.ReportErrorAndExit(tracer, "Prefetching commits and trees failed (via mount): " + prefetchResponse.Error);
                        return true;

                    case NamedPipeMessages.PrefetchCommits.MountNotReadyResult:
                        tracer.RelatedInfo("TryPrefetchCommitsViaMountProcess: Mount not ready, falling back to direct prefetch");
                        return false;

                    default:
                        // Older mount that doesn't recognize PrefetchCommits
                        tracer.RelatedInfo("TryPrefetchCommitsViaMountProcess: Unexpected response '{0}', falling back to direct prefetch", response.Header);
                        return false;
                }
            }
        }

        /// <summary>
        /// Attempts to offload the blob prefetch to a running mount process,
        /// which already has warm authentication. Returns true if the mount
        /// handled the request (success or failure); returns false if offload
        /// is unavailable and the caller should fall back to direct auth.
        /// </summary>
        private bool TryPrefetchBlobsViaMountProcess(
            ITracer tracer,
            GVFSEnlistment enlistment,
            List<string> filesList,
            List<string> foldersList,
            string headCommitId)
        {
            using (NamedPipeClient pipeClient = new NamedPipeClient(enlistment.NamedPipeName))
            {
                if (!pipeClient.Connect())
                {
                    tracer.RelatedInfo("TryPrefetchBlobsViaMountProcess: Mount not running, falling back to direct prefetch");
                    return false;
                }

                NamedPipeMessages.PrefetchBlobs.Request request = new NamedPipeMessages.PrefetchBlobs.Request
                {
                    Files = filesList,
                    Folders = foldersList,
                    HeadCommitId = headCommitId,
                };

                if (!pipeClient.TrySendRequest(request.CreateMessage()))
                {
                    tracer.RelatedWarning("TryPrefetchBlobsViaMountProcess: Failed to send request, falling back to direct prefetch");
                    return false;
                }

                NamedPipeMessages.Message response;
                if (!pipeClient.TryReadResponse(out response))
                {
                    tracer.RelatedWarning("TryPrefetchBlobsViaMountProcess: Failed to read response, falling back to direct prefetch");
                    return false;
                }

                switch (response.Header)
                {
                    case NamedPipeMessages.PrefetchBlobs.CompleteResult:
                        NamedPipeMessages.PrefetchBlobs.Response blobResponse =
                            NamedPipeMessages.PrefetchBlobs.Response.FromMessage(response);

                        if (blobResponse.Success)
                        {
                            tracer.RelatedInfo("TryPrefetchBlobsViaMountProcess: Mount completed blob prefetch successfully");

                            Console.WriteLine();
                            Console.WriteLine("Stats:");
                            Console.WriteLine("  Matched blobs:    " + blobResponse.MatchedBlobCount);
                            Console.WriteLine("  Already cached:   " + (blobResponse.MatchedBlobCount - blobResponse.DownloadedBlobCount));
                            Console.WriteLine("  Downloaded:       " + blobResponse.DownloadedBlobCount);

                            return true;
                        }

                        this.ReportErrorAndExit(tracer, "Prefetching blobs failed (via mount): " + blobResponse.Error);
                        return true;

                    case NamedPipeMessages.PrefetchBlobs.MountNotReadyResult:
                        tracer.RelatedInfo("TryPrefetchBlobsViaMountProcess: Mount not ready, falling back to direct prefetch");
                        return false;

                    default:
                        tracer.RelatedInfo("TryPrefetchBlobsViaMountProcess: Unexpected response '{0}', falling back to direct prefetch", response.Header);
                        return false;
                }
            }
        }

        private void PrefetchCommits(ITracer tracer, GVFSEnlistment enlistment, GitObjectsHttpRequestor objectRequestor, CacheServerInfo cacheServer)
        {
            bool success;
            string error = string.Empty;
            PhysicalFileSystem fileSystem = new PhysicalFileSystem();
            GitRepo repo = new GitRepo(tracer, enlistment, fileSystem);
            GVFSContext context = new GVFSContext(tracer, fileSystem, repo, enlistment);
            GitObjects gitObjects = new GVFSGitObjects(context, objectRequestor);

            if (this.Verbose)
            {
                success = new PrefetchStep(context, gitObjects, requireCacheLock: false).TryPrefetchCommitsAndTrees(out error);
            }
            else
            {
                success = this.ShowStatusWhileRunning(
                    () => new PrefetchStep(context, gitObjects, requireCacheLock: false).TryPrefetchCommitsAndTrees(out error),
                "Fetching commits and trees " + this.GetCacheServerDisplay(cacheServer, enlistment.RepoUrl));
            }

            if (!success)
            {
                this.ReportErrorAndExit(tracer, "Prefetching commits and trees failed: " + error);
            }
        }

        private void LoadBlobPrefetchArgs(
            ITracer tracer,
            GVFSEnlistment enlistment,
            out string headCommitId,
            out List<string> filesList,
            out List<string> foldersList,
            out FileBasedDictionary<string, string> prefetchCache,
            out int prefetchCacheSize)
        {
            string error;

            // Read cache size from git config
            prefetchCacheSize = BlobPrefetcher.DefaultPrefetchCacheSize;
            GitProcess gitProcess = new GitProcess(enlistment);
            if (gitProcess.TryGetFromConfig(BlobPrefetcher.PrefetchCacheSizeConfigKey, forceOutsideEnlistment: false, out string cacheSizeValue))
            {
                if (int.TryParse(cacheSizeValue, out int parsedSize))
                {
                    prefetchCacheSize = Math.Clamp(parsedSize, 0, BlobPrefetcher.MaxPrefetchCacheSize);
                }
                else
                {
                    tracer.RelatedWarning($"Invalid value '{cacheSizeValue}' for {BlobPrefetcher.PrefetchCacheSizeConfigKey}, using default {BlobPrefetcher.DefaultPrefetchCacheSize}");
                }
            }

            prefetchCache = null;
            if (prefetchCacheSize > 0)
            {
                if (!FileBasedDictionary<string, string>.TryCreate(
                        tracer,
                        Path.Combine(enlistment.DotGVFSRoot, BlobPrefetcher.BlobPrefetchCacheFile),
                        new PhysicalFileSystem(),
                        out prefetchCache,
                        out error))
                {
                    tracer.RelatedWarning("Unable to load prefetch cache: " + error);
                }
            }

            filesList = new List<string>();
            foldersList = new List<string>();

            if (!BlobPrefetcher.TryLoadFileList(enlistment, this.Files, this.FilesListFile, filesList, readListFromStdIn: this.FilesFromStdIn, error: out error))
            {
                this.ReportErrorAndExit(tracer, error);
            }

            if (!BlobPrefetcher.TryLoadFolderList(enlistment, this.Folders, this.FoldersListFile, foldersList, readListFromStdIn: this.FoldersFromStdIn, error: out error))
            {
                this.ReportErrorAndExit(tracer, error);
            }

            GitProcess.Result result = gitProcess.RevParse(GVFSConstants.DotGit.HeadName);
            if (result.ExitCodeIsFailure)
            {
                this.ReportErrorAndExit(tracer, result.Errors);
            }

            headCommitId = result.Output.Trim();
        }

        private void PrefetchBlobs(
            ITracer tracer,
            GVFSEnlistment enlistment,
            string headCommitId,
            List<string> filesList,
            List<string> foldersList,
            FileBasedDictionary<string, string> prefetchCache,
            int prefetchCacheSize,
            GitObjectsHttpRequestor objectRequestor,
            CacheServerInfo cacheServer)
        {
            BlobPrefetcher blobPrefetcher = new BlobPrefetcher(
                tracer,
                enlistment,
                objectRequestor,
                filesList,
                foldersList,
                prefetchCache,
                prefetchCacheSize,
                ChunkSize,
                SearchThreadCount,
                DownloadThreadCount,
                IndexThreadCount);

            if (blobPrefetcher.FolderList.Count == 0 &&
                blobPrefetcher.FileList.Count == 0)
            {
                this.ReportErrorAndExit(tracer, "Did you mean to fetch all blobs? If so, specify `--files '*'` to confirm.");
            }

            if (this.HydrateFiles)
            {
                if (!this.CheckIsMounted(verbose: true))
                {
                    this.ReportErrorAndExit("You can only specify --hydrate if the repo is mounted. Run 'gvfs mount' and try again.");
                }
            }

            int matchedBlobCount = 0;
            int downloadedBlobCount = 0;
            int hydratedFileCount = 0;

            Func<bool> doPrefetch =
                () =>
                {
                    try
                    {
                        blobPrefetcher.PrefetchWithStats(
                            headCommitId,
                            isBranch: false,
                            hydrateFilesAfterDownload: this.HydrateFiles,
                            matchedBlobCount: out matchedBlobCount,
                            downloadedBlobCount: out downloadedBlobCount,
                            hydratedFileCount: out hydratedFileCount);
                        return !blobPrefetcher.HasFailures;
                    }
                    catch (BlobPrefetcher.FetchException e)
                    {
                        tracer.RelatedError(e.Message);
                        return false;
                    }
                };

            if (this.Verbose)
            {
                doPrefetch();
            }
            else
            {
                string message =
                    this.HydrateFiles
                    ? "Fetching blobs and hydrating files "
                    : "Fetching blobs ";
                this.ShowStatusWhileRunning(doPrefetch, message + this.GetCacheServerDisplay(cacheServer, enlistment.RepoUrl));
            }

            if (blobPrefetcher.HasFailures)
            {
                Environment.ExitCode = 1;
            }
            else
            {
                Console.WriteLine();
                Console.WriteLine("Stats:");
                Console.WriteLine("  Matched blobs:    " + matchedBlobCount);
                Console.WriteLine("  Already cached:   " + (matchedBlobCount - downloadedBlobCount));
                Console.WriteLine("  Downloaded:       " + downloadedBlobCount);
                if (this.HydrateFiles)
                {
                    Console.WriteLine("  Hydrated files:   " + hydratedFileCount);
                }
            }
        }

        private bool CheckIsMounted(bool verbose)
        {
            Func<bool> checkMount = () => this.Execute<StatusVerb>(
                    this.EnlistmentRootPathParameter,
                    verb => verb.Output = new StreamWriter(new MemoryStream())) == ReturnCode.Success;

            if (verbose)
            {
                return ConsoleHelper.ShowStatusWhileRunning(
                    checkMount,
                    "Checking that GVFS is mounted",
                    this.Output,
                    showSpinner: true,
                    gvfsLogEnlistmentRoot: null);
            }
            else
            {
                return checkMount();
            }
        }

        private string GetCacheServerDisplay(CacheServerInfo cacheServer, string repoUrl)
        {
            if (!cacheServer.IsNone(repoUrl))
            {
                return "from cache server";
            }

            return "from origin (no cache server)";
        }

        /// <summary>
        /// Hydrates files matching the file/folder filters by reading 1 byte from each.
        /// Runs in the verb process (not the mount) to avoid ProjFS self-callbacks.
        /// Blobs should already be in the object cache from a prior download phase.
        /// </summary>
        private void HydrateMatchingFiles(
            ITracer tracer,
            GVFSEnlistment enlistment,
            List<string> filesList,
            List<string> foldersList)
        {
            string workingDir = enlistment.WorkingDirectoryRoot;
            List<string> filesToHydrate = new List<string>();

            // Collect files from folder filters
            foreach (string folder in foldersList)
            {
                string normalizedFolder = folder.Replace(GVFSConstants.GitPathSeparator, Path.DirectorySeparatorChar).TrimEnd(Path.DirectorySeparatorChar);
                string fullFolderPath = Path.Combine(workingDir, normalizedFolder);
                if (Directory.Exists(fullFolderPath))
                {
                    filesToHydrate.AddRange(Directory.EnumerateFiles(fullFolderPath, "*", SearchOption.AllDirectories));
                }
            }

            // Collect files from file filters (supports simple prefix wildcards like *.txt)
            foreach (string filePattern in filesList)
            {
                string normalizedPattern = filePattern.Replace(GVFSConstants.GitPathSeparator, Path.DirectorySeparatorChar);

                if (normalizedPattern.StartsWith("*"))
                {
                    // Prefix wildcard — search entire working directory
                    filesToHydrate.AddRange(Directory.EnumerateFiles(workingDir, normalizedPattern, SearchOption.AllDirectories));
                }
                else
                {
                    // Exact file path
                    string fullPath = Path.Combine(workingDir, normalizedPattern);
                    if (File.Exists(fullPath))
                    {
                        filesToHydrate.Add(fullPath);
                    }
                }
            }

            if (filesToHydrate.Count == 0)
            {
                tracer.RelatedInfo("HydrateMatchingFiles: No files to hydrate");
                return;
            }

            int hydratedCount = 0;
            int failedCount = 0;
            int maxParallelism = Math.Max(1, Environment.ProcessorCount / 2);

            bool success = true;
            Func<bool> doHydrate = () =>
            {
                Parallel.ForEach(
                    filesToHydrate,
                    new ParallelOptions { MaxDegreeOfParallelism = maxParallelism },
                    filePath =>
                    {
                        if (GVFSPlatform.Instance.FileSystem.HydrateFile(filePath, new byte[1]))
                        {
                            Interlocked.Increment(ref hydratedCount);
                        }
                        else
                        {
                            tracer.RelatedWarning("HydrateMatchingFiles: Failed to hydrate " + filePath);
                            Interlocked.Increment(ref failedCount);
                        }
                    });

                return failedCount == 0;
            };

            if (this.Verbose)
            {
                success = doHydrate();
            }
            else
            {
                success = this.ShowStatusWhileRunning(doHydrate, "Hydrating files");
            }

            Console.WriteLine();
            Console.WriteLine("  Hydrated files:   " + hydratedCount);
            if (failedCount > 0)
            {
                Console.WriteLine("  Failed to hydrate: " + failedCount);
                Environment.ExitCode = 1;
            }
        }
    }
}
