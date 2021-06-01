using CommandLine;
using GVFS.Common;
using GVFS.Common.FileSystem;
using GVFS.Common.Git;
using GVFS.Common.Http;
using GVFS.Common.NamedPipes;
using GVFS.Common.Tracing;
using GVFS.DiskLayoutUpgrades;
using GVFS.Virtualization.Projection;
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

        public bool SkipMountedCheck { get; set; }
        public bool SkipVersionCheck { get; set; }
        public bool SkipInstallHooks { get; set; }
        public CacheServerInfo ResolvedCacheServer { get; set; }
        public ServerGVFSConfig DownloadedGVFSConfig { get; set; }

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
            string errorMessage;
            string enlistmentRoot;
            if (!GVFSPlatform.Instance.TryGetGVFSEnlistmentRoot(this.EnlistmentRootPathParameter, out enlistmentRoot, out errorMessage))
            {
                this.ReportErrorAndExit("Error: '{0}' is not a valid GVFS enlistment", this.EnlistmentRootPathParameter);
            }

            if (!this.SkipMountedCheck)
            {
                if (this.IsExistingPipeListening(enlistmentRoot))
                {
                    this.ReportErrorAndExit(tracer: null, exitCode: ReturnCode.Success, error: $"The repo at '{enlistmentRoot}' is already mounted.");
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
            string mountExecutableLocation = null;
            using (JsonTracer tracer = new JsonTracer(GVFSConstants.GVFSEtwProviderName, "ExecuteMount"))
            {
                PhysicalFileSystem fileSystem = new PhysicalFileSystem();
                GitRepo gitRepo = new GitRepo(tracer, enlistment, fileSystem);
                GVFSContext context = new GVFSContext(tracer, fileSystem, gitRepo, enlistment);

                if (!this.SkipInstallHooks && !HooksInstaller.InstallHooks(context, out errorMessage))
                {
                    this.ReportErrorAndExit("Error installing hooks: " + errorMessage);
                }

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
                        { "IsElevated", GVFSPlatform.Instance.IsElevated() },
                        { "NamedPipeName", enlistment.NamedPipeName },
                        { nameof(this.EnlistmentRootPathParameter), this.EnlistmentRootPathParameter },
                    });

                if (!GVFSPlatform.Instance.KernelDriver.IsReady(tracer, enlistment.EnlistmentRoot, this.Output, out errorMessage))
                {
                    tracer.RelatedEvent(
                        EventLevel.Informational,
                        $"{nameof(MountVerb)}_{nameof(this.Execute)}_EnablingKernelDriverViaService",
                        new EventMetadata
                        {
                            { "KernelDriver.IsReady_Error", errorMessage },
                            { TracingConstants.MessageKey.InfoMessage, "Service will retry" }
                        });

                    if (!this.ShowStatusWhileRunning(
                        () => { return this.TryEnableAndAttachPrjFltThroughService(enlistment.EnlistmentRoot, out errorMessage); },
                        $"Attaching ProjFS to volume"))
                    {
                        this.ReportErrorAndExit(tracer, ReturnCode.FilterError, errorMessage);
                    }
                }

                RetryConfig retryConfig = null;
                ServerGVFSConfig serverGVFSConfig = this.DownloadedGVFSConfig;
                if (!this.SkipVersionCheck)
                {
                    string authErrorMessage;
                    if (!this.TryAuthenticate(tracer, enlistment, out authErrorMessage))
                    {
                        this.Output.WriteLine("    WARNING: " + authErrorMessage);
                        this.Output.WriteLine("    Mount will proceed, but new files cannot be accessed until GVFS can authenticate.");
                    }

                    if (serverGVFSConfig == null)
                    {
                        if (retryConfig == null)
                        {
                            retryConfig = this.GetRetryConfig(tracer, enlistment);
                        }

                        serverGVFSConfig = this.QueryGVFSConfig(tracer, enlistment, retryConfig);
                    }

                    this.ValidateClientVersions(tracer, enlistment, serverGVFSConfig, showWarnings: true);

                    CacheServerResolver cacheServerResolver = new CacheServerResolver(tracer, enlistment);
                    cacheServer = cacheServerResolver.ResolveNameFromRemote(cacheServer.Url, serverGVFSConfig);
                    this.Output.WriteLine("Configured cache server: " + cacheServer);
                }

                this.InitializeLocalCacheAndObjectsPaths(tracer, enlistment, retryConfig, serverGVFSConfig, cacheServer);

                if (!this.ShowStatusWhileRunning(
                    () => { return this.PerformPreMountValidation(tracer, enlistment, out mountExecutableLocation, out errorMessage); },
                    "Validating repo"))
                {
                    this.ReportErrorAndExit(tracer, errorMessage);
                }

                if (!this.SkipVersionCheck)
                {
                    string error;
                    if (!RepoMetadata.TryInitialize(tracer, enlistment.DotGVFSRoot, out error))
                    {
                        this.ReportErrorAndExit(tracer, error);
                    }

                    try
                    {
                        GitProcess git = new GitProcess(enlistment);
                        this.LogEnlistmentInfoAndSetConfigValues(tracer, git, enlistment);
                    }
                    finally
                    {
                        RepoMetadata.Shutdown();
                    }
                }

                if (!this.ShowStatusWhileRunning(
                    () => { return this.TryMount(tracer, enlistment, mountExecutableLocation, out errorMessage); },
                    "Mounting"))
                {
                    this.ReportErrorAndExit(tracer, errorMessage);
                }

                if (!this.Unattended)
                {
                    tracer.RelatedInfo($"{nameof(this.Execute)}: Registering for automount");

                    if (this.ShowStatusWhileRunning(
                        () => { return this.RegisterMount(enlistment, out errorMessage); },
                        "Registering for automount"))
                    {
                        tracer.RelatedInfo($"{nameof(this.Execute)}: Registered for automount");
                    }
                    else
                    {
                        this.Output.WriteLine("    WARNING: " + errorMessage);
                        tracer.RelatedInfo($"{nameof(this.Execute)}: Failed to register for automount");
                    }
                }
            }
        }

        private bool PerformPreMountValidation(ITracer tracer, GVFSEnlistment enlistment, out string mountExecutableLocation, out string errorMessage)
        {
            errorMessage = string.Empty;
            mountExecutableLocation = string.Empty;

            // We have to parse these parameters here to make sure they are valid before
            // handing them to the background process which cannot tell the user when they are bad
            EventLevel verbosity;
            Keywords keywords;
            this.ParseEnumArgs(out verbosity, out keywords);

            mountExecutableLocation = Path.Combine(ProcessHelper.GetCurrentProcessLocation(), GVFSPlatform.Instance.Constants.MountExecutableName);
            if (!File.Exists(mountExecutableLocation))
            {
                errorMessage = $"Could not find {GVFSPlatform.Instance.Constants.MountExecutableName}. You may need to reinstall GVFS.";
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
                GitIndexProjection.ReadIndex(tracer, Path.Combine(enlistment.WorkingDirectoryBackingRoot, GVFSConstants.DotGit.Index));
            }
            catch (Exception e)
            {
                EventMetadata metadata = new EventMetadata();
                metadata.Add("Exception", e.ToString());
                tracer.RelatedError(metadata, "Index validation failed");
                errorMessage = "Index validation failed, run 'gvfs repair' to repair index.";

                return false;
            }

            if (!GVFSPlatform.Instance.FileSystem.IsFileSystemSupported(enlistment.EnlistmentRoot, out string error))
            {
                errorMessage = $"FileSystem unsupported: {error}";
                return false;
            }

            return true;
        }

        private bool TryMount(ITracer tracer, GVFSEnlistment enlistment, string mountExecutableLocation, out string errorMessage)
        {
            if (!GVFSVerb.TrySetRequiredGitConfigSettings(enlistment))
            {
                errorMessage = "Unable to configure git repo";
                return false;
            }

            const string ParamPrefix = "--";

            tracer.RelatedInfo($"{nameof(this.TryMount)}: Launching background process('{mountExecutableLocation}') for {enlistment.EnlistmentRoot}");

            GVFSPlatform.Instance.StartBackgroundVFS4GProcess(
                tracer,
                mountExecutableLocation,
                new[]
                {
                    enlistment.EnlistmentRoot,
                    ParamPrefix + GVFSConstants.VerbParameters.Mount.Verbosity,
                    this.Verbosity,
                    ParamPrefix + GVFSConstants.VerbParameters.Mount.Keywords,
                    this.KeywordsCsv,
                    ParamPrefix + GVFSConstants.VerbParameters.Mount.StartedByService,
                    this.StartedByService.ToString(),
                    ParamPrefix + GVFSConstants.VerbParameters.Mount.StartedByVerb,
                    true.ToString()
                });

            tracer.RelatedInfo($"{nameof(this.TryMount)}: Waiting for repo to be mounted");
            return GVFSEnlistment.WaitUntilMounted(tracer, enlistment.EnlistmentRoot, this.Unattended, out errorMessage);
        }

        private bool RegisterMount(GVFSEnlistment enlistment, out string errorMessage)
        {
            errorMessage = string.Empty;

            NamedPipeMessages.RegisterRepoRequest request = new NamedPipeMessages.RegisterRepoRequest();
            request.EnlistmentRoot = enlistment.EnlistmentRoot;

            request.OwnerSID = GVFSPlatform.Instance.GetCurrentUser();

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