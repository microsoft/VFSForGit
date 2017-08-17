using CommandLine;
using FastFetch;
using GVFS.Common;
using GVFS.Common.Git;
using GVFS.Common.Http;
using GVFS.Common.Tracing;
using Microsoft.Diagnostics.Tracing;
using System;
using System.IO;

namespace GVFS.CommandLine
{
    [Verb(PrefetchVerb.PrefetchVerbName, HelpText = "Prefetch remote objects for the current head")]
    public class PrefetchVerb : GVFSVerb.ForExistingEnlistment
    {
        private const string PrefetchVerbName = "prefetch";

        private const int ChunkSize = 4000;
        private static readonly int SearchThreadCount = Environment.ProcessorCount;
        private static readonly int DownloadThreadCount = Environment.ProcessorCount;
        private static readonly int IndexThreadCount = Environment.ProcessorCount;

        [Option(
            'f',
            Parameters.Folders,
            Required = false,
            Default = Parameters.DefaultPathWhitelist,
            HelpText = "A semicolon-delimited list of paths to fetch")]
        public string PathWhitelist { get; set; }

        [Option(
            Parameters.FoldersList,
            Required = false,
            Default = Parameters.DefaultPathWhitelistFile,
            HelpText = "A file containing line-delimited list of paths to fetch")]
        public string PathWhitelistFile { get; set; }

        [Option(
            'c',
            Parameters.Commits,
            Required = false,
            Default = false,
            HelpText = "Prefetch the latest set of commit and tree packs")]
        public bool Commits { get; set; }

        [Option(
            "verbose",
            Required = false,
            Default = false,
            HelpText = "Show all outputs on the console in addition to writing them to a log file")]
        public bool Verbose { get; set; }

        protected override string VerbName
        {
            get { return PrefetchVerbName; }
        }

        public override void InitializeDefaultParameterValues()
        {
            this.PathWhitelist = Parameters.DefaultPathWhitelist;
            this.PathWhitelistFile = Parameters.DefaultPathWhitelistFile;
        }

        protected override void Execute(GVFSEnlistment enlistment)
        {
            using (JsonEtwTracer tracer = new JsonEtwTracer(GVFSConstants.GVFSEtwProviderName, "Prefetch"))
            {
                if (this.Verbose)
                {
                    tracer.AddDiagnosticConsoleEventListener(EventLevel.Informational, Keywords.Any);
                }
                else
                {
                    tracer.AddPrettyConsoleEventListener(EventLevel.Error, Keywords.Any);
                }

                tracer.AddLogFileEventListener(
                    GVFSEnlistment.GetNewGVFSLogFileName(enlistment.GVFSLogsRoot, GVFSConstants.LogFileTypes.Prefetch),
                    EventLevel.Informational,
                    Keywords.Any);

                RetryConfig retryConfig;
                string error;
                if (!RetryConfig.TryLoadFromGitConfig(tracer, enlistment, out retryConfig, out error))
                {
                    tracer.RelatedError("Failed to determine GVFS timeout and max retries: " + error);
                    Environment.Exit((int)ReturnCode.GenericError);
                }

                retryConfig.Timeout = TimeSpan.FromMinutes(RetryConfig.FetchAndCloneTimeoutMinutes);
                
                CacheServerInfo cache;
                if (!CacheServerInfo.TryDetermineCacheServer(null, tracer, enlistment, retryConfig, out cache, out error))
                {
                    tracer.RelatedError(error);
                    Environment.ExitCode = (int)ReturnCode.GenericError;
                    return;
                }

                tracer.WriteStartEvent(
                    enlistment.EnlistmentRoot,
                    enlistment.RepoUrl,
                    cache.Url);

                try
                {
                    EventMetadata metadata = new EventMetadata();
                    metadata.Add("Commits", this.Commits);
                    metadata.Add("PathWhitelist", this.PathWhitelist);
                    metadata.Add("PathWhitelistFile", this.PathWhitelistFile);
                    tracer.RelatedEvent(EventLevel.Informational, "PerformPrefetch", metadata);

                    GitObjectsHttpRequestor objectRequestor = new GitObjectsHttpRequestor(tracer, enlistment, cache, retryConfig);
                    if (this.Commits)
                    {
                        this.PrefetchCommits(tracer, enlistment, objectRequestor);
                    }
                    else
                    {
                        this.PrefetchBlobs(tracer, enlistment, objectRequestor);
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
                                { "ErrorMessage", $"Unhandled {innerException.GetType().Name}: {innerException.Message}" },
                                { "Exception", innerException.ToString() }
                            });
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
                            { "ErrorMessage", $"Unhandled {e.GetType().Name}: {e.Message}" },
                            { "Exception", e.ToString() }
                        });
                }
            }
        }

        private void PrefetchCommits(ITracer tracer, GVFSEnlistment enlistment, GitObjectsHttpRequestor objectRequestor)
        {
            if (!string.IsNullOrEmpty(this.PathWhitelistFile) ||
                            !string.IsNullOrWhiteSpace(this.PathWhitelist))
            {
                this.ReportErrorAndExit("Cannot supply both --commits (-c) and --folders (-f)");
            }

            if (this.Verbose)
            {
                this.TryPrefetchCommitsAndTrees(tracer, enlistment, objectRequestor);
            }
            else
            {
                this.ShowStatusWhileRunning(
                    () => { return this.TryPrefetchCommitsAndTrees(tracer, enlistment, objectRequestor); },
                    "Fetching commits and trees");
            }
        }

        private void PrefetchBlobs(ITracer tracer, GVFSEnlistment enlistment, GitObjectsHttpRequestor blobRequestor)
        {
            FetchHelper fetchHelper = new FetchHelper(
                tracer,
                enlistment,
                blobRequestor,
                ChunkSize,
                SearchThreadCount,
                DownloadThreadCount,
                IndexThreadCount);

            if (!FetchHelper.TryLoadPathWhitelist(tracer, this.PathWhitelist, this.PathWhitelistFile, enlistment, fetchHelper.PathWhitelist))
            {
                Environment.ExitCode = (int)ReturnCode.GenericError;
                return;
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

            string headCommitId = result.Output;
            Func<bool> doPrefetch =
                () =>
                {
                    try
                    {
                        fetchHelper.FastFetch(headCommitId.Trim(), isBranch: false);
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
                this.ShowStatusWhileRunning(doPrefetch, "Fetching blobs");
            }

            if (fetchHelper.HasFailures)
            {
                Environment.ExitCode = 1;
            }
        }

        private bool TryPrefetchCommitsAndTrees(ITracer tracer, GVFSEnlistment enlistment, GitObjectsHttpRequestor objectRequestor)
        {
            GitObjects gitObjects = new GitObjects(tracer, enlistment, objectRequestor);

            string[] packs = gitObjects.ReadPackFileNames(GVFSConstants.PrefetchPackPrefix);
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

        private static class Parameters
        {
            public const string Folders = "folders";
            public const string FoldersList = "folders-list";
            public const string Commits = "commits";

            public const string DefaultPathWhitelist = "";
            public const string DefaultPathWhitelistFile = "";
        }
    }
}
