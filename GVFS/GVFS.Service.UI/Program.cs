using GVFS.Common;
using GVFS.Common.NamedPipes;
using GVFS.Common.Tracing;
using GVFS.PlatformLoader;
using GVFS.Service.UI.Data;
using System;
using System.IO;
using System.Linq;
using System.ServiceProcess;
using System.Xml;
using System.Xml.Serialization;
using Windows.UI.Notifications;
using XmlDocument = Windows.Data.Xml.Dom.XmlDocument;

namespace GVFS.Service.UI
{
    public class GVFSServiceUI
    {
        private const string ServiceAppId = "GVFS";

        private readonly ITracer tracer;

        public GVFSServiceUI(ITracer tracer)
        {
            this.tracer = tracer;
        }

        public static void Main(string[] args)
        {
            GVFSPlatformLoader.Initialize();

            using (JsonTracer tracer = new JsonTracer("Microsoft.Git.GVFS.Service.UI", "Service.UI"))
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
                                this.ShowToast(activity, toastRequest);
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

        private void ShowToast(ITracer tracer, NamedPipeMessages.Notification.Request request)
        {
            ToastData toastData = new ToastData();
            toastData.Visual = new VisualData();

            BindingData binding = new BindingData();
            toastData.Visual.Binding = binding;

            binding.Template = "ToastGeneric";
            binding.Items = new XmlList<BindingItem>();
            binding.Items.Add(new BindingItem.TextData(request.Title));
            binding.Items.AddRange(request.Message.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).Select(t => new BindingItem.TextData(t)));

            XmlDocument toastXml = new XmlDocument();
            using (StringWriter stringWriter = new StringWriter())
            using (XmlWriter xmlWriter = XmlWriter.Create(stringWriter, new XmlWriterSettings { OmitXmlDeclaration = true }))
            {
                XmlSerializer serializer = new XmlSerializer(toastData.GetType());
                XmlSerializerNamespaces namespaces = new XmlSerializerNamespaces();
                namespaces.Add(string.Empty, string.Empty);

                serializer.Serialize(xmlWriter, toastData, namespaces);

                toastXml.LoadXml(stringWriter.ToString());
            }

            ToastNotification toastNotification = new ToastNotification(toastXml);

            ToastNotifier toastNotifier = ToastNotificationManager.CreateToastNotifier(ServiceAppId);
            toastNotifier.Show(toastNotification);
        }
    }
}