using GVFS.Common.NamedPipes;
using GVFS.Common.Tracing;
using System;

namespace GVFS.Service.Handlers
{
    public class NotificationHandler
    {
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
            // Log the notification till platform specific notifications become available.
            tracer.RelatedInfo($"MacOS notification: {request.Title} - {request.Message}.");
        }
    }
}
