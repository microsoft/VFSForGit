using GVFS.Common;
using GVFS.Common.Git;
using GVFS.Common.Tracing;
using Microsoft.Diagnostics.Tracing;
using System;

namespace FastFetch.Jobs
{
    public class UpdateRefsHelper
    {
        private const string AreaPath = nameof(UpdateRefsHelper);
        
        private Enlistment enlistment;

        public UpdateRefsHelper(Enlistment enlistment)
        {
            this.enlistment = enlistment;
        }
        
        /// <returns>True on success, false otherwise</returns>
        public bool UpdateRef(ITracer tracer, string refName, string targetCommitish)
        {
            EventMetadata metadata = new EventMetadata();
            metadata.Add("RefName", refName);
            metadata.Add("TargetCommitish", targetCommitish);
            using (ITracer activity = tracer.StartActivity(AreaPath, EventLevel.Informational, metadata))
            {
                GitProcess gitProcess = new GitProcess(this.enlistment);
                GitProcess.Result result = null;
                if (this.IsSymbolicRef(targetCommitish))
                {
                    // Using update-ref with a branch name will leave a SHA in the ref file which detaches HEAD, so use symbolic-ref instead.
                    result = gitProcess.UpdateBranchSymbolicRef(refName, targetCommitish);
                }
                else
                {
                    result = gitProcess.UpdateBranchSha(refName, targetCommitish);
                }

                if (result.HasErrors)
                {
                    activity.RelatedError(result.Errors);
                    return false;
                }

                return true;
            }
        }

        private bool IsSymbolicRef(string targetCommitish)
        {
            return targetCommitish.StartsWith("refs/", StringComparison.OrdinalIgnoreCase);
        }
    }
}
