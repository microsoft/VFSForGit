using RGFS.Common;
using RGFS.Common.FileSystem;
using RGFS.Common.Tracing;
using System.Collections.Generic;
using System.IO;

namespace RGFS.CommandLine.RepairJobs
{
    public class PlaceholderDatabaseRepairJob : RepairJob
    {
        private readonly string databasePath;

        public PlaceholderDatabaseRepairJob(ITracer tracer, TextWriter output, RGFSEnlistment enlistment)
            : base(tracer, output, enlistment)
        {
            this.databasePath = Path.Combine(this.Enlistment.DotRGFSRoot, RGFSConstants.DotRGFS.Databases.PlaceholderList);
        }

        public override string Name
        {
            get { return "Placeholder Database"; }
        }

        public override IssueType HasIssue(List<string> messages)
        {
            string error;
            PlaceholderListDatabase placeholders;
            if (!PlaceholderListDatabase.TryCreate(
                this.Tracer,
                this.databasePath,
                new PhysicalFileSystem(),
                out placeholders,
                out error))
            {
                messages.Add(error);
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
