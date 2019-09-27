using GVFS.Common.NamedPipes;

namespace GVFS.Service.Handlers
{
    public interface INotificationHandler
    {
        void SendNotification(NamedPipeMessages.Notification.Request request);
    }
}
