using GVFS.Common.Git;
using GVFS.Common.Tracing;
using System.Collections.Generic;
using System.IO;

namespace GVFS.Common.Cleanup
{
    public class PostFetchCleanupStep : GitCleanupStep
    {
        private const string CommitGraphLock = "commit-graph.lock";
        private const string MultiPackIndexLock = "multi-pack-index.lock";
        private List<string> packIndexes;

        public PostFetchCleanupStep(GVFSContext context, GitObjects gitObjects, List<string> packIndexes)
            : base(context, gitObjects)
        {
            this.packIndexes = packIndexes;
        }

        public override string TelemetryKey => "PostFetchCleanupStep";

        protected override void RunGitAction()
        {
            using (ITracer activity = this.Context.Tracer.StartActivity("TryWriteMultiPackIndex", EventLevel.Informational, Keywords.Telemetry, metadata: null))
            {
                string multiPackIndexLockPath = Path.Combine(this.Context.Enlistment.GitPackRoot, MultiPackIndexLock);
                this.Context.FileSystem.TryDeleteFile(multiPackIndexLockPath);

                this.RunGitCommand((process) => process.WriteMultiPackIndex(this.Context.Enlistment.GitObjectsRoot));
            }

            if (this.packIndexes == null || this.packIndexes.Count == 0)
            {
                this.Context.Tracer.RelatedInfo(this.TelemetryKey + ": Skipping commit-graph write due to no new packfiles");
                return;
            }

            using (ITracer activity = this.Context.Tracer.StartActivity("TryWriteGitCommitGraph", EventLevel.Informational, Keywords.Telemetry, metadata: null))
            {
                string commitGraphLockPath = Path.Combine(this.Context.Enlistment.GitObjectsRoot, "info", CommitGraphLock);
                this.Context.FileSystem.TryDeleteFile(commitGraphLockPath);

                this.RunGitCommand((process) => process.WriteCommitGraph(this.Context.Enlistment.GitObjectsRoot, this.packIndexes));
            }
        }
    }
}
