using GVFS.Common.NamedPipes;
using GVFS.Common.Tracing;

namespace GVFS.Service.Handlers
{
    public class UnregisterRepoHandler : MessageHandler
    {
        private NamedPipeServer.Connection connection;
        private NamedPipeMessages.UnregisterRepoRequest request;
        private ITracer tracer;
        private IRepoRegistry registry;

        public UnregisterRepoHandler(
            ITracer tracer,
            IRepoRegistry registry,
            NamedPipeServer.Connection connection,
            NamedPipeMessages.UnregisterRepoRequest request)
        {
            this.tracer = tracer;
            this.registry = registry;
            this.connection = connection;
            this.request = request;
        }

        public void Run()
        {
            string errorMessage = string.Empty;
            NamedPipeMessages.UnregisterRepoRequest.Response response = new NamedPipeMessages.UnregisterRepoRequest.Response();

            if (this.registry.TryDeactivateRepo(this.request.EnlistmentRoot, out errorMessage))
            {
                response.State = NamedPipeMessages.CompletionState.Success;
                this.tracer.RelatedInfo("Deactivated repo {0}", this.request.EnlistmentRoot);
            }
            else
            {
                response.ErrorMessage = errorMessage;
                response.State = NamedPipeMessages.CompletionState.Failure;
                this.tracer.RelatedError("Failed to deactivate repo {0} with error: {1}", this.request.EnlistmentRoot, errorMessage);
            }

            this.WriteToClient(response.ToMessage(), this.connection, this.tracer);
        }
    }
}
