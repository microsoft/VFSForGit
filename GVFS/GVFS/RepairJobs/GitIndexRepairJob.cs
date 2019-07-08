using GVFS.Common;
using GVFS.Common.Git;
using GVFS.Common.Tracing;
using System.Collections.Generic;
using System.IO;

namespace GVFS.RepairJobs
{
    public class GitIndexRepairJob : RepairJob
    {
        private readonly string indexPath;

        public GitIndexRepairJob(ITracer tracer, TextWriter output, GVFSEnlistment enlistment)
            : base(tracer, output, enlistment)
        {
            this.indexPath = Path.Combine(this.Enlistment.DotGitRoot, GVFSConstants.DotGit.IndexName);
        }

        public override string Name
        {
            get { return GVFSConstants.DotGit.Index; }
        }

        public override IssueType HasIssue(List<string> messages)
        {
            if (!File.Exists(this.indexPath))
            {
                messages.Add($"{GVFSConstants.DotGit.Index} not found");
                return IssueType.Fixable;
            }
            else
            {
                return this.TryParseIndex(this.indexPath, messages);
            }
        }

        public override FixResult TryFixIssues(List<string> messages)
        {
            string indexBackupPath = null;
            if (File.Exists(this.indexPath))
            {
                if (!this.TryRenameToBackupFile(this.indexPath, out indexBackupPath, messages))
                {
                    return FixResult.Failure;
                }
            }

            GitIndexGenerator indexGen = new GitIndexGenerator(this.Tracer, this.Enlistment, shouldHashIndex: false);
            indexGen.CreateFromHeadTree(indexVersion: 4);

            if (indexGen.HasFailures || this.TryParseIndex(this.indexPath, messages) != IssueType.None)
            {
                if (indexBackupPath != null)
                {
                    this.RestoreFromBackupFile(indexBackupPath, this.indexPath, messages);
                }

                return FixResult.Failure;
            }

            if (indexBackupPath != null)
            {
                if (!this.TryDeleteFile(indexBackupPath))
                {
                    messages.Add($"Warning: Could not delete backed up {GVFSConstants.DotGit.Index} at: " + indexBackupPath);
                }
            }

            return FixResult.Success;
        }
    }
}
