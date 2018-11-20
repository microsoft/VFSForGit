using System.Collections.Generic;
using System.IO;
using System.Linq;
using GVFS.Common;
using GVFS.Common.Tracing;

namespace GVFS.RepairJobs
{
    public class GitRefsHeadsRepairJob : GitRefsRepairJob
    {
        public GitRefsHeadsRepairJob(ITracer tracer, TextWriter output, GVFSEnlistment enlistment)
            : base(tracer, output, enlistment)
        {
        }

        public override string Name
        {
            get { return @".git\refs\heads\**\*"; }
        }

        protected override IEnumerable<string> GetRefs()
        {
            string refsHeadsPath = Path.Combine(this.Enlistment.WorkingDirectoryRoot, GVFSConstants.DotGit.Refs.Heads.RootFolder);
            string dotGitPath = Path.Combine(this.Enlistment.WorkingDirectoryRoot, GVFSConstants.DotGit.Root) + Path.DirectorySeparatorChar;

            return Directory.EnumerateFiles(refsHeadsPath, "*", SearchOption.AllDirectories)
                .Select(x => Paths.GetRelativePath(dotGitPath, x));
        }
    }
}