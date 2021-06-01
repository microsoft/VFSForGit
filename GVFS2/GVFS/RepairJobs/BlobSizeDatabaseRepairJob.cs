using GVFS.Common;
using GVFS.Common.Database;
using GVFS.Common.FileSystem;
using GVFS.Common.Tracing;
using GVFS.Virtualization.BlobSize;
using System.Collections.Generic;
using System.IO;

namespace GVFS.RepairJobs
{
    public class BlobSizeDatabaseRepairJob : RepairJob
    {
        private string blobSizeRoot;

        public BlobSizeDatabaseRepairJob(ITracer tracer, TextWriter output, GVFSEnlistment enlistment)
            : base(tracer, output, enlistment)
        {
        }

        public override string Name
        {
            get { return "Blob Size Database"; }
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

                if (!RepoMetadata.Instance.TryGetBlobSizesRoot(out this.blobSizeRoot, out error))
                {
                    messages.Add("Could not find blob sizes root in repo metadata: " + error);
                    return IssueType.CantFix;
                }
            }
            finally
            {
                RepoMetadata.Shutdown();
            }

            string blobsizesDatabasePath = Path.Combine(this.blobSizeRoot, BlobSizes.DatabaseName);
            if (SqliteDatabase.HasIssue(blobsizesDatabasePath, new PhysicalFileSystem(), out error))
            {
                messages.Add("Could not load blob size database: " + error);
                return IssueType.Fixable;
            }

            return IssueType.None;
        }

        public override FixResult TryFixIssues(List<string> messages)
        {
            if (string.IsNullOrWhiteSpace(this.blobSizeRoot))
            {
                return FixResult.Failure;
            }

            return this.TryDeleteFolder(this.blobSizeRoot) ? FixResult.Success : FixResult.Failure;
        }
    }
}