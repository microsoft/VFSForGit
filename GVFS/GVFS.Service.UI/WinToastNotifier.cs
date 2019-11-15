using GVFS.Common;
using GVFS.Common.Tracing;
using GVFS.Service.UI.Data;
using System;
using System.IO;
using System.Xml;
using System.Xml.Serialization;
using Windows.UI.Notifications;
using XmlDocument = Windows.Data.Xml.Dom.XmlDocument;

namespace GVFS.Service.UI
{
    public class WinToastNotifier : IToastNotifier
    {
        private const string ServiceAppId = "GVFS";
        private const string GVFSIconName = "GitVirtualFileSystem.ico";
        private ITracer tracer;

        public WinToastNotifier(ITracer tracer)
        {
            this.tracer = tracer;
        }

        public Action<string> UserResponseCallback { get; set; }

        public void Notify(string title, string message, string actionButtonTitle, string callbackArgs)
        {
            // Reference: https://docs.microsoft.com/en-us/windows/uwp/design/shell/tiles-and-notifications/adaptive-interactive-toasts
            ToastData toastData = new ToastData();

            toastData.Visual = new VisualData();

            BindingData binding = new BindingData();
            toastData.Visual.Binding = binding;

            // ToastGeneric- Our toast contains VFSForGit icon and text
            binding.Template = "ToastGeneric";
            binding.Items = new XmlList<BindingItem>();
            binding.Items.Add(new BindingItem.TextData(title));
            binding.Items.Add(new BindingItem.TextData(message));

            string logo = "file:///" + Path.Combine(ProcessHelper.GetCurrentProcessLocation(), GVFSIconName);
            binding.Items.Add(new BindingItem.ImageData()
            {
                Source = logo,
                Placement = "appLogoOverride",
                HintCrop = "circle"
            });

            if (!string.IsNullOrEmpty(actionButtonTitle))
            {
                ActionsData actionsData = new ActionsData();
                actionsData.Actions = new XmlList<ActionItem>();
                actionsData.Actions.Add(new ActionItem()
                {
                    Content = actionButtonTitle,
                    Arguments = string.IsNullOrEmpty(callbackArgs) ? string.Empty : callbackArgs,
                    ActivationType = "background"
                });

                toastData.Actions = actionsData;
            }

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
            toastNotification.Activated += this.ToastActivated;
            toastNotification.Dismissed += this.ToastDismissed;
            toastNotification.Failed += this.ToastFailed;

            ToastNotifier toastNotifier = ToastNotificationManager.CreateToastNotifier(ServiceAppId);
            toastNotifier.Show(toastNotification);
        }

        private void ToastActivated(ToastNotification sender, object e)
        {
            ToastActivatedEventArgs args = (ToastActivatedEventArgs)e;

            this.UserResponseCallback?.Invoke(args.Arguments);
        }

        private void ToastDismissed(ToastNotification sender, ToastDismissedEventArgs e)
        {
            this.tracer.RelatedInfo($"{nameof(this.ToastDismissed)}: {e.Reason}");
        }

        private void ToastFailed(ToastNotification sender, ToastFailedEventArgs e)
        {
            this.tracer.RelatedInfo($"{nameof(this.ToastFailed)}: {e.ErrorCode.ToString()}");
        }
    }
}
