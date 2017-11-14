using GVFS.Common;
using GVFS.Common.NamedPipes;
using GVFS.Common.Tracing;

namespace GVFS.Service.Handlers
{
    public class ExcludeFromAntiVirusHandler : MessageHandler
    {
        private NamedPipeServer.Connection connection;
        private NamedPipeMessages.ExcludeFromAntiVirusRequest request;
        private ITracer tracer;

        public ExcludeFromAntiVirusHandler(
            ITracer tracer,
            NamedPipeServer.Connection connection,
            NamedPipeMessages.ExcludeFromAntiVirusRequest request)
        {
            this.tracer = tracer;
            this.connection = connection;
            this.request = request;
        }

        public static void CheckAntiVirusExclusion(ITracer tracer, string path, out bool isExcluded, out string errorMessage)
        {
            errorMessage = string.Empty;
            if (AntiVirusExclusions.TryGetIsPathExcluded(path, out isExcluded, out errorMessage))
            {
                if (!isExcluded)
                {
                    if (AntiVirusExclusions.AddAntiVirusExclusion(path, out errorMessage))
                    {
                        if (!AntiVirusExclusions.TryGetIsPathExcluded(path, out isExcluded, out errorMessage))
                        {
                            errorMessage = string.Format("Unable to determine if this repo is excluded from antivirus after adding exclusion: {0}", errorMessage);
                            tracer.RelatedWarning(errorMessage);
                        }
                    }
                    else
                    {
                        errorMessage = string.Format("Could not add this repo to the antivirus exclusion list: {0}", errorMessage);
                        tracer.RelatedWarning(errorMessage);
                    }
                }
            }
            else
            {
                errorMessage = string.Format("Unable to determine if this repo is excluded from antivirus: {0}", errorMessage);
                tracer.RelatedWarning(errorMessage);
            }
        }

        public void Run()
        {
            string errorMessage;
            NamedPipeMessages.CompletionState state = NamedPipeMessages.CompletionState.Success;

            bool isExcluded;
            CheckAntiVirusExclusion(this.tracer, this.request.ExclusionPath, out isExcluded, out errorMessage);

            if (!isExcluded)
            {
                state = NamedPipeMessages.CompletionState.Failure;
            }

            NamedPipeMessages.ExcludeFromAntiVirusRequest.Response response = new NamedPipeMessages.ExcludeFromAntiVirusRequest.Response();

            response.State = state;
            response.ErrorMessage = errorMessage;

            this.WriteToClient(response.ToMessage(), this.connection, this.tracer);
        }
    }
}
