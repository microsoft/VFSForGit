using GVFS.Common;
using GVFS.Common.Tracing;
using System.Collections.Generic;
using System.IO;

namespace GVFS.CommandLine.RepairJobs
{
    public class RepoMetadataDatabaseRepairJob : RepairJob
    {
        public RepoMetadataDatabaseRepairJob(ITracer tracer, TextWriter output, GVFSEnlistment enlistment)
            : base(tracer, output, enlistment)
        {
        }

        public override string Name
        {
            get { return "Repo Metadata Database"; }
        }
        
        public override IssueType HasIssue(List<string> messages)
        {
            string error;
            if (!RepoMetadata.TryInitialize(this.Tracer, this.Enlistment.DotGVFSRoot, out error))
            {
                messages.Add("Could not open repo metadata: " + error);
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
