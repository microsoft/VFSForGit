using System;
using System.Runtime.InteropServices;

namespace DotnetClient
{
    public class NotificationHandler
    {
        public NotificationHandler()
        {
        }

        public bool SendNotification(string title, string message)
        {
            return DisplayNotification(0, title, message, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty);
        }

        [DllImport("libNativeNotification.dylib", EntryPoint = "DisplayNotification", SetLastError = true)]
        private static extern bool DisplayNotification(
            int notificationType,
            string title,
            string message,
            string defaultActionName,
            string cancelActionName,
            string defaultCommand,
            string defaultCommandArgs,
            string cancelCommand,
            string cancelCommandArgs);
    }
}
