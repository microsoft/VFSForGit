using RGFS.Common;
using RGFS.Common.Tracing;
using System;
using System.Diagnostics;
using System.ServiceProcess;

namespace RGFS.Service
{
    public static class Program
    {
        public static void Main(string[] args)
        {
            AppDomain.CurrentDomain.UnhandledException += UnhandledExceptionHandler;

            using (JsonEtwTracer tracer = new JsonEtwTracer(RGFSConstants.Service.ServiceName, RGFSConstants.Service.ServiceName))
            {
                using (RGFSService service = new RGFSService(tracer))
                {
                    // This will fail with a popup from a command prompt. To install as a service, run:
                    // %windir%\Microsoft.NET\Framework64\v4.0.30319\installutil RGFS.Service.exe
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
                    "Unhandled exception in RGFS.Service: " + e.ExceptionObject.ToString(),
                    EventLogEntryType.Error);
            }
        }
    }
}