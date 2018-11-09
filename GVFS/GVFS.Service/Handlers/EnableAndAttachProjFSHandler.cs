using GVFS.Common;
using GVFS.Common.FileSystem;
using GVFS.Common.NamedPipes;
using GVFS.Common.Tracing;
using GVFS.Platform.Windows;

namespace GVFS.Service.Handlers
{
    public class EnableAndAttachProjFSHandler : MessageHandler
    {
        private const string EtwArea = nameof(EnableAndAttachProjFSHandler);

        private static object enablePrjFltLock = new object();

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
                bool isServiceInstalled;
                bool isDriverFileInstalled;
                bool isNativeLibInstalled;
                bool isRunning = ProjFSFilter.IsServiceRunningAndInstalled(tracer, fileSystem, out isServiceInstalled, out isDriverFileInstalled, out isNativeLibInstalled);
                bool isInstalled = isServiceInstalled && isDriverFileInstalled && isNativeLibInstalled;

                prjFltHealthMetadata.Add($"Initial_{nameof(isRunning)}", isRunning);
                prjFltHealthMetadata.Add($"Initial_{nameof(isServiceInstalled)}", isServiceInstalled);
                prjFltHealthMetadata.Add($"Initial_{nameof(isDriverFileInstalled)}", isDriverFileInstalled);
                prjFltHealthMetadata.Add($"Initial_{nameof(isNativeLibInstalled)}", isNativeLibInstalled);
                prjFltHealthMetadata.Add($"Initial_{nameof(isInstalled)}", isInstalled);

                if (!isRunning)
                {
                    if (!isInstalled)
                    {
                        uint windowsBuildNumber;
                        bool isInboxProjFSFinalAPI;
                        bool isProjFSFeatureAvailable;
                        if (ProjFSFilter.TryEnableOrInstallDriver(tracer, fileSystem, out windowsBuildNumber, out isInboxProjFSFinalAPI, out isProjFSFeatureAvailable))
                        {
                            isInstalled = true;
                        }
                        else
                        {
                            error = "Failed to install (or enable) PrjFlt";
                            tracer.RelatedError($"{nameof(TryEnablePrjFlt)}: {error}");
                        }

                        prjFltHealthMetadata.Add(nameof(windowsBuildNumber), windowsBuildNumber);
                        prjFltHealthMetadata.Add(nameof(isInboxProjFSFinalAPI), isInboxProjFSFinalAPI);
                        prjFltHealthMetadata.Add(nameof(isProjFSFeatureAvailable), isProjFSFeatureAvailable);
                    }

                    if (isInstalled)
                    {
                        if (ProjFSFilter.TryStartService(tracer))
                        {
                            isRunning = true;
                        }
                        else
                        {
                            error = "Failed to start prjflt service";
                            tracer.RelatedError($"{nameof(TryEnablePrjFlt)}: {error}");
                        }
                    }
                }

                isNativeLibInstalled = ProjFSFilter.IsNativeLibInstalled(tracer, new PhysicalFileSystem());
                if (!isNativeLibInstalled)
                {
                    string missingNativeLibMessage = "Native library is not installed";
                    error = string.IsNullOrEmpty(error) ? missingNativeLibMessage : $"{error}. {missingNativeLibMessage}";
                    tracer.RelatedError($"{nameof(TryEnablePrjFlt)}: {missingNativeLibMessage}");
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

                prjFltHealthMetadata.Add(nameof(isInstalled), isInstalled);
                prjFltHealthMetadata.Add(nameof(isRunning), isRunning);
                prjFltHealthMetadata.Add(nameof(isAutoLoggerEnabled), isAutoLoggerEnabled);
                prjFltHealthMetadata.Add(nameof(isNativeLibInstalled), isNativeLibInstalled);
                tracer.RelatedEvent(EventLevel.Informational, $"{nameof(TryEnablePrjFlt)}_Summary", prjFltHealthMetadata, Keywords.Telemetry);

                return isInstalled && isRunning;
            }
        }

        public void Run()
        {
            string errorMessage;
            NamedPipeMessages.CompletionState state = NamedPipeMessages.CompletionState.Success;

            if (!TryEnablePrjFlt(this.tracer, out errorMessage))
            {
                state = NamedPipeMessages.CompletionState.Failure;
                this.tracer.RelatedError("Unable to install or enable PrjFlt. Enlistment root: {0} \nError: {1} ", this.request.EnlistmentRoot, errorMessage);
            }

            if (!string.IsNullOrEmpty(this.request.EnlistmentRoot))
            {
                if (!ProjFSFilter.TryAttach(this.request.EnlistmentRoot, out errorMessage))
                {
                    state = NamedPipeMessages.CompletionState.Failure;
                    this.tracer.RelatedError("Unable to attach filter to volume. Enlistment root: {0} \nError: {1} ", this.request.EnlistmentRoot, errorMessage);
                }
            }

            NamedPipeMessages.EnableAndAttachProjFSRequest.Response response = new NamedPipeMessages.EnableAndAttachProjFSRequest.Response();

            response.State = state;
            response.ErrorMessage = errorMessage;

            this.WriteToClient(response.ToMessage(), this.connection, this.tracer);
        }
    }
}
