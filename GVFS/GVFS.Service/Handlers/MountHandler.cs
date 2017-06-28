using GVFS.Common;
using GVFS.Common.NamedPipes;
using GVFS.Common.Tracing;

namespace GVFS.Service.Handlers
{
    public class MountHandler
    {
        private NamedPipeServer.Connection connection;
        private NamedPipeMessages.MountRepoRequest request;
        private ITracer tracer;
        private RepoRegistry registry;

        public MountHandler(ITracer tracer, RepoRegistry registry, NamedPipeServer.Connection connection, NamedPipeMessages.MountRepoRequest request)
        {
            this.tracer = tracer;
            this.registry = registry;
            this.connection = connection;
            this.request = request;
        }

        public void Run()
        {
            GVFSMountProcess gvfs = new GVFSMountProcess(this.tracer, this.request.EnlistmentRoot);

            if (!gvfs.Mount(
                this.request.Verbosity,
                this.request.Keywords,
                this.request.ShowDebugWindow))
            {
                NamedPipeMessages.MountRepoRequest.Response response = new NamedPipeMessages.MountRepoRequest.Response();
                response.State = NamedPipeMessages.CompletionState.Failure;
                response.ErrorMessage = "Failed to mount, run 'gvfs log' for details";
                this.WriteToClient(response);
                return;
            }

            if (!this.registry.TryRegisterRepo(this.request.EnlistmentRoot))
            {
                this.tracer.RelatedError(
                    "Successfully mounted repo at '{0}' but could not register it in GVFS.Service. See {1} log for more details.",
                    this.request.EnlistmentRoot,
                    GVFSConstants.Service.ServiceName);
            }

            this.WriteToClient(new NamedPipeMessages.MountRepoRequest.Response() { State = NamedPipeMessages.CompletionState.Success });
        }

        private void WriteToClient(NamedPipeMessages.MountRepoRequest.Response response)
        {
            NamedPipeMessages.Message message = response.ToMessage();
            if (!this.connection.TrySendResponse(message))
            {
                this.tracer.RelatedError("Failed to send line to client: {0}", message);
            }
        }
    }
}
