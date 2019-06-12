using GVFS.Common;
using GVFS.Common.NamedPipes;
using GVFS.Common.Tracing;
using System;
using System.Linq;
using System.Threading;

namespace GVFS.Service.Linux
{
    public class GVFSService
    {
        private const string ServiceNameArgPrefix = "--servicename=";
        private const string EtwArea = nameof(GVFSService);

        private JsonTracer tracer;
        private Thread serviceThread;
        private ManualResetEvent serviceStopped;
        private string serviceName;

        public GVFSService(JsonTracer tracer)
        {
            this.tracer = tracer;
            this.serviceName = GVFSConstants.Service.ServiceName;
            this.serviceStopped = new ManualResetEvent(false);
            this.serviceThread = new Thread(this.ServiceThreadMain);
        }

        public void RunWithArgs(string[] args)
        {
            string nameArg = args.FirstOrDefault(arg => arg.StartsWith(ServiceNameArgPrefix, StringComparison.Ordinal));
            if (!string.IsNullOrEmpty(nameArg))
            {
                this.serviceName = nameArg.Substring(ServiceNameArgPrefix.Length);
            }

            try
            {
                this.serviceThread.Start();
                this.serviceThread.Join();
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
