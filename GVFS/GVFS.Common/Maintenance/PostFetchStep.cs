using GVFS.Common.FileSystem;
using GVFS.Common.Git;
using GVFS.Common.Tracing;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace GVFS.Common.Maintenance
{
    public class PostFetchStep : GitMaintenanceStep
    {
        private const string CommitGraphChainLock = "commit-graph-chain.lock";
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
                string commitGraphLockPath = Path.Combine(this.Context.Enlistment.GitObjectsRoot, "info", "commit-graphs", CommitGraphChainLock);
                this.Context.FileSystem.TryDeleteFile(commitGraphLockPath);

                GitProcess.Result writeResult = this.RunGitCommand((process) => process.WriteCommitGraph(this.Context.Enlistment.GitObjectsRoot, this.packIndexes), nameof(GitProcess.WriteCommitGraph));

                StringBuilder sb = new StringBuilder();
                string commitGraphsDir = Path.Combine(this.Context.Enlistment.GitObjectsRoot, "info", "commit-graphs");

                foreach (DirectoryItemInfo info in this.Context.FileSystem.ItemsInDirectory(commitGraphsDir))
                {
                    sb.Append(info.Name);
                    sb.Append(";");
                }

                activity.RelatedInfo($"commit-graph list after write: {sb}");

                if (writeResult.ExitCodeIsFailure)
                {
                    this.LogErrorAndRewriteCommitGraph(activity, this.packIndexes);
                }

                GitProcess.Result verifyResult = this.RunGitCommand((process) => process.VerifyCommitGraph(this.Context.Enlistment.GitObjectsRoot), nameof(GitProcess.VerifyCommitGraph));

                if (!this.Stopping && verifyResult.ExitCodeIsFailure)
                {
                    this.LogErrorAndRewriteCommitGraph(activity, this.packIndexes);
                }
            }
        }
    }
}
