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
        private const string WorktreeBranch = "worktree-test-branch";

        [TestCase]
        public void WorktreeAddRemoveCycle()
        {
            string worktreePath = Path.Combine(this.Enlistment.EnlistmentRoot, "test-wt-" + Guid.NewGuid().ToString("N").Substring(0, 8));

            try
            {
                // 1. Create worktree
                ProcessResult addResult = GitHelpers.InvokeGitAgainstGVFSRepo(
                    this.Enlistment.RepoRoot,
                    $"worktree add -b {WorktreeBranch} \"{worktreePath}\"");
                addResult.ExitCode.ShouldEqual(0, $"worktree add failed: {addResult.Errors}");

                // 2. Verify directory exists with projected files
                Directory.Exists(worktreePath).ShouldBeTrue("Worktree directory should exist");
                File.Exists(Path.Combine(worktreePath, "Readme.md")).ShouldBeTrue("Readme.md should be projected");

                string readmeContent = File.ReadAllText(Path.Combine(worktreePath, "Readme.md"));
                readmeContent.ShouldContain(
                    expectedSubstrings: new[] { "GVFS" });

                // 3. Verify git status is clean
                ProcessResult statusResult = GitHelpers.InvokeGitAgainstGVFSRepo(
                    worktreePath,
                    "status --porcelain");
                statusResult.ExitCode.ShouldEqual(0, $"git status failed: {statusResult.Errors}");
                statusResult.Output.Trim().ShouldBeEmpty("Worktree should have clean status");

                // 4. Verify worktree list shows both
                ProcessResult listResult = GitHelpers.InvokeGitAgainstGVFSRepo(
                    this.Enlistment.RepoRoot,
                    "worktree list");
                listResult.ExitCode.ShouldEqual(0, $"worktree list failed: {listResult.Errors}");
                string listOutput = listResult.Output;
                string repoRootGitFormat = this.Enlistment.RepoRoot.Replace('\\', '/');
                string worktreePathGitFormat = worktreePath.Replace('\\', '/');
                Assert.IsTrue(
                    listOutput.Contains(repoRootGitFormat),
                    $"worktree list should contain repo root. Output: {listOutput}");
                Assert.IsTrue(
                    listOutput.Contains(worktreePathGitFormat),
                    $"worktree list should contain worktree path. Output: {listOutput}");

                // 5. Make a change in the worktree, commit on the branch
                string testFile = Path.Combine(worktreePath, "worktree-test.txt");
                File.WriteAllText(testFile, "created in worktree");

                ProcessResult addFile = GitHelpers.InvokeGitAgainstGVFSRepo(
                    worktreePath, "add worktree-test.txt");
                addFile.ExitCode.ShouldEqual(0, $"git add failed: {addFile.Errors}");

                ProcessResult commit = GitHelpers.InvokeGitAgainstGVFSRepo(
                    worktreePath, "commit -m \"test commit from worktree\"");
                commit.ExitCode.ShouldEqual(0, $"git commit failed: {commit.Errors}");

                // 6. Remove without --force should fail with helpful message
                ProcessResult removeNoForce = GitHelpers.InvokeGitAgainstGVFSRepo(
                    this.Enlistment.RepoRoot,
                    $"worktree remove \"{worktreePath}\"");
                removeNoForce.ExitCode.ShouldNotEqual(0, "worktree remove without --force should fail");
                removeNoForce.Errors.ShouldContain(
                    expectedSubstrings: new[] { "--force" });

                // Worktree should still be intact after failed remove
                File.Exists(Path.Combine(worktreePath, "Readme.md")).ShouldBeTrue("Files should still be projected after failed remove");

                // 6. Remove with --force should succeed
                ProcessResult removeResult = GitHelpers.InvokeGitAgainstGVFSRepo(
                    this.Enlistment.RepoRoot,
                    $"worktree remove --force \"{worktreePath}\"");
                removeResult.ExitCode.ShouldEqual(0, $"worktree remove --force failed: {removeResult.Errors}");

                // 7. Verify cleanup
                Directory.Exists(worktreePath).ShouldBeFalse("Worktree directory should be deleted");

                ProcessResult listAfter = GitHelpers.InvokeGitAgainstGVFSRepo(
                    this.Enlistment.RepoRoot,
                    "worktree list");
                listAfter.Output.ShouldNotContain(
                    ignoreCase: false,
                    unexpectedSubstrings: new[] { worktreePathGitFormat });

                // 8. Verify commit from worktree is accessible from main enlistment
                ProcessResult logFromMain = GitHelpers.InvokeGitAgainstGVFSRepo(
                    this.Enlistment.RepoRoot,
                    $"log -1 --format=%s {WorktreeBranch}");
                logFromMain.ExitCode.ShouldEqual(0, $"git log from main failed: {logFromMain.Errors}");
                logFromMain.Output.ShouldContain(
                    expectedSubstrings: new[] { "test commit from worktree" });
            }
            finally
            {
                this.ForceCleanupWorktree(worktreePath);
            }
        }

        private void ForceCleanupWorktree(string worktreePath)
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
                    // Kill any stuck GVFS.Mount for this worktree
                    foreach (Process p in Process.GetProcessesByName("GVFS.Mount"))
                    {
                        try
                        {
                            if (p.StartInfo.Arguments?.Contains(worktreePath) == true)
                            {
                                p.Kill();
                            }
                        }
                        catch
                        {
                        }
                    }

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
                    $"branch -D {WorktreeBranch}");
            }
            catch
            {
            }
        }
    }
}
