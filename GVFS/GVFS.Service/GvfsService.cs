using GVFS.Common.Tracing;
using System;
using System.ServiceProcess;
using System.Threading;

namespace GVFS.Service
{
    public class GVFSService : ServiceBase
    {
        private ITracer tracer;
        private Thread serviceThread = null;
        private bool shouldStop = false;

        public GVFSService(ITracer tracer)
        {
            this.tracer = tracer;
        }

        public void Run()
        {
            while (!this.shouldStop)
            {
                // TODO: All the things
            }
        }

        public void StopRunning()
        {
            this.shouldStop = true;
            if (this.serviceThread != null)
            {
                this.serviceThread.Join();
            }
        }

        protected override void OnStart(string[] args)
        {
            if (this.serviceThread != null)
            {
                throw new InvalidOperationException("Cannot start service twice in a row.");
            }

            this.serviceThread = new Thread(this.Run);
            this.serviceThread.Start();
        }
        
        protected override void OnStop()
        {
            this.StopRunning();
        }

        protected override void Dispose(bool disposing)
        {
            if (this.tracer != null)
            {
                this.tracer.Dispose();
                this.tracer = null;
            }

            base.Dispose(disposing);
        }
    }
}
