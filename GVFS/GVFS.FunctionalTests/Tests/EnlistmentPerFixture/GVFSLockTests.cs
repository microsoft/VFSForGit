using GVFS.FunctionalTests.FileSystemRunners;
using GVFS.FunctionalTests.Tools;
using GVFS.Tests.Should;
using NUnit.Framework;
using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;

namespace GVFS.FunctionalTests.Tests.EnlistmentPerFixture
{
    [TestFixture]
    public class GVFSLockTests : TestsWithEnlistmentPerFixture
    {
        private FileSystemRunner fileSystem;

        public GVFSLockTests()
        {
            this.fileSystem = new SystemIORunner();
        }

        [Flags]
        private enum MoveFileFlags : uint
        {
            MoveFileReplaceExisting = 0x00000001,    // MOVEFILE_REPLACE_EXISTING
            MoveFileCopyAllowed = 0x00000002,        // MOVEFILE_COPY_ALLOWED           
            MoveFileDelayUntilReboot = 0x00000004,   // MOVEFILE_DELAY_UNTIL_REBOOT     
            MoveFileWriteThrough = 0x00000008,       // MOVEFILE_WRITE_THROUGH          
            MoveFileCreateHardlink = 0x00000010,     // MOVEFILE_CREATE_HARDLINK        
            MoveFileFailIfNotTrackable = 0x00000020, // MOVEFILE_FAIL_IF_NOT_TRACKABLE  
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

        [TestCase]
        public void LockPreventsRenameFromOutsideRootOnTopOfIndex()
        {
            this.OverwritingIndexShouldFail(Path.Combine(this.Enlistment.EnlistmentRoot, "LockPreventsRenameFromOutsideRootOnTopOfIndex.txt"));
        }

        [TestCase]
        public void LockPreventsRenameFromInsideWorkingTreeOnTopOfIndex()
        {
            this.OverwritingIndexShouldFail(this.Enlistment.GetVirtualPathTo("LockPreventsRenameFromInsideWorkingTreeOnTopOfIndex.txt"));
        }

        [TestCase]
        public void LockPreventsRenameOfIndexLockOnTopOfIndex()
        {
            this.OverwritingIndexShouldFail(this.Enlistment.GetVirtualPathTo(".git\\index.lock"));
        }

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool MoveFileEx(
            string existingFileName,
            string newFileName,
            uint flags);

        private void OverwritingIndexShouldFail(string testFilePath)
        {
            string indexPath = this.Enlistment.GetVirtualPathTo(".git\\index");

            this.Enlistment.WaitForBackgroundOperations().ShouldEqual(true, "Background operations failed to complete.");
            byte[] indexContents = File.ReadAllBytes(indexPath);

            string testFileContents = "OverwriteIndexTest";
            this.fileSystem.WriteAllText(testFilePath, testFileContents);

            this.Enlistment.WaitForBackgroundOperations().ShouldEqual(true, "Background operations failed to complete.");
            MoveFileEx(
                testFilePath, 
                indexPath, 
                (uint)(MoveFileFlags.MoveFileReplaceExisting | MoveFileFlags.MoveFileCopyAllowed)).ShouldBeFalse("GVFS should prevent renaming on top of index when GVFSLock is not held");
            byte[] newIndexContents = File.ReadAllBytes(indexPath);

            indexContents.SequenceEqual(newIndexContents).ShouldBeTrue("Index contenst should not have changed");

            this.fileSystem.DeleteFile(testFilePath);
        }
    }
}
