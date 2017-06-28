using GVFS.Common.NamedPipes;
using GVFS.Common.Tracing;
using Newtonsoft.Json;

namespace GVFS.Service.Handlers
{
    public class UnmountHandler
    {
        private NamedPipeServer.Connection connection;
        private NamedPipeMessages.UnmountRepoRequest request;
        private ITracer tracer;
        private RepoRegistry registry;

        public UnmountHandler(ITracer tracer, RepoRegistry registry, NamedPipeServer.Connection connection, NamedPipeMessages.UnmountRepoRequest request)
        {
            this.tracer = tracer;
            this.registry = registry;
            this.connection = connection;
            this.request = request;
        }

        public void Run()
        {
            string errorMessage = string.Empty;
            NamedPipeMessages.UnmountRepoRequest.Response response = new NamedPipeMessages.UnmountRepoRequest.Response();

            if (this.Unmount(out errorMessage))
            {
                response.State = NamedPipeMessages.CompletionState.Success;
            }
            else
            {
                response.State = NamedPipeMessages.CompletionState.Failure;
                response.UserText = errorMessage;
            }

            this.WriteToClient(response);
        }
        
        private bool Unmount(out string errorMessage)
        {
            errorMessage = string.Empty;

            string pipeName = NamedPipeClient.GetPipeNameFromPath(this.request.EnlistmentRoot);
            string rawGetStatusResponse = string.Empty;

            try
            {
                using (NamedPipeClient pipeClient = new NamedPipeClient(pipeName))
                {
                    if (!pipeClient.Connect())
                    {
                        errorMessage = "Unable to connect to GVFS.Mount";
                        return false;
                    }

                    pipeClient.SendRequest(NamedPipeMessages.GetStatus.Request);
                    rawGetStatusResponse = pipeClient.ReadRawResponse();
                    NamedPipeMessages.GetStatus.Response getStatusResponse =
                        NamedPipeMessages.GetStatus.Response.FromJson(rawGetStatusResponse);

                    switch (getStatusResponse.MountStatus)
                    {
                        case NamedPipeMessages.GetStatus.Mounting:
                            errorMessage = "Still mounting, please try again later";
                            return false;

                        case NamedPipeMessages.GetStatus.Unmounting:
                            errorMessage = "Already unmounting, please wait";
                            return false;

                        case NamedPipeMessages.GetStatus.Ready:
                            break;

                        case NamedPipeMessages.GetStatus.MountFailed:
                            break;

                        default:
                            errorMessage = "Unrecognized response to GetStatus: " + rawGetStatusResponse;
                            return false;
                    }
                    
                    pipeClient.SendRequest(NamedPipeMessages.Unmount.Request);
                    string unmountResponse = pipeClient.ReadRawResponse();

                    switch (unmountResponse)
                    {
                        case NamedPipeMessages.Unmount.Acknowledged:
                            string finalResponse = pipeClient.ReadRawResponse();
                            if (finalResponse == NamedPipeMessages.Unmount.Completed)
                            {
                                this.registry.TryDeactivateRepo(this.request.EnlistmentRoot);

                                errorMessage = string.Empty;
                                return true;
                            }
                            else
                            {
                                errorMessage = "Unrecognized final response to unmount: " + finalResponse;
                                return false;
                            }

                        case NamedPipeMessages.Unmount.NotMounted:
                            errorMessage = "Unable to unmount, repo was not mounted";
                            return false;

                        case NamedPipeMessages.Unmount.MountFailed:
                            errorMessage = "Unable to unmount, previous mount attempt failed";
                            return false;

                        default:
                            errorMessage = "Unrecognized response to unmount: " + unmountResponse;
                            return false;
                    }
                }
            }
            catch (BrokenPipeException e)
            {
                errorMessage = "Unable to communicate with GVFS: " + e.ToString();
                return false;
            }
            catch (JsonReaderException e)
            {
                EventMetadata metadata = new EventMetadata();
                metadata.Add("Area", "GVFSService");
                metadata.Add("Exception", e.ToString());
                metadata.Add("ErrorMessage", "Unmount: failed to parse response from GVFS.Mount");
                metadata.Add("rawGetStatusResponse", rawGetStatusResponse);
                this.tracer.RelatedError(metadata);
                return false;
            }
        }

        private void WriteToClient(NamedPipeMessages.UnmountRepoRequest.Response response)
        {
            NamedPipeMessages.Message message = response.ToMessage();
            if (!this.connection.TrySendResponse(message))
            {
                this.tracer.RelatedError("Failed to send line to client: {0}", message);
            }
        }
    }
}
