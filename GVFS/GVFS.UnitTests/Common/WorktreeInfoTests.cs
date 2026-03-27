using GVFS.Common;
using GVFS.Tests.Should;
using NUnit.Framework;
using System.IO;

namespace GVFS.UnitTests.Common
{
    [TestFixture]
    public class WorktreeInfoTests
    {
        private string testRoot;

        [SetUp]
        public void SetUp()
        {
            this.testRoot = Path.Combine(Path.GetTempPath(), "GVFSWorktreeTests_" + Path.GetRandomFileName());
            Directory.CreateDirectory(this.testRoot);
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(this.testRoot))
            {
                Directory.Delete(this.testRoot, recursive: true);
            }
        }

        [TestCase]
        public void ReturnsNullForNonWorktreeDirectory()
        {
            // A directory without a .git file is not a worktree
            GVFSEnlistment.WorktreeInfo info = GVFSEnlistment.TryGetWorktreeInfo(this.testRoot);
            info.ShouldBeNull();
        }

        [TestCase]
        public void ReturnsNullWhenDotGitIsDirectory()
        {
            // A .git directory (not file) means primary enlistment, not a worktree
            Directory.CreateDirectory(Path.Combine(this.testRoot, ".git"));
            GVFSEnlistment.WorktreeInfo info = GVFSEnlistment.TryGetWorktreeInfo(this.testRoot);
            info.ShouldBeNull();
        }

        [TestCase]
        public void ReturnsNullWhenDotGitFileHasNoGitdirPrefix()
        {
            File.WriteAllText(Path.Combine(this.testRoot, ".git"), "not a gitdir line");
            GVFSEnlistment.WorktreeInfo info = GVFSEnlistment.TryGetWorktreeInfo(this.testRoot);
            info.ShouldBeNull();
        }

        [TestCase]
        public void DetectsWorktreeFromAbsoluteGitdir()
        {
            // Simulate a worktree: .git file pointing to .git/worktrees/<name>
            string primaryGitDir = Path.Combine(this.testRoot, "primary", ".git");
            string worktreeGitDir = Path.Combine(primaryGitDir, "worktrees", "agent-1");
            Directory.CreateDirectory(worktreeGitDir);

            // Create commondir file pointing back to shared .git
            File.WriteAllText(Path.Combine(worktreeGitDir, "commondir"), "../..");

            // Create the worktree directory with a .git file
            string worktreeDir = Path.Combine(this.testRoot, "wt");
            Directory.CreateDirectory(worktreeDir);
            File.WriteAllText(Path.Combine(worktreeDir, ".git"), "gitdir: " + worktreeGitDir);

            GVFSEnlistment.WorktreeInfo info = GVFSEnlistment.TryGetWorktreeInfo(worktreeDir);
            info.ShouldNotBeNull();
            info.Name.ShouldEqual("agent-1");
            info.WorktreePath.ShouldEqual(worktreeDir);
            info.WorktreeGitDir.ShouldEqual(worktreeGitDir);
            info.SharedGitDir.ShouldEqual(primaryGitDir);
            info.PipeSuffix.ShouldEqual("_WT_AGENT-1");
        }

        [TestCase]
        public void DetectsWorktreeFromRelativeGitdir()
        {
            // Simulate worktree with relative gitdir path
            string primaryGitDir = Path.Combine(this.testRoot, "primary", ".git");
            string worktreeGitDir = Path.Combine(primaryGitDir, "worktrees", "feature-branch");
            Directory.CreateDirectory(worktreeGitDir);

            File.WriteAllText(Path.Combine(worktreeGitDir, "commondir"), "../..");

            // Worktree as sibling of primary
            string worktreeDir = Path.Combine(this.testRoot, "feature-branch");
            Directory.CreateDirectory(worktreeDir);

            // Use a relative path: ../primary/.git/worktrees/feature-branch
            string relativePath = "../primary/.git/worktrees/feature-branch";
            File.WriteAllText(Path.Combine(worktreeDir, ".git"), "gitdir: " + relativePath);

            GVFSEnlistment.WorktreeInfo info = GVFSEnlistment.TryGetWorktreeInfo(worktreeDir);
            info.ShouldNotBeNull();
            info.Name.ShouldEqual("feature-branch");
            info.PipeSuffix.ShouldEqual("_WT_FEATURE-BRANCH");
        }

        [TestCase]
        public void ReturnsNullWithoutCommondirFile()
        {
            // Worktree git dir without a commondir file is invalid
            string worktreeGitDir = Path.Combine(this.testRoot, "primary", ".git", "worktrees", "no-common");
            Directory.CreateDirectory(worktreeGitDir);

            string worktreeDir = Path.Combine(this.testRoot, "no-common");
            Directory.CreateDirectory(worktreeDir);
            File.WriteAllText(Path.Combine(worktreeDir, ".git"), "gitdir: " + worktreeGitDir);

            GVFSEnlistment.WorktreeInfo info = GVFSEnlistment.TryGetWorktreeInfo(worktreeDir);
            info.ShouldBeNull();
        }

        [TestCase]
        public void PipeSuffixReturnsNullForNonWorktree()
        {
            string suffix = GVFSEnlistment.GetWorktreePipeSuffix(this.testRoot);
            suffix.ShouldBeNull();
        }

        [TestCase]
        public void PipeSuffixReturnsCorrectValueForWorktree()
        {
            string worktreeGitDir = Path.Combine(this.testRoot, "primary", ".git", "worktrees", "my-wt");
            Directory.CreateDirectory(worktreeGitDir);
            File.WriteAllText(Path.Combine(worktreeGitDir, "commondir"), "../..");

            string worktreeDir = Path.Combine(this.testRoot, "my-wt");
            Directory.CreateDirectory(worktreeDir);
            File.WriteAllText(Path.Combine(worktreeDir, ".git"), "gitdir: " + worktreeGitDir);

            string suffix = GVFSEnlistment.GetWorktreePipeSuffix(worktreeDir);
            suffix.ShouldEqual("_WT_MY-WT");
        }

        [TestCase]
        public void ReturnsNullForNonexistentDirectory()
        {
            string nonexistent = Path.Combine(this.testRoot, "does-not-exist");
            GVFSEnlistment.WorktreeInfo info = GVFSEnlistment.TryGetWorktreeInfo(nonexistent);
            info.ShouldBeNull();
        }

        [TestCase]
        public void DetectsWorktreeFromSubdirectory()
        {
            // Set up a worktree at testRoot/wt-sub with .git file
            string primaryGitDir = Path.Combine(this.testRoot, "primary", ".git");
            string worktreeGitDir = Path.Combine(primaryGitDir, "worktrees", "wt-sub");
            Directory.CreateDirectory(worktreeGitDir);
            File.WriteAllText(Path.Combine(worktreeGitDir, "commondir"), "../..");

            string worktreeDir = Path.Combine(this.testRoot, "wt-sub");
            Directory.CreateDirectory(worktreeDir);
            File.WriteAllText(Path.Combine(worktreeDir, ".git"), "gitdir: " + worktreeGitDir);

            // Create a subdirectory inside the worktree
            string subDir = Path.Combine(worktreeDir, "a", "b", "c");
            Directory.CreateDirectory(subDir);

            // TryGetWorktreeInfo should walk up and find the worktree root
            GVFSEnlistment.WorktreeInfo info = GVFSEnlistment.TryGetWorktreeInfo(subDir);
            info.ShouldNotBeNull();
            info.Name.ShouldEqual("wt-sub");
            info.WorktreePath.ShouldEqual(worktreeDir);
        }

        [TestCase]
        public void ReturnsNullForPrimaryFromSubdirectory()
        {
            // Set up a primary repo with a real .git directory
            string primaryDir = Path.Combine(this.testRoot, "primary-repo");
            Directory.CreateDirectory(Path.Combine(primaryDir, ".git"));

            // Walking up from a subdirectory should find the .git dir and return null
            string subDir = Path.Combine(primaryDir, "src", "folder");
            Directory.CreateDirectory(subDir);

            GVFSEnlistment.WorktreeInfo info = GVFSEnlistment.TryGetWorktreeInfo(subDir);
            info.ShouldBeNull();
        }
    }
}
