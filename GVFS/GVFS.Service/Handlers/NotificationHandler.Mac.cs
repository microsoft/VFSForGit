using GVFS.Common.NamedPipes;
using GVFS.Common.Tracing;
using System;
using System.IO;

namespace GVFS.Service.Handlers
{
    public class NotificationHandler : INotificationHandler
    {
        private const string NotificationServerPipeName = "vfsforgit.notification";
        private ITracer tracer;

        public NotificationHandler(ITracer tracer)
        {
            this.tracer = tracer;
        }

        public void SendNotification(NamedPipeMessages.Notification.Request request)
        {
            string pipeName = Path.Combine(Path.GetTempPath(), NotificationServerPipeName);
            using (NamedPipeClient client = new NamedPipeClient(pipeName))
            {
                if (client.Connect())
                {
                    try
                    {
                        client.SendRequest(request.ToMessage());
                    }
                    catch (Exception ex)
                    {
                        EventMetadata metadata = new EventMetadata();
                        metadata.Add("Area", nameof(NotificationHandler));
                        metadata.Add("Exception", ex.ToString());
                        metadata.Add(TracingConstants.MessageKey.ErrorMessage, "MacOS notification display error");
                        this.tracer.RelatedError(metadata, $"MacOS notification: {request.Title} - {request.Message}.");
                    }
                }
                else
                {
                    this.tracer.RelatedError($"ERROR: Communication failure with native notification display tool. Notification info: {request.Title} - {request.Message}.");
                }
            }
        }
    }
}
