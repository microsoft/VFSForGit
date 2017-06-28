using GVFS.Common;
using GVFS.Common.Physical.FileSystem;
using GVFS.Common.Tracing;
using Microsoft.Isam.Esent;
using Microsoft.Isam.Esent.Collections.Generic;
using System;
using System.Collections.Generic;
using System.IO;

namespace GVFS.CommandLine.RepairJobs
{
    public class BlobSizeDatabaseRepairJob : RepairJob
    {
        private readonly string databasePath;

        public BlobSizeDatabaseRepairJob(ITracer tracer, TextWriter output, GVFSEnlistment enlistment)
            : base(tracer, output, enlistment)
        {
            this.databasePath = Path.Combine(this.Enlistment.DotGVFSRoot, GVFSConstants.DatabaseNames.BlobSizes);
        }
        
        public override string Name
        {
            get { return "Blob Size Database"; }
        }

        public override IssueType HasIssue(List<string> messages)
        {
            try
            {
                using (new PersistentDictionary<string, long>(this.databasePath))
                {
                }

                return IssueType.None;
            }
            catch (EsentException corruptionEx)
            {
                messages.Add(corruptionEx.Message);
                return IssueType.Fixable;
            }
        }
        
        public override bool TryFixIssues(List<string> messages)
        {
            try
            {
                PhysicalFileSystem.RecursiveDelete(this.databasePath);
            }
            catch (Exception e)
            {
                this.Tracer.RelatedError("Exception while deleting blob size database: " + e.ToString());
                return false;
            }

            return true;
        }
    }
}
