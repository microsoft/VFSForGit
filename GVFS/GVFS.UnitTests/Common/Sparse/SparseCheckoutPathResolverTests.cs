using System;
using System.IO;
using GVFS.Common;
using GVFS.Common.Sparse;
using GVFS.Tests.Should;
using GVFS.UnitTests.Category;
using GVFS.UnitTests.Mock.Common;
using NUnit.Framework;

namespace GVFS.UnitTests.Common.Sparse
{
    [TestFixture]
    public class SparseCheckoutPathResolverTests
    {
        private const string PrimaryGitDir = @"mock:\repo\src\.git";
        private const string WorktreeGitDir = @"mock:\repo\src\.git\worktrees\linked1";

        [TestCase]
        public void PrimaryEnlistmentUsesDotGitRoot()
        {
            SparseCheckoutPathResolver.GetSparseCheckoutFilePath(isWorktree: false, dotGitRoot: PrimaryGitDir, worktreeGitDir: WorktreeGitDir)
                .ShouldEqual(Path.Combine(PrimaryGitDir, "info", "sparse-checkout"));
        }

        [TestCase]
        public void LinkedWorktreeUsesWorktreeGitDir()
        {
            // GVFSEnlistment.DotGitRoot is the shared git dir for a worktree, but each worktree
            // has its own sparse-checkout file.
            SparseCheckoutPathResolver.GetSparseCheckoutFilePath(isWorktree: true, dotGitRoot: PrimaryGitDir, worktreeGitDir: WorktreeGitDir)
                .ShouldEqual(Path.Combine(WorktreeGitDir, "info", "sparse-checkout"));
        }

        [TestCase]
        public void PrimaryEnlistmentOverloadUsesDotGitRoot()
        {
            GVFSEnlistment enlistment = new MockGVFSEnlistment();

            SparseCheckoutPathResolver.GetSparseCheckoutFilePath(enlistment)
                .ShouldEqual(Path.Combine(enlistment.DotGitRoot, "info", "sparse-checkout"));
        }

        [TestCase]
        [Category(CategoryConstants.ExceptionExpected)]
        public void PrimaryEnlistmentThrowsWhenDotGitRootMissing()
        {
            Assert.Throws<ArgumentException>(
                () => SparseCheckoutPathResolver.GetSparseCheckoutFilePath(isWorktree: false, dotGitRoot: null, worktreeGitDir: WorktreeGitDir));
            Assert.Throws<ArgumentException>(
                () => SparseCheckoutPathResolver.GetSparseCheckoutFilePath(isWorktree: false, dotGitRoot: string.Empty, worktreeGitDir: WorktreeGitDir));
        }

        [TestCase]
        [Category(CategoryConstants.ExceptionExpected)]
        public void WorktreeEnlistmentThrowsWhenWorktreeGitDirMissing()
        {
            Assert.Throws<ArgumentException>(
                () => SparseCheckoutPathResolver.GetSparseCheckoutFilePath(isWorktree: true, dotGitRoot: PrimaryGitDir, worktreeGitDir: null));
            Assert.Throws<ArgumentException>(
                () => SparseCheckoutPathResolver.GetSparseCheckoutFilePath(isWorktree: true, dotGitRoot: PrimaryGitDir, worktreeGitDir: string.Empty));
        }

        [TestCase]
        [Category(CategoryConstants.ExceptionExpected)]
        public void EnlistmentOverloadThrowsOnNull()
        {
            Assert.Throws<ArgumentNullException>(
                () => SparseCheckoutPathResolver.GetSparseCheckoutFilePath(null));
        }
    }
}
