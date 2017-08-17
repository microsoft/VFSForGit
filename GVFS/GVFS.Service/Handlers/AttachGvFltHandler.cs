using GVFS.Common.FileSystem;
using GVFS.Common.NamedPipes;
using GVFS.Common.Tracing;

namespace GVFS.Service.Handlers
{
    public class AttachGvFltHandler
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

            this.WriteToClient(new NamedPipeMessages.AttachGvFltRequest.Response() { State = state, ErrorMessage = errorMessage });
        }

        private void WriteToClient(NamedPipeMessages.AttachGvFltRequest.Response response)
        {
            NamedPipeMessages.Message message = response.ToMessage();
            if (!this.connection.TrySendResponse(message))
            {
                this.tracer.RelatedError("Failed to send line to client: {0}", message);
            }
        }
    }
}
