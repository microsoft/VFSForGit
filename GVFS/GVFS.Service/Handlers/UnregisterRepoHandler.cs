using GVFS.Common.NamedPipes;
using GVFS.Common.Tracing;

namespace GVFS.Service.Handlers
{
    public class UnregisterRepoHandler
    {
        private NamedPipeServer.Connection connection;
        private NamedPipeMessages.UnregisterRepoRequest request;
        private ITracer tracer;
        private RepoRegistry registry;

        public UnregisterRepoHandler(ITracer tracer, RepoRegistry registry, NamedPipeServer.Connection connection, NamedPipeMessages.UnregisterRepoRequest request)
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
            }
            else
            {
                response.ErrorMessage = errorMessage;
                response.State = NamedPipeMessages.CompletionState.Failure;
            }

            this.WriteToClient(response);
        }
        
        private void WriteToClient(NamedPipeMessages.UnregisterRepoRequest.Response response)
        {
            NamedPipeMessages.Message message = response.ToMessage();
            if (!this.connection.TrySendResponse(message))
            {
                this.tracer.RelatedError("Failed to send line to client: {0}", message);
            }
        }
    }
}
