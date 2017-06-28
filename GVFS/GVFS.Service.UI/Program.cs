using GVFS.Common;
using GVFS.Common.NamedPipes;
using GVFS.Common.Tracing;
using Microsoft.Diagnostics.Tracing;
using System;
using System.IO;
using System.ServiceProcess;

namespace GVFS.Service.UI
{
    public class GVFSServiceUI
    {
        private readonly ITracer tracer;
        
        public GVFSServiceUI(ITracer tracer)
        {
            this.tracer = tracer;
        }
        
        public static void Main(string[] args)
        {
            using (JsonEtwTracer tracer = new JsonEtwTracer("Microsoft.Git.GVFS.Service.UI", "Service.UI"))
            {
                string logLocation = Path.Combine(
                    Environment.GetEnvironmentVariable("LocalAppData"),
                    GVFSConstants.Service.UIName,
                    "serviceUI.log");
                
                tracer.AddLogFileEventListener(logLocation, EventLevel.Informational, Keywords.Any);
                GVFSServiceUI process = new GVFSServiceUI(tracer);
                process.Start(args);
            }
        }

        private void Start(string[] args)
        {
            using (ITracer activity = this.tracer.StartActivity("Start", EventLevel.Informational))
            using (NamedPipeServer server = NamedPipeServer.StartNewServer(GVFSConstants.Service.UIName, this.tracer, this.HandleRequest))
            {
                ServiceController controller = new ServiceController(GVFSConstants.Service.ServiceName);
                try
                {
                    controller.WaitForStatus(ServiceControllerStatus.Stopped);
                }
                catch (InvalidOperationException)
                {
                    // Service might not exist anymore -- that's ok, just exit
                }

                this.tracer.RelatedInfo("{0} stop detected -- exiting UI.", GVFSConstants.Service.ServiceName);
            }
        }
        
        private void HandleRequest(ITracer tracer, string request, NamedPipeServer.Connection connection)
        {
            try
            {
                NamedPipeMessages.Message message = NamedPipeMessages.Message.FromString(request);
                switch (message.Header)
                {
                    case NamedPipeMessages.Notification.Request.Header:
                        NamedPipeMessages.Notification.Request toastRequest = NamedPipeMessages.Notification.Request.FromMessage(message);
                        if (toastRequest != null)
                        {
                            using (ITracer activity = this.tracer.StartActivity("SendToast", EventLevel.Informational))
                            {
                                ToastHelper.Toast(activity, toastRequest);
                            }
                        }

                        break;
                }
            }
            catch (Exception e)
            {
                this.tracer.RelatedError("Unhandled exception: {0}", e.ToString());
            }
        }
    }
}