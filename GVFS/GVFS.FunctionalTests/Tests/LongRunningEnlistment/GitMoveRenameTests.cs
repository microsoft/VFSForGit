using GVFS.FunctionalTests.FileSystemRunners;
using GVFS.FunctionalTests.Should;
using GVFS.FunctionalTests.Tools;
using GVFS.Tests.Should;
using NUnit.Framework;

namespace GVFS.FunctionalTests.Tests.LongRunningEnlistment
{
    [TestFixtureSource(typeof(FileSystemRunner), FileSystemRunner.TestRunners)]
    public class GitMoveRenameTests : TestsWithLongRunningEnlistment
    {
        private string testFileContents = "0123456789";
        private FileSystemRunner fileSystem;
        public GitMoveRenameTests(FileSystemRunner fileSystem)
        {
            this.fileSystem = fileSystem;
        }

        [TestCase, Order(1)]
        public void GitStatus()
        {
            GitHelpers.CheckGitCommand(
                this.Enlistment.RepoRoot,
                "status",
                "On branch " + Properties.Settings.Default.Commitish,
                "nothing to commit, working tree clean");
        }

        [TestCase, Order(2)]
        public void GitStatusAfterNewFile()
        {
            string filename = "new.cs";

            this.fileSystem.WriteAllText(this.Enlistment.GetVirtualPathTo(filename), this.testFileContents);

            this.Enlistment.GetVirtualPathTo(filename).ShouldBeAFile(this.fileSystem).WithContents(this.testFileContents);

            GitHelpers.CheckGitCommand(
                this.Enlistment.RepoRoot,
                "status",
                "On branch " + Properties.Settings.Default.Commitish,
                "Untracked files:",
                filename);
        }

        [TestCase, Order(3)]
        public void GitStatusAfterFileNameCaseChange()
        {
            string oldFilename = "new.cs";
            this.Enlistment.GetVirtualPathTo(oldFilename).ShouldBeAFile(this.fileSystem);

            string newFilename = "New.cs";
            this.fileSystem.MoveFile(this.Enlistment.GetVirtualPathTo(oldFilename), this.Enlistment.GetVirtualPathTo(newFilename));

            GitHelpers.CheckGitCommand(
                this.Enlistment.RepoRoot,
                "status",
                "On branch " + Properties.Settings.Default.Commitish,
                "Untracked files:",
                newFilename);
        }

        [TestCase, Order(4)]
        public void GitStatusAfterFileRename()
        {
            string oldFilename = "New.cs";
            this.Enlistment.GetVirtualPathTo(oldFilename).ShouldBeAFile(this.fileSystem);

            string newFilename = "test.cs";
            this.fileSystem.MoveFile(this.Enlistment.GetVirtualPathTo(oldFilename), this.Enlistment.GetVirtualPathTo(newFilename));

            GitHelpers.CheckGitCommand(
                this.Enlistment.RepoRoot,
                "status",
                "On branch " + Properties.Settings.Default.Commitish,
                "Untracked files:",
                newFilename);
        }

        [TestCase, Order(5)]
        public void GitStatusAndObjectAfterGitAdd()
        {
            string existingFilename = "test.cs";
            this.Enlistment.GetVirtualPathTo(existingFilename).ShouldBeAFile(this.fileSystem);

            GitHelpers.CheckGitCommand(
                this.Enlistment.RepoRoot, 
                "add " + existingFilename, 
                new string[] { });

            // Status should be correct
            GitHelpers.CheckGitCommand(
                this.Enlistment.RepoRoot,
                "status",
                "On branch " + Properties.Settings.Default.Commitish,
                "Changes to be committed:",
                existingFilename);

            // Object file for the test file should have the correct contents
            ProcessResult result = GitProcess.InvokeProcess(
                this.Enlistment.RepoRoot,
                "hash-object " + existingFilename);

            string objectHash = result.Output.Trim();
            result.Errors.ShouldBeEmpty();

            this.Enlistment.GetObjectPathTo(objectHash).ShouldBeAFile(this.fileSystem);

            GitHelpers.CheckGitCommand(
                this.Enlistment.RepoRoot,
                "cat-file -p " + objectHash,
                this.testFileContents);
        }

        [TestCase, Order(6)]
        public void GitStatusAfterUnstage()
        {
            string existingFilename = "test.cs";
            this.Enlistment.GetVirtualPathTo(existingFilename).ShouldBeAFile(this.fileSystem);

            GitHelpers.CheckGitCommand(
                this.Enlistment.RepoRoot, 
                "reset HEAD " + existingFilename, 
                new string[] { });

            GitHelpers.CheckGitCommand(
                this.Enlistment.RepoRoot,
                "status",
                "On branch " + Properties.Settings.Default.Commitish,
                "Untracked files:",
                existingFilename);
        }

        [TestCase, Order(7)]
        public void GitStatusAfterFileDelete()
        {
            string existingFilename = "test.cs";
            this.Enlistment.GetVirtualPathTo(existingFilename).ShouldBeAFile(this.fileSystem);
            this.fileSystem.DeleteFile(this.Enlistment.GetVirtualPathTo(existingFilename));
            this.Enlistment.GetVirtualPathTo(existingFilename).ShouldNotExistOnDisk(this.fileSystem);

            GitHelpers.CheckGitCommand(
                this.Enlistment.RepoRoot,
                "status",
                "On branch " + Properties.Settings.Default.Commitish,
                "nothing to commit, working tree clean");
        }
    }
}
