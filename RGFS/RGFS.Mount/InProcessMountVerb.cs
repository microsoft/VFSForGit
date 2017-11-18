using CommandLine;
using RGFS.Common;
using RGFS.Common.Git;
using RGFS.Common.Http;
using RGFS.Common.Tracing;
using Microsoft.Diagnostics.Tracing;
using System;
using System.IO;

namespace RGFS.Mount
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
            RGFSConstants.VerbParameters.Mount.Verbosity,
            Default = RGFSConstants.VerbParameters.Mount.DefaultVerbosity,
            Required = false,
            HelpText = "Sets the verbosity of console logging. Accepts: Verbose, Informational, Warning, Error")]
        public string Verbosity { get; set; }

        [Option(
            'k',
            RGFSConstants.VerbParameters.Mount.Keywords,
            Default = RGFSConstants.VerbParameters.Mount.DefaultKeywords,
            Required = false,
            HelpText = "A CSV list of logging filter keywords. Accepts: Any, Network")]
        public string KeywordsCsv { get; set; }

        [Option(
            'd',
            RGFSConstants.VerbParameters.Mount.DebugWindow,
            Default = false,
            Required = false,
            HelpText = "Show the debug window.  By default, all output is written to a log file and no debug window is shown.")]
        public bool ShowDebugWindow { get; set; }

        [Value(
                0,
                Required = true,
                MetaName = "Enlistment Root Path",
                HelpText = "Full or relative path to the RGFS enlistment root")]
        public string EnlistmentRootPath { get; set; }
        
        public void InitializeDefaultParameterValues()
        {
            this.Verbosity = RGFSConstants.VerbParameters.Mount.DefaultVerbosity;
            this.KeywordsCsv = RGFSConstants.VerbParameters.Mount.DefaultKeywords;
        }

        public void Execute()
        {
            RGFSEnlistment enlistment = this.CreateEnlistment(this.EnlistmentRootPath);

            EventLevel verbosity;
            Keywords keywords;
            this.ParseEnumArgs(out verbosity, out keywords);
            
            JsonEtwTracer tracer = this.CreateTracer(enlistment, verbosity, keywords);

            CacheServerInfo cacheServer = CacheServerResolver.GetCacheServerFromConfig(enlistment);

            tracer.WriteStartEvent(
                enlistment.EnlistmentRoot,
                enlistment.RepoUrl,
                cacheServer.Url,
                enlistment.GitObjectsRoot,
                new EventMetadata
                {
                    { "IsElevated", ProcessHelper.IsAdminElevated() },
                });

            AppDomain.CurrentDomain.UnhandledException += (object sender, UnhandledExceptionEventArgs e) =>
            {
                this.UnhandledRGFSExceptionHandler(tracer, sender, e);
            };

            RetryConfig retryConfig;
            string error;
            if (!RetryConfig.TryLoadFromGitConfig(tracer, enlistment, out retryConfig, out error))
            {
                this.ReportErrorAndExit("Failed to determine RGFS timeout and max retries: " + error);
            }

            InProcessMount mountHelper = new InProcessMount(tracer, enlistment, cacheServer, retryConfig, this.ShowDebugWindow);

            try
            {
                mountHelper.Mount(verbosity, keywords);
            }
            catch (Exception ex)
            {
                tracer.RelatedError("Failed to mount: {0}", ex.ToString());
                this.ReportErrorAndExit("Failed to mount: {0}", ex.Message);
            }
        }

        private void UnhandledRGFSExceptionHandler(ITracer tracer, object sender, UnhandledExceptionEventArgs e)
        {
            Exception exception = e.ExceptionObject as Exception;

            EventMetadata metadata = new EventMetadata();
            metadata.Add("Exception", exception.ToString());
            metadata.Add("IsTerminating", e.IsTerminating);
            tracer.RelatedError(metadata, "UnhandledRGFSExceptionHandler caught unhandled exception");
        }

        private JsonEtwTracer CreateTracer(RGFSEnlistment enlistment, EventLevel verbosity, Keywords keywords)
        {
            JsonEtwTracer tracer = new JsonEtwTracer(RGFSConstants.RGFSEtwProviderName, "RGFSMount");
            tracer.AddLogFileEventListener(
                RGFSEnlistment.GetNewRGFSLogFileName(enlistment.RGFSLogsRoot, RGFSConstants.LogFileTypes.MountProcess),
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

        private RGFSEnlistment CreateEnlistment(string enlistmentRootPath)
        {
            string gitBinPath = GitProcess.GetInstalledGitBinPath();
            if (string.IsNullOrWhiteSpace(gitBinPath))
            {
                this.ReportErrorAndExit("Error: " + RGFSConstants.GitIsNotInstalledError);
            }

            RGFSEnlistment enlistment = null;
            try
            {
                enlistment = RGFSEnlistment.CreateFromDirectory(enlistmentRootPath, gitBinPath, ProcessHelper.GetCurrentProcessLocation());
                if (enlistment == null)
                {
                    this.ReportErrorAndExit(
                        "Error: '{0}' is not a valid RGFS enlistment",
                        enlistmentRootPath);
                }
            }
            catch (InvalidRepoException e)
            {
                this.ReportErrorAndExit(
                    "Error: '{0}' is not a valid RGFS enlistment. {1}",
                    enlistmentRootPath,
                    e.Message);
            }

            return enlistment;
        }

        private void ReportErrorAndExit(string error, params object[] args)
        {
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