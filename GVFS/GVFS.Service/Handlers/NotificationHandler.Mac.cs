using GVFS.Common.NamedPipes;
using GVFS.Common.Tracing;
using System;
using System.IO;
using System.IO.Pipes;

namespace GVFS.Service.Handlers
{
    public class NotificationHandler
    {
        private const string NotificationServerPipeName = "vfsforgit_native_notification_server";

        // NotificationHandler uses a singleton so in the future, we can create callback actions
        // from responses sent by GVFS.Service.UI when a user clicks on a notification.
        private static NotificationHandler instance = new NotificationHandler();

        private NotificationHandler()
        {
        }

        public static NotificationHandler Instance
        {
            get { return instance; }
        }

        public void SendNotification(ITracer tracer, int sessionId, NamedPipeMessages.Notification.Request request)
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
                        tracer.RelatedError(metadata, $"MacOS notification: {request.Title} - {request.Message}.");
                    }
                }
                else
                {
                    tracer.RelatedError($"ERROR: Communication failure with native notification display tool. Notification info: {request.Title} - {request.Message}.");
                }
            }
        }
    }
}
