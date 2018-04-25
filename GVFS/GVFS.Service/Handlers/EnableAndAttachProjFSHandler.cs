using GVFS.Common.FileSystem;
using GVFS.Common.NamedPipes;
using GVFS.Common.Tracing;
using Microsoft.Diagnostics.Tracing;

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
            EventMetadata prjFltHealthMetdata = new EventMetadata();
            prjFltHealthMetdata.Add("Area", EtwArea);

            PhysicalFileSystem fileSystem = new PhysicalFileSystem();

            lock (enablePrjFltLock)
            {
                bool isServiceInstalled;
                bool isDriverFileInstalled;
                bool isNativeLibInstalled;
                bool isRunning = ProjFSFilter.IsServiceRunningAndInstalled(tracer, fileSystem, out isServiceInstalled, out isDriverFileInstalled, out isNativeLibInstalled);
                bool isInstalled = isServiceInstalled && isDriverFileInstalled && isNativeLibInstalled;

                prjFltHealthMetdata.Add($"Initial_{nameof(isRunning)}", isRunning);
                prjFltHealthMetdata.Add($"Initial_{nameof(isServiceInstalled)}", isServiceInstalled);
                prjFltHealthMetdata.Add($"Initial_{nameof(isDriverFileInstalled)}", isDriverFileInstalled);
                prjFltHealthMetdata.Add($"Initial_{nameof(isNativeLibInstalled)}", isNativeLibInstalled);
                prjFltHealthMetdata.Add($"Initial_{nameof(isInstalled)}", isInstalled);

                if (!isRunning)
                {
                    if (!isInstalled)
                    {
                        if (ProjFSFilter.TryEnableOrInstallDriver(tracer, fileSystem))
                        {
                            isInstalled = true;
                        }
                        else
                        {
                            error = "Failed to install (or enable) PrjFlt";
                            tracer.RelatedError($"{nameof(TryEnablePrjFlt)}: {error}");
                        }
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
                else if (!isNativeLibInstalled)
                {
                    tracer.RelatedWarning($"{nameof(TryEnablePrjFlt)}: prjflt service is running, but native library is not installed");

                    if (ProjFSFilter.TryInstallNativeLib(tracer, fileSystem))
                    {
                        isInstalled = true;
                    }
                    else
                    {
                        error = "Failed to install native ProjFs library";
                        tracer.RelatedError($"{nameof(TryEnablePrjFlt)}: {error}");
                    }
                }

                bool isAutoLoggerEnabled = ProjFSFilter.IsAutoLoggerEnabled(tracer);
                prjFltHealthMetdata.Add("InitiallyAutoLoggerEnabled", isAutoLoggerEnabled);

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

                prjFltHealthMetdata.Add(nameof(isInstalled), isInstalled);
                prjFltHealthMetdata.Add(nameof(isRunning), isRunning);
                prjFltHealthMetdata.Add(nameof(isAutoLoggerEnabled), isAutoLoggerEnabled);
                tracer.RelatedEvent(EventLevel.Informational, $"{nameof(TryEnablePrjFlt)}_Summary", prjFltHealthMetdata, Keywords.Telemetry);

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
                if (!ProjFSFilter.TryAttach(this.tracer, this.request.EnlistmentRoot, out errorMessage))
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
