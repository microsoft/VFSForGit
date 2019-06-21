using GVFS.Common.NamedPipes;
using GVFS.Common.Tracing;
using System.Runtime.Serialization;

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
        protected const string EnableProjFSRequestDescription = "attach volume";
        protected string requestDescription;

        private const string MountRequestDescription = "mount";
        private const string UnmountRequestDescription = "unmount";
        private const string RepoListRequestDescription = "repo list";
        private const string UnknownRequestDescription = "unknown";

        private string etwArea;
        private ITracer tracer;
        private IRepoRegistry repoRegistry;

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
    }
}
