using CommandLine;
using GVFS.Common;
using GVFS.Common.FileSystem;
using GVFS.Common.Git;
using GVFS.Common.Http;
using GVFS.Common.NamedPipes;
using GVFS.Common.Prefetch;
using GVFS.Common.Tracing;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;

namespace GVFS.CommandLine
{
    [Verb(PrefetchVerb.PrefetchVerbName, HelpText = "Prefetch remote objects for the current head")]
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

        [Option(
            "files",
            Required = false,
            Default = "",
            HelpText = "A semicolon-delimited list of files to fetch. Simple prefix wildcards, e.g. *.txt, are supported.")]
        public string Files { get; set; }

        [Option(
            "folders",
            Required = false,
            Default = "",
            HelpText = "A semicolon-delimited list of folders to fetch. Wildcards are not supported.")]
        public string Folders { get; set; }

        [Option(
            "folders-list",
            Required = false,
            Default = "",
            HelpText = "A file containing line-delimited list of folders to fetch. Wildcards are not supported.")]
        public string FoldersListFile { get; set; }

        [Option(
            "hydrate",
            Required = false,
            Default = false,
            HelpText = "Specify this flag to also hydrate files in the working directory")]
        public bool HydrateFiles { get; set; }

        [Option(
            'c',
            "commits",
            Required = false,
            Default = false,
            HelpText = "Fetch the latest set of commit and tree packs. This option cannot be used with any of the file- or folder-related options.")]
        public bool Commits { get; set; }

        [Option(
            "verbose",
            Required = false,
            Default = false,
            HelpText = "Show all outputs on the console in addition to writing them to a log file")]
        public bool Verbose { get; set; }

        public bool SkipVersionCheck { get; set; }
        public CacheServerInfo ResolvedCacheServer { get; set; }
        public GVFSConfig GVFSConfig { get; set; }

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

                string cacheServerUrl = CacheServerResolver.GetUrlFromConfig(enlistment);

                tracer.AddLogFileEventListener(
                    GVFSEnlistment.GetNewGVFSLogFileName(enlistment.GVFSLogsRoot, GVFSConstants.LogFileTypes.Prefetch),
                    EventLevel.Informational,
                    Keywords.Any);
                tracer.WriteStartEvent(
                    enlistment.EnlistmentRoot,
                    enlistment.RepoUrl,
                    cacheServerUrl);

                RetryConfig retryConfig = this.GetRetryConfig(tracer, enlistment, TimeSpan.FromMinutes(RetryConfig.FetchAndCloneTimeoutMinutes));

                CacheServerInfo cacheServer = this.ResolvedCacheServer;
                GVFSConfig gvfsConfig = this.GVFSConfig;
                if (!this.SkipVersionCheck)
                {
                    string authErrorMessage;
                    if (!this.ShowStatusWhileRunning(
                        () => enlistment.Authentication.TryRefreshCredentials(tracer, out authErrorMessage),
                        "Authenticating"))
                    {
                        this.ReportErrorAndExit(tracer, "Unable to prefetch because authentication failed");
                    }

                    if (gvfsConfig == null)
                    {
                        gvfsConfig = this.QueryGVFSConfig(tracer, enlistment, retryConfig);
                    }

                    if (cacheServer == null)
                    {
                        CacheServerResolver cacheServerResolver = new CacheServerResolver(tracer, enlistment);
                        cacheServer = cacheServerResolver.ResolveNameFromRemote(cacheServerUrl, gvfsConfig);
                    }

                    this.ValidateClientVersions(tracer, enlistment, gvfsConfig, showWarnings: false);

                    this.Output.WriteLine("Configured cache server: " + cacheServer);
                }

                this.InitializeLocalCacheAndObjectsPaths(tracer, enlistment, retryConfig, gvfsConfig, cacheServer);

                try
                {
                    EventMetadata metadata = new EventMetadata();
                    metadata.Add("Commits", this.Commits);
                    metadata.Add("Files", this.Files);
                    metadata.Add("Folders", this.Folders);
                    metadata.Add("FoldersListFile", this.FoldersListFile);
                    metadata.Add("HydrateFiles", this.HydrateFiles);
                    tracer.RelatedEvent(EventLevel.Informational, "PerformPrefetch", metadata);

                    GitObjectsHttpRequestor objectRequestor = new GitObjectsHttpRequestor(tracer, enlistment, cacheServer, retryConfig);

                    if (this.Commits)
                    {
                        if (!string.IsNullOrWhiteSpace(this.Files) ||
                            !string.IsNullOrWhiteSpace(this.Folders) ||
                            !string.IsNullOrWhiteSpace(this.FoldersListFile))
                        {
                            this.ReportErrorAndExit(tracer, "You cannot prefetch commits and blobs at the same time.");
                        }

                        if (this.HydrateFiles)
                        {
                            this.ReportErrorAndExit(tracer, "You can only specify --hydrate with --files or --folders");
                        }

                        PhysicalFileSystem fileSystem = new PhysicalFileSystem();
                        using (FileBasedLock prefetchLock = new FileBasedLock(
                            fileSystem,
                            tracer,
                            Path.Combine(enlistment.GitPackRoot, PrefetchCommitsAndTreesLock),
                            enlistment.EnlistmentRoot,
                            overwriteExistingLock: true))
                        {
                            this.WaitUntilLockIsAcquired(tracer, prefetchLock);
                            this.PrefetchCommits(tracer, enlistment, objectRequestor, cacheServer);
                        }
                    }
                    else
                    {
                        this.PrefetchBlobs(tracer, enlistment, objectRequestor, cacheServer);
                    }
                }
                catch (VerbAbortedException)
                {
                    throw;
                }
                catch (AggregateException aggregateException)
                {
                    this.Output.WriteLine(
                        "Cannot prefetch {0}. " + ConsoleHelper.GetGVFSLogMessage(enlistment.EnlistmentRoot),
                        enlistment.EnlistmentRoot);
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
                        "Cannot prefetch {0}. " + ConsoleHelper.GetGVFSLogMessage(enlistment.EnlistmentRoot),
                        enlistment.EnlistmentRoot);
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

        private void WaitUntilLockIsAcquired(ITracer tracer, FileBasedLock fileBasedLock)
        {
            int attempt = 0;
            while (!fileBasedLock.TryAcquireLockAndDeleteOnClose())
            {
                Thread.Sleep(LockWaitTimeMs);
                ++attempt;
                if (attempt == WaitingOnLockLogThreshold)
                {
                    attempt = 0;
                    tracer.RelatedInfo("WaitUntilLockIsAcquired: Waiting to acquire prefetch lock");
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
            List<string> packIndexes = null;
            if (this.Verbose)
            {
                success = this.TryPrefetchCommitsAndTrees(tracer, enlistment, fileSystem, gitObjects, out error, out packIndexes);
            }
            else
            {
                success = this.ShowStatusWhileRunning(
                    () => this.TryPrefetchCommitsAndTrees(tracer, enlistment, fileSystem, gitObjects, out error, out packIndexes),
                    "Fetching commits and trees " + this.GetCacheServerDisplay(cacheServer));
            }

            if (success)
            {
                if (packIndexes.Count == 0)
                {
                    return;
                }

                // We make a best-effort request to run MIDX and commit-graph writes
                using (NamedPipeClient pipeClient = new NamedPipeClient(enlistment.NamedPipeName))
                {
                    if (!pipeClient.Connect())
                    {
                        tracer.RelatedWarning(
                            metadata: null,
                            message: "Failed to connect to GVFS. Skipping post-fetch job request.",
                            keywords: Keywords.Telemetry);
                        return;
                    }

                    NamedPipeMessages.RunPostFetchJob.Request request = new NamedPipeMessages.RunPostFetchJob.Request(packIndexes);
                    if (pipeClient.TrySendRequest(request.CreateMessage()))
                    {
                        NamedPipeMessages.Message response;

                        if (pipeClient.TryReadResponse(out response))
                        {
                            tracer.RelatedInfo("Requested post-fetch job with resonse '{0}'", response.Header);
                        }
                        else
                        {
                            tracer.RelatedWarning(
                                metadata: null,
                                message: "Requested post-fetch job failed to respond",
                                keywords: Keywords.Telemetry);
                        }
                    }
                    else
                    {
                        tracer.RelatedWarning(
                            metadata: null,
                            message: "Message to named pipe failed to send, skipping post-fetch job request.",
                            keywords: Keywords.Telemetry);
                    }
                }
            }
            else
            {
                this.ReportErrorAndExit(tracer, "Prefetching commits and trees failed: " + error);
            }
        }

        private void PrefetchBlobs(ITracer tracer, GVFSEnlistment enlistment, GitObjectsHttpRequestor blobRequestor, CacheServerInfo cacheServer)
        {
            PrefetchHelper fetchHelper = new PrefetchHelper(
                tracer,
                enlistment,
                blobRequestor,
                ChunkSize,
                SearchThreadCount,
                DownloadThreadCount,
                IndexThreadCount);

            string error;
            if (!PrefetchHelper.TryLoadFolderList(enlistment, this.Folders, this.FoldersListFile, fetchHelper.FolderList, out error))
            {
                this.ReportErrorAndExit(tracer, error);
            }

            if (!PrefetchHelper.TryLoadFileList(enlistment, this.Files, fetchHelper.FileList, out error))
            {
                this.ReportErrorAndExit(tracer, error);
            }

            if (fetchHelper.FolderList.Count == 0 &&
                fetchHelper.FileList.Count == 0)
            {
                this.ReportErrorAndExit(tracer, "Did you mean to fetch all blobs? If so, specify `--files *` to confirm.");
            }

            if (this.HydrateFiles)
            {
                if (!this.CheckIsMounted(verbose: true))
                {
                    this.ReportErrorAndExit("You can only specify --hydrate if the repo is mounted. Run 'gvfs mount' and try again.");
                }
            }

            GitProcess gitProcess = new GitProcess(enlistment);
            GitProcess.Result result = gitProcess.RevParse(GVFSConstants.DotGit.HeadName);
            if (result.HasErrors)
            {
                tracer.RelatedError(result.Errors);
                this.Output.WriteLine(result.Errors);
                Environment.ExitCode = (int)ReturnCode.GenericError;
                return;
            }

            int matchedBlobCount = 0;
            int downloadedBlobCount = 0;
            int readFileCount = 0;

            string headCommitId = result.Output;
            Func<bool> doPrefetch =
                () =>
                {
                    try
                    {
                        fetchHelper.PrefetchWithStats(
                            headCommitId.Trim(),
                            isBranch: false,
                            readFilesAfterDownload: this.HydrateFiles,
                            matchedBlobCount: out matchedBlobCount,
                            downloadedBlobCount: out downloadedBlobCount,
                            readFileCount: out readFileCount);
                        return !fetchHelper.HasFailures;
                    }
                    catch (PrefetchHelper.FetchException e)
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
                this.ShowStatusWhileRunning(doPrefetch, message + this.GetCacheServerDisplay(cacheServer));
            }

            if (fetchHelper.HasFailures)
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
                    Console.WriteLine("  Hydrated files:   " + readFileCount);
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

        private bool TryPrefetchCommitsAndTrees(
            ITracer tracer,
            GVFSEnlistment enlistment,
            PhysicalFileSystem fileSystem,
            GitObjects gitObjects,
            out string error,
            out List<string> packIndexes)
        {
            long maxGoodTimeStamp;
            if (!this.TryGetMaxGoodPrefetchTimestamp(tracer, enlistment, fileSystem, gitObjects, out maxGoodTimeStamp, out error))
            {
                packIndexes = null;
                return false;
            }

            if (!gitObjects.TryDownloadPrefetchPacks(maxGoodTimeStamp, out packIndexes))
            {
                error = "Failed to download prefetch packs";
                return false;
            }

            return true;
        }

        private bool TryGetMaxGoodPrefetchTimestamp(
            ITracer tracer,
            GVFSEnlistment enlistment,
            PhysicalFileSystem fileSystem,
            GitObjects gitObjects,
            out long maxGoodTimestamp,
            out string error)
        {
            gitObjects.DeleteStaleTempPrefetchPackAndIdxs();

            string[] packs = gitObjects.ReadPackFileNames(enlistment.GitPackRoot, GVFSConstants.PrefetchPackPrefix);
            List<PrefetchPackInfo> orderedPacks = packs
                .Where(pack => this.GetTimestamp(pack).HasValue)
                .Select(pack => new PrefetchPackInfo(this.GetTimestamp(pack).Value, pack))
                .OrderBy(packInfo => packInfo.Timestamp)
                .ToList();

            maxGoodTimestamp = -1;

            int firstBadPack = -1;
            for (int i = 0; i < orderedPacks.Count; ++i)
            {
                long timestamp = orderedPacks[i].Timestamp;
                string packPath = orderedPacks[i].Path;
                string idxPath = Path.ChangeExtension(packPath, ".idx");
                if (!fileSystem.FileExists(idxPath))
                {
                    EventMetadata metadata = new EventMetadata();
                    metadata.Add("pack", packPath);
                    metadata.Add("idxPath", idxPath);
                    metadata.Add("timestamp", timestamp);
                    GitProcess.Result indexResult = gitObjects.IndexPackFile(packPath);
                    if (indexResult.HasErrors)
                    {
                        firstBadPack = i;

                        metadata.Add("Errors", indexResult.Errors);
                        tracer.RelatedWarning(metadata, $"{nameof(this.TryPrefetchCommitsAndTrees)}: Found pack file that's missing idx file, and failed to regenerate idx");
                        break;
                    }
                    else
                    {
                        maxGoodTimestamp = timestamp;

                        metadata.Add(TracingConstants.MessageKey.InfoMessage, $"{nameof(this.TryPrefetchCommitsAndTrees)}: Found pack file that's missing idx file, and regenerated idx");
                        tracer.RelatedEvent(EventLevel.Informational, $"{nameof(this.TryPrefetchCommitsAndTrees)}_RebuildIdx", metadata);
                    }
                }
                else
                {
                    maxGoodTimestamp = timestamp;
                }
            }

            if (firstBadPack != -1)
            {
                const int MaxDeleteRetries = 200; // 200 * IoFailureRetryDelayMS (50ms) = 10 seconds
                const int RetryLoggingThreshold = 40; // 40 * IoFailureRetryDelayMS (50ms) = 2 seconds

                // Delete packs and indexes in reverse order so that if prefetch is killed, subseqeuent prefetch commands will
                // find the right starting spot.
                for (int i = orderedPacks.Count - 1; i >= firstBadPack; --i)
                {
                    string packPath = orderedPacks[i].Path;
                    string idxPath = Path.ChangeExtension(packPath, ".idx");

                    EventMetadata metadata = new EventMetadata();
                    metadata.Add("path", idxPath);
                    metadata.Add(TracingConstants.MessageKey.InfoMessage, $"{nameof(this.TryPrefetchCommitsAndTrees)} deleting bad idx file");
                    tracer.RelatedEvent(EventLevel.Informational, $"{nameof(this.TryPrefetchCommitsAndTrees)}_DeleteBadIdx", metadata);
                    if (!fileSystem.TryWaitForDelete(tracer, idxPath, IoFailureRetryDelayMS, MaxDeleteRetries, RetryLoggingThreshold))
                    {
                        error = $"Unable to delete {idxPath}";
                        return false;
                    }

                    metadata = new EventMetadata();
                    metadata.Add("path", packPath);
                    metadata.Add(TracingConstants.MessageKey.InfoMessage, $"{nameof(this.TryPrefetchCommitsAndTrees)} deleting bad pack file");
                    tracer.RelatedEvent(EventLevel.Informational, $"{nameof(this.TryPrefetchCommitsAndTrees)}_DeleteBadPack", metadata);
                    if (!fileSystem.TryWaitForDelete(tracer, packPath, IoFailureRetryDelayMS, MaxDeleteRetries, RetryLoggingThreshold))
                    {
                        error = $"Unable to delete {packPath}";
                        return false;
                    }
                }
            }

            error = null;
            return true;
        }

        private long? GetTimestamp(string packName)
        {
            string filename = Path.GetFileName(packName);
            if (!filename.StartsWith(GVFSConstants.PrefetchPackPrefix))
            {
                return null;
            }

            string[] parts = filename.Split('-');
            long parsed;
            if (parts.Length > 1 && long.TryParse(parts[1], out parsed))
            {
                return parsed;
            }

            return null;
        }

        private string GetCacheServerDisplay(CacheServerInfo cacheServer)
        {
            if (cacheServer.Name != null && !cacheServer.Name.Equals(CacheServerInfo.ReservedNames.None))
            {
                return "from cache server";
            }

            return "from origin (no cache server)";
        }

        private class PrefetchPackInfo
        {
            public PrefetchPackInfo(long timestamp, string path)
            {
                this.Timestamp = timestamp;
                this.Path = path;
            }

            public long Timestamp { get; }
            public string Path { get; }
        }
    }
}
