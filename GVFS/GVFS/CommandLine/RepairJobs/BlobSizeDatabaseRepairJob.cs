using GVFS.Common;
using GVFS.Common.Tracing;
using System.Collections.Generic;
using System.IO;

namespace GVFS.CommandLine.RepairJobs
{
    public class BlobSizeDatabaseRepairJob : RepairJob
    {
        private readonly string databasePath;

        public BlobSizeDatabaseRepairJob(ITracer tracer, TextWriter output, GVFSEnlistment enlistment)
            : base(tracer, output, enlistment)
        {
            this.databasePath = Path.Combine(this.Enlistment.DotGVFSRoot, GVFSConstants.DatabaseNames.BlobSizes);
        }

        public override string Name
        {
            get { return "Blob Size Database"; }
        }

        public override IssueType HasIssue(List<string> messages)
        {
            if (!this.TryCreatePersistentDictionary<string, long>(this.databasePath, messages))
            {
                return IssueType.Fixable;
            }

            return IssueType.None;
        }

        public override FixResult TryFixIssues(List<string> messages)
        {
            return this.TryDeleteFolder(this.databasePath) ? FixResult.Success : FixResult.Failure;
        }
    }
}