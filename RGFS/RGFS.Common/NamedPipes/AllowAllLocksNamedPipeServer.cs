using RGFS.Common.Tracing;

namespace RGFS.Common.NamedPipes
{
    public class AllowAllLocksNamedPipeServer
    {
        public static NamedPipeServer Create(ITracer tracer, RGFSEnlistment enlistment)
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
