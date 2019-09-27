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
        private List<string> packIndexes;

        public PostFetchStep(GVFSContext context, List<string> packIndexes, bool requireObjectCacheLock = true)
            : base(context, requireObjectCacheLock)
        {
            this.packIndexes = packIndexes;
        }

        public override string Area => "PostFetchMaintenanceStep";

        protected override void PerformMaintenance()
        {
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

                if (this.Context.FileSystem.DirectoryExists(commitGraphsDir))
                {
                    foreach (DirectoryItemInfo info in this.Context.FileSystem.ItemsInDirectory(commitGraphsDir))
                    {
                        sb.Append(info.Name);
                        sb.Append(";");
                    }
                }

                activity.RelatedInfo($"commit-graph list after write: {sb}");

                if (writeResult.ExitCodeIsFailure)
                {
                    this.LogErrorAndRewriteCommitGraph(activity, this.packIndexes);
                }

                GitProcess.Result verifyResult = this.RunGitCommand((process) => process.VerifyCommitGraph(this.Context.Enlistment.GitObjectsRoot), nameof(GitProcess.VerifyCommitGraph));

                // Currently, Git does not fail when looking for the commit-graphs in the chain of
                // incremental files. This is by design, as there is a race condition otherwise.
                // However, 'git commit-graph verify' should change this behavior to fail if we
                // cannot find all commit-graph files. Until that change happens in Git, look for
                // the error message to get out of this state.
                if (!this.Stopping && (verifyResult.ExitCodeIsFailure || verifyResult.Errors.Contains("unable to find all commit-graph files")))
                {
                    this.LogErrorAndRewriteCommitGraph(activity, this.packIndexes);
                }
            }
        }
    }
}
