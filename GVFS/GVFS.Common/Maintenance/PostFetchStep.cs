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
            using (ITracer activity = this.Context.Tracer.StartActivity("TryWriteMultiPackIndex", EventLevel.Informational, Keywords.Telemetry, metadata: null))
            {
                string multiPackIndexLockPath = Path.Combine(this.Context.Enlistment.GitPackRoot, MultiPackIndexLock);
                this.Context.FileSystem.TryDeleteFile(multiPackIndexLockPath);

                this.RunGitCommand((process) => process.WriteMultiPackIndex(this.Context.Enlistment.GitObjectsRoot));

                GitProcess.Result verifyResult = this.RunGitCommand((process) => process.VerifyMultiPackIndex(this.Context.Enlistment.GitObjectsRoot));

                if (verifyResult.ExitCodeIsFailure)
                {
                    EventMetadata metadata = this.CreateEventMetadata();
                    metadata["MultiPackIndexVerifyOutput"] = verifyResult.Output;
                    metadata["MultiPackIndexVerifyErrors"] = verifyResult.Errors;
                    string multiPackIndexPath = Path.Combine(this.Context.Enlistment.GitPackRoot, "multi-pack-index");
                    metadata["TryDeleteFileResult"] = this.Context.FileSystem.TryDeleteFile(multiPackIndexPath);
                    activity.RelatedError(metadata, "multi-pack-index is corrupt after write. Deleting and rewriting.");

                    this.RunGitCommand((process) => process.WriteMultiPackIndex(this.Context.Enlistment.GitObjectsRoot));
                }
            }

            if (this.packIndexes == null || this.packIndexes.Count == 0)
            {
                this.Context.Tracer.RelatedInfo(this.Area + ": Skipping commit-graph write due to no new packfiles");
                return;
            }

            using (ITracer activity = this.Context.Tracer.StartActivity("TryWriteGitCommitGraph", EventLevel.Informational, Keywords.Telemetry, metadata: null))
            {
                string commitGraphLockPath = Path.Combine(this.Context.Enlistment.GitObjectsRoot, "info", CommitGraphLock);
                this.Context.FileSystem.TryDeleteFile(commitGraphLockPath);

                this.RunGitCommand((process) => process.WriteCommitGraph(this.Context.Enlistment.GitObjectsRoot, this.packIndexes));

                GitProcess.Result verifyResult = this.RunGitCommand((process) => process.VerifyCommitGraph(this.Context.Enlistment.GitObjectsRoot));

                if (verifyResult.ExitCodeIsFailure)
                {
                    EventMetadata metadata = this.CreateEventMetadata();
                    metadata["CommitGraphVerifyOutput"] = verifyResult.Output;
                    metadata["CommitGraphVerifyErrors"] = verifyResult.Errors;
                    string commitGraphPath = Path.Combine(this.Context.Enlistment.GitObjectsRoot, "info", "commit-graph");
                    metadata["TryDeleteFileResult"] = this.Context.FileSystem.TryDeleteFile(commitGraphPath);
                    activity.RelatedError(metadata, "commit-graph is corrupt after write. Deleting and rewriting.");

                    this.RunGitCommand((process) => process.WriteCommitGraph(this.Context.Enlistment.GitObjectsRoot, this.packIndexes));
                }
            }
        }
    }
}
