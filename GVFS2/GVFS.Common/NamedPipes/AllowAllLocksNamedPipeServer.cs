using GVFS.Common.Tracing;

namespace GVFS.Common.NamedPipes
{
    public class AllowAllLocksNamedPipeServer
    {
        public static NamedPipeServer Create(ITracer tracer, GVFSEnlistment enlistment)
        {
            return NamedPipeServer.StartNewServer(enlistment.NamedPipeName, tracer, AllowAllLocksNamedPipeServer.HandleRequest);
        }

        private static void HandleRequest(ITracer tracer, string request, NamedPipeServer.Connection connection)
        {
            NamedPipeMessages.Message message = NamedPipeMessages.Message.FromString(request);

            switch (message.Header)
            {
                case NamedPipeMessages.AcquireLock.AcquireRequest:
                    NamedPipeMessages.AcquireLock.Response response = new NamedPipeMessages.AcquireLock.Response(NamedPipeMessages.AcquireLock.AcceptResult);
                    connection.TrySendResponse(response.CreateMessage());
                    break;

                case NamedPipeMessages.ReleaseLock.Request:
                    connection.TrySendResponse(NamedPipeMessages.ReleaseLock.SuccessResult);
                    break;

                case NamedPipeMessages.ModifiedPaths.ListRequest:
                    string gitAttributes = GVFSConstants.SpecialGitFiles.GitAttributes + "\0";
                    NamedPipeMessages.ModifiedPaths.Response listResponse = new NamedPipeMessages.ModifiedPaths.Response(NamedPipeMessages.ModifiedPaths.SuccessResult, gitAttributes);
                    connection.TrySendResponse(listResponse.CreateMessage());
                    break;

                default:
                    connection.TrySendResponse(NamedPipeMessages.UnknownRequest);

                    if (tracer != null)
                    {
                        EventMetadata metadata = new EventMetadata();
                        metadata.Add("Area", "AllowAllLocksNamedPipeServer");
                        metadata.Add("Header", message.Header);
                        tracer.RelatedWarning(metadata, "HandleRequest: Unknown request", Keywords.Telemetry);
                    }

                    break;
            }
        }
    }
}
