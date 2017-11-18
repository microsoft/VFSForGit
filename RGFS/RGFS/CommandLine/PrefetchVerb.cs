using CommandLine;
using FastFetch;
using RGFS.Common;
using RGFS.Common.FileSystem;
using RGFS.Common.Git;
using RGFS.Common.Http;
using RGFS.Common.Tracing;
using Microsoft.Diagnostics.Tracing;
using System;
using System.IO;

namespace RGFS.CommandLine
{
    [Verb(PrefetchVerb.PrefetchVerbName, HelpText = "Prefetch remote objects for the current head")]
    public class PrefetchVerb : RGFSVerb.ForExistingEnlistment
    {
        private const string PrefetchVerbName = "prefetch";

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

        protected override string VerbName
        {
            get { return PrefetchVerbName; }
        }

        protected override void Execute(RGFSEnlistment enlistment)
        {
            using (JsonEtwTracer tracer = new JsonEtwTracer(RGFSConstants.RGFSEtwProviderName, "Prefetch"))
            {
                if (this.Verbose)
                {
                    tracer.AddDiagnosticConsoleEventListener(EventLevel.Informational, Keywords.Any);
                }

                string cacheServerUrl = CacheServerResolver.GetUrlFromConfig(enlistment);

                tracer.AddLogFileEventListener(
                    RGFSEnlistment.GetNewRGFSLogFileName(enlistment.RGFSLogsRoot, RGFSConstants.LogFileTypes.Prefetch),
                    EventLevel.Informational,
                    Keywords.Any);
                tracer.WriteStartEvent(
                    enlistment.EnlistmentRoot,
                    enlistment.RepoUrl,
                    cacheServerUrl,
                    enlistment.GitObjectsRoot);

                RetryConfig retryConfig = this.GetRetryConfig(tracer, enlistment, TimeSpan.FromMinutes(RetryConfig.FetchAndCloneTimeoutMinutes));

                CacheServerInfo cacheServer = this.ResolvedCacheServer;
                if (!this.SkipVersionCheck)
                {
                    string authErrorMessage;
                    if (!this.ShowStatusWhileRunning(
                        () => enlistment.Authentication.TryRefreshCredentials(tracer, out authErrorMessage),
                        "Authenticating"))
                    {
                        this.ReportErrorAndExit(tracer, "Unable to prefetch because authentication failed");
                    }

                    RGFSConfig rgfsConfig = this.QueryRGFSConfig(tracer, enlistment, retryConfig);

                    CacheServerResolver cacheServerResolver = new CacheServerResolver(tracer, enlistment);
                    cacheServer = cacheServerResolver.ResolveNameFromRemote(cacheServerUrl, rgfsConfig);

                    this.ValidateClientVersions(tracer, enlistment, rgfsConfig, showWarnings: false);
                }

                try
                {
                    EventMetadata metadata = new EventMetadata();
                    metadata.Add("Commits", this.Commits);
                    metadata.Add("Files", this.Files);
                    metadata.Add("Folders", this.Folders);
                    metadata.Add("FoldersListFile", this.FoldersListFile);
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

                        this.PrefetchCommits(tracer, enlistment, objectRequestor, cacheServer);
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
                        "Cannot prefetch {0}. " + ConsoleHelper.GetRGFSLogMessage(enlistment.EnlistmentRoot),
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
                        "Cannot prefetch {0}. " + ConsoleHelper.GetRGFSLogMessage(enlistment.EnlistmentRoot),
                        enlistment.EnlistmentRoot);
                    tracer.RelatedError(
                        new EventMetadata
                        {
                            { "Verb", typeof(PrefetchVerb).Name },
                            { "Exception", e.ToString() }
                        },
                        $"Unhandled {e.GetType().Name}: {e.Message}");
                }
            }
        }

        private void PrefetchCommits(ITracer tracer, RGFSEnlistment enlistment, GitObjectsHttpRequestor objectRequestor, CacheServerInfo cacheServer)
        {
            if (this.Verbose)
            {
                this.TryPrefetchCommitsAndTrees(tracer, enlistment, objectRequestor);
            }
            else
            {
                this.ShowStatusWhileRunning(
                    () => { return this.TryPrefetchCommitsAndTrees(tracer, enlistment, objectRequestor); },
                    "Fetching commits and trees " + this.GetCacheServerDisplay(cacheServer));
            }
        }

        private void PrefetchBlobs(ITracer tracer, RGFSEnlistment enlistment, GitObjectsHttpRequestor blobRequestor, CacheServerInfo cacheServer)
        {
            FetchHelper fetchHelper = new FetchHelper(
                tracer,
                enlistment,
                blobRequestor,
                ChunkSize,
                SearchThreadCount,
                DownloadThreadCount,
                IndexThreadCount);

            string error;
            if (!FetchHelper.TryLoadFolderList(enlistment, this.Folders, this.FoldersListFile, fetchHelper.FolderList, out error))
            {
                this.ReportErrorAndExit(tracer, error);
            }

            if (!FetchHelper.TryLoadFileList(enlistment, this.Files, fetchHelper.FileList, out error))
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
                if (!ConsoleHelper.ShowStatusWhileRunning(
                    () => this.Execute<StatusVerb>(
                        this.EnlistmentRootPath,
                        verb => verb.Output = new StreamWriter(new MemoryStream())) == ReturnCode.Success,
                    "Checking that RGFS is mounted",
                    this.Output,
                    showSpinner: true,
                    rgfsLogEnlistmentRoot: null))
                {
                    this.ReportErrorAndExit("You can only specify --hydrate if the repo is mounted. Run 'rgfs mount' and try again.");
                }
            }

            GitProcess gitProcess = new GitProcess(enlistment);
            GitProcess.Result result = gitProcess.RevParse(RGFSConstants.DotGit.HeadName);
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
                        fetchHelper.FastFetchWithStats(
                            headCommitId.Trim(),
                            isBranch: false,
                            readFilesAfterDownload: this.HydrateFiles,
                            matchedBlobCount: out matchedBlobCount,
                            downloadedBlobCount: out downloadedBlobCount,
                            readFileCount: out readFileCount);
                        return !fetchHelper.HasFailures;
                    }
                    catch (FetchHelper.FetchException e)
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

        private bool TryPrefetchCommitsAndTrees(ITracer tracer, RGFSEnlistment enlistment, GitObjectsHttpRequestor objectRequestor)
        {
            PhysicalFileSystem fileSystem = new PhysicalFileSystem();
            GitRepo repo = new GitRepo(tracer, enlistment, fileSystem);
            RGFSContext context = new RGFSContext(tracer, fileSystem, repo, enlistment);
            GitObjects gitObjects = new RGFSGitObjects(context, objectRequestor);

            string[] packs = gitObjects.ReadPackFileNames(RGFSConstants.PrefetchPackPrefix);
            long max = -1;
            foreach (string pack in packs)
            {
                long? timestamp = this.GetTimestamp(pack);
                if (timestamp.HasValue && timestamp > max)
                {
                    max = timestamp.Value;
                }
            }

            return gitObjects.TryDownloadPrefetchPacks(max);
        }

        private long? GetTimestamp(string packName)
        {
            string filename = Path.GetFileName(packName);
            if (!filename.StartsWith(RGFSConstants.PrefetchPackPrefix))
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
            if (cacheServer.HasResolvedName())
            {
                return "from " + cacheServer.Name + " cache server";
            }

            return "from " + cacheServer.Url;
        }
    }
}
