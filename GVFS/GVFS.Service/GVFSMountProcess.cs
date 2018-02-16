using GVFS.Common;
using GVFS.Common.FileSystem;
using GVFS.Common.Git;
using GVFS.Common.Tracing;
using GVFS.Service.Handlers;
using System;

namespace GVFS.Service
{
    public class GVFSMountProcess : IDisposable
    {
        private const string ParamPrefix = "--";

        private readonly ITracer tracer;

        public GVFSMountProcess(ITracer tracer, int sessionId)
        {
            this.tracer = tracer;
            this.CurrentUser = new CurrentUser(this.tracer, sessionId);
        }

        public CurrentUser CurrentUser { get; private set; }

        public bool Mount(string repoRoot)
        {
            string error;
            if (!GvFltFilter.IsHealthy(out error, this.tracer))
            {
                return false;
            }

            string unusedMessage;
            if (!GvFltFilter.TryAttach(this.tracer, repoRoot, out unusedMessage))
            {
                return false;
            }

            if (!this.CallGVFSMount(repoRoot))
            {
                this.tracer.RelatedError("Unable to start the GVFS.exe process.");
                return false;
            }

            string errorMessage;
            if (!GVFSEnlistment.WaitUntilMounted(repoRoot, false, out errorMessage))
            {
                this.tracer.RelatedError(errorMessage);
                return false;
            }

            return true;
        }

        public void Dispose()
        {
            if (this.CurrentUser != null)
            {
                this.CurrentUser.Dispose();
                this.CurrentUser = null;
            }
        }

        private bool CallGVFSMount(string repoRoot)
        {
            return this.CurrentUser.RunAs(Configuration.Instance.GVFSLocation, "mount " + repoRoot);
        }
    }
}
