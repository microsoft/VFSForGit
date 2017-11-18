using RGFS.Common;
using RGFS.Common.FileSystem;
using RGFS.Common.Tracing;
using Microsoft.Isam.Esent;
using Microsoft.Isam.Esent.Collections.Generic;
using System.Collections.Generic;
using System.IO;

namespace RGFS.CommandLine.RepairJobs
{
    public class BlobSizeDatabaseRepairJob : RepairJob
    {
        private readonly string databasePath;

        public BlobSizeDatabaseRepairJob(ITracer tracer, TextWriter output, RGFSEnlistment enlistment)
            : base(tracer, output, enlistment)
        {
            this.databasePath = Path.Combine(this.Enlistment.DotRGFSRoot, RGFSConstants.DotRGFS.BlobSizesName);
        }

        public override string Name
        {
            get { return "Blob Size Database"; }
        }

        public override IssueType HasIssue(List<string> messages)
        {
            try
            {
                using (PersistentDictionary<string, long> dict = new PersistentDictionary<string, long>(this.databasePath))
                {
                }
            }
            catch (EsentException error)
            {
                messages.Add("Could not load blob size database: " + error);
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