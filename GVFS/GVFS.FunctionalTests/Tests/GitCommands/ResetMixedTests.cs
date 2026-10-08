using GVFS.FunctionalTests.Properties;
using GVFS.FunctionalTests.Should;
using GVFS.FunctionalTests.Tools;
using GVFS.Tests.Should;
using NUnit.Framework;
using System.IO;

namespace GVFS.FunctionalTests.Tests.GitCommands
{
    [TestFixtureSource(typeof(GitRepoTests), nameof(GitRepoTests.ValidateWorkingTree))]
    [Category(Categories.GitCommands)]
    public class ResetMixedTests : GitRepoTests
    {
        public ResetMixedTests(Settings.ValidateWorkingTreeMode validateWorkingTree)
            : base(enlistmentPerTest: true, validateWorkingTree: validateWorkingTree)
        {
        }

        [TestCase]
        public void ResetMixed()
        {
            this.ValidateGitCommand("checkout " + GitRepoTests.ConflictTargetBranch);
            this.ValidateGitCommand("reset --mixed HEAD~1");
            this.FilesShouldMatchCheckoutOfTargetBranch();
        }

        [TestCase]
        public void ResetMixedAfterPrefetch()
        {
            this.ValidateGitCommand("checkout " + GitRepoTests.ConflictTargetBranch);
            this.Enlistment.Prefetch("--files * --hydrate");
            this.ValidateGitCommand("reset --mixed HEAD~1");
            this.FilesShouldMatchCheckoutOfTargetBranch();
        }

        /// <summary>
        /// A mixed reset must clear skip-worktree on a hydrated placeholder whose
        /// index entry the reset changes. A hydrated placeholder is on disk but is
        /// not in ModifiedPaths, so it still has skip-worktree. The reset does not
        /// update the working tree, so the file on disk no longer matches the index.
        /// If skip-worktree stays set, git does not compare the file to the index,
        /// and reset and status do not report it as modified.
        ///
        /// Placeholders that are not on disk do not have this problem, because git
        /// writes them to disk and clears skip-worktree. For that reason, this test
        /// asserts each precondition before the reset.
        ///
        /// The git side of this behavior requires microsoft/git v2.55.0.vfs.0.3 or later.
        /// </summary>
        [TestCase]
        public void ResetMixedClearsSkipWorktreeOnHydratedPlaceholder()
        {
            string filePath = Path.Combine("Test_ConflictTests", "ModifiedFiles", "ChangeInTarget.txt");
            string gitPath = filePath.Replace(Path.DirectorySeparatorChar, TestConstants.GitPathSeparator);

            // Create local branches for both commits in both repos.
            this.ValidateGitCommand("checkout " + GitRepoTests.ConflictSourceBranch);
            this.ValidateGitCommand("checkout " + GitRepoTests.ConflictTargetBranch);

            // Precondition: the reset changes the index entry for the file.
            GitProcess.InvokeProcess(
                this.ControlGitRepo.RootPath,
                $"diff --quiet {GitRepoTests.ConflictSourceBranch} {GitRepoTests.ConflictTargetBranch} -- {gitPath}")
                .ExitCode.ShouldEqual(1, $"{gitPath} must differ between the reset source and target");

            // Precondition: the file is a hydrated placeholder. A read hydrates it
            // but does not add it to ModifiedPaths, so skip-worktree stays set.
            this.Enlistment.GetVirtualPathTo(filePath).ShouldBeAFile(this.FileSystem).WithContents();
            GVFSHelpers.ModifiedPathsShouldNotContain(this.Enlistment, this.FileSystem, gitPath);
            this.SkipWorktreeFlagShouldBe(gitPath, expectedFlag: 'S');

            // The reset output and status must report the file as modified, as in the control repo.
            this.ValidateGitCommand("reset --mixed " + GitRepoTests.ConflictSourceBranch);

            // After the reset, skip-worktree is cleared, and GVFS adds the file to
            // ModifiedPaths so that later git commands also compare it to the index.
            this.SkipWorktreeFlagShouldBe(gitPath, expectedFlag: 'H');
            GVFSHelpers.ModifiedPathsShouldContain(this.Enlistment, this.FileSystem, gitPath);
            this.FileContentsShouldMatch(filePath);
        }

        [TestCase]
        public void ResetMixedAndCheckoutNewBranch()
        {
            this.ValidateGitCommand("checkout " + GitRepoTests.ConflictTargetBranch);
            this.ValidateGitCommand("reset --mixed HEAD~1");

            // Use RunGitCommand rather than ValidateGitCommand as G4W optimizations for "checkout -b" mean that the
            // command will not report modified and deleted files
            this.RunGitCommand("checkout -b tests/functional/ResetMixedAndCheckoutNewBranch");
            this.FilesShouldMatchCheckoutOfTargetBranch();
            this.ValidateGitCommand("status");
        }

        [TestCase]
        public void ResetMixedAndCheckoutOrphanBranch()
        {
            this.ValidateGitCommand("checkout " + GitRepoTests.ConflictTargetBranch);
            this.ValidateGitCommand("reset --mixed HEAD~1");
            this.ValidateGitCommand("checkout --orphan tests/functional/ResetMixedAndCheckoutOrphanBranch");
            this.FilesShouldMatchCheckoutOfTargetBranch();
        }

        [TestCase]
        public void ResetMixedAndRemount()
        {
            this.ValidateGitCommand("checkout " + GitRepoTests.ConflictTargetBranch);
            this.ValidateGitCommand("reset --mixed HEAD~1");
            this.FilesShouldMatchCheckoutOfTargetBranch();

            this.Enlistment.UnmountGVFS();
            this.Enlistment.MountGVFS();
            this.ValidateGitCommand("status");
            this.FilesShouldMatchCheckoutOfTargetBranch();
        }

        [TestCase]
        public void ResetMixedThenCheckoutWithConflicts()
        {
            this.ValidateGitCommand("checkout " + GitRepoTests.ConflictTargetBranch);
            this.ValidateGitCommand("reset --mixed HEAD~1");

            // Because git while using the sparse-checkout feature
            // will check for index merge conflicts and error out before it checks
            // for untracked files that will be overwritten we just run the command
            this.RunGitCommand("checkout " + GitRepoTests.ConflictSourceBranch, ignoreErrors: true);
            this.FilesShouldMatchCheckoutOfTargetBranch();
        }

        [TestCase]
        public void ResetMixedAndCheckoutFile()
        {
            this.ControlGitRepo.Fetch("FunctionalTests/20201014_ResetMixedAndCheckoutFile");

            // We start with a branch that deleted two files that were present in its parent commit
            this.ValidateGitCommand("checkout FunctionalTests/20201014_ResetMixedAndCheckoutFile");

            // Then reset --mixed to the parent commit, and validate that the deleted files did not come back into the projection
            this.ValidateGitCommand("reset --mixed HEAD~1");
            this.Enlistment.RepoRoot.ShouldBeADirectory(this.FileSystem)
                .WithDeepStructure(this.FileSystem, this.ControlGitRepo.RootPath, withinPrefixes: this.pathPrefixes);

            // And checkout a file (without changing branches) and ensure that that doesn't update the projection either
            this.ValidateGitCommand("checkout HEAD~2 .gitattributes");
            this.Enlistment.RepoRoot.ShouldBeADirectory(this.FileSystem)
                .WithDeepStructure(this.FileSystem, this.ControlGitRepo.RootPath, withinPrefixes: this.pathPrefixes);

            // And now if we checkout the original commit, the deleted files should stay deleted
            this.ValidateGitCommand("checkout FunctionalTests/20201014_ResetMixedAndCheckoutFile");
            this.Enlistment.RepoRoot.ShouldBeADirectory(this.FileSystem)
                .WithDeepStructure(this.FileSystem, this.ControlGitRepo.RootPath, withinPrefixes: this.pathPrefixes);
        }

        protected override void CreateEnlistment()
        {
            base.CreateEnlistment();
            this.ControlGitRepo.Fetch(GitRepoTests.ConflictTargetBranch);
            this.ControlGitRepo.Fetch(GitRepoTests.ConflictSourceBranch);
        }

        private void SkipWorktreeFlagShouldBe(string gitPath, char expectedFlag)
        {
            // "ls-files -v" prefixes each entry with a tag: "S" means skip-worktree is set, "H" means it is not.
            ProcessResult result = GitProcess.InvokeProcess(this.Enlistment.RepoRoot, "ls-files -v -- " + gitPath);
            result.ExitCode.ShouldEqual(0, result.Errors);
            result.Output.Trim().ShouldEqual($"{expectedFlag} {gitPath}");
        }
    }
}
