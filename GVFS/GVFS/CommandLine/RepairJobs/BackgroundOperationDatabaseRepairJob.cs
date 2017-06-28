using GVFS.Common;
using GVFS.Common.Tracing;
using Microsoft.Isam.Esent;
using Microsoft.Isam.Esent.Collections.Generic;
using System.Collections.Generic;
using System.IO;

namespace GVFS.CommandLine.RepairJobs
{
    public class BackgroundOperationDatabaseRepairJob : RepairJob
    {
        private readonly string databasePath;

        public BackgroundOperationDatabaseRepairJob(ITracer tracer, TextWriter output, GVFSEnlistment enlistment)
            : base(tracer, output, enlistment)
        {
            this.databasePath = Path.Combine(this.Enlistment.DotGVFSRoot, GVFSConstants.DatabaseNames.BackgroundGitUpdates);
        }

        public override string Name
        {
            get { return "Background Operation Database"; }
        }

        public override IssueType HasIssue(List<string> messages)
        {
            try
            {                
                using (new PersistentDictionary<long, GVFlt.GVFltCallbacks.BackgroundGitUpdate>(this.databasePath))
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
