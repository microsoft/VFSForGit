using GVFS.Common;
using GVFS.Common.FileSystem;
using GVFS.Common.NamedPipes;
using GVFS.Common.Tracing;
using GVFS.Service.Handlers;
using System;
using System.IO;
using System.Linq;
using System.Threading;

namespace GVFS.Service
{
    public class GVFSService
    {
        private const string ServiceNameArgPrefix = "--servicename=";
        private const string EtwArea = nameof(GVFSService);

        private JsonTracer tracer;
        private Thread serviceThread;
        private ManualResetEvent serviceStopped;
        private string serviceName;
        private RepoRegistry repoRegistry;
        private RequestHandler requestHandler;

        public GVFSService(JsonTracer tracer)
        {
            string logFilePath = Path.Combine(
                    GVFSPlatform.Instance.GetDataRootForGVFSComponent(GVFSConstants.Service.ServiceName),
                    GVFSConstants.Service.LogDirectory);
            Directory.CreateDirectory(logFilePath);

            this.tracer = tracer;
            this.tracer.AddLogFileEventListener(
                GVFSEnlistment.GetNewGVFSLogFileName(logFilePath, GVFSConstants.LogFileTypes.Service),
                EventLevel.Verbose,
                Keywords.Any);

            this.serviceName = GVFSConstants.Service.ServiceName;
            this.serviceStopped = new ManualResetEvent(false);
            this.serviceThread = new Thread(this.ServiceThreadMain);
            this.repoRegistry = new RepoRegistry(
                this.tracer,
                new PhysicalFileSystem(),
                GVFSPlatform.Instance.GetDataRootForGVFSComponent(this.serviceName));
            this.requestHandler = new RequestHandler(this.tracer, EtwArea, this.repoRegistry);
        }

        public void RunWithArgs(string[] args)
        {
            string nameArg = args.FirstOrDefault(arg => arg.StartsWith(ServiceNameArgPrefix, StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrEmpty(nameArg))
            {
                this.serviceName = nameArg.Substring(ServiceNameArgPrefix.Length);
            }

            try
            {
                string pipeName = this.serviceName + ".Pipe";
                this.tracer.RelatedInfo("Starting pipe server with name: " + pipeName);

                using (NamedPipeServer pipeServer = NamedPipeServer.StartNewServer(
                    pipeName,
                    this.tracer,
                    this.requestHandler.HandleRequest))
                {
                    this.serviceThread.Start();
                    this.serviceThread.Join();
                }
            }
            catch (Exception e)
            {
                this.LogExceptionAndExit(e, nameof(this.RunWithArgs));
            }
        }

        private void ServiceThreadMain()
        {
            try
            {
                EventMetadata metadata = new EventMetadata();
                metadata.Add("Version", ProcessHelper.GetCurrentProcessVersion());
                this.tracer.RelatedEvent(EventLevel.Informational, $"{nameof(GVFSService)}_{nameof(this.ServiceThreadMain)}", metadata);

                this.serviceStopped.WaitOne();
                this.serviceStopped.Dispose();
            }
            catch (Exception e)
            {
                this.LogExceptionAndExit(e, nameof(this.ServiceThreadMain));
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
