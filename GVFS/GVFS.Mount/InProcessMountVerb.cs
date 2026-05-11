using GVFS.Common;
using GVFS.Common.Git;
using GVFS.Common.Http;
using GVFS.Common.Tracing;
using System;
using System.CommandLine;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;

namespace GVFS.Mount
{
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

        public string Verbosity { get; set; }

        public string KeywordsCsv { get; set; }

        public bool ShowDebugWindow { get; set; }

        public string StartedByService { get; set; }

        public bool StartedByVerb { get; set; }

        public string EnlistmentRootPathParameter { get; set; }

        public static RootCommand BuildRootCommand()
        {
            RootCommand rootCommand = new RootCommand("Starts the background mount process");

            Argument<string> enlistmentRootPathArg = new Argument<string>("enlistment-root-path")
            {
                Arity = ArgumentArity.ExactlyOne
            };
            rootCommand.Add(enlistmentRootPathArg);

            Option<string> verbosityOption = new Option<string>("--verbosity", new[] { "-v" })
            {
                Description = "Sets the verbosity of console logging",
                DefaultValueFactory = (_) => GVFSConstants.VerbParameters.Mount.DefaultVerbosity
            };
            rootCommand.Add(verbosityOption);

            Option<string> keywordsOption = new Option<string>("--keywords", new[] { "-k" })
            {
                Description = "A CSV list of logging filter keywords",
                DefaultValueFactory = (_) => GVFSConstants.VerbParameters.Mount.DefaultKeywords
            };
            rootCommand.Add(keywordsOption);

            Option<bool> debugWindowOption = new Option<bool>("--debug-window", new[] { "-d" }) { Description = "Show the debug window" };
            rootCommand.Add(debugWindowOption);

            Option<string> startedByServiceOption = new Option<string>("--StartedByService", new[] { "-s" })
            {
                Description = "Service initiated mount.",
                DefaultValueFactory = (_) => "false"
            };
            rootCommand.Add(startedByServiceOption);

            Option<bool> startedByVerbOption = new Option<bool>("--StartedByVerb", new[] { "-b" }) { Description = "Verb initiated mount." };
            rootCommand.Add(startedByVerbOption);

            rootCommand.SetAction((ParseResult result) =>
            {
                InProcessMountVerb verb = new InProcessMountVerb();
                verb.EnlistmentRootPathParameter = result.GetValue(enlistmentRootPathArg);
                verb.Verbosity = result.GetValue(verbosityOption) ?? "";
                verb.KeywordsCsv = result.GetValue(keywordsOption) ?? "";
                verb.ShowDebugWindow = result.GetValue(debugWindowOption);
                verb.StartedByService = result.GetValue(startedByServiceOption) ?? "false";
                verb.StartedByVerb = result.GetValue(startedByVerbOption);
                verb.Execute();
            });

            return rootCommand;
        }

        public void InitializeDefaultParameterValues()
        {
            this.Verbosity = GVFSConstants.VerbParameters.Mount.DefaultVerbosity;
            this.KeywordsCsv = GVFSConstants.VerbParameters.Mount.DefaultKeywords;
        }

        public void Execute()
        {
            if (this.StartedByVerb)
            {
                // If this process was started by a verb it means that StartBackgroundVFS4GProcess was used
                // and we should be running in the background.  PrepareProcessToRunInBackground will perform
                // any platform specific preparation required to run as a background process.
                GVFSPlatform.Instance.PrepareProcessToRunInBackground();
            }

            GVFSEnlistment enlistment = this.CreateEnlistment(this.EnlistmentRootPathParameter);

            EventLevel verbosity;
            Keywords keywords;
            this.ParseEnumArgs(out verbosity, out keywords);

            JsonTracer tracer = this.CreateTracer(enlistment, verbosity, keywords);

            CacheServerInfo cacheServer = CacheServerResolver.GetCacheServerFromConfig(enlistment);

            tracer.WriteStartEvent(
                enlistment.EnlistmentRoot,
                enlistment.RepoUrl,
                cacheServer.Url,
                new EventMetadata
                {
                    { "IsElevated", GVFSPlatform.Instance.IsElevated() },
                    { nameof(this.EnlistmentRootPathParameter), this.EnlistmentRootPathParameter },
                    { nameof(this.StartedByService), this.StartedByService },
                    { nameof(this.StartedByVerb), this.StartedByVerb },
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

            GitStatusCacheConfig gitStatusCacheConfig;
            if (!GitStatusCacheConfig.TryLoadFromGitConfig(tracer, enlistment, out gitStatusCacheConfig, out error))
            {
                tracer.RelatedWarning("Failed to determine GVFS status cache backoff time: " + error);
                gitStatusCacheConfig = GitStatusCacheConfig.DefaultConfig;
            }

            InProcessMount mountHelper = new InProcessMount(tracer, enlistment, cacheServer, retryConfig, gitStatusCacheConfig, this.ShowDebugWindow);

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

        private JsonTracer CreateTracer(GVFSEnlistment enlistment, EventLevel verbosity, Keywords keywords)
        {
            JsonTracer tracer = new JsonTracer(GVFSConstants.GVFSEtwProviderName, "GVFSMount", enlistment.GetEnlistmentId(), enlistment.GetMountId());
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
            string gitBinPath = GVFSPlatform.Instance.GitInstallation.GetInstalledGitBinPath();
            if (string.IsNullOrWhiteSpace(gitBinPath))
            {
                this.ReportErrorAndExit("Error: " + GVFSConstants.GitIsNotInstalledError);
            }

            GVFSEnlistment enlistment = null;
            try
            {
                enlistment = GVFSEnlistment.CreateFromDirectory(enlistmentRootPath, gitBinPath, authentication: null);
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
