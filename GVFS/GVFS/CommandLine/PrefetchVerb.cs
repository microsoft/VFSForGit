using CommandLine;
using FastFetch;
using GVFS.Common;
using GVFS.Common.Tracing;
using Microsoft.Diagnostics.Tracing;
using System;

namespace GVFS.CommandLine
{
    [Verb(PrefetchVerb.PrefetchVerbName, HelpText = "Prefetch remote objects for the current head")]
    public class PrefetchVerb : GVFSVerb.ForExistingEnlistment
    {
        public const string PrefetchVerbName = "prefetch";

        private const int ChunkSize = 4000;
        private static readonly int SearchThreadCount = Environment.ProcessorCount;
        private static readonly int DownloadThreadCount = Environment.ProcessorCount;
        private static readonly int IndexThreadCount = Environment.ProcessorCount;

        [Option(
            'v',
            Parameters.Verbosity,
            Default = Parameters.DefaultVerbosity,
            Required = false,
            HelpText = "Sets the verbosity of console logging. Accepts: Verbose, Informational, Warning, Error")]
        public string Verbosity { get; set; }

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

        protected override string VerbName
        {
            get { return PrefetchVerbName; }
        }

        public override void InitializeDefaultParameterValues()
        {
            this.Verbosity = Parameters.DefaultVerbosity;
            this.PathWhitelist = Parameters.DefaultPathWhitelist;
            this.PathWhitelistFile = Parameters.DefaultPathWhitelistFile;
        }

        protected override void Execute(GVFSEnlistment enlistment, ITracer tracer = null)
        {
            EventLevel verbosity;
            if (!Enum.TryParse(this.Verbosity, out verbosity))
            {
                this.ReportErrorAndExit("Error: Invalid verbosity: " + this.Verbosity);
            }

            if (tracer != null)
            {
                this.PerformPrefetch(enlistment, tracer);
            }
            else
            {
                using (JsonEtwTracer prefetchTracer = new JsonEtwTracer(GVFSConstants.GVFSEtwProviderName, "Prefetch"))
                {
                    prefetchTracer.AddLogFileEventListener(
                            GVFSEnlistment.GetNewGVFSLogFileName(enlistment.GVFSLogsRoot, this.VerbName),
                            EventLevel.Informational,
                            Keywords.Any);

                    prefetchTracer.AddConsoleEventListener(verbosity, ~Keywords.Network);

                    prefetchTracer.WriteStartEvent(
                        enlistment.EnlistmentRoot,
                        enlistment.RepoUrl,
                        enlistment.CacheServerUrl);

                    this.PerformPrefetch(enlistment, prefetchTracer);
                }
            }
        }

        private void PerformPrefetch(GVFSEnlistment enlistment, ITracer tracer)
        {
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
                        enlistment,
                        DownloadThreadCount);
                    prefetchHelper.PrefetchCommitsAndTrees();
                    return;
                }

                FetchHelper fetchHelper = new FetchHelper(
                    tracer,
                    enlistment,
                    ChunkSize,
                    SearchThreadCount,
                    DownloadThreadCount,
                    IndexThreadCount);

                if (!FetchHelper.TryLoadPathWhitelist(this.PathWhitelist, this.PathWhitelistFile, tracer, fetchHelper.PathWhitelist))
                {
                    Environment.ExitCode = (int)ReturnCode.GenericError;
                    return;
                }

                bool gvfsHeadFileExists;
                string error;
                string projectedCommitId;

                if (!enlistment.TryParseGVFSHeadFile(out gvfsHeadFileExists, out error, out projectedCommitId))
                {
                    tracer.RelatedError(error);
                    this.Output.WriteLine(error);
                    Environment.ExitCode = (int)ReturnCode.GenericError;
                    return;
                }

                fetchHelper.FastFetch(projectedCommitId.Trim(), isBranch: false);
                if (fetchHelper.HasFailures)
                {
                    Environment.ExitCode = 1;
                }
            }
            catch (AggregateException e)
            {
                this.Output.WriteLine("Cannot prefetch @ {0}:", enlistment.EnlistmentRoot);
                foreach (Exception ex in e.Flatten().InnerExceptions)
                {
                    this.Output.WriteLine("Exception: {0}", ex.ToString());
                }

                Environment.ExitCode = (int)ReturnCode.GenericError;
            }
            catch (VerbAbortedException)
            {
                throw;
            }
            catch (Exception e)
            {
                this.ReportErrorAndExit("Cannot prefetch @ {0}: {1}", enlistment.EnlistmentRoot, e.ToString());
            }
        }

        private static class Parameters
        {
            public const string Verbosity = "verbosity";
            public const string Folders = "folders";
            public const string FoldersList = "folders-list";
            public const string Commits = "commits";

            public const string DefaultVerbosity = "Informational";
            public const string DefaultPathWhitelist = "";
            public const string DefaultPathWhitelistFile = "";
        }
    }
}
