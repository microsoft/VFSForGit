using GVFS.Common.NamedPipes;
using GVFS.Common.Tracing;
using GVFS.Service.Handlers;
using System.Runtime.Serialization;

namespace GVFS.Service.Handlers
{
    public class WindowsRequestHandler : RequestHandler
    {
        public WindowsRequestHandler(
            ITracer tracer,
            string etwArea,
            RepoRegistry repoRegistry) : base(tracer, etwArea, repoRegistry)
        {
        }

        protected override void HandleMessage(
            ITracer tracer,
            NamedPipeMessages.Message message,
            NamedPipeServer.Connection connection)
        {
            if (message.Header == NamedPipeMessages.EnableAndAttachProjFSRequest.Header)
            {
                this.requestDescription = EnableProjFSRequestDescription;
                NamedPipeMessages.EnableAndAttachProjFSRequest attachRequest = NamedPipeMessages.EnableAndAttachProjFSRequest.FromMessage(message);
                EnableAndAttachProjFSHandler attachHandler = new EnableAndAttachProjFSHandler(tracer, connection, attachRequest);
                attachHandler.Run();
            }
            else
            {
                base.HandleMessage(tracer, message, connection);
            }
        }
    }
}
