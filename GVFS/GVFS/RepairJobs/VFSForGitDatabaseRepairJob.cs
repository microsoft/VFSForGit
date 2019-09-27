using GVFS.Common;
using GVFS.Common.Database;
using GVFS.Common.FileSystem;
using GVFS.Common.Tracing;
using System.Collections.Generic;
using System.IO;

namespace GVFS.RepairJobs
{
    public class VFSForGitDatabaseRepairJob : RepairJob
    {
        public VFSForGitDatabaseRepairJob(ITracer tracer, TextWriter output, GVFSEnlistment enlistment)
            : base(tracer, output, enlistment)
        {
        }

        public override string Name
        {
            get { return "Placeholder Database"; }
        }

        public override IssueType HasIssue(List<string> messages)
        {
            string error;
            string databasePath = Path.Combine(this.Enlistment.DotGVFSRoot, GVFSConstants.DotGVFS.Databases.VFSForGit);
            if (SqliteDatabase.HasIssue(databasePath, new PhysicalFileSystem(), out error))
            {
                messages.Add($"Could not load {this.Name}: {error}");
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
