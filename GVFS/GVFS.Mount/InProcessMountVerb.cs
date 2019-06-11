using CommandLine;
using GVFS.Common;
using GVFS.Common.Git;
using GVFS.Common.Http;
using GVFS.Common.Tracing;
using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;

namespace GVFS.Mount
{
    [Verb("mount", HelpText = "Starts the background mount process")]
    public class InProcessMountVerb
    {
        private const int StdInFileNo = 0; // STDIN_FILENO  -> standard input file descriptor
        private const int StdOutFileNo = 1; // STDOUT_FILENO -> standard output file descriptor
        private const int StdErrFileNo = 2; // STDERR_FILENO -> standard error file descriptor

        private TextWriter output;

        public InProcessMountVerb()
        {
            this.output = Console.Out;
            this.ReturnCode = ReturnCode.Success;

            this.InitializeDefaultParameterValues();
        }

        [Flags]
        public enum OpenFlags
        {
            // Access modes (mutually exclusive)
            O_RDONLY = 0x0000,
            O_WRONLY = 0x0001,
            O_RDWR = 0x0002,

            // Flags (combinable)
            O_CLOEXEC = 0x0010,
            O_CREAT = 0x0020,
            O_EXCL = 0x0040,
            O_TRUNC = 0x0080,
            O_SYNC = 0x0100,
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

        [Option(
            's',
            GVFSConstants.VerbParameters.Mount.StartedByService,
            Default = "false",
            Required = false,
            HelpText = "Service initiated mount.")]
        public string StartedByService { get; set; }

        [Option(
            'l',
            GVFSConstants.VerbParameters.Mount.StartedByVerb,
            Default = false,
            Required = false,
            HelpText = "Verb initiated mount.")]
        public bool StartedByVerb { get; set; }

        [Value(
                0,
                Required = true,
                MetaName = "Enlistment Root Path",
                HelpText = "Full or relative path to the GVFS enlistment root")]
        public string EnlistmentRootPathParameter { get; set; }

        public void InitializeDefaultParameterValues()
        {
            this.Verbosity = GVFSConstants.VerbParameters.Mount.DefaultVerbosity;
            this.KeywordsCsv = GVFSConstants.VerbParameters.Mount.DefaultKeywords;
        }

        public void Execute()
        {
            if (this.StartedByVerb && RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                Close(StdInFileNo);
                Close(StdOutFileNo);
                Close(StdErrFileNo);

                int fdin = Open("/dev/null", (int)OpenFlags.O_RDONLY);
                if (fdin == -1)
                {
                    this.ReportErrorAndExit("Unable to open file descriptor for stdin", Marshal.GetLastWin32Error());
                }

                int fdout = Open("/dev/null", (int)OpenFlags.O_WRONLY);
                if (fdout == -1)
                {
                    this.ReportErrorAndExit("Unable to open file descriptor for stdout", Marshal.GetLastWin32Error());
                }

                // Redirect stdout/stdin/stderr to "/dev/null"
                if (Dup2(fdin, StdInFileNo) == -1 ||
                    Dup2(fdout, StdOutFileNo) == -1 ||
                    Dup2(fdout, StdErrFileNo) == -1)
                {
                    this.ReportErrorAndExit("Error redirecting stdout/stdin/stderr", Marshal.GetLastWin32Error());
                }

                // Become session leader of a new session
                if (SetSid() == -1)
                {
                    this.ReportErrorAndExit("Error calling SetSid()", Marshal.GetLastWin32Error());
                }

                /*
                if (Daemon(nochdir: 1, noclose: 0) != 0)
                {
                    throw new Win32Exception(Marshal.GetLastWin32Error(, "Error calling Daemon()");
                }
                */
            }

            GVFSEnlistment enlistment = this.CreateEnlistment(this.EnlistmentRootPathParameter);

            // Create an empty marker file to know the mount process has started
            File.WriteAllText(GVFSEnlistment.GetNewGVFSLogFileName(enlistment.GVFSLogsRoot, "mount_starting"), "Starting up");

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

        [DllImport("libc", EntryPoint = "setsid", SetLastError = true)]
        private static extern int SetSid();

        [DllImport("libc", EntryPoint = "open", SetLastError = true)]
        private static extern int Open(string path, int flag);

        [DllImport("libc", EntryPoint = "close", SetLastError = true)]
        private static extern int Close(int filedes);

        [DllImport("libc", EntryPoint = "dup2", SetLastError = true)]
        private static extern int Dup2(int oldfd, int newfd);

        [DllImport("libc", EntryPoint = "daemon", SetLastError = true)]
        private static extern int Daemon(int nochdir, int noclose);

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
                enlistment = GVFSEnlistment.CreateFromDirectory(enlistmentRootPath, gitBinPath, ProcessHelper.GetCurrentProcessLocation(), authentication: null);
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
