using CommandLine;
using FastFetch;
using GVFS.Common;
using GVFS.Common.Git;
using GVFS.Common.Tracing;
using Microsoft.Diagnostics.Tracing;
using System;

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
                tracer.WriteStartEvent(
                    enlistment.EnlistmentRoot,
                    enlistment.RepoUrl,
                    enlistment.CacheServerUrl);

                try
                {
                    EventMetadata metadata = new EventMetadata();
                    metadata.Add("Commits", this.Commits);
                    metadata.Add("PathWhitelist", this.PathWhitelist);
                    metadata.Add("PathWhitelistFile", this.PathWhitelistFile);
                    tracer.RelatedEvent(EventLevel.Informational, "PerformPrefetch", metadata);

                    if (this.Commits)
                    {
                        if (!string.IsNullOrEmpty(this.PathWhitelistFile) ||
                            !string.IsNullOrWhiteSpace(this.PathWhitelist))
                        {
                            this.ReportErrorAndExit("Cannot supply both --commits (-c) and --folders (-f)");
                        }

                        PrefetchHelper prefetchHelper = new PrefetchHelper(
                            tracer,
                            enlistment);

                        if (this.Verbose)
                        {
                            prefetchHelper.TryPrefetchCommitsAndTrees();
                        }
                        else
                        {
                            this.ShowStatusWhileRunning(
                                () => { return prefetchHelper.TryPrefetchCommitsAndTrees(); },
                                "Fetching commits and trees");
                        }

                        return;
                    }

                    FetchHelper fetchHelper = new FetchHelper(
                        tracer,
                        enlistment,
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
                catch (AggregateException aggregateException)
                {
                    this.Output.WriteLine("Cannot prefetch @ {0}:", enlistment.EnlistmentRoot);
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
                catch (VerbAbortedException)
                {
                    throw;
                }
                catch (Exception e)
                {
                    this.Output.WriteLine("Cannot prefetch @ {0}:", enlistment.EnlistmentRoot);
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
