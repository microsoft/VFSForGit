using GVFS.Common.NamedPipes;
using GVFS.Common.Tracing;

namespace GVFS.Service.Handlers
{
    public class RegisterRepoHandler
    {
        private NamedPipeServer.Connection connection;
        private NamedPipeMessages.RegisterRepoRequest request;
        private ITracer tracer;
        private RepoRegistry registry;

        public RegisterRepoHandler(
            ITracer tracer,
            RepoRegistry registry,
            NamedPipeServer.Connection connection,
            NamedPipeMessages.RegisterRepoRequest request)
        {
            this.tracer = tracer;
            this.registry = registry;
            this.connection = connection;
            this.request = request;
        }

        public void Run()
        {
            string errorMessage = string.Empty;
            NamedPipeMessages.RegisterRepoRequest.Response response = new NamedPipeMessages.RegisterRepoRequest.Response();

            if (this.registry.TryRegisterRepo(this.request.EnlistmentRoot, this.request.OwnerSID, out errorMessage))
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

        private void WriteToClient(NamedPipeMessages.RegisterRepoRequest.Response response)
        {
            NamedPipeMessages.Message message = response.ToMessage();
            if (!this.connection.TrySendResponse(message))
            {
                this.tracer.RelatedError("Failed to send line to client: {0}", message);
            }
        }
    }
}
