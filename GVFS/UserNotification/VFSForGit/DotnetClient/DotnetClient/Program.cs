using System;

namespace DotnetClient
{
    class Program
    {
        static void Main(string[] args)
        {
            string title = args[1];
            string message = args[2];

            NotificationHandler notificationHandler = new NotificationHandler();
            notificationHandler.SendNotification(title, message);
        }
    }
}
