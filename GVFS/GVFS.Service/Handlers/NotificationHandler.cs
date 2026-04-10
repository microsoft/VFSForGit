using GVFS.Common.NamedPipes;
using GVFS.Common.Tracing;

namespace GVFS.Service.Handlers
{
    public class NotificationHandler : INotificationHandler
    {
        public NotificationHandler(ITracer tracer)
        {
        }

        public void SendNotification(NamedPipeMessages.Notification.Request request)
        {
        }
    }
}
