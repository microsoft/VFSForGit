using CommandLine;
using GVFS.CommandLine.DiskLayoutUpgrades;
using GVFS.Common;
using GVFS.Common.FileSystem;
using GVFS.Common.Git;
using GVFS.Common.Http;
using GVFS.Common.NamedPipes;
using GVFS.Common.Tracing;
using GVFS.GVFlt.DotGit;
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
        public CacheServerInfo ResolvedCacheServer { get; set; }
        public GVFSConfig DownloadedGVFSConfig { get; set; }

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
                        this.ReportErrorAndExit(tracer: null, exitCode: ReturnCode.Success, error: "This repo is already mounted.");
                    }
                }
            }
            
            if (!DiskLayoutUpgrade.TryRunAllUpgrades(enlistmentRoot))
            {
                this.ReportErrorAndExit("Failed to upgrade repo disk layout. " + ConsoleHelper.GetGVFSLogMessage(enlistmentRoot));
            }

            string error;
            if (!DiskLayoutUpgrade.TryCheckDiskLayoutVersion(tracer: null, enlistmentRoot: enlistmentRoot, error: out error))
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

            string mountExeLocation = null;
            using (JsonEtwTracer tracer = new JsonEtwTracer(GVFSConstants.GVFSEtwProviderName, "PreMount"))
            {
                CacheServerInfo cacheServer = this.ResolvedCacheServer ?? CacheServerResolver.GetCacheServerFromConfig(enlistment);

                tracer.AddLogFileEventListener(
                    GVFSEnlistment.GetNewGVFSLogFileName(enlistment.GVFSLogsRoot, GVFSConstants.LogFileTypes.MountVerb),
                    EventLevel.Verbose,
                    Keywords.Any);
                tracer.WriteStartEvent(
                    enlistment.EnlistmentRoot,
                    enlistment.RepoUrl,
                    cacheServer.Url,
                    new EventMetadata
                    {
                        { "Unattended", this.Unattended },
                        { "IsElevated", ProcessHelper.IsAdminElevated() },
                    });
                
                // TODO 1050199: Once the service is an optional component, GVFS should only attempt to attach
                // GvFlt via the service if the service is present\enabled
                if (!GvFltFilter.TryAttach(tracer, enlistment.EnlistmentRoot, out errorMessage))
                {
                    if (!this.ShowStatusWhileRunning(
                        () => { return this.AttachGvFltThroughService(enlistment, out errorMessage); },
                        "Attaching GvFlt to volume"))
                    {
                        this.ReportErrorAndExit(tracer, errorMessage);
                    }
                }

                RetryConfig retryConfig = null;
                GVFSConfig gvfsConfig = this.DownloadedGVFSConfig;
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

                    if (gvfsConfig == null)
                    {
                        if (retryConfig == null)
                        {
                            retryConfig = this.GetRetryConfig(tracer, enlistment);
                        }

                        gvfsConfig = this.QueryGVFSConfig(tracer, enlistment, retryConfig);
                    }

                    this.ValidateClientVersions(tracer, enlistment, gvfsConfig, showWarnings: true);

                    CacheServerResolver cacheServerResolver = new CacheServerResolver(tracer, enlistment);
                    cacheServer = cacheServerResolver.ResolveNameFromRemote(cacheServer.Url, gvfsConfig);
                    this.Output.WriteLine("Configured cache server: " + cacheServer);
                }

                this.InitializeLocalCacheAndObjectsPaths(tracer, enlistment, retryConfig, gvfsConfig, cacheServer);

                if (!this.ShowStatusWhileRunning(
                    () => { return this.PerformPreMountValidation(tracer, enlistment, out mountExeLocation, out errorMessage); },
                    "Validating repo"))
                {
                    this.ReportErrorAndExit(tracer, errorMessage);
                }
            }

            if (!this.ShowStatusWhileRunning(
                () => { return this.TryMount(enlistment, mountExeLocation, out errorMessage); },
                "Mounting"))
            {
                this.ReportErrorAndExit(errorMessage);
            }

            if (!this.Unattended)
            {
                if (!this.ShowStatusWhileRunning(
                    () => { return this.RegisterMount(enlistment, out errorMessage); },
                    "Registering for automount"))
                {
                    this.Output.WriteLine("    WARNING: " + errorMessage);
                }
            }
        }

        private bool PerformPreMountValidation(ITracer tracer, GVFSEnlistment enlistment, out string mountExeLocation, out string errorMessage)
        {
            errorMessage = string.Empty;
            mountExeLocation = string.Empty;

            // We have to parse these parameters here to make sure they are valid before 
            // handing them to the background process which cannot tell the user when they are bad
            EventLevel verbosity;
            Keywords keywords;
            this.ParseEnumArgs(out verbosity, out keywords);

            mountExeLocation = Path.Combine(ProcessHelper.GetCurrentProcessLocation(), GVFSConstants.MountExecutableName);
            if (!File.Exists(mountExeLocation))
            {
                errorMessage = "Could not find GVFS.Mount.exe. You may need to reinstall GVFS.";
                return false;
            }

            GitProcess git = new GitProcess(enlistment);
            if (!git.IsValidRepo())
            {
                errorMessage = "The .git folder is missing or has invalid contents";
                return false;
            }

            try
            {
                GitIndexProjection.ReadIndex(Path.Combine(enlistment.WorkingDirectoryRoot, GVFSConstants.DotGit.Index));
            }
            catch (Exception e)
            {
                EventMetadata metadata = new EventMetadata();
                metadata.Add("Exception", e.ToString());
                tracer.RelatedError(metadata, "Index validation failed");
                errorMessage = "Index validation failed, run 'gvfs repair' to repair index.";

                return false;
            }

            return true;
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
                    errorMessage = "Unable to mount because GVFS.Service is not responding. " + GVFSVerb.StartServiceInstructions;
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

        private bool TryMount(GVFSEnlistment enlistment, string mountExeLocation, out string errorMessage)
        {
            if (!GVFSVerb.TrySetGitConfigSettings(enlistment))
            {
                errorMessage = "Unable to configure git repo";
                return false;
            }

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

            return GVFSEnlistment.WaitUntilMounted(enlistment.EnlistmentRoot, this.Unattended, out errorMessage);
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