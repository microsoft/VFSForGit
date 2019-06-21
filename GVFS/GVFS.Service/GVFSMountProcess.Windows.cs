using GVFS.Common;
using GVFS.Common.Tracing;
using GVFS.Platform.Windows;
using GVFS.Service.Handlers;

namespace GVFS.Service
{
    public class GVFSMountProcess : IRepoMounter
    {
        private readonly ITracer tracer;

        public GVFSMountProcess(ITracer tracer)
        {
            this.tracer = tracer;
        }

        public bool MountRepository(string repoRoot, int sessionId)
        {
            if (!ProjFSFilter.IsServiceRunning(this.tracer))
            {
                string error;
                if (!EnableAndAttachProjFSHandler.TryEnablePrjFlt(this.tracer, out error))
                {
                    this.tracer.RelatedError($"{nameof(this.MountRepository)}: Could not enable PrjFlt: {error}");
                    return false;
                }
            }

            using (CurrentUser currentUser = new CurrentUser(this.tracer, sessionId))
            {
                if (!this.CallGVFSMount(repoRoot, currentUser))
                {
                    this.tracer.RelatedError($"{nameof(this.MountRepository)}: Unable to start the GVFS.exe process.");
                    return false;
                }

                string errorMessage;
                if (!GVFSEnlistment.WaitUntilMounted(this.tracer, repoRoot, false, out errorMessage))
                {
                    this.tracer.RelatedError(errorMessage);
                    return false;
                }
            }

            return true;
        }

        private bool CallGVFSMount(string repoRoot, CurrentUser currentUser)
        {
            InternalVerbParameters mountInternal = new InternalVerbParameters(startedByService: true);
            return currentUser.RunAs(
                Configuration.Instance.GVFSLocation,
                $"mount {repoRoot} --{GVFSConstants.VerbParameters.InternalUseOnly} {mountInternal.ToJson()}");
        }
    }
}
