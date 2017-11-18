using RGFS.Common;
using RGFS.Common.Tracing;
using System.Collections.Generic;
using System.IO;

namespace RGFS.CommandLine.RepairJobs
{
    public class RepoMetadataDatabaseRepairJob : RepairJob
    {
        public RepoMetadataDatabaseRepairJob(ITracer tracer, TextWriter output, RGFSEnlistment enlistment)
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
            if (!RepoMetadata.TryInitialize(this.Tracer, this.Enlistment.DotRGFSRoot, out error))
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
