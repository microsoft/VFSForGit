using RGFS.Common;
using RGFS.Common.FileSystem;
using RGFS.Common.Git;
using RGFS.Common.Tracing;
using RGFS.Service.Handlers;
using System;

namespace RGFS.Service
{
    public class RGFSMountProcess : IDisposable
    {
        private const string ParamPrefix = "--";

        private readonly ITracer tracer;

        public RGFSMountProcess(ITracer tracer, int sessionId)
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

            // Ensure the repo is excluded from antivirus before calling 'rgfs mount' 
            // to reduce chatter between RGFS.exe and RGFS.Service.exe
            string errorMessage;
            bool isExcluded;
            ExcludeFromAntiVirusHandler.CheckAntiVirusExclusion(this.tracer, repoRoot, out isExcluded, out errorMessage);

            string unusedMessage;
            if (!GvFltFilter.TryAttach(this.tracer, repoRoot, out unusedMessage))
            {
                return false;
            }

            if (!this.CallRGFSMount(repoRoot))
            {
                this.tracer.RelatedError("Unable to start the RGFS.exe process.");
                return false;
            }

            if (!RGFSEnlistment.WaitUntilMounted(repoRoot, false, out errorMessage))
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

        private bool CallRGFSMount(string repoRoot)
        {
            return this.CurrentUser.RunAs(Configuration.Instance.RGFSLocation, "mount " + repoRoot);
        }
    }
}
