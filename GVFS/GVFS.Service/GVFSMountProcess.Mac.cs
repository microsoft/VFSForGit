using GVFS.Common;
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
        }

        public bool Mount(string repoRoot)
        {
            if (!this.CallGVFSMount(repoRoot))
            {
                this.tracer.RelatedError($"{nameof(this.Mount)}: Unable to start the GVFS process.");
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
        }

        public string GetUserId()
        {
            return GVFSPlatform.Instance.GetCurrentUser();
        }

        private bool CallGVFSMount(string repoRoot)
        {
            InternalVerbParameters mountInternal = new InternalVerbParameters(startedByService: true);
            throw new NotImplementedException();
        }
    }
}
