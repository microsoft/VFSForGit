using RGFS.Common;
using RGFS.Common.FileSystem;
using RGFS.Common.Tracing;
using RGFS.GVFlt;
using System.Collections.Generic;
using System.IO;

namespace RGFS.CommandLine.RepairJobs
{
    public class BackgroundOperationDatabaseRepairJob : RepairJob
    {
        private readonly string dataPath;

        public BackgroundOperationDatabaseRepairJob(ITracer tracer, TextWriter output, RGFSEnlistment enlistment)
            : base(tracer, output, enlistment)
        {
            this.dataPath = Path.Combine(this.Enlistment.DotRGFSRoot, RGFSConstants.DotRGFS.Databases.BackgroundGitOperations);
        }

        public override string Name
        {
            get { return "Background Operation Database"; }
        }
        
        public override IssueType HasIssue(List<string> messages)
        {
            string error;
            BackgroundGitUpdateQueue instance;
            if (!BackgroundGitUpdateQueue.TryCreate(
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
