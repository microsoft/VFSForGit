using GVFS.Common;
using GVFS.Common.Tracing;
using System;
using System.Collections.Generic;
using System.Diagnostics;
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

        public override IssueType HasIssue(List<string> messages)
        {
            if (base.HasIssue(messages) == IssueType.None)
            {
                return IssueType.None;
            }

            if (!this.CanBeRepaired(messages))
            {
                return IssueType.CantFix;
            }

            return IssueType.Fixable;
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

        private bool CanBeRepaired(List<string> messages)
        {
            Func<string, string> createErrorMessage = operation => string.Format("Can't repair HEAD while a {0} operation is in progress", operation);

            string rebasePath = Path.Combine(this.Enlistment.WorkingDirectoryRoot, GVFSConstants.DotGit.RebaseApply);
            if (Directory.Exists(rebasePath))
            {
                messages.Add(createErrorMessage("rebase"));
                return false;
            }

            string mergeHeadPath = Path.Combine(this.Enlistment.WorkingDirectoryRoot, GVFSConstants.DotGit.MergeHead);
            if (File.Exists(mergeHeadPath))
            {
                messages.Add(createErrorMessage("merge"));
                return false;
            }

            string bisectStartPath = Path.Combine(this.Enlistment.WorkingDirectoryRoot, GVFSConstants.DotGit.BisectStart);
            if (File.Exists(bisectStartPath))
            {
                messages.Add(createErrorMessage("bisect"));
                return false;
            }

            string cherrypickHeadPath = Path.Combine(this.Enlistment.WorkingDirectoryRoot, GVFSConstants.DotGit.CherryPickHead);
            if (File.Exists(cherrypickHeadPath))
            {
                messages.Add(createErrorMessage("cherry-pick"));
                return false;
            }

            string revertHeadPath = Path.Combine(this.Enlistment.WorkingDirectoryRoot, GVFSConstants.DotGit.RevertHead);
            if (File.Exists(revertHeadPath))
            {
                messages.Add(createErrorMessage("revert"));
                return false;
            }

            return true;
        }
    }
}
