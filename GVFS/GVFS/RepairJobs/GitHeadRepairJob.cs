using GVFS.Common;
using GVFS.Common.Tracing;
using System.Collections.Generic;
using System.IO;

namespace GVFS.RepairJobs
{
    public class GitHeadRepairJob : GitRefsRepairJob
    {
        public GitHeadRepairJob(ITracer tracer, TextWriter output, GVFSEnlistment enlistment) 
            : base(tracer, output, enlistment)
        {
        }

        public override string Name
        {
            get { return @".git\HEAD"; }
        }

        public override FixResult TryFixIssues(List<string> messages)
        {
            FixResult result = base.TryFixIssues(messages);

            if (result == FixResult.Success)
            {
                // Read the new SHA1 commit ID in the HEAD ref
                string headRefFilePath = Path.Combine(this.Enlistment.WorkingDirectoryRoot, GVFSConstants.DotGit.Head);
                string contents = File.ReadAllText(headRefFilePath);
                string newHeadSha = contents.Trim();

                this.Tracer.RelatedEvent(
                    EventLevel.Informational,
                    "MovedHead",
                    new EventMetadata
                    {
                        { "DestinationCommit", newHeadSha }
                    });

                messages.Add("As a result of the repair, 'git status' will now complain that HEAD is detached");
                messages.Add("You can fix this by creating a branch using 'git checkout -b <branchName>'");
            }

            return result;
        }

        protected override IEnumerable<string> GetRefs()
        {
            return new[] { GVFSConstants.DotGit.HeadName };
        }
    }
}
