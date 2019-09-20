using GVFS.Common;
using GVFS.Common.Tracing;
using GVFS.PlatformLoader;
using System;

namespace GVFS.Service.UI
{
    public static class Program
    {
        public static void Main(string[] args)
        {
            GVFSPlatformLoader.Initialize();

            using (JsonTracer tracer = new JsonTracer("Microsoft.Git.GVFS.Service.UI", "Service.UI"))
            {
                string error;
                string serviceUILogDirectory = GVFSPlatform.Instance.GetDataRootForGVFSComponent(GVFSConstants.Service.UIName);
                if (!GVFSPlatform.Instance.FileSystem.TryCreateDirectoryWithAdminAndUserModifyPermissions(serviceUILogDirectory, out error))
                {
                    EventMetadata metadata = new EventMetadata();
                    metadata.Add(nameof(serviceUILogDirectory), serviceUILogDirectory);
                    metadata.Add(nameof(error), error);
                    tracer.RelatedWarning(
                        metadata,
                        "Failed to create service UI logs directory",
                        Keywords.Telemetry);
                }
                else
                {
                    string logFilePath = GVFSEnlistment.GetNewGVFSLogFileName(
                        serviceUILogDirectory,
                        GVFSConstants.LogFileTypes.ServiceUI,
                        logId: Environment.UserName);

                    tracer.AddLogFileEventListener(logFilePath, EventLevel.Informational, Keywords.Any);
                }

                WinToastNotifier winToastNotifier = new WinToastNotifier(tracer);
                GVFSToastRequestHandler toastRequestHandler = new GVFSToastRequestHandler(winToastNotifier, tracer);
                GVFSServiceUI process = new GVFSServiceUI(tracer, toastRequestHandler);

                process.Start(args);
            }
        }
    }
}