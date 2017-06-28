using GVFS.Common;
using GVFS.Common.NamedPipes;
using GVFS.Common.Tracing;
using GVFS.Service.Handlers;
using Microsoft.Diagnostics.Tracing;
using System;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.ServiceProcess;
using System.Threading;

namespace GVFS.Service
{
    public class GVFSService : ServiceBase
    {
        public const string ServiceNameArgPrefix = "--servicename=";
        private const string EtwArea = nameof(GVFSService);

        private JsonEtwTracer tracer;
        private Thread serviceThread;
        private ManualResetEvent serviceStopped;
        private string serviceName;
        private string serviceDataLocation;
        private RepoRegistry repoRegistry;
        
        public GVFSService(JsonEtwTracer tracer)
        {
            this.tracer = tracer;
            this.serviceName = GVFSConstants.Service.ServiceName;
            this.CanHandleSessionChangeEvent = true;
        }

        public static string GetServiceDataRoot(string serviceName)
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData, Environment.SpecialFolderOption.Create),
                "GVFS",
                serviceName);
        }

        public static string GetServiceLogsRoot(string serviceName)
        {
            return Path.Combine(GetServiceDataRoot(serviceName), "Logs");
        }

        public void Run()
        {
            try
            {
                this.repoRegistry = new RepoRegistry(this.tracer, this.serviceDataLocation);
                using (ITracer activity = this.tracer.StartActivity("StartUp", EventLevel.Informational))
                {
                    this.repoRegistry.AutoMountRepos();
                    this.repoRegistry.TraceStatus();
                }

                string pipeName = this.serviceName + ".Pipe";
                this.tracer.RelatedInfo("Starting pipe server with name: " + pipeName);
                using (NamedPipeServer pipeServer = NamedPipeServer.StartNewServer(pipeName, this.tracer, this.HandleRequest))
                {
                    this.serviceStopped.WaitOne();
                }
            }
            catch (Exception e)
            {
                this.LogExceptionAndExit(e, nameof(this.Run));
            }
        }

        public void StopRunning()
        {
            if (this.serviceStopped == null)
            {
                return;
            }

            try
            {
                if (this.tracer != null)
                {
                    this.tracer.RelatedInfo("Stopping");
                }

                if (this.serviceStopped != null)
                {
                    this.serviceStopped.Set();
                }

                if (this.serviceThread != null)
                {
                    this.serviceThread.Join();
                    this.serviceThread = null;

                    if (this.serviceStopped != null)
                    {
                        this.serviceStopped.Dispose();
                        this.serviceStopped = null;
                    }
                }
            }
            catch (Exception e)
            {
                this.LogExceptionAndExit(e, nameof(this.StopRunning));
            }
        }

        protected override void OnSessionChange(SessionChangeDescription changeDescription)
        {
            try
            {
                base.OnSessionChange(changeDescription);

                if (changeDescription.Reason == SessionChangeReason.SessionLogon)
                {
                    this.tracer.RelatedInfo("Logon detected, starting service.");
                    this.StopRunning();
                    this.Start();
                }
                else if (changeDescription.Reason == SessionChangeReason.SessionLogoff)
                {
                    this.tracer.RelatedInfo("Logoff detected, stopping service.");
                    this.StopRunning();
                }
            }
            catch (Exception e)
            {
                this.LogExceptionAndExit(e, nameof(this.OnSessionChange));
            }
        }

        protected override void OnStart(string[] args)
        {
            if (this.serviceThread != null)
            {
                throw new InvalidOperationException("Cannot start service twice in a row.");
            }

            // TODO: 865304 Used for functional tests only. Replace with a smarter appConfig-based solution
            string serviceName = args.FirstOrDefault(arg => arg.StartsWith(ServiceNameArgPrefix));
            if (serviceName != null)
            {
                this.serviceName = serviceName.Substring(ServiceNameArgPrefix.Length);
            }

            this.serviceDataLocation = GVFSService.GetServiceDataRoot(this.serviceName);
            Directory.CreateDirectory(this.serviceDataLocation);

            this.tracer.AddLogFileEventListener(
                GVFSEnlistment.GetNewGVFSLogFileName(GVFSService.GetServiceLogsRoot(this.serviceName), GVFSConstants.LogFileTypes.Service),
                EventLevel.Verbose,
                Keywords.Any);

            try
            {
                this.Start();
            }
            catch (Exception e)
            {
                this.LogExceptionAndExit(e, nameof(this.OnStart));
            }
        }

        protected override void OnStop()
        {
            try
            {
                this.StopRunning();
            }
            catch (Exception e)
            {
                this.LogExceptionAndExit(e, nameof(this.OnStart));
            }
        }

        protected override void Dispose(bool disposing)
        {
            this.StopRunning();

            if (this.tracer != null)
            {
                this.tracer.Dispose();
                this.tracer = null;
            }

            base.Dispose(disposing);
        }

        private void Start()
        {
            if (this.serviceStopped != null)
            {
                return;
            }

            this.serviceStopped = new ManualResetEvent(false);
            this.serviceThread = new Thread(this.Run);

            this.serviceThread.Start();
        }

        private void HandleRequest(ITracer tracer, string request, NamedPipeServer.Connection connection)
        {
            NamedPipeMessages.Message message = NamedPipeMessages.Message.FromString(request);
            if (string.IsNullOrWhiteSpace(message.Header))
            {
                return;
            }

            using (ITracer activity = this.tracer.StartActivity(message.Header, EventLevel.Informational, new EventMetadata { { "request", request } }))
            {
                switch (message.Header)
                {
                    case NamedPipeMessages.MountRepoRequest.Header:
                        try
                        {
                            NamedPipeMessages.MountRepoRequest mountRequest = NamedPipeMessages.MountRepoRequest.FromMessage(message);
                            MountHandler mountHandler = new MountHandler(activity, this.repoRegistry, connection, mountRequest);
                            mountHandler.Run();
                        }
                        catch (SerializationException ex)
                        {
                            activity.RelatedError("Could not deserialize mount request: {0}", ex.Message);
                        }

                        break;

                    case NamedPipeMessages.UnmountRepoRequest.Header:
                        try
                        {
                            NamedPipeMessages.UnmountRepoRequest unmountRequest = NamedPipeMessages.UnmountRepoRequest.FromMessage(message);
                            UnmountHandler unmountHandler = new UnmountHandler(activity, this.repoRegistry, connection, unmountRequest);
                            unmountHandler.Run();
                        }
                        catch (SerializationException ex)
                        {
                            activity.RelatedError("Could not deserialize unmount request: {0}", ex.Message);
                        }

                        break;

                    case NamedPipeMessages.Notification.Request.Header:
                        try
                        {
                            NamedPipeMessages.Notification.Request notificationRequest = NamedPipeMessages.Notification.Request.FromMessage(message);
                            NotificationHandler.Instance.SendNotification(activity, notificationRequest);
                        }
                        catch (SerializationException ex)
                        {
                            activity.RelatedError("Could not deserialize notification request: {0}", ex.Message);
                        }

                        break;

                    default:
                        EventMetadata metadata = new EventMetadata();
                        metadata.Add("Area", EtwArea);
                        metadata.Add("Header", message.Header);
                        metadata.Add("ErrorMessage", "HandleNewConnection: Unknown request");
                        this.tracer.RelatedError(metadata);

                        connection.TrySendResponse(NamedPipeMessages.UnknownRequest);
                        break;
                }
            }
        }

        private void LogExceptionAndExit(Exception e, string method)
        {
            EventMetadata metadata = new EventMetadata();
            metadata.Add("Area", EtwArea);
            metadata.Add("Exception", e.ToString());
            metadata.Add("ErrorMessage", "Unhandled exception in " + method);
            this.tracer.RelatedError(metadata);
            Environment.Exit((int)ReturnCode.GenericError);
        }
    }
}