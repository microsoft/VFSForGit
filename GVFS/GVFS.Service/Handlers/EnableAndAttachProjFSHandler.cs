using GVFS.Common;
using GVFS.Common.FileSystem;
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
                        if (ProjFSFilter.TryStartService(tracer))
                        {
                            isPrjfltServiceRunning = true;
                        }
                        else
                        {
                            error = "Failed to start prjflt service";
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
                string attachError;
                if (!ProjFSFilter.TryAttachToVolume(this.request.EnlistmentRoot, this.tracer, out attachError))
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
    }
}
