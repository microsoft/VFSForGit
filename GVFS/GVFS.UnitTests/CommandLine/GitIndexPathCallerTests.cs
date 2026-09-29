using GVFS.CommandLine;
using GVFS.Common;
using GVFS.Common.Git;
using GVFS.RepairJobs;
using GVFS.Tests.Should;
using GVFS.UnitTests.Mock.Common;
using NUnit.Framework;
using System;
using System.IO;

namespace GVFS.UnitTests.CommandLine
{
    [TestFixture]
    public class GitIndexPathCallerTests
    {
        private string testRoot;

        [SetUp]
        public void SetUp()
        {
            if (GVFSPlatform.Instance == null)
            {
                GVFSPlatform.Register(new MockPlatform());
            }

            this.testRoot = Path.Combine(Path.GetTempPath(), "GitIndexPathCallerTests_" + Guid.NewGuid().ToString("N"));
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

        [TestCase(false)]
        [TestCase(true)]
        public void GitIndexGeneratorUsesEnlistmentIndexPath(bool isWorktree)
        {
            GVFSEnlistment enlistment = this.CreateEnlistment(isWorktree);
            Directory.CreateDirectory(Path.GetDirectoryName(enlistment.GitIndexPath));

            File.WriteAllText(enlistment.GitIndexPath, "old index");
            if (isWorktree)
            {
                File.WriteAllText(Path.Combine(enlistment.DotGitRoot, GVFSConstants.DotGit.IndexName), "shared index");
            }

            GitIndexGenerator generator = new GitIndexGenerator(new MockTracer(), enlistment, shouldHashIndex: false);
            generator.TemporaryIndexFilePath.ShouldEqual(enlistment.GitIndexPath + ".lock2");
            File.WriteAllText(generator.TemporaryIndexFilePath, "new index");

            generator.ReplaceExistingIndex();

            File.ReadAllText(enlistment.GitIndexPath).ShouldEqual("new index");
            if (isWorktree)
            {
                File.ReadAllText(Path.Combine(enlistment.DotGitRoot, GVFSConstants.DotGit.IndexName)).ShouldEqual("shared index");
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void GitIndexRepairJobUsesEnlistmentIndexPath(bool isWorktree)
        {
            GVFSEnlistment enlistment = this.CreateEnlistment(isWorktree);
            GitIndexRepairJob repairJob = new GitIndexRepairJob(new MockTracer(), TextWriter.Null, enlistment);

            repairJob.IndexPath.ShouldEqual(enlistment.GitIndexPath);
        }

        [TestCase(false, false)]
        [TestCase(false, true)]
        [TestCase(true, false)]
        public void DehydrateBacksUpEnlistmentIndexPath(bool isWorktree, bool move)
        {
            GVFSEnlistment enlistment = this.CreateEnlistment(isWorktree);
            string indexDirectory = Path.GetDirectoryName(enlistment.GitIndexPath);
            Directory.CreateDirectory(indexDirectory);
            File.WriteAllText(enlistment.GitIndexPath, "working tree index");

            string sharedIndexPath = Path.Combine(enlistment.DotGitRoot, GVFSConstants.DotGit.IndexName);
            if (isWorktree)
            {
                File.WriteAllText(sharedIndexPath, "shared index");
            }

            string backupGit = Path.Combine(this.testRoot, "backup-" + isWorktree + "-" + move, GVFSConstants.DotGit.Root);
            Directory.CreateDirectory(backupGit);

            DehydrateVerb verb = new DehydrateVerb();
            bool result = verb.TryBackupGitIndex(
                new MockTracer(),
                enlistment,
                backupGit,
                move,
                out string errorMessage);

            result.ShouldEqual(true, errorMessage);
            File.ReadAllText(Path.Combine(backupGit, GVFSConstants.DotGit.IndexName)).ShouldEqual("working tree index");
            File.Exists(enlistment.GitIndexPath).ShouldEqual(!move);
            if (isWorktree)
            {
                File.ReadAllText(sharedIndexPath).ShouldEqual("shared index");
            }
        }

        [TestCase(false, false)]
        [TestCase(false, true)]
        [TestCase(true, false)]
        public void DehydrateBacksUpOnlyCurrentWorkingTreeLocks(bool isWorktree, bool move)
        {
            GVFSEnlistment enlistment = this.CreateEnlistment(isWorktree);
            string indexDirectory = Path.GetDirectoryName(enlistment.GitIndexPath);
            Directory.CreateDirectory(indexDirectory);
            string indexLockPath = Path.Combine(indexDirectory, GVFSConstants.DotGit.IndexName + ".lock");
            File.WriteAllText(indexLockPath, "working tree lock");

            string sharedLockPath = Path.Combine(enlistment.DotGitRoot, "shared.lock");
            if (isWorktree)
            {
                File.WriteAllText(sharedLockPath, "shared lock");
            }

            string backupGit = Path.Combine(this.testRoot, "lock-backup-" + isWorktree + "-" + move, GVFSConstants.DotGit.Root);
            Directory.CreateDirectory(backupGit);

            DehydrateVerb verb = new DehydrateVerb();
            bool result = verb.TryBackupGitLocks(
                new MockTracer(),
                enlistment,
                backupGit,
                move,
                out string errorMessage);

            result.ShouldEqual(true, errorMessage);
            File.ReadAllText(Path.Combine(backupGit, GVFSConstants.DotGit.IndexName + ".lock")).ShouldEqual("working tree lock");
            File.Exists(indexLockPath).ShouldEqual(!move);
            if (isWorktree)
            {
                File.ReadAllText(sharedLockPath).ShouldEqual("shared lock");
                File.Exists(Path.Combine(backupGit, "shared.lock")).ShouldBeFalse();
            }
        }

        [TestCase]
        public void FullDehydrateRejectsWorktree()
        {
            GVFSEnlistment enlistment = this.CreateEnlistment(isWorktree: true);
            StringWriter output = new StringWriter();
            DehydrateVerb verb = new DehydrateVerb
            {
                Full = true,
                Output = output,
            };

            Assert.Throws<GVFSVerb.VerbAbortedException>(
                () => verb.ValidateFullDehydrate(new MockTracer(), enlistment));

            output.ToString().ShouldContain("Dehydrate --full is not supported for git worktrees.");
        }

        [TestCase]
        public void FullDehydrateRejectsPrimaryWithLinkedWorktree()
        {
            GVFSEnlistment enlistment = this.CreateEnlistment(isWorktree: false);
            string worktreeGitDir = Path.Combine(enlistment.DotGitRoot, "worktrees", "linked-worktree");
            Directory.CreateDirectory(worktreeGitDir);
            File.WriteAllText(
                Path.Combine(worktreeGitDir, "gitdir"),
                Path.Combine(this.testRoot, "linked-worktree", GVFSConstants.DotGit.Root));

            StringWriter output = new StringWriter();
            DehydrateVerb verb = new DehydrateVerb
            {
                Full = true,
                Output = output,
            };

            Assert.Throws<GVFSVerb.VerbAbortedException>(
                () => verb.ValidateFullDehydrate(new MockTracer(), enlistment));

            output.ToString().ShouldContain("Dehydrate --full is not supported while linked worktrees exist.");
        }

        [TestCase]
        public void FullDehydrateAllowsPrimaryWithoutLinkedWorktrees()
        {
            GVFSEnlistment enlistment = this.CreateEnlistment(isWorktree: false);
            DehydrateVerb verb = new DehydrateVerb
            {
                Full = true,
                Output = TextWriter.Null,
            };

            Assert.DoesNotThrow(
                () => verb.ValidateFullDehydrate(new MockTracer(), enlistment));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void BackupRootIsUniqueAndWorktreeLocal(bool isWorktree)
        {
            GVFSEnlistment enlistment = this.CreateEnlistment(isWorktree);
            DateTime timestamp = new DateTime(2026, 9, 23, 13, 0, 0, 123);

            string backupRoot = DehydrateVerb.GetBackupRoot(enlistment, timestamp);

            string expectedParent = isWorktree
                ? Path.GetDirectoryName(enlistment.WorkingDirectoryBackingRoot)
                : enlistment.PrimaryEnlistmentRoot;
            string expectedSubfolder = isWorktree
                ? Path.Combine("dehydrate_backup", enlistment.Worktree.Name)
                : "dehydrate_backup";
            backupRoot.ShouldEqual(
                Path.Combine(
                    expectedParent,
                    expectedSubfolder,
                    timestamp.ToString("yyyyMMdd_HHmmss_fffffff")));
        }

        [TestCase]
        public void MissingIndexReturnsBackupError()
        {
            GVFSEnlistment enlistment = this.CreateEnlistment(isWorktree: true);
            string backupGit = Path.Combine(this.testRoot, "missing-index-backup");
            Directory.CreateDirectory(backupGit);

            DehydrateVerb verb = new DehydrateVerb();
            bool result = verb.TryBackupGitIndex(
                new MockTracer(),
                enlistment,
                backupGit,
                move: false,
                out string errorMessage);

            result.ShouldBeFalse();
            errorMessage.ShouldNotBeNull();
        }

        [TestCase]
        public void MissingLockDirectoryReturnsBackupError()
        {
            GVFSEnlistment enlistment = this.CreateEnlistment(isWorktree: true);
            Directory.Delete(Path.GetDirectoryName(enlistment.GitIndexPath));
            string backupGit = Path.Combine(this.testRoot, "missing-lock-backup");
            Directory.CreateDirectory(backupGit);

            DehydrateVerb verb = new DehydrateVerb();
            bool result = verb.TryBackupGitLocks(
                new MockTracer(),
                enlistment,
                backupGit,
                move: false,
                out string errorMessage);

            result.ShouldBeFalse();
            errorMessage.ShouldNotBeNull();
        }

        private GVFSEnlistment CreateEnlistment(bool isWorktree)
        {
            string gitBinPath = Path.Combine(this.testRoot, "git.exe");
            string primaryRoot = Path.Combine(this.testRoot, isWorktree ? "worktree-primary" : "standard");
            string primarySrc = Path.Combine(primaryRoot, GVFSConstants.WorkingDirectoryRootName);
            string sharedGitDir = Path.Combine(primarySrc, GVFSConstants.DotGit.Root);
            Directory.CreateDirectory(sharedGitDir);

            if (!isWorktree)
            {
                return new GVFSEnlistment(primaryRoot, "https://mock/repo", gitBinPath, authentication: null);
            }

            string worktreePath = Path.Combine(this.testRoot, "linked-worktree");
            string worktreeGitDir = Path.Combine(sharedGitDir, "worktrees", "linked-worktree");
            Directory.CreateDirectory(worktreePath);
            Directory.CreateDirectory(worktreeGitDir);

            GVFSEnlistment.WorktreeInfo worktreeInfo = new GVFSEnlistment.WorktreeInfo
            {
                Name = "linked-worktree",
                WorktreePath = worktreePath,
                WorktreeGitDir = worktreeGitDir,
                SharedGitDir = sharedGitDir,
                PipeSuffix = "_WT_LINKED-WORKTREE",
            };

            return GVFSEnlistment.CreateForWorktree(
                primaryRoot,
                gitBinPath,
                authentication: null,
                worktreeInfo,
                repoUrl: "https://mock/repo");
        }
    }
}
