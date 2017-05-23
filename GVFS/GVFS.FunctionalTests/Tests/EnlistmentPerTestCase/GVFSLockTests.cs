using GVFS.FunctionalTests.FileSystemRunners;
using GVFS.FunctionalTests.Tools;
using GVFS.Tests.Should;
using NUnit.Framework;
using System.IO;

namespace GVFS.FunctionalTests.Tests.EnlistmentPerTestCase
{
    [TestFixture]
    public class GVFSLockTests : TestsWithEnlistmentPerTestCase
    {
        private FileSystemRunner fileSystem;

        public GVFSLockTests()
        {
            this.fileSystem = new SystemIORunner();
        }

         [TestCase]
        public void GitCheckoutFailsOutsideLock()
        {
            const string BackupPrefix = "BACKUP_";
            const string PreCommand = "pre-command.exe";
            const string PostCommand = "post-command.exe";

            string hooksBase = Path.Combine(this.Enlistment.RepoRoot, ".git", "hooks");

            try
            {
                // Get hooks out of the way to simulate lock not being acquired as expected
                this.fileSystem.MoveFile(Path.Combine(hooksBase, PreCommand), Path.Combine(hooksBase, BackupPrefix + PreCommand));
                this.fileSystem.MoveFile(Path.Combine(hooksBase, PostCommand), Path.Combine(hooksBase, BackupPrefix + PostCommand));

                ProcessResult result = GitHelpers.InvokeGitAgainstGVFSRepo(this.Enlistment.RepoRoot, "checkout FunctionalTests/20170510_minor");
                result.Errors.ShouldContain("fatal: unable to write new index file");

                // Ensure that branch didnt move, note however that work dir might not be clean
                GitHelpers.CheckGitCommandAgainstGVFSRepo(
                    this.Enlistment.RepoRoot,
                    "status",
                    "On branch " + Properties.Settings.Default.Commitish);
            }
            finally
            {
                // Reset hooks for cleanup.
                this.fileSystem.MoveFile(Path.Combine(hooksBase, BackupPrefix + PreCommand), Path.Combine(hooksBase, PreCommand));
                this.fileSystem.MoveFile(Path.Combine(hooksBase, BackupPrefix + PostCommand), Path.Combine(hooksBase, PostCommand));
            }
       }
    }
}
