using System.IO;
using GVFS.FunctionalTests.FileSystemRunners;
using GVFS.FunctionalTests.Should;
using GVFS.FunctionalTests.Tools;
using GVFS.Tests.Should;
using NUnit.Framework;

namespace GVFS.FunctionalTests.Tests.EnlistmentPerFixture
{
    // MacOnly until issue #297 (add SymLink support for Windows) is complete
    [Category(Categories.MacOnly)]
    [TestFixture]
    public class SymbolicLinkTests : TestsWithEnlistmentPerFixture
    {
        private const string TestFolderName = "Test_EPF_SymbolicLinks";

        // FunctionalTests/20180925_SymLinksPart1 files
        private const string TestFileName = "TestFile.txt";
        private const string TestFileContents = "This is a real file";
        private const string TestFile2Name = "TestFile2.txt";
        private const string TestFile2Contents = "This is the second real file";
        private const string ChildFolderName = "ChildDir";
        private const string ChildLinkName = "LinkToFileInFolder";
        private const string GrandChildLinkName = "LinkToFileInParentFolder";

        // FunctionalTests/20180925_SymLinksPart2 files
        // Note: In this branch ChildLinkName has been changed to point to TestFile2Name
        private const string GrandChildFileName = "TestFile3.txt";
        private const string GrandChildFileContents = "This is the third file";
        private const string GrandChildLinkNowAFileContents = "This was a link but is now a file";

        private BashRunner bashRunner;
        public SymbolicLinkTests()
        {
            this.bashRunner = new BashRunner();
        }

        [TestCase, Order(1)]
        public void CheckoutBranchWithSymLinks()
        {
            GitHelpers.InvokeGitAgainstGVFSRepo(this.Enlistment.RepoRoot, "checkout FunctionalTests/20180925_SymLinksPart1");
            GitHelpers.CheckGitCommandAgainstGVFSRepo(
                this.Enlistment.RepoRoot,
                "status",
                "On branch FunctionalTests/20180925_SymLinksPart1",
                "nothing to commit, working tree clean");

            string testFilePath = this.Enlistment.GetVirtualPathTo(Path.Combine(TestFolderName, TestFileName));
            testFilePath.ShouldBeAFile(this.bashRunner).WithContents(TestFileContents);
            this.bashRunner.IsSymbolicLink(testFilePath).ShouldBeFalse($"{testFilePath} should not be a symlink");

            string testFile2Path = this.Enlistment.GetVirtualPathTo(Path.Combine(TestFolderName, TestFile2Name));
            testFile2Path.ShouldBeAFile(this.bashRunner).WithContents(TestFile2Contents);
            this.bashRunner.IsSymbolicLink(testFile2Path).ShouldBeFalse($"{testFile2Path} should not be a symlink");

            string childLinkPath = this.Enlistment.GetVirtualPathTo(Path.Combine(TestFolderName, ChildLinkName));
            this.bashRunner.IsSymbolicLink(childLinkPath).ShouldBeTrue($"{childLinkPath} should be a symlink");
            childLinkPath.ShouldBeAFile(this.bashRunner).WithContents(TestFileContents);

            string grandChildLinkPath = this.Enlistment.GetVirtualPathTo(Path.Combine(TestFolderName, ChildFolderName, GrandChildLinkName));
            this.bashRunner.IsSymbolicLink(grandChildLinkPath).ShouldBeTrue($"{grandChildLinkPath} should be a symlink");
            grandChildLinkPath.ShouldBeAFile(this.bashRunner).WithContents(TestFile2Contents);
        }

        [TestCase, Order(2)]
        public void CheckoutBranchWhereSymLinksChangeContentsAndTransitionToFile()
        {
            GitHelpers.InvokeGitAgainstGVFSRepo(this.Enlistment.RepoRoot, "checkout FunctionalTests/20180925_SymLinksPart2");
            GitHelpers.CheckGitCommandAgainstGVFSRepo(
                this.Enlistment.RepoRoot,
                "status",
                "On branch FunctionalTests/20180925_SymLinksPart2",
                "nothing to commit, working tree clean");

            // testFilePath and testFile2Path are unchanged from FunctionalTests/20180925_SymLinksPart2
            string testFilePath = this.Enlistment.GetVirtualPathTo(Path.Combine(TestFolderName, TestFileName));
            testFilePath.ShouldBeAFile(this.bashRunner).WithContents(TestFileContents);
            this.bashRunner.IsSymbolicLink(testFilePath).ShouldBeFalse($"{testFilePath} should not be a symlink");

            string testFile2Path = this.Enlistment.GetVirtualPathTo(Path.Combine(TestFolderName, TestFile2Name));
            testFile2Path.ShouldBeAFile(this.bashRunner).WithContents(TestFile2Contents);
            this.bashRunner.IsSymbolicLink(testFile2Path).ShouldBeFalse($"{testFile2Path} should not be a symlink");

            // In this branch childLinkPath has been changed to point to testFile2Path
            string childLinkPath = this.Enlistment.GetVirtualPathTo(Path.Combine(TestFolderName, ChildLinkName));
            this.bashRunner.IsSymbolicLink(childLinkPath).ShouldBeTrue($"{childLinkPath} should be a symlink");
            childLinkPath.ShouldBeAFile(this.bashRunner).WithContents(TestFile2Contents);

            // grandChildLinkPath should now be a file
            string grandChildLinkPath = this.Enlistment.GetVirtualPathTo(Path.Combine(TestFolderName, ChildFolderName, GrandChildLinkName));
            this.bashRunner.IsSymbolicLink(grandChildLinkPath).ShouldBeFalse($"{grandChildLinkPath} should not be a symlink");
            grandChildLinkPath.ShouldBeAFile(this.bashRunner).WithContents(GrandChildLinkNowAFileContents);

            // There should also be a new file in the child folder
            string newGrandChildFilePath = this.Enlistment.GetVirtualPathTo(Path.Combine(TestFolderName, ChildFolderName, GrandChildFileName));
            newGrandChildFilePath.ShouldBeAFile(this.bashRunner).WithContents(GrandChildFileContents);
            this.bashRunner.IsSymbolicLink(newGrandChildFilePath).ShouldBeFalse($"{newGrandChildFilePath} should not be a symlink");
        }

        [TestCase, Order(3)]
        public void CheckoutBranchWhereFilesTransitionToSymLinks()
        {
            GitHelpers.InvokeGitAgainstGVFSRepo(this.Enlistment.RepoRoot, "checkout FunctionalTests/20180925_SymLinksPart3");
            GitHelpers.CheckGitCommandAgainstGVFSRepo(
                this.Enlistment.RepoRoot,
                "status",
                "On branch FunctionalTests/20180925_SymLinksPart3",
                "nothing to commit, working tree clean");

            // In this branch testFilePath has been changed to point to newGrandChildFilePath
            string testFilePath = this.Enlistment.GetVirtualPathTo(Path.Combine(TestFolderName, TestFileName));
            testFilePath.ShouldBeAFile(this.bashRunner).WithContents(GrandChildFileContents);
            this.bashRunner.IsSymbolicLink(testFilePath).ShouldBeTrue($"{testFilePath} should be a symlink");

            // The rest of the files are unchanged from FunctionalTests/20180925_SymLinksPart2
            string testFile2Path = this.Enlistment.GetVirtualPathTo(Path.Combine(TestFolderName, TestFile2Name));
            testFile2Path.ShouldBeAFile(this.bashRunner).WithContents(TestFile2Contents);
            this.bashRunner.IsSymbolicLink(testFile2Path).ShouldBeFalse($"{testFile2Path} should not be a symlink");

            string childLinkPath = this.Enlistment.GetVirtualPathTo(Path.Combine(TestFolderName, ChildLinkName));
            this.bashRunner.IsSymbolicLink(childLinkPath).ShouldBeTrue($"{childLinkPath} should be a symlink");
            childLinkPath.ShouldBeAFile(this.bashRunner).WithContents(TestFile2Contents);

            string grandChildLinkPath = this.Enlistment.GetVirtualPathTo(Path.Combine(TestFolderName, ChildFolderName, GrandChildLinkName));
            this.bashRunner.IsSymbolicLink(grandChildLinkPath).ShouldBeFalse($"{grandChildLinkPath} should not be a symlink");
            grandChildLinkPath.ShouldBeAFile(this.bashRunner).WithContents(GrandChildLinkNowAFileContents);

            string newGrandChildFilePath = this.Enlistment.GetVirtualPathTo(Path.Combine(TestFolderName, ChildFolderName, GrandChildFileName));
            newGrandChildFilePath.ShouldBeAFile(this.bashRunner).WithContents(GrandChildFileContents);
            this.bashRunner.IsSymbolicLink(newGrandChildFilePath).ShouldBeFalse($"{newGrandChildFilePath} should not be a symlink");
        }

        [TestCase, Order(4)]
        public void GitStatusReportsSymLinkChanges()
        {
            GitHelpers.CheckGitCommandAgainstGVFSRepo(
                this.Enlistment.RepoRoot,
                "status",
                "On branch FunctionalTests/20180925_SymLinksPart3",
                "nothing to commit, working tree clean");
            
            string testFilePath = this.Enlistment.GetVirtualPathTo(Path.Combine(TestFolderName, TestFileName));
            testFilePath.ShouldBeAFile(this.bashRunner).WithContents(GrandChildFileContents);
            this.bashRunner.IsSymbolicLink(testFilePath).ShouldBeTrue($"{testFilePath} should be a symlink");

            string testFile2Path = this.Enlistment.GetVirtualPathTo(Path.Combine(TestFolderName, TestFile2Name));
            testFile2Path.ShouldBeAFile(this.bashRunner).WithContents(TestFile2Contents);
            this.bashRunner.IsSymbolicLink(testFile2Path).ShouldBeFalse($"{testFile2Path} should not be a symlink");

            // Update testFilePath's symlink to point to testFile2Path
            this.bashRunner.CreateSymbolicLink(testFilePath, testFile2Path);

            testFilePath.ShouldBeAFile(this.bashRunner).WithContents(TestFile2Contents);
            this.bashRunner.IsSymbolicLink(testFilePath).ShouldBeTrue($"{testFilePath} should be a symlink");

            GitHelpers.CheckGitCommandAgainstGVFSRepo(
                this.Enlistment.RepoRoot,
                "status",
                "On branch FunctionalTests/20180925_SymLinksPart3",
                $"modified:   {TestFolderName}/{TestFileName}");
        }
    }
}
