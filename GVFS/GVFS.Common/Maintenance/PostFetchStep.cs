using GVFS.Common.Git;
using GVFS.Common.Tracing;
using System.Collections.Generic;
using System.IO;

namespace GVFS.Common.Maintenance
{
    public class PostFetchStep : GitMaintenanceStep
    {
        private const string CommitGraphLock = "commit-graph.lock";
        private const string MultiPackIndexLock = "multi-pack-index.lock";
        private List<string> packIndexes;

        public PostFetchStep(GVFSContext context, List<string> packIndexes, bool requireObjectCacheLock = true)
            : base(context, requireObjectCacheLock)
        {
            this.packIndexes = packIndexes;
        }

        public override string Area => "PostFetchMaintenanceStep";

        protected override void PerformMaintenance()
        {
            using (ITracer activity = this.Context.Tracer.StartActivity("TryWriteMultiPackIndex", EventLevel.Informational))
            {
                string multiPackIndexLockPath = Path.Combine(this.Context.Enlistment.GitPackRoot, MultiPackIndexLock);
                this.Context.FileSystem.TryDeleteFile(multiPackIndexLockPath);

                this.RunGitCommand((process) => process.WriteMultiPackIndex(this.Context.Enlistment.GitObjectsRoot), nameof(GitProcess.WriteMultiPackIndex));

                GitProcess.Result verifyResult = this.RunGitCommand((process) => process.VerifyMultiPackIndex(this.Context.Enlistment.GitObjectsRoot), nameof(GitProcess.VerifyMultiPackIndex));
                if (!this.Stopping && verifyResult.ExitCodeIsFailure)
                {
                    this.LogErrorAndRewriteMultiPackIndex(activity);
                }
            }

            if (this.packIndexes == null || this.packIndexes.Count == 0)
            {
                this.Context.Tracer.RelatedInfo(this.Area + ": Skipping commit-graph write due to no new packfiles");
                return;
            }

            using (ITracer activity = this.Context.Tracer.StartActivity("TryWriteGitCommitGraph", EventLevel.Informational))
            {
                string commitGraphLockPath = Path.Combine(this.Context.Enlistment.GitObjectsRoot, "info", CommitGraphLock);
                this.Context.FileSystem.TryDeleteFile(commitGraphLockPath);

                this.RunGitCommand((process) => process.WriteCommitGraph(this.Context.Enlistment.GitObjectsRoot, this.packIndexes), nameof(GitProcess.WriteCommitGraph));

                // Turning off Verify for commit graph due to performance issues.
                /*
                GitProcess.Result verifyResult = this.RunGitCommand((process) => process.VerifyCommitGraph(this.Context.Enlistment.GitObjectsRoot), nameof(GitProcess.VerifyCommitGraph));

                if (!this.Stopping && verifyResult.ExitCodeIsFailure)
                {
                    this.LogErrorAndRewriteCommitGraph(activity, this.packIndexes);
                }
                */
            }
        }
    }
}
