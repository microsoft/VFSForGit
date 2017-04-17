namespace GVFS.Common.NamedPipes
{
    public class AllowAllLocksNamedPipeServer
    {
        public static NamedPipeServer Create(GVFSEnlistment enlistment)
        {
            return NamedPipeServer.StartNewServer(enlistment.NamedPipeName, AllowAllLocksNamedPipeServer.HandleRequest);
        }

        private static void HandleRequest(string request, NamedPipeServer.Connection connection)
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
                    break;
            }
        }
    }
}
