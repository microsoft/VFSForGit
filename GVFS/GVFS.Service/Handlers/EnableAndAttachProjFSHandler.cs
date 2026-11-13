using GVFS.Common;
using GVFS.Common.FileSystem;
using GVFS.Common.Git;
using GVFS.Common.NamedPipes;
using GVFS.Common.Tracing;
using GVFS.Platform.Windows;
using System.Threading;

namespace GVFS.Service.Handlers
{
    public class EnableAndAttachProjFSHandler : MessageHandler
    {
        private const string EtwArea = nameof(EnableAndAttachProjFSHandler);

        private static Lock enablePrjFltLock = new Lock();

        private NamedPipeServer.Connection connection;
        private NamedPipeMessages.EnableAndAttachProjFSRequest request;
        private ITracer tracer;

        public EnableAndAttachProjFSHandler(
            ITracer tracer,
            NamedPipeServer.Connection connection,
            NamedPipeMessages.EnableAndAttachProjFSRequest request)
        {
            this.tracer = tracer;
            this.connection = connection;
            this.request = request;
        }

        public static bool TryEnablePrjFlt(ITracer tracer, out string error)
        {
            error = null;
            EventMetadata prjFltHealthMetadata = new EventMetadata();
            prjFltHealthMetadata.Add("Area", EtwArea);

            PhysicalFileSystem fileSystem = new PhysicalFileSystem();

            lock (enablePrjFltLock)
            {
                bool isPrjfltServiceInstalled;
                bool isPrjfltDriverInstalled;
                bool isNativeProjFSLibInstalled;
                bool isPrjfltServiceRunning = ProjFSFilter.IsServiceRunningAndInstalled(tracer, fileSystem, out isPrjfltServiceInstalled, out isPrjfltDriverInstalled, out isNativeProjFSLibInstalled);

                prjFltHealthMetadata.Add($"Initial_{nameof(isPrjfltDriverInstalled)}", isPrjfltDriverInstalled);
                prjFltHealthMetadata.Add($"Initial_{nameof(isPrjfltServiceInstalled)}", isPrjfltServiceInstalled);
                prjFltHealthMetadata.Add($"Initial_{nameof(isPrjfltServiceRunning)}", isPrjfltServiceRunning);
                prjFltHealthMetadata.Add($"Initial_{nameof(isNativeProjFSLibInstalled)}", isNativeProjFSLibInstalled);

                if (!isPrjfltServiceRunning)
                {
                    if (!isPrjfltServiceInstalled || !isPrjfltDriverInstalled)
                    {
                        uint windowsBuildNumber;
                        bool isInboxProjFSFinalAPI;
                        bool isProjFSFeatureAvailable;
                        if (ProjFSFilter.TryEnableOrInstallDriver(tracer, fileSystem, out windowsBuildNumber, out isInboxProjFSFinalAPI, out isProjFSFeatureAvailable))
                        {
                            isPrjfltServiceInstalled = true;
                            isPrjfltDriverInstalled = true;
                        }
                        else
                        {
                            error = "Failed to enable ProjFS. Ensure the Windows Projected File System optional feature is enabled. "
                                + "From an elevated PowerShell prompt, run: "
                                + "Enable-WindowsOptionalFeature -Online -FeatureName Client-ProjFS";
                            tracer.RelatedError($"{nameof(TryEnablePrjFlt)}: {error}");
                        }

                        prjFltHealthMetadata.Add(nameof(windowsBuildNumber), windowsBuildNumber);
                        prjFltHealthMetadata.Add(nameof(isInboxProjFSFinalAPI), isInboxProjFSFinalAPI);
                        prjFltHealthMetadata.Add(nameof(isProjFSFeatureAvailable), isProjFSFeatureAvailable);
                    }

                    if (isPrjfltServiceInstalled)
                    {
                        ProjFSFilter.ServiceStartFailureReason startFailureReason;
                        string startServiceError;
                        if (ProjFSFilter.TryStartService(tracer, out startFailureReason, out startServiceError))
                        {
                            isPrjfltServiceRunning = true;
                        }
                        else
                        {
                            error = string.IsNullOrEmpty(startServiceError) ? "Failed to start prjflt service" : startServiceError;
                            prjFltHealthMetadata.Add(nameof(startFailureReason), startFailureReason.ToString());
                            tracer.RelatedError($"{nameof(TryEnablePrjFlt)}: {error}");
                        }
                    }
                }

                // Check again if the native library is installed.  If the code above enabled the
                // ProjFS optional feature then the native library will now be installed.
                isNativeProjFSLibInstalled = ProjFSFilter.IsNativeLibInstalled(tracer, fileSystem);
                if (!isNativeProjFSLibInstalled)
                {
                    error = "ProjFS native library (ProjectedFSLib.dll) is not installed in System32. "
                        + "Ensure the Windows Projected File System optional feature is enabled. "
                        + "From an elevated PowerShell prompt, run: "
                        + "Enable-WindowsOptionalFeature -Online -FeatureName Client-ProjFS";
                    tracer.RelatedError($"{nameof(TryEnablePrjFlt)}: {error}");
                }

                bool isAutoLoggerEnabled = ProjFSFilter.IsAutoLoggerEnabled(tracer);
                prjFltHealthMetadata.Add($"Initial_{nameof(isAutoLoggerEnabled)}", isAutoLoggerEnabled);

                if (!isAutoLoggerEnabled)
                {
                    if (ProjFSFilter.TryEnableAutoLogger(tracer))
                    {
                        isAutoLoggerEnabled = true;
                    }
                    else
                    {
                        tracer.RelatedError($"{nameof(TryEnablePrjFlt)}: Failed to enable prjflt AutoLogger");
                    }
                }

                prjFltHealthMetadata.Add(nameof(isPrjfltDriverInstalled), isPrjfltDriverInstalled);
                prjFltHealthMetadata.Add(nameof(isPrjfltServiceInstalled), isPrjfltServiceInstalled);
                prjFltHealthMetadata.Add(nameof(isPrjfltServiceRunning), isPrjfltServiceRunning);
                prjFltHealthMetadata.Add(nameof(isNativeProjFSLibInstalled), isNativeProjFSLibInstalled);
                prjFltHealthMetadata.Add(nameof(isAutoLoggerEnabled), isAutoLoggerEnabled);
                tracer.RelatedEvent(EventLevel.Informational, $"{nameof(TryEnablePrjFlt)}_Summary", prjFltHealthMetadata, Keywords.Telemetry);

                return isPrjfltDriverInstalled && isPrjfltServiceInstalled && isPrjfltServiceRunning && isNativeProjFSLibInstalled;
            }
        }

        public void Run()
        {
            string errorMessage = null;
            NamedPipeMessages.CompletionState state = NamedPipeMessages.CompletionState.Success;

            if (!TryEnablePrjFlt(this.tracer, out errorMessage))
            {
                state = NamedPipeMessages.CompletionState.Failure;
                this.tracer.RelatedError("Unable to install or enable PrjFlt. Enlistment root: {0} \nError: {1} ", this.request.EnlistmentRoot, errorMessage);
            }

            if (state == NamedPipeMessages.CompletionState.Success
                && !string.IsNullOrEmpty(this.request.EnlistmentRoot))
            {
                bool attached = ProjFSFilter.TryAttachToVolume(
                    this.request.EnlistmentRoot, this.tracer, out string attachError, out bool devDriveFilterRejected, out string volumeRoot);
                if (!attached && devDriveFilterRejected)
                {
                    attached = this.TryRecoverFilter(volumeRoot, out attachError);
                }

                if (!attached)
                {
                    state = NamedPipeMessages.CompletionState.Failure;
                    errorMessage = attachError;
                    this.tracer.RelatedError("Unable to attach filter to volume. Enlistment root: {0} \nError: {1} ", this.request.EnlistmentRoot, attachError);
                }
            }

            NamedPipeMessages.EnableAndAttachProjFSRequest.Response response = new NamedPipeMessages.EnableAndAttachProjFSRequest.Response();

            response.State = state;
            response.ErrorMessage = errorMessage;

            this.WriteToClient(response.ToMessage(), this.connection, this.tracer);
        }

        private bool TryRecoverFilter(string volumeRoot, out string error)
        {
            return DevDriveFilterRecovery.TryRecover(
                volumeRoot,
                this.tracer,
                () =>
                {
                    bool enabled = this.IsAutoAllowDevDriveFilterEnabled(out string reason);
                    return (enabled, reason);
                },
                () =>
                {
                    DevDriveFilterPolicy.AllowFilterResult result = DevDriveFilterPolicy.TryAllowFilter(volumeRoot, this.tracer, out string policyError);
                    return (result, policyError);
                },
                () =>
                {
                    bool attached = ProjFSFilter.TryAttachToVolume(
                        this.request.EnlistmentRoot, this.tracer, out string attachError, out bool rejected, out string _);
                    return (attached, rejected, attachError);
                },
                out error);
        }

        /// <summary>
        /// Reads gvfs.auto-allow-devdrive-filter fresh for this request. The value is deliberately
        /// not cached: the service runs for the lifetime of the machine, so a cached value would
        /// keep an operator's change from taking effect until the service restarted.
        /// </summary>
        /// <remarks>
        /// The service runs as LocalSystem and cannot see the user's global git config, so it reads
        /// the system scope and combines it with the value the client resolved against the
        /// enlistment. Either side can turn the feature off; neither can turn it on alone.
        /// </remarks>
        private bool IsAutoAllowDevDriveFilterEnabled(out string disabledReason)
        {
            string gitBinPath = GVFSPlatform.Instance.GitInstallation.GetInstalledGitBinPath();
            LibGit2Repo.TryReadConfigBoolWithGit reader = null;
            if (!string.IsNullOrEmpty(gitBinPath))
            {
                reader = (string key, out bool? value, out string readError) =>
                    GitProcess.TryGetBoolFromSystemConfig(gitBinPath, key, out value, out readError);
            }

            return DevDriveFilterConfig.IsEnabled(reader, this.request.AutoAllowDevDriveFilter, "system", this.tracer, out disabledReason);
        }
    }
}
