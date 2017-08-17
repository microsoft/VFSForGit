using GVFS.Common;
using GVFS.Common.FileSystem;
using GVFS.Common.Tracing;
using Microsoft.Diagnostics.Tracing;
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
            string warning;
            if (!GvFltFilter.IsHealthy(out error, out warning, this.tracer))
            {
                return false;
            }

            this.CheckAntiVirusExclusion(this.tracer, repoRoot);

            string unusedMessage;
            if (!GvFltFilter.TryAttach(this.tracer, repoRoot, out unusedMessage))
            {
                return false;
            }

            if (!this.CallGVFSMount(repoRoot))
            {
                this.tracer.RelatedError("Unable to start the GVFS.Mount process.");
                return false;
            }

            string errorMessage;
            if (!GVFSEnlistment.WaitUntilMounted(repoRoot, out errorMessage))
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
            return this.CurrentUser.RunAs(Configuration.Instance.GVFSMountLocation, repoRoot);
        }

        private void CheckAntiVirusExclusion(ITracer tracer, string path)
        {
            string errorMessage;
            bool isExcluded;
            if (AntiVirusExclusions.TryGetIsPathExcluded(path, out isExcluded, out errorMessage))
            {
                if (!isExcluded)
                {
                    if (!AntiVirusExclusions.AddAntiVirusExclusion(path, out errorMessage))
                    {
                        tracer.RelatedError("Could not add this repo to the antivirus exclusion list. Error: {0}", errorMessage);
                    }
                }
            }
            else
            {
                tracer.RelatedError("Unable to determine if this repo is excluded from antivirus. Error: {0}", errorMessage);
            }
        }
    }
}
