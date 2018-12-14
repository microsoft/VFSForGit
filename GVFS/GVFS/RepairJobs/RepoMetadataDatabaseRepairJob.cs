using GVFS.Common;
using GVFS.Common.Tracing;
using System.Collections.Generic;
using System.IO;

namespace GVFS.RepairJobs
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
            try
            {
                if (!RepoMetadata.TryInitialize(this.Tracer, this.Enlistment.DotGVFSRoot, out error))
                {
                    messages.Add("Could not open repo metadata: " + error);
                    return IssueType.CantFix;
                }
            }
            finally
            {
                RepoMetadata.Shutdown();
            }

            return IssueType.None;
        }

        public override FixResult TryFixIssues(List<string> messages)
        {
            return FixResult.Failure;
        }
    }
}
