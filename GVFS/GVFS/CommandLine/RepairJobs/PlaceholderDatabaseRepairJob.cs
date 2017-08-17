using GVFS.Common;
using GVFS.Common.Tracing;
using System.Collections.Generic;
using System.IO;

namespace GVFS.CommandLine.RepairJobs
{
    public class PlaceholderDatabaseRepairJob : RepairJob
    {
        private readonly string databasePath;

        public PlaceholderDatabaseRepairJob(ITracer tracer, TextWriter output, GVFSEnlistment enlistment)
            : base(tracer, output, enlistment)
        {
            this.databasePath = Path.Combine(this.Enlistment.DotGVFSRoot, GVFSConstants.DatabaseNames.PlaceholderList);
        }

        public override string Name
        {
            get { return "Placeholder Database"; }
        }

        public override IssueType HasIssue(List<string> messages)
        {
            if (!this.TryCreatePersistentDictionary<string, string>(this.databasePath, messages))
            {
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
