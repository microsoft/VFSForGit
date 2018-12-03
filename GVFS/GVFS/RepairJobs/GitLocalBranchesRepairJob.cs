using System.Collections.Generic;
using System.IO;
using GVFS.Common;
using GVFS.Common.Tracing;

namespace GVFS.RepairJobs
{
    public class GitLocalBranchesRepairJob : GitRefsRepairJob
    {
        public GitLocalBranchesRepairJob(ITracer tracer, TextWriter output, GVFSEnlistment enlistment)
            : base(tracer, output, enlistment)
        {
        }

        public override string Name
        {
            get { return @"Local branches"; }
        }

        protected override IEnumerable<string> GetRefs()
        {
            string refsHeadsPath = Path.Combine(this.Enlistment.WorkingDirectoryRoot, GVFSConstants.DotGit.Refs.Heads.RootFolder);

            return Paths.GetFilesRecursive(refsHeadsPath);
        }
    }
}
