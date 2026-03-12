using CommandLine;
using GVFS.Common;
using GVFS.Common.Http;
using GVFS.Common.NamedPipes;
using GVFS.Common.Tracing;
using GVFS.DiskLayoutUpgrades;
using System;
using System.IO;
using System.Threading;

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

            // Always check if the given path is a worktree first, before
            // falling back to the standard .gvfs/ walk-up. A worktree dir
            // may be under the enlistment tree, so TryGetGVFSEnlistmentRoot
            // can succeed by walking up — but we still need worktree-specific handling.
            string pathToCheck = string.IsNullOrEmpty(this.EnlistmentRootPathParameter)
                ? Environment.CurrentDirectory
                : this.EnlistmentRootPathParameter;

            GVFSEnlistment.WorktreeInfo wtInfo = GVFSEnlistment.TryGetWorktreeInfo(pathToCheck);
            if (wtInfo?.SharedGitDir != null)
            {
                // This is a worktree mount request. Find the primary enlistment root.
                string srcDir = Path.GetDirectoryName(wtInfo.SharedGitDir);
                enlistmentRoot = srcDir != null ? Path.GetDirectoryName(srcDir) : null;

                if (enlistmentRoot == null)
                {
                    this.ReportErrorAndExit("Error: could not determine enlistment root for worktree '{0}'", pathToCheck);
                }

                // Check the worktree-specific pipe, not the primary
                if (!this.SkipMountedCheck)
                {
                    string worktreePipeName = GVFSPlatform.Instance.GetNamedPipeName(enlistmentRoot) + wtInfo.PipeSuffix;
                    using (NamedPipeClient pipeClient = new NamedPipeClient(worktreePipeName))
                    {
                        if (pipeClient.Connect(500))
                        {
                            this.ReportErrorAndExit(tracer: null, exitCode: ReturnCode.Success, error: $"The worktree at '{wtInfo.WorktreePath}' is already mounted.");
                        }
                    }
                }
            }
            else if (!GVFSPlatform.Instance.TryGetGVFSEnlistmentRoot(this.EnlistmentRootPathParameter, out enlistmentRoot, out errorMessage))
            {
                this.ReportErrorAndExit("Error: '{0}' is not a valid GVFS enlistment", this.EnlistmentRootPathParameter);
            }
            else
            {
                // Primary enlistment — check primary pipe as before
                if (!this.SkipMountedCheck)
                {
                    if (this.IsExistingPipeListening(enlistmentRoot))
                    {
                        this.ReportErrorAndExit(tracer: null, exitCode: ReturnCode.Success, error: $"The repo at '{enlistmentRoot}' is already mounted.");
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
            string mountExecutableLocation = null;
            using (JsonTracer tracer = new JsonTracer(GVFSConstants.GVFSEtwProviderName, "ExecuteMount"))
            {
                // Validate these before handing them to the background process
                // which cannot tell the user when they are bad
                this.ValidateEnumArgs();

                CacheServerInfo cacheServerFromConfig = CacheServerResolver.GetCacheServerFromConfig(enlistment);

                tracer.AddLogFileEventListener(
                    GVFSEnlistment.GetNewGVFSLogFileName(enlistment.GVFSLogsRoot, GVFSConstants.LogFileTypes.MountVerb),
                    EventLevel.Verbose,
                    Keywords.Any);
                tracer.WriteStartEvent(
                    enlistment.EnlistmentRoot,
                    enlistment.RepoUrl,
                    cacheServerFromConfig.Url,
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

                // Verify mount executable exists before launching
                mountExecutableLocation = Path.Combine(ProcessHelper.GetCurrentProcessLocation(), GVFSPlatform.Instance.Constants.MountExecutableName);
                if (!File.Exists(mountExecutableLocation))
                {
                    this.ReportErrorAndExit(tracer, $"Could not find {GVFSPlatform.Instance.Constants.MountExecutableName}. You may need to reinstall GVFS.");
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
        private bool TryMount(ITracer tracer, GVFSEnlistment enlistment, string mountExecutableLocation, out string errorMessage)
        {
            const string ParamPrefix = "--";

            // For worktrees, pass the worktree path so GVFS.Mount.exe creates the right enlistment
            string mountPath = enlistment.IsWorktree
                ? enlistment.WorkingDirectoryRoot
                : enlistment.EnlistmentRoot;

            tracer.RelatedInfo($"{nameof(this.TryMount)}: Launching background process('{mountExecutableLocation}') for {mountPath}");

            GVFSPlatform.Instance.StartBackgroundVFS4GProcess(
                tracer,
                mountExecutableLocation,
                new[]
                {
                    mountPath,
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

            return GVFSEnlistment.WaitUntilMounted(tracer, enlistment.NamedPipeName, enlistment.EnlistmentRoot, this.Unattended, out errorMessage);
        }

        private bool RegisterMount(GVFSEnlistment enlistment, out string errorMessage)
        {
            errorMessage = string.Empty;

            NamedPipeMessages.RegisterRepoRequest request = new NamedPipeMessages.RegisterRepoRequest();

            // Worktree mounts register with their worktree path so they can be
            // listed and unregistered independently of the primary enlistment.
            request.EnlistmentRoot = enlistment.IsWorktree
                ? enlistment.WorkingDirectoryRoot
                : enlistment.EnlistmentRoot;

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

        private void ValidateEnumArgs()
        {
            if (!Enum.TryParse(this.KeywordsCsv, out Keywords _))
            {
                this.ReportErrorAndExit("Error: Invalid logging filter keywords: " + this.KeywordsCsv);
            }

            if (!Enum.TryParse(this.Verbosity, out EventLevel _))
            {
                this.ReportErrorAndExit("Error: Invalid logging verbosity: " + this.Verbosity);
            }
        }
    }
}