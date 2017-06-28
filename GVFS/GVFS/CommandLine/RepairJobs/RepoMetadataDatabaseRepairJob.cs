using GVFS.Common;
using GVFS.Common.Physical;
using GVFS.Common.Tracing;
using Microsoft.Isam.Esent;
using System.Collections.Generic;
using System.IO;

namespace GVFS.CommandLine.RepairJobs
{
    public class RepoMetadataDatabaseRepairJob : RepairJob
    {
        private readonly string databasePath;

        public RepoMetadataDatabaseRepairJob(ITracer tracer, TextWriter output, GVFSEnlistment enlistment)
            : base(tracer, output, enlistment)
        {
            this.databasePath = Path.Combine(this.Enlistment.DotGVFSRoot, GVFSConstants.DatabaseNames.RepoMetadata);
        }

        public override string Name
        {
            get { return "Repo Metadata Database"; }
        }

        public override IssueType HasIssue(List<string> messages)
        {
            try
            {
                using (new RepoMetadata(this.Enlistment.DotGVFSRoot))
                {
                }

                return IssueType.None;
            }
            catch (EsentException corruptionEx)
            {
                messages.Add(corruptionEx.Message);
                return IssueType.CantFix;
            }
        }
        
        public override bool TryFixIssues(List<string> messages)
        {
            return false;
        }
    }
}
