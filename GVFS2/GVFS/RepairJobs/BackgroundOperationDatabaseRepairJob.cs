using GVFS.Common;
using GVFS.Common.FileSystem;
using GVFS.Common.Tracing;
using GVFS.Virtualization.Background;
using System.Collections.Generic;
using System.IO;

namespace GVFS.RepairJobs
{
    public class BackgroundOperationDatabaseRepairJob : RepairJob
    {
        private readonly string dataPath;

        public BackgroundOperationDatabaseRepairJob(ITracer tracer, TextWriter output, GVFSEnlistment enlistment)
            : base(tracer, output, enlistment)
        {
            this.dataPath = Path.Combine(this.Enlistment.DotGVFSRoot, GVFSConstants.DotGVFS.Databases.BackgroundFileSystemTasks);
        }

        public override string Name
        {
            get { return "Background Operation Database"; }
        }

        public override IssueType HasIssue(List<string> messages)
        {
            string error;
            FileSystemTaskQueue instance;
            if (!FileSystemTaskQueue.TryCreate(
                this.Tracer,
                this.dataPath,
                new PhysicalFileSystem(),
                out instance,
                out error))
            {
                messages.Add("Failed to read background operations: " + error);
                return IssueType.CantFix;
            }

            return IssueType.None;
        }

        public override FixResult TryFixIssues(List<string> messages)
        {
            return FixResult.Failure;
        }
    }
}
