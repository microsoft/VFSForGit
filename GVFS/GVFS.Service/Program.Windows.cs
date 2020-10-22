using GVFS.Common;
using GVFS.Common.Tracing;
using GVFS.Platform.Windows;
using System;
using System.Diagnostics;
using System.ServiceProcess;

namespace GVFS.Service
{
    public static class Program
    {
        public static void Main(string[] args)
        {
            GVFSPlatform.Register(new WindowsPlatform());

            AppDomain.CurrentDomain.UnhandledException += UnhandledExceptionHandler;

            using (JsonTracer tracer = new JsonTracer(GVFSConstants.Service.ServiceName, GVFSConstants.Service.ServiceName))
            {
                using (GVFSService service = new GVFSService(tracer))
                {
                    // This will fail with a popup from a command prompt. To install as a service, run:
                    // %windir%\Microsoft.NET\Framework64\v4.0.30319\installutil GVFS.Service.exe
                    ServiceBase.Run(service);
                }
            }
        }

        private static void UnhandledExceptionHandler(object sender, UnhandledExceptionEventArgs e)
        {
            using (EventLog eventLog = new EventLog("Application"))
            {
                eventLog.Source = "Application";
                eventLog.WriteEntry(
                    "Unhandled exception in GVFS.Service: " + e.ExceptionObject.ToString(),
                    EventLogEntryType.Error);
            }
        }
    }
}