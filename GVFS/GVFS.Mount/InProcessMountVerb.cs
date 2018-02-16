using CommandLine;
using GVFS.Common;
using GVFS.Common.Git;
using GVFS.Common.Http;
using GVFS.Common.Tracing;
using Microsoft.Diagnostics.Tracing;
using System;
using System.IO;

namespace GVFS.Mount
{
    [Verb("mount", HelpText = "Starts the background mount process")]
    public class InProcessMountVerb 
    {
        private TextWriter output;

        public InProcessMountVerb()
        {
            this.output = Console.Out;
            this.ReturnCode = ReturnCode.Success;

            this.InitializeDefaultParameterValues();
        }

        public ReturnCode ReturnCode { get; private set; }

        [Option(
            'v',
            GVFSConstants.VerbParameters.Mount.Verbosity,
            Default = GVFSConstants.VerbParameters.Mount.DefaultVerbosity,
            Required = false,
            HelpText = "Sets the verbosity of console logging. Accepts: Verbose, Informational, Warning, Error")]
        public string Verbosity { get; set; }

        [Option(
            'k',
            GVFSConstants.VerbParameters.Mount.Keywords,
            Default = GVFSConstants.VerbParameters.Mount.DefaultKeywords,
            Required = false,
            HelpText = "A CSV list of logging filter keywords. Accepts: Any, Network")]
        public string KeywordsCsv { get; set; }

        [Option(
            'd',
            GVFSConstants.VerbParameters.Mount.DebugWindow,
            Default = false,
            Required = false,
            HelpText = "Show the debug window.  By default, all output is written to a log file and no debug window is shown.")]
        public bool ShowDebugWindow { get; set; }

        [Value(
                0,
                Required = true,
                MetaName = "Enlistment Root Path",
                HelpText = "Full or relative path to the GVFS enlistment root")]
        public string EnlistmentRootPath { get; set; }
        
        public void InitializeDefaultParameterValues()
        {
            this.Verbosity = GVFSConstants.VerbParameters.Mount.DefaultVerbosity;
            this.KeywordsCsv = GVFSConstants.VerbParameters.Mount.DefaultKeywords;
        }

        public void Execute()
        {
            GVFSEnlistment enlistment = this.CreateEnlistment(this.EnlistmentRootPath);

            EventLevel verbosity;
            Keywords keywords;
            this.ParseEnumArgs(out verbosity, out keywords);

            JsonEtwTracer tracer = this.CreateTracer(enlistment, verbosity, keywords);
            
            CacheServerInfo cacheServer = CacheServerResolver.GetCacheServerFromConfig(enlistment);

            tracer.WriteStartEvent(
                enlistment.EnlistmentRoot,
                enlistment.RepoUrl,
                cacheServer.Url,
                new EventMetadata
                {
                    { "IsElevated", ProcessHelper.IsAdminElevated() },
                });

            AppDomain.CurrentDomain.UnhandledException += (object sender, UnhandledExceptionEventArgs e) =>
            {
                this.UnhandledGVFSExceptionHandler(tracer, sender, e);
            };

            string error;
            RetryConfig retryConfig;
            if (!RetryConfig.TryLoadFromGitConfig(tracer, enlistment, out retryConfig, out error))
            {
                this.ReportErrorAndExit(tracer, "Failed to determine GVFS timeout and max retries: " + error);
            }

            InProcessMount mountHelper = new InProcessMount(tracer, enlistment, cacheServer, retryConfig, this.ShowDebugWindow);

            try
            {
                mountHelper.Mount(verbosity, keywords);
            }
            catch (Exception ex)
            {
                this.ReportErrorAndExit(tracer, "Failed to mount: {0}", ex.Message);
            }
        }

        private void UnhandledGVFSExceptionHandler(ITracer tracer, object sender, UnhandledExceptionEventArgs e)
        {
            Exception exception = e.ExceptionObject as Exception;

            EventMetadata metadata = new EventMetadata();
            metadata.Add("Exception", exception.ToString());
            metadata.Add("IsTerminating", e.IsTerminating);
            tracer.RelatedError(metadata, "UnhandledGVFSExceptionHandler caught unhandled exception");
        }

        private JsonEtwTracer CreateTracer(GVFSEnlistment enlistment, EventLevel verbosity, Keywords keywords)
        {
            JsonEtwTracer tracer = new JsonEtwTracer(GVFSConstants.GVFSEtwProviderName, "GVFSMount");
            tracer.AddLogFileEventListener(
                GVFSEnlistment.GetNewGVFSLogFileName(enlistment.GVFSLogsRoot, GVFSConstants.LogFileTypes.MountProcess),
                verbosity,
                keywords);
            if (this.ShowDebugWindow)
            {
                tracer.AddDiagnosticConsoleEventListener(verbosity, keywords);
            }

            return tracer;
        }

        private void ParseEnumArgs(out EventLevel verbosity, out Keywords keywords)
        {
            if (!Enum.TryParse(this.KeywordsCsv, out keywords))
            {
                this.ReportErrorAndExit("Error: Invalid logging filter keywords: " + this.KeywordsCsv);
            }

            if (!Enum.TryParse(this.Verbosity, out verbosity))
            {
                this.ReportErrorAndExit("Error: Invalid logging verbosity: " + this.Verbosity);
            }
        }

        private GVFSEnlistment CreateEnlistment(string enlistmentRootPath)
        {
            string gitBinPath = GitProcess.GetInstalledGitBinPath();
            if (string.IsNullOrWhiteSpace(gitBinPath))
            {
                this.ReportErrorAndExit("Error: " + GVFSConstants.GitIsNotInstalledError);
            }

            GVFSEnlistment enlistment = null;
            try
            {
                enlistment = GVFSEnlistment.CreateFromDirectory(enlistmentRootPath, gitBinPath, ProcessHelper.GetCurrentProcessLocation());
                if (enlistment == null)
                {
                    this.ReportErrorAndExit(
                        "Error: '{0}' is not a valid GVFS enlistment",
                        enlistmentRootPath);
                }
            }
            catch (InvalidRepoException e)
            {
                this.ReportErrorAndExit(
                    "Error: '{0}' is not a valid GVFS enlistment. {1}",
                    enlistmentRootPath,
                    e.Message);
            }

            return enlistment;
        }

        private void ReportErrorAndExit(string error, params object[] args)
        {
            this.ReportErrorAndExit(null, error, args);
        }

        private void ReportErrorAndExit(ITracer tracer, string error, params object[] args)
        {
            if (tracer != null)
            {
                tracer.RelatedError(error, args);
            }

            if (error != null)
            {
                this.output.WriteLine(error, args);
            }

            if (this.ShowDebugWindow)
            {
                Console.WriteLine("\nPress Enter to Exit");
                Console.ReadLine();
            }

            this.ReturnCode = ReturnCode.GenericError;
            throw new MountAbortedException(this);
        }
    }
}
