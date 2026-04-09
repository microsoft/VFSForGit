using GVFS.Common.NamedPipes;
using GVFS.Common.Tracing;

namespace GVFS.Service.Handlers
{
    public class RegisterRepoHandler : MessageHandler
    {
        private NamedPipeServer.Connection connection;
        private NamedPipeMessages.RegisterRepoRequest request;
        private ITracer tracer;
        private IRepoRegistry registry;

        public RegisterRepoHandler(
            ITracer tracer,
            IRepoRegistry registry,
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
                this.tracer.RelatedInfo("Registered repo {0}", this.request.EnlistmentRoot);
            }
            else
            {
                response.ErrorMessage = errorMessage;
                response.State = NamedPipeMessages.CompletionState.Failure;
                this.tracer.RelatedError("Failed to register repo {0} with error: {1}", this.request.EnlistmentRoot, errorMessage);
            }

            this.WriteToClient(response.ToMessage(), this.connection, this.tracer);
        }
    }
}
