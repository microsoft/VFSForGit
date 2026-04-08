using GVFS.Common;
using GVFS.Tests.Should;
using NUnit.Framework;
using System.IO;

namespace GVFS.UnitTests.Common
{
    [TestFixture]
    public class WorktreeEnlistmentTests
    {
        private string testRoot;
        private string primaryRoot;
        private string sharedGitDir;
        private string worktreePath;
        private string worktreeGitDir;

        [SetUp]
        public void SetUp()
        {
            this.testRoot = Path.Combine(Path.GetTempPath(), "GVFSWTEnlTests_" + Path.GetRandomFileName());
            this.primaryRoot = Path.Combine(this.testRoot, "enlistment");
            string primarySrc = Path.Combine(this.primaryRoot, "src");
            this.sharedGitDir = Path.Combine(primarySrc, ".git");
            this.worktreePath = Path.Combine(this.testRoot, "agent-wt-1");
            this.worktreeGitDir = Path.Combine(this.sharedGitDir, "worktrees", "agent-wt-1");

            Directory.CreateDirectory(this.sharedGitDir);
            Directory.CreateDirectory(this.worktreeGitDir);
            Directory.CreateDirectory(this.worktreePath);
            Directory.CreateDirectory(Path.Combine(this.primaryRoot, ".gvfs"));

            File.WriteAllText(
                Path.Combine(this.sharedGitDir, "config"),
                "[core]\n\trepositoryformatversion = 0\n[remote \"origin\"]\n\turl = https://mock/repo\n");
            File.WriteAllText(
                Path.Combine(this.sharedGitDir, "HEAD"),
                "ref: refs/heads/main\n");
            File.WriteAllText(
                Path.Combine(this.worktreePath, ".git"),
                "gitdir: " + this.worktreeGitDir);
            File.WriteAllText(
                Path.Combine(this.worktreeGitDir, "commondir"),
                "../..");
            File.WriteAllText(
                Path.Combine(this.worktreeGitDir, "HEAD"),
                "ref: refs/heads/agent-wt-1\n");
            File.WriteAllText(
                Path.Combine(this.worktreeGitDir, "gitdir"),
                Path.Combine(this.worktreePath, ".git"));
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(this.testRoot))
            {
                Directory.Delete(this.testRoot, recursive: true);
            }
        }

        private GVFSEnlistment CreateWorktreeEnlistment()
        {
            string gitBinPath = GVFSPlatform.Instance.GitInstallation.GetInstalledGitBinPath()
                ?? @"C:\Program Files\Git\cmd\git.exe";
            return GVFSEnlistment.CreateForWorktree(
                this.primaryRoot, gitBinPath, authentication: null,
                GVFSEnlistment.TryGetWorktreeInfo(this.worktreePath),
                repoUrl: "https://mock/repo");
        }

        [TestCase]
        public void IsWorktreeReturnsTrueForWorktreeEnlistment()
        {
            GVFSEnlistment enlistment = this.CreateWorktreeEnlistment();
            enlistment.IsWorktree.ShouldBeTrue();
        }

        [TestCase]
        public void WorktreeInfoIsPopulated()
        {
            GVFSEnlistment enlistment = this.CreateWorktreeEnlistment();
            enlistment.Worktree.ShouldNotBeNull();
            enlistment.Worktree.Name.ShouldEqual("agent-wt-1");
            enlistment.Worktree.WorktreePath.ShouldEqual(this.worktreePath);
        }

        [TestCase]
        public void DotGitRootPointsToSharedGitDir()
        {
            GVFSEnlistment enlistment = this.CreateWorktreeEnlistment();
            enlistment.DotGitRoot.ShouldEqual(this.sharedGitDir);
        }

        [TestCase]
        public void WorkingDirectoryRootIsWorktreePath()
        {
            GVFSEnlistment enlistment = this.CreateWorktreeEnlistment();
            enlistment.WorkingDirectoryRoot.ShouldEqual(this.worktreePath);
        }

        [TestCase]
        public void LocalObjectsRootIsSharedGitObjects()
        {
            GVFSEnlistment enlistment = this.CreateWorktreeEnlistment();
            enlistment.LocalObjectsRoot.ShouldEqual(
                Path.Combine(this.sharedGitDir, "objects"));
        }

        [TestCase]
        public void LocalObjectsRootDoesNotDoubleGitPath()
        {
            GVFSEnlistment enlistment = this.CreateWorktreeEnlistment();
            Assert.IsFalse(
                enlistment.LocalObjectsRoot.Contains(Path.Combine(".git", ".git")),
                "LocalObjectsRoot should not have doubled .git path");
        }

        [TestCase]
        public void GitIndexPathUsesWorktreeGitDir()
        {
            GVFSEnlistment enlistment = this.CreateWorktreeEnlistment();
            enlistment.GitIndexPath.ShouldEqual(
                Path.Combine(this.worktreeGitDir, "index"));
        }

        [TestCase]
        public void NamedPipeNameIncludesWorktreeSuffix()
        {
            GVFSEnlistment enlistment = this.CreateWorktreeEnlistment();
            Assert.IsTrue(
                enlistment.NamedPipeName.Contains("_WT_AGENT-WT-1"),
                "NamedPipeName should contain worktree suffix");
        }

        [TestCase]
        public void DotGVFSRootIsInWorktreeGitDir()
        {
            GVFSEnlistment enlistment = this.CreateWorktreeEnlistment();
            Assert.IsTrue(
                enlistment.DotGVFSRoot.Contains(this.worktreeGitDir),
                "DotGVFSRoot should be inside worktree git dir");
        }

        [TestCase]
        public void EnlistmentRootIsPrimaryRoot()
        {
            GVFSEnlistment enlistment = this.CreateWorktreeEnlistment();
            enlistment.EnlistmentRoot.ShouldEqual(this.primaryRoot);
        }

        [TestCase]
        public void RepoUrlIsReadFromSharedConfig()
        {
            GVFSEnlistment enlistment = this.CreateWorktreeEnlistment();
            enlistment.RepoUrl.ShouldEqual("https://mock/repo");
        }
    }
}
