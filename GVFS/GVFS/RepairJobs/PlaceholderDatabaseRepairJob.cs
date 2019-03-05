using GVFS.Common;
using GVFS.Common.FileBasedCollections;
using GVFS.Common.FileSystem;
using GVFS.Common.Tracing;
using System.Collections.Generic;
using System.IO;

namespace GVFS.RepairJobs
{
    public class PlaceholderDatabaseRepairJob : RepairJob
    {
        private readonly string databasePath;

        public PlaceholderDatabaseRepairJob(ITracer tracer, TextWriter output, GVFSEnlistment enlistment)
            : base(tracer, output, enlistment)
        {
            this.databasePath = Path.Combine(this.Enlistment.DotGVFSRoot, GVFSConstants.DotGVFS.Databases.PlaceholderList);
        }

        public override string Name
        {
            get { return "Placeholder Database"; }
        }

        public override IssueType HasIssue(List<string> messages)
        {
            string error;
            BinaryPlaceholderListDatabase placeholders;
            if (!BinaryPlaceholderListDatabase.TryCreate(
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
