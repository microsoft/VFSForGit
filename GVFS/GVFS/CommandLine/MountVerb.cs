using CommandLine;
using GVFS.Common;
using GVFS.Common.FileSystem;
using GVFS.Common.Git;
using GVFS.Common.Http;
using GVFS.Common.NamedPipes;
using GVFS.Common.Tracing;
using Microsoft.Diagnostics.Tracing;
using System;
using System.IO;
using System.Security.Principal;

namespace GVFS.CommandLine
{
    [Verb(MountVerb.MountVerbName, HelpText = "Mount a GVFS virtual repo")]
    public class MountVerb : GVFSVerb.ForExistingEnlistment
    {
        private const string MountVerbName = "mount";

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

        public bool SkipMountedCheck { get; set; }
        public bool SkipVersionCheck { get; set; }

        protected override string VerbName
        {
            get { return MountVerbName; }
        }

        public override void InitializeDefaultParameterValues()
        {
            this.Verbosity = GVFSConstants.VerbParameters.Mount.DefaultVerbosity;
            this.KeywordsCsv = GVFSConstants.VerbParameters.Mount.DefaultKeywords;
        }

        protected override void PreCreateEnlistment()
        {
            this.CheckGVFltHealthy();

            string enlistmentRoot = Paths.GetGVFSEnlistmentRoot(this.EnlistmentRootPath);
            if (enlistmentRoot == null)
            {
                this.ReportErrorAndExit("Error: '{0}' is not a valid GVFS enlistment", this.EnlistmentRootPath);
            }

            if (!this.SkipMountedCheck)
            {
                using (NamedPipeClient pipeClient = new NamedPipeClient(Paths.GetNamedPipeName(enlistmentRoot)))
                {
                    if (pipeClient.Connect(500))
                    {
                        this.ReportErrorAndExit(ReturnCode.Success, "This repo is already mounted.");
                    }
                }
            }

            bool allowUpgrade = true;
            string error;
            if (!RepoMetadata.CheckDiskLayoutVersion(Path.Combine(enlistmentRoot, GVFSConstants.DotGVFS.Root), allowUpgrade, out error))
            {
                this.ReportErrorAndExit("Error: " + error);
            }
        }

        protected override void Execute(GVFSEnlistment enlistment)
        {
            string errorMessage = null;
            if (!HooksInstaller.InstallHooks(enlistment, out errorMessage))
            {
                this.ReportErrorAndExit("Error installing hooks: " + errorMessage);
            }

            if (!enlistment.TryConfigureAlternate(out errorMessage))
            {
                this.ReportErrorAndExit("Error configuring alternate: " + errorMessage);
            }

            using (JsonEtwTracer tracer = new JsonEtwTracer(GVFSConstants.GVFSEtwProviderName, "PreMount"))
            {
                tracer.AddLogFileEventListener(
                    GVFSEnlistment.GetNewGVFSLogFileName(enlistment.GVFSLogsRoot, GVFSConstants.LogFileTypes.Mount),
                    EventLevel.Verbose,
                    Keywords.Any);
                                
                if (!this.SkipVersionCheck)
                {
                    string authErrorMessage = null;
                    if (!this.ShowStatusWhileRunning(
                        () => enlistment.Authentication.TryRefreshCredentials(tracer, out authErrorMessage),
                        "Authenticating"))
                    {
                        this.Output.WriteLine("    WARNING: " + authErrorMessage);
                        this.Output.WriteLine("    Mount will proceed, but new files cannot be accessed until GVFS can authenticate.");
                    }
                }

                RetryConfig retryConfig = null;
                string error;
                if (!RetryConfig.TryLoadFromGitConfig(tracer, enlistment, out retryConfig, out error))
                {
                    this.ReportErrorAndExit("Failed to determine GVFS timeout and max retries: " + error);
                }

                GVFSConfig gvfsConfig;
                CacheServerInfo cacheServer;
                using (ConfigHttpRequestor configRequestor = new ConfigHttpRequestor(tracer, enlistment, retryConfig))
                {
                    gvfsConfig = configRequestor.QueryGVFSConfig();
                }

                if (!CacheServerInfo.TryDetermineCacheServer(null, enlistment, gvfsConfig.CacheServers, out cacheServer, out error))
                {
                    this.ReportErrorAndExit(error);
                }

                tracer.WriteStartEvent(
                    enlistment.EnlistmentRoot,
                    enlistment.RepoUrl,
                    cacheServer.Url);

                if (!GvFltFilter.TryAttach(tracer, enlistment.EnlistmentRoot, out errorMessage))
                {
                    if (!this.ShowStatusWhileRunning(
                        () => { return this.AttachGvFltThroughService(enlistment, out errorMessage); },
                        "Attaching GvFlt to volume"))
                    {
                        this.ReportErrorAndExit(errorMessage);
                    }
                }

                this.ValidateClientVersions(tracer, enlistment, gvfsConfig);
            }

            if (!this.ShowStatusWhileRunning(
                () => { return this.TryMount(enlistment, out errorMessage); },
                "Mounting"))
            {
                this.ReportErrorAndExit(errorMessage);
            }

            if (!this.ShowStatusWhileRunning(
                () => { return this.RegisterMount(enlistment, out errorMessage); },
                "Registering for automount"))
            {
                this.Output.WriteLine("    WARNING: " + errorMessage);
            }
        }

        private bool AttachGvFltThroughService(GVFSEnlistment enlistment, out string errorMessage)
        {
            errorMessage = string.Empty;

            NamedPipeMessages.AttachGvFltRequest request = new NamedPipeMessages.AttachGvFltRequest();
            request.EnlistmentRoot = enlistment.EnlistmentRoot;

            using (NamedPipeClient client = new NamedPipeClient(this.ServicePipeName))
            {
                if (!client.Connect())
                {
                    errorMessage = "Unable to mount because GVFS.Service is not responding. Run 'sc start GVFS.Service' from an elevated command prompt to ensure it is running.";
                    return false;
                }

                try
                {
                    client.SendRequest(request.ToMessage());
                    NamedPipeMessages.Message response = client.ReadResponse();
                    if (response.Header == NamedPipeMessages.AttachGvFltRequest.Response.Header)
                    {
                        NamedPipeMessages.AttachGvFltRequest.Response message = NamedPipeMessages.AttachGvFltRequest.Response.FromMessage(response);

                        if (!string.IsNullOrEmpty(message.ErrorMessage))
                        {
                            errorMessage = message.ErrorMessage;
                            return false;
                        }

                        if (message.State != NamedPipeMessages.CompletionState.Success)
                        {
                            errorMessage = "Failed to attach GvFlt to volume.";
                            return false;
                        }
                        else
                        {
                            return true;
                        }
                    }
                    else
                    {
                        errorMessage = string.Format("GVFS.Service responded with unexpected message: {0}", response);
                        return false;
                    }
                }
                catch (BrokenPipeException e)
                {
                    errorMessage = "Unable to communicate with GVFS.Service: " + e.ToString();
                    return false;
                }
            }
        }

        private bool TryMount(GVFSEnlistment enlistment, out string errorMessage)
        {
            // We have to parse these parameters here to make sure they are valid before 
            // handing them to the background process which cannot tell the user when they are bad
            EventLevel verbosity;
            Keywords keywords;
            this.ParseEnumArgs(out verbosity, out keywords);

            string mountExeLocation = Path.Combine(ProcessHelper.GetCurrentProcessLocation(), GVFSConstants.MountExecutableName);
            if (!File.Exists(mountExeLocation))
            {
                errorMessage = "Could not find GVFS.Mount.exe. You may need to reinstall GVFS.";
                return false;
            }

            GitProcess git = new GitProcess(enlistment);
            if (!git.IsValidRepo())
            {
                errorMessage = "The physical git repo is missing or invalid";
                return false;
            }

            this.SetGitConfigSettings(git);

            const string ParamPrefix = "--";
            ProcessHelper.StartBackgroundProcess(
                mountExeLocation,
                string.Join(
                    " ",
                    enlistment.EnlistmentRoot,
                    ParamPrefix + GVFSConstants.VerbParameters.Mount.Verbosity,
                    this.Verbosity,
                    ParamPrefix + GVFSConstants.VerbParameters.Mount.Keywords,
                    this.KeywordsCsv,
                    this.ShowDebugWindow ? ParamPrefix + GVFSConstants.VerbParameters.Mount.DebugWindow : string.Empty),
                createWindow: this.ShowDebugWindow);

            return GVFSEnlistment.WaitUntilMounted(enlistment.EnlistmentRoot, out errorMessage);
        }

        private bool RegisterMount(GVFSEnlistment enlistment, out string errorMessage)
        {
            errorMessage = string.Empty;

            NamedPipeMessages.RegisterRepoRequest request = new NamedPipeMessages.RegisterRepoRequest();
            request.EnlistmentRoot = enlistment.EnlistmentRoot;

            WindowsIdentity identity = WindowsIdentity.GetCurrent();
            WindowsPrincipal principal = new WindowsPrincipal(identity);

            request.OwnerSID = identity.User.Value;

            using (NamedPipeClient client = new NamedPipeClient(this.ServicePipeName))
            {
                if (!client.Connect())
                {
                    errorMessage = "Unable to register repo because GVFS.Service is not responding.";
                    return false;
                }

                try
                {
                    client.SendRequest(request.ToMessage());
                    NamedPipeMessages.Message response = client.ReadResponse();
                    if (response.Header == NamedPipeMessages.RegisterRepoRequest.Response.Header)
                    {
                        NamedPipeMessages.RegisterRepoRequest.Response message = NamedPipeMessages.RegisterRepoRequest.Response.FromMessage(response);

                        if (!string.IsNullOrEmpty(message.ErrorMessage))
                        {
                            errorMessage = message.ErrorMessage;
                            return false;
                        }

                        if (message.State != NamedPipeMessages.CompletionState.Success)
                        {
                            errorMessage = "Unable to register repo. " + errorMessage;
                            return false;
                        }
                        else
                        {
                            return true;
                        }
                    }
                    else
                    {
                        errorMessage = string.Format("GVFS.Service responded with unexpected message: {0}", response);
                        return false;
                    }
                }
                catch (BrokenPipeException e)
                {
                    errorMessage = "Unable to communicate with GVFS.Service: " + e.ToString();
                    return false;
                }
            }
        }

        private void SetGitConfigSettings(GitProcess git)
        {
            if (!GVFSVerb.TrySetGitConfigSettings(git))
            {
                this.ReportErrorAndExit("Unable to configure git repo");
            }
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
    }
}