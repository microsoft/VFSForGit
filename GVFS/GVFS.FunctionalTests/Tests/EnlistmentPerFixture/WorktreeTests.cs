using GVFS.FunctionalTests.Tools;
using GVFS.Tests.Should;
using NUnit.Framework;
using System;
using System.Diagnostics;
using System.IO;

namespace GVFS.FunctionalTests.Tests.EnlistmentPerFixture
{
    [TestFixture]
    [Category(Categories.GitCommands)]
    public class WorktreeTests : TestsWithEnlistmentPerFixture
    {
        private const string WorktreeBranchA = "worktree-test-branch-a";
        private const string WorktreeBranchB = "worktree-test-branch-b";

        [TestCase]
        public void ConcurrentWorktreeAddCommitRemove()
        {
            string worktreePathA = Path.Combine(this.Enlistment.EnlistmentRoot, "test-wt-a-" + Guid.NewGuid().ToString("N").Substring(0, 8));
            string worktreePathB = Path.Combine(this.Enlistment.EnlistmentRoot, "test-wt-b-" + Guid.NewGuid().ToString("N").Substring(0, 8));

            try
            {
                // 1. Create both worktrees in parallel
                ProcessResult addResultA = null;
                ProcessResult addResultB = null;
                System.Threading.Tasks.Parallel.Invoke(
                    () => addResultA = GitHelpers.InvokeGitAgainstGVFSRepo(
                        this.Enlistment.RepoRoot,
                        $"worktree add -b {WorktreeBranchA} \"{worktreePathA}\""),
                    () => addResultB = GitHelpers.InvokeGitAgainstGVFSRepo(
                        this.Enlistment.RepoRoot,
                        $"worktree add -b {WorktreeBranchB} \"{worktreePathB}\""));

                addResultA.ExitCode.ShouldEqual(0, $"worktree add A failed: {addResultA.Errors}");
                addResultB.ExitCode.ShouldEqual(0, $"worktree add B failed: {addResultB.Errors}");

                // 2. Verify both have projected files
                Directory.Exists(worktreePathA).ShouldBeTrue("Worktree A directory should exist");
                Directory.Exists(worktreePathB).ShouldBeTrue("Worktree B directory should exist");
                File.Exists(Path.Combine(worktreePathA, "Readme.md")).ShouldBeTrue("Readme.md should be projected in A");
                File.Exists(Path.Combine(worktreePathB, "Readme.md")).ShouldBeTrue("Readme.md should be projected in B");

                // 3. Verify git status is clean in both
                ProcessResult statusA = GitHelpers.InvokeGitAgainstGVFSRepo(worktreePathA, "status --porcelain");
                ProcessResult statusB = GitHelpers.InvokeGitAgainstGVFSRepo(worktreePathB, "status --porcelain");
                statusA.ExitCode.ShouldEqual(0, $"git status A failed: {statusA.Errors}");
                statusB.ExitCode.ShouldEqual(0, $"git status B failed: {statusB.Errors}");
                statusA.Output.Trim().ShouldBeEmpty("Worktree A should have clean status");
                statusB.Output.Trim().ShouldBeEmpty("Worktree B should have clean status");

                // 4. Verify worktree list shows all three
                ProcessResult listResult = GitHelpers.InvokeGitAgainstGVFSRepo(
                    this.Enlistment.RepoRoot, "worktree list");
                listResult.ExitCode.ShouldEqual(0, $"worktree list failed: {listResult.Errors}");
                string listOutput = listResult.Output;
                Assert.IsTrue(listOutput.Contains(worktreePathA.Replace('\\', '/')),
                    $"worktree list should contain A. Output: {listOutput}");
                Assert.IsTrue(listOutput.Contains(worktreePathB.Replace('\\', '/')),
                    $"worktree list should contain B. Output: {listOutput}");

                // 5. Make commits in both worktrees
                File.WriteAllText(Path.Combine(worktreePathA, "from-a.txt"), "created in worktree A");
                GitHelpers.InvokeGitAgainstGVFSRepo(worktreePathA, "add from-a.txt")
                    .ExitCode.ShouldEqual(0);
                GitHelpers.InvokeGitAgainstGVFSRepo(worktreePathA, "commit -m \"commit from A\"")
                    .ExitCode.ShouldEqual(0);

                File.WriteAllText(Path.Combine(worktreePathB, "from-b.txt"), "created in worktree B");
                GitHelpers.InvokeGitAgainstGVFSRepo(worktreePathB, "add from-b.txt")
                    .ExitCode.ShouldEqual(0);
                GitHelpers.InvokeGitAgainstGVFSRepo(worktreePathB, "commit -m \"commit from B\"")
                    .ExitCode.ShouldEqual(0);

                // 6. Verify commits are visible from all worktrees (shared objects)
                GitHelpers.InvokeGitAgainstGVFSRepo(this.Enlistment.RepoRoot, $"log -1 --format=%s {WorktreeBranchA}")
                    .Output.ShouldContain(expectedSubstrings: new[] { "commit from A" });
                GitHelpers.InvokeGitAgainstGVFSRepo(this.Enlistment.RepoRoot, $"log -1 --format=%s {WorktreeBranchB}")
                    .Output.ShouldContain(expectedSubstrings: new[] { "commit from B" });

                // A can see B's commit and vice versa
                GitHelpers.InvokeGitAgainstGVFSRepo(worktreePathA, $"log -1 --format=%s {WorktreeBranchB}")
                    .Output.ShouldContain(expectedSubstrings: new[] { "commit from B" });
                GitHelpers.InvokeGitAgainstGVFSRepo(worktreePathB, $"log -1 --format=%s {WorktreeBranchA}")
                    .Output.ShouldContain(expectedSubstrings: new[] { "commit from A" });

                // 7. Remove both in parallel
                ProcessResult removeA = null;
                ProcessResult removeB = null;
                System.Threading.Tasks.Parallel.Invoke(
                    () => removeA = GitHelpers.InvokeGitAgainstGVFSRepo(
                        this.Enlistment.RepoRoot,
                        $"worktree remove --force \"{worktreePathA}\""),
                    () => removeB = GitHelpers.InvokeGitAgainstGVFSRepo(
                        this.Enlistment.RepoRoot,
                        $"worktree remove --force \"{worktreePathB}\""));

                removeA.ExitCode.ShouldEqual(0, $"worktree remove A failed: {removeA.Errors}");
                removeB.ExitCode.ShouldEqual(0, $"worktree remove B failed: {removeB.Errors}");

                // 8. Verify cleanup
                Directory.Exists(worktreePathA).ShouldBeFalse("Worktree A directory should be deleted");
                Directory.Exists(worktreePathB).ShouldBeFalse("Worktree B directory should be deleted");
            }
            finally
            {
                this.ForceCleanupWorktree(worktreePathA, WorktreeBranchA);
                this.ForceCleanupWorktree(worktreePathB, WorktreeBranchB);
            }
        }

        private void ForceCleanupWorktree(string worktreePath, string branchName)
        {
            // Best-effort cleanup for test failure cases
            try
            {
                GitHelpers.InvokeGitAgainstGVFSRepo(
                    this.Enlistment.RepoRoot,
                    $"worktree remove --force \"{worktreePath}\"");
            }
            catch
            {
            }

            if (Directory.Exists(worktreePath))
            {
                try
                {
                    // Unmount any running GVFS mount for this worktree
                    Process unmount = Process.Start("gvfs", $"unmount \"{worktreePath}\"");
                    unmount?.WaitForExit(30000);
                }
                catch
                {
                }

                try
                {
                    Directory.Delete(worktreePath, recursive: true);
                }
                catch
                {
                }
            }

            // Clean up branch
            try
            {
                GitHelpers.InvokeGitAgainstGVFSRepo(
                    this.Enlistment.RepoRoot,
                    $"branch -D {branchName}");
            }
            catch
            {
            }
        }
    }
}
