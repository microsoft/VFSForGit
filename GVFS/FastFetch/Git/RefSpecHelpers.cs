using GVFS.Common;
using GVFS.Common.Git;
using GVFS.Common.Tracing;
using Microsoft.Diagnostics.Tracing;
using System;
using System.Linq;

namespace FastFetch.Git
{
    public static class RefSpecHelpers
    {
        public const string RefsHeadsGitPath = "refs/heads/";

        public static bool UpdateRefSpec(ITracer tracer, Enlistment enlistment, string branchOrCommit, GitRefs refs)
        {
            using (ITracer activity = tracer.StartActivity("UpdateRefSpec", EventLevel.Informational))
            {
                const string OriginRefMapSettingName = "remote.origin.fetch";

                // We must update the refspec to get proper "git pull" functionality.
                string localBranch = branchOrCommit.StartsWith(RefsHeadsGitPath) ? branchOrCommit : (RefsHeadsGitPath + branchOrCommit);
                string remoteBranch = refs.GetBranchRefPairs().Single().Key;
                string refSpec = "+" + localBranch + ":" + remoteBranch;

                GitProcess git = new GitProcess(enlistment);

                // Replace all ref-specs this
                // * ensures the default refspec (remote.origin.fetch=+refs/heads/*:refs/remotes/origin/*) is removed which avoids some "git fetch/pull" failures
                // * gives added "git fetch" performance since git will only fetch the branch provided in the refspec.
                GitProcess.Result setResult = git.SetInLocalConfig(OriginRefMapSettingName, refSpec, replaceAll: true);
                if (setResult.HasErrors)
                {
                    activity.RelatedError("Could not update ref spec to {0}: {1}", refSpec, setResult.Errors);
                    return false;
                }
            }

            return true;
        }
    }
}
