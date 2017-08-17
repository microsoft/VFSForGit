using FastFetch.Git;
using GVFS.Common;
using GVFS.Common.FileSystem;
using GVFS.Common.Tracing;
using GVFS.GVFlt.DotGit;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace GVFS.CommandLine.RepairJobs
{
    public class GitIndexRepairJob : RepairJob
    {
        private readonly string indexPath;
        private readonly string sparseCheckoutPath;
        
        public GitIndexRepairJob(ITracer tracer, TextWriter output, GVFSEnlistment enlistment)
            : base(tracer, output, enlistment)
        {
            this.indexPath = Path.Combine(this.Enlistment.DotGitRoot, GVFSConstants.DotGit.IndexName);
            this.sparseCheckoutPath = Path.Combine(this.Enlistment.WorkingDirectoryRoot, GVFSConstants.DotGit.Info.SparseCheckoutPath);
        }

        public override string Name
        {
            get { return @".git\index"; }
        }

        public override IssueType HasIssue(List<string> messages)
        {
            if (!File.Exists(this.indexPath))
            {
                messages.Add(".git\\index not found");
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

            HashSet<string> sparseCheckoutFiles = null;
            if (File.Exists(this.sparseCheckoutPath))
            {
                GVFSContext context = new GVFSContext(
                    this.Tracer,
                    new PhysicalFileSystem(),
                    repository: null,
                    enlistment: this.Enlistment);

                SparseCheckout sparseCheckout = new SparseCheckout(context, this.sparseCheckoutPath);
                sparseCheckout.LoadOrCreate();
                sparseCheckoutFiles = new HashSet<string>(sparseCheckout.Entries.Select(line => line.TrimStart('/')));
            }

            GitIndexGenerator indexGen = new GitIndexGenerator(this.Tracer, this.Enlistment, shouldHashIndex: false);
            indexGen.CreateFromHeadTree(indexVersion: 4, sparseCheckoutEntries: sparseCheckoutFiles);

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
                    messages.Add("Warning: Could not delete backed up .git\\index at: " + indexBackupPath);
                }
            }

            return FixResult.Success;
        }
    }
}
