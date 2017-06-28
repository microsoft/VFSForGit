using GVFS.Common.NamedPipes;
using GVFS.Common.Tracing;
using GVFS.Service.UI.Data;
using System;
using System.IO;
using System.Linq;
using System.Xml;
using System.Xml.Serialization;
using Windows.UI.Notifications;
using XmlDocument = Windows.Data.Xml.Dom.XmlDocument;

namespace GVFS.Service.UI
{
    // Xml Schema: https://blogs.msdn.microsoft.com/tiles_and_toasts/2015/07/02/adaptive-and-interactive-toast-notifications-for-windows-10/
    public class ToastHelper
    {
        private const string ServiceAppId = "GVFS";

        public static void Toast(ITracer tracer, NamedPipeMessages.Notification.Request request)
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