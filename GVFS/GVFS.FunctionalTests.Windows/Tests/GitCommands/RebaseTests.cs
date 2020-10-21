using GVFS.FunctionalTests.Properties;
using NUnit.Framework;

namespace GVFS.FunctionalTests.Tests.GitCommands
{
    [TestFixtureSource(typeof(GitRepoTests), nameof(GitRepoTests.ValidateWorkingTree))]
    [Category(Categories.GitCommands)]
    public class RebaseTests : GitRepoTests
    {
        public RebaseTests(Settings.ValidateWorkingTreeMode validateWorkingTree)
            : base(enlistmentPerTest: true, validateWorkingTree: validateWorkingTree)
        {
        }

        [TestCase]
        [Ignore("This is producing different output because git is not checking out files in the rebase.  The virtual file system changes should address this issue.")]
        public void RebaseSmallNoConflicts()
        {
            // 5d299512450f4029d7a1fe8d67e833b84247d393 is the tip of FunctionalTests/RebaseTestsSource_20170130
            string sourceCommit = "5d299512450f4029d7a1fe8d67e833b84247d393";

            // Target commit 47fabb534c35af40156db6e8365165cb04f9dd75 is part of the history of
            // FunctionalTests/20170130
            string targetCommit = "47fabb534c35af40156db6e8365165cb04f9dd75";

            this.ControlGitRepo.Fetch(sourceCommit);
            this.ControlGitRepo.Fetch(targetCommit);

            this.ValidateGitCommand("checkout {0}", sourceCommit);
            this.ValidateGitCommand("rebase {0}", targetCommit);
        }

        [TestCase]
        public void RebaseSmallOneFileConflict()
        {
            // 5d299512450f4029d7a1fe8d67e833b84247d393 is the tip of FunctionalTests/RebaseTestsSource_20170130
            string sourceCommit = "5d299512450f4029d7a1fe8d67e833b84247d393";

            // Target commit 99fc72275f950b0052c8548bbcf83a851f2b4467 is part of the history of
            // FunctionalTests/20170130
            string targetCommit = "99fc72275f950b0052c8548bbcf83a851f2b4467";

            this.ControlGitRepo.Fetch(sourceCommit);
            this.ControlGitRepo.Fetch(targetCommit);

            this.ValidateGitCommand("checkout {0}", sourceCommit);
            this.ValidateGitCommand("rebase {0}", targetCommit);
        }

        [TestCase]
        [Ignore("This is producing different output because git is not checking out files in the rebase.  The virtual file system changes should address this issue.")]
        public void RebaseEditThenDelete()
        {
            // 23a238b04497da2449fd730966c06f84b6326c3a is the tip of FunctionalTests/RebaseTestsSource_20170208
            string sourceCommit = "23a238b04497da2449fd730966c06f84b6326c3a";

            // Target commit 47fabb534c35af40156db6e8365165cb04f9dd75 is part of the history of
            // FunctionalTests/20170208
            string targetCommit = "47fabb534c35af40156db6e8365165cb04f9dd75";

            this.ControlGitRepo.Fetch(sourceCommit);
            this.ControlGitRepo.Fetch(targetCommit);

            this.ValidateGitCommand("checkout {0}", sourceCommit);
            this.ValidateGitCommand("rebase {0}", targetCommit);
        }

        [TestCase]
        public void RebaseWithDirectoryNameSameAsFile()
        {
            this.SetupForFileDirectoryTest();
            this.ValidateFileDirectoryTest("rebase");
        }

        [TestCase]
        public void RebaseWithDirectoryNameSameAsFileEnumerate()
        {
            this.RunFileDirectoryEnumerateTest("rebase");
        }

        [TestCase]
        public void RebaseWithDirectoryNameSameAsFileWithRead()
        {
            this.RunFileDirectoryReadTest("rebase");
        }

        [TestCase]
        public void RebaseWithDirectoryNameSameAsFileWithWrite()
        {
            this.RunFileDirectoryWriteTest("rebase");
        }

        [TestCase]
        public void RebaseDirectoryWithOneFile()
        {
            this.SetupForFileDirectoryTest(commandBranch: GitRepoTests.DirectoryWithDifferentFileAfterBranch);
            this.ValidateFileDirectoryTest("rebase", commandBranch: GitRepoTests.DirectoryWithDifferentFileAfterBranch);
        }

        [TestCase]
        public void RebaseDirectoryWithOneFileEnumerate()
        {
            this.RunFileDirectoryEnumerateTest("rebase", commandBranch: GitRepoTests.DirectoryWithDifferentFileAfterBranch);
        }

        [TestCase]
        public void RebaseDirectoryWithOneFileRead()
        {
            this.RunFileDirectoryReadTest("rebase", commandBranch: GitRepoTests.DirectoryWithDifferentFileAfterBranch);
        }

        [TestCase]
        public void RebaseDirectoryWithOneFileWrite()
        {
            this.RunFileDirectoryWriteTest("rebase", commandBranch: GitRepoTests.DirectoryWithDifferentFileAfterBranch);
        }
    }
}
