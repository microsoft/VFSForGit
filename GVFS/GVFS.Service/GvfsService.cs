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
        private const string ServiceNameArgPrefix = "--servicename=";
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

        public void Run()
        {
            try
            {
                this.repoRegistry = new RepoRegistry(this.tracer, this.serviceDataLocation);
                this.repoRegistry.Upgrade();
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

                if (!GVFSEnlistment.IsUnattended(tracer: null))
                {
                    if (changeDescription.Reason == SessionChangeReason.SessionLogon)
                    {
                        this.tracer.RelatedInfo("SessionLogon detected, sessionId: {0}", changeDescription.SessionId);
                        using (ITracer activity = this.tracer.StartActivity("LogonAutomount", EventLevel.Informational))
                        {
                            this.repoRegistry.AutoMountRepos(changeDescription.SessionId);
                            this.repoRegistry.TraceStatus();
                        }
                    }
                    else if (changeDescription.Reason == SessionChangeReason.SessionLogoff)
                    {
                        this.tracer.RelatedInfo("SessionLogoff detected");
                    }
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

            // TODO: 865304 Used for functional tests and development only. Replace with a smarter appConfig-based solution
            string serviceName = args.FirstOrDefault(arg => arg.StartsWith(ServiceNameArgPrefix));
            if (serviceName != null)
            {
                this.serviceName = serviceName.Substring(ServiceNameArgPrefix.Length);
            }

            this.serviceDataLocation = Paths.GetServiceDataRoot(this.serviceName);
            Directory.CreateDirectory(this.serviceDataLocation);

            this.tracer.AddLogFileEventListener(
                GVFSEnlistment.GetNewGVFSLogFileName(Paths.GetServiceLogsPath(this.serviceName), GVFSConstants.LogFileTypes.Service),
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
                    case NamedPipeMessages.RegisterRepoRequest.Header:
                        try
                        {
                            NamedPipeMessages.RegisterRepoRequest mountRequest = NamedPipeMessages.RegisterRepoRequest.FromMessage(message);
                            RegisterRepoHandler mountHandler = new RegisterRepoHandler(activity, this.repoRegistry, connection, mountRequest);
                            mountHandler.Run();
                        }
                        catch (SerializationException ex)
                        {
                            activity.RelatedError("Could not deserialize mount request: {0}", ex.Message);
                        }

                        break;

                    case NamedPipeMessages.UnregisterRepoRequest.Header:
                        try
                        {
                            NamedPipeMessages.UnregisterRepoRequest unmountRequest = NamedPipeMessages.UnregisterRepoRequest.FromMessage(message);
                            UnregisterRepoHandler unmountHandler = new UnregisterRepoHandler(activity, this.repoRegistry, connection, unmountRequest);
                            unmountHandler.Run();
                        }
                        catch (SerializationException ex)
                        {
                            activity.RelatedError("Could not deserialize unmount request: {0}", ex.Message);
                        }

                        break;

                    case NamedPipeMessages.AttachGvFltRequest.Header:
                        try
                        {
                            NamedPipeMessages.AttachGvFltRequest attachRequest = NamedPipeMessages.AttachGvFltRequest.FromMessage(message);
                            AttachGvFltHandler attachHandler = new AttachGvFltHandler(activity, connection, attachRequest);
                            attachHandler.Run();
                        }
                        catch (SerializationException ex)
                        {
                            activity.RelatedError("Could not deserialize attach volume request: {0}", ex.Message);
                        }

                        break;

                    case NamedPipeMessages.ExcludeFromAntiVirusRequest.Header:
                        try
                        {
                            NamedPipeMessages.ExcludeFromAntiVirusRequest excludeFromAntiVirusRequest = NamedPipeMessages.ExcludeFromAntiVirusRequest.FromMessage(message);
                            ExcludeFromAntiVirusHandler excludeHandler = new ExcludeFromAntiVirusHandler(activity, connection, excludeFromAntiVirusRequest);
                            excludeHandler.Run();
                        }
                        catch (SerializationException ex)
                        {
                            activity.RelatedError("Could not deserialize exclude from antivirus request: {0}", ex.Message);
                        }

                        break;

                    default:
                        EventMetadata metadata = new EventMetadata();
                        metadata.Add("Area", EtwArea);
                        metadata.Add("Header", message.Header);
                        this.tracer.RelatedWarning(metadata, "HandleNewConnection: Unknown request", Keywords.Telemetry);

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
            this.tracer.RelatedError(metadata, "Unhandled exception in " + method);
            Environment.Exit((int)ReturnCode.GenericError);
        }
    }
}