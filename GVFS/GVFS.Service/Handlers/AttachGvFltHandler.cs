using GVFS.Common.FileSystem;
using GVFS.Common.NamedPipes;
using GVFS.Common.Tracing;

namespace GVFS.Service.Handlers
{
    public class AttachGvFltHandler : MessageHandler
    {
        private NamedPipeServer.Connection connection;
        private NamedPipeMessages.AttachGvFltRequest request;
        private ITracer tracer;

        public AttachGvFltHandler(
            ITracer tracer,
            NamedPipeServer.Connection connection,
            NamedPipeMessages.AttachGvFltRequest request)
        {
            this.tracer = tracer;
            this.connection = connection;
            this.request = request;
        }

        public void Run()
        {
            string errorMessage;
            NamedPipeMessages.CompletionState state = NamedPipeMessages.CompletionState.Success;
            if (!GvFltFilter.TryAttach(this.tracer, this.request.EnlistmentRoot, out errorMessage))
            {
                state = NamedPipeMessages.CompletionState.Failure;
                this.tracer.RelatedError("Unable to attach filter to volume. Enlistment root: {0} \nError: {1} ", this.request.EnlistmentRoot, errorMessage);
            }

            NamedPipeMessages.AttachGvFltRequest.Response response = new NamedPipeMessages.AttachGvFltRequest.Response();

            response.State = state;
            response.ErrorMessage = errorMessage;

            this.WriteToClient(response.ToMessage(), this.connection, this.tracer);
        }
    }
}
