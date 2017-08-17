using GVFS.Common;
using GVFS.Common.Tracing;
using System.Collections.Generic;
using System.IO;

namespace GVFS.CommandLine.RepairJobs
{
    public class BackgroundOperationDatabaseRepairJob : RepairJob
    {
        private readonly string databasePath;

        public BackgroundOperationDatabaseRepairJob(ITracer tracer, TextWriter output, GVFSEnlistment enlistment)
            : base(tracer, output, enlistment)
        {
            this.databasePath = Path.Combine(this.Enlistment.DotGVFSRoot, GVFSConstants.DatabaseNames.BackgroundGitUpdates);
        }

        public override string Name
        {
            get { return "Background Operation Database"; }
        }
        
        public override IssueType HasIssue(List<string> messages)
        {
            if (!this.TryCreatePersistentDictionary<long, GVFlt.GVFltCallbacks.BackgroundGitUpdate>(this.databasePath, messages))
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
