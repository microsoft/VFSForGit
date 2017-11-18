using RGFS.Common;
using RGFS.Common.NamedPipes;
using RGFS.Common.Tracing;
using System;
using System.Diagnostics;

namespace RGFS.Service.Handlers
{
    public class NotificationHandler
    {
        // NotificationHandler uses a singleton so in the future, we can create callback actions 
        // from responses sent by RGFS.Service.UI when a user clicks on a notification.
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
            NamedPipeClient client;
            if (!this.TryOpenConnectionToUIProcess(tracer, out client))
            {
                this.TerminateExistingProcess(tracer, RGFSConstants.Service.UIName);

                CurrentUser currentUser = new CurrentUser(tracer, sessionId);
                if (!currentUser.RunAs(
                    Configuration.Instance.RGFSServiceUILocation,
                    string.Empty))
                {
                    tracer.RelatedError("Could not start " + RGFSConstants.Service.UIName);
                    return;
                }

                this.TryOpenConnectionToUIProcess(tracer, out client);
            }

            if (client == null)
            {
                tracer.RelatedError("Failed to connect to " + RGFSConstants.Service.UIName);
                return;
            }

            try
            {
                if (!client.TrySendRequest(request.ToMessage()))
                {
                    tracer.RelatedInfo("Failed to send notification request to " + RGFSConstants.Service.UIName);
                }
            }
            finally
            {
                client.Dispose();
            }
        }

        private bool TryOpenConnectionToUIProcess(ITracer tracer, out NamedPipeClient client)
        {
            client = new NamedPipeClient(RGFSConstants.Service.UIName);
            if (client.Connect())
            {
                return true;
            }

            client.Dispose();
            client = null;
            return false;
        }

        private void TerminateExistingProcess(ITracer tracer, string processName)
        {
            try
            {
                foreach (Process process in Process.GetProcessesByName(processName))
                {
                    process.Kill();
                }
            }
            catch (Exception ex)
            {
                tracer.RelatedError("Could not find and kill existing instances of {0}: {1}", processName, ex.Message);
            }
        }
    }
}
