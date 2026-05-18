using GVFS.Common.NamedPipes;
using GVFS.Common.Tracing;
using System.Runtime.Serialization;
using System.Threading;

namespace GVFS.Service.Handlers
{
    /// <summary>
    /// RequestHandler - Routes client requests that reach GVFS.Service to
    /// appropriate MessageHandler object.
    /// Example requests - gvfs mount/unmount command sends requests to
    /// register/un-register repositories for automount. RequestHandler
    /// routes them to RegisterRepoHandler and UnRegisterRepoHandler
    /// respectively.
    /// </summary>
    public class RequestHandler
    {
        private const int PendingUpgradeDelayMs = 2000;

        protected const string EnableProjFSRequestDescription = "attach volume";
        protected string requestDescription;

        private const string MountRequestDescription = "mount";
        private const string UnmountRequestDescription = "unmount";
        private const string RepoListRequestDescription = "repo list";
        private const string UnknownRequestDescription = "unknown";

        private string etwArea;
        private ITracer tracer;
        private IRepoRegistry repoRegistry;
        private Timer pendingUpgradeTimer;
        private readonly Lock pendingUpgradeTimerLock = new Lock();

        public RequestHandler(ITracer tracer, string etwArea, IRepoRegistry repoRegistry)
        {
            this.tracer = tracer;
            this.etwArea = etwArea;
            this.repoRegistry = repoRegistry;
        }

        public void HandleRequest(ITracer tracer, string request, NamedPipeServer.Connection connection)
        {
            NamedPipeMessages.Message message = NamedPipeMessages.Message.FromString(request);
            if (string.IsNullOrWhiteSpace(message.Header))
            {
                return;
            }

            using (ITracer activity = this.tracer.StartActivity(message.Header, EventLevel.Informational, new EventMetadata { { nameof(request), request } }))
            {
                try
                {
                    this.HandleMessage(activity, message, connection);
                }
                catch (SerializationException ex)
                {
                    EventMetadata metadata = new EventMetadata();
                    metadata.Add("Area", this.etwArea);
                    metadata.Add("Header", message.Header);
                    metadata.Add("Exception", ex.ToString());

                    activity.RelatedError(metadata, $"Could not deserialize {this.requestDescription} request: {ex.Message}");
                }
            }
        }

        protected virtual void HandleMessage(
            ITracer tracer,
            NamedPipeMessages.Message message,
            NamedPipeServer.Connection connection)
        {
            switch (message.Header)
            {
                case NamedPipeMessages.RegisterRepoRequest.Header:
                    this.requestDescription = MountRequestDescription;
                    NamedPipeMessages.RegisterRepoRequest mountRequest = NamedPipeMessages.RegisterRepoRequest.FromMessage(message);
                    RegisterRepoHandler mountHandler = new RegisterRepoHandler(tracer, this.repoRegistry, connection, mountRequest);
                    mountHandler.Run();

                    break;

                case NamedPipeMessages.UnregisterRepoRequest.Header:
                    this.requestDescription = UnmountRequestDescription;
                    NamedPipeMessages.UnregisterRepoRequest unmountRequest = NamedPipeMessages.UnregisterRepoRequest.FromMessage(message);
                    UnregisterRepoHandler unmountHandler = new UnregisterRepoHandler(tracer, this.repoRegistry, connection, unmountRequest);
                    unmountHandler.Run();

                    // After unmount, check for pending staged upgrade on a
                    // background thread. The deferred check gives the calling
                    // GVFS.Mount process time to exit so its executable is no
                    // longer locked when the upgrade runs.
                    // Use the long-lived service tracer, not the scoped activity
                    // tracer which will be disposed when this handler returns.
                    this.TryDeferredPendingUpgradeCheck(this.tracer);

                    break;

                case NamedPipeMessages.GetActiveRepoListRequest.Header:
                    this.requestDescription = RepoListRequestDescription;
                    NamedPipeMessages.GetActiveRepoListRequest repoListRequest = NamedPipeMessages.GetActiveRepoListRequest.FromMessage(message);
                    GetActiveRepoListHandler excludeHandler = new GetActiveRepoListHandler(tracer, this.repoRegistry, connection, repoListRequest);
                    excludeHandler.Run();

                    break;

                case NamedPipeMessages.EnableAndAttachProjFSRequest.Header:

                    // This request is ignored on non Windows platforms.
                    NamedPipeMessages.EnableAndAttachProjFSRequest.Response response = new NamedPipeMessages.EnableAndAttachProjFSRequest.Response();
                    response.State = NamedPipeMessages.CompletionState.Success;

                    this.TrySendResponse(tracer, response.ToMessage().ToString(), connection);
                    break;

                default:
                    this.requestDescription = UnknownRequestDescription;
                    EventMetadata metadata = new EventMetadata();
                    metadata.Add("Area", this.etwArea);
                    metadata.Add("Header", message.Header);
                    tracer.RelatedWarning(metadata, "HandleNewConnection: Unknown request", Keywords.Telemetry);

                    this.TrySendResponse(tracer, NamedPipeMessages.UnknownRequest, connection);
                    break;
            }
        }

        private void TrySendResponse(
            ITracer tracer,
            string message,
            NamedPipeServer.Connection connection)
        {
            if (!connection.TrySendResponse(message))
            {
                tracer.RelatedError($"{nameof(this.TrySendResponse)}: Could not send response to client. Reply Info: {message}");
            }
        }

        private void TryDeferredPendingUpgradeCheck(ITracer tracer)
        {
            string installDir = Service.Configuration.AssemblyPath;
            string pendingUpgradeDir = System.IO.Path.Combine(installDir, PendingUpgradeHandler.PendingUpgradeDirectoryName);
            if (!System.IO.Directory.Exists(pendingUpgradeDir))
            {
                return;
            }

            // Debounce: reset the timer on each unmount so the check fires
            // once after the last unmount settles. If multiple repos unmount
            // in quick succession, only one upgrade attempt runs.
            lock (this.pendingUpgradeTimerLock)
            {
                if (this.pendingUpgradeTimer == null)
                {
                    this.pendingUpgradeTimer = new Timer(
                        _ =>
                        {
                            tracer.RelatedInfo("TryDeferredPendingUpgradeCheck: Checking pending upgrade after unmount");
                            PendingUpgradeHandler.TryApplyPendingUpgrade(tracer);
                        },
                        null,
                        PendingUpgradeDelayMs,
                        Timeout.Infinite);
                }
                else
                {
                    this.pendingUpgradeTimer.Change(PendingUpgradeDelayMs, Timeout.Infinite);
                }
            }
        }
    }
}
