using GVFS.Common;
using GVFS.Common.NamedPipes;
using GVFS.FunctionalTests.Tools;
using GVFS.Tests.Should;
using NUnit.Framework;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using ProcessResult = GVFS.FunctionalTests.Tools.ProcessResult;

namespace GVFS.FunctionalTests.Tests.EnlistmentPerFixture
{
    [TestFixture]
    [Category(Categories.GitCommands)]
    public class WorktreeTests : TestsWithEnlistmentPerFixture
    {
        private const int MinWorktreeCount = 4;

        [TestCase]
        public void ConcurrentWorktreeAddCommitRemove()
        {
            int count = Math.Max(Environment.ProcessorCount, MinWorktreeCount);
            string[] worktreePaths;
            string[] branchNames;

            Stopwatch testSw = Stopwatch.StartNew();
            DiagLog($"Starting test with count={count} (ProcessorCount={Environment.ProcessorCount})");

            // Adaptively scale down if concurrent adds overwhelm the primary
            // GVFS mount. CI runners with fewer resources may not handle as
            // many concurrent git operations as a developer workstation.
            while (true)
            {
                this.InitWorktreeArrays(count, out worktreePaths, out branchNames);
                DiagLog($"Phase 1: ConcurrentWorktreeAdd count={count}");
                ProcessResult[] addResults = this.ConcurrentWorktreeAdd(worktreePaths, branchNames, count);
                DiagLog($"Phase 1: ConcurrentWorktreeAdd done in {testSw.Elapsed.TotalSeconds:F1}s");

                for (int i = 0; i < count; i++)
                {
                    if (addResults[i].ExitCode != 0)
                    {
                        DiagLog($"  worktree add [{i}] FAILED exit={addResults[i].ExitCode}: {addResults[i].Errors}");
                    }
                }

                bool overloaded = addResults.Any(r =>
                    r.ExitCode != 0 &&
                    r.Errors != null &&
                    r.Errors.Contains("does not appear to be mounted"));

                // Only retry if ALL failures are overload-related. If any
                // failure has a different cause, it's a real regression and
                // must not be masked by retrying at lower concurrency.
                bool hasNonOverloadFailure = addResults.Any(r =>
                    r.ExitCode != 0 &&
                    !(r.Errors != null && r.Errors.Contains("does not appear to be mounted")));

                if (hasNonOverloadFailure)
                {
                    // Fall through to the assertion loop below which will
                    // report the specific failure(s).
                }
                else if (overloaded)
                {
                    this.CleanupAllWorktrees(worktreePaths, branchNames, count);
                    int reduced = count / 2;
                    if (reduced < MinWorktreeCount)
                    {
                        Assert.Fail(
                            $"Primary GVFS mount overloaded even at count={count}. " +
                            $"Cannot reduce below {MinWorktreeCount}.");
                    }

                    DiagLog($"Overloaded at count={count}, reducing to {reduced}");
                    count = reduced;
                    continue;
                }

                // Non-overload failures are real errors
                for (int i = 0; i < count; i++)
                {
                    addResults[i].ExitCode.ShouldEqual(0,
                        $"worktree add [{i}] failed: {addResults[i].Errors}");
                }

                break;
            }

            try
            {
                // 2. Primary assertion: verify GVFS mount is running for each
                //    worktree by probing the worktree-specific named pipe.
                DiagLog($"Phase 2: AssertWorktreeMounted x{count}");
                for (int i = 0; i < count; i++)
                {
                    this.AssertWorktreeMounted(worktreePaths[i], $"worktree [{i}]");
                }

                DiagLog($"Phase 2 done in {testSw.Elapsed.TotalSeconds:F1}s");

                // 3. Verify projected files are visible (secondary assertion)
                DiagLog("Phase 3: Verify projected files");
                for (int i = 0; i < count; i++)
                {
                    Directory.Exists(worktreePaths[i]).ShouldBeTrue(
                        $"Worktree [{i}] directory should exist");
                    File.Exists(Path.Combine(worktreePaths[i], "Readme.md")).ShouldBeTrue(
                        $"Readme.md should be projected in [{i}]");
                }

                DiagLog($"Phase 3 done in {testSw.Elapsed.TotalSeconds:F1}s");

                // 4. Verify git status is clean in each worktree
                DiagLog("Phase 4: git status in each worktree");
                for (int i = 0; i < count; i++)
                {
                    DiagLog($"  git status [{i}]...");
                    ProcessResult status = GitHelpers.InvokeGitAgainstGVFSRepo(
                        worktreePaths[i], "status --porcelain");
                    DiagLog($"  git status [{i}] exit={status.ExitCode} in {testSw.Elapsed.TotalSeconds:F1}s");
                    status.ExitCode.ShouldEqual(0,
                        $"git status [{i}] failed: {status.Errors}");
                    status.Output.Trim().ShouldBeEmpty(
                        $"Worktree [{i}] should have clean status");
                }

                DiagLog($"Phase 4 done in {testSw.Elapsed.TotalSeconds:F1}s");

                // 5. Verify worktree list shows all entries
                DiagLog("Phase 5: git worktree list");
                ProcessResult listResult = GitHelpers.InvokeGitAgainstGVFSRepo(
                    this.Enlistment.RepoRoot, "worktree list");
                DiagLog($"Phase 5 done exit={listResult.ExitCode} in {testSw.Elapsed.TotalSeconds:F1}s");
                listResult.ExitCode.ShouldEqual(0, $"worktree list failed: {listResult.Errors}");
                string listOutput = listResult.Output;
                for (int i = 0; i < count; i++)
                {
                    Assert.IsTrue(
                        listOutput.Contains(worktreePaths[i].Replace('\\', '/')),
                        $"worktree list should contain [{i}]. Output: {listOutput}");
                }

                // 6. Make commits in all worktrees
                DiagLog("Phase 6: commits in each worktree");
                for (int i = 0; i < count; i++)
                {
                    DiagLog($"  commit [{i}]...");
                    File.WriteAllText(
                        Path.Combine(worktreePaths[i], $"from-{i}.txt"),
                        $"created in worktree {i}");
                    GitHelpers.InvokeGitAgainstGVFSRepo(worktreePaths[i], $"add from-{i}.txt")
                        .ExitCode.ShouldEqual(0);
                    GitHelpers.InvokeGitAgainstGVFSRepo(
                        worktreePaths[i], $"commit -m \"commit from {i}\"")
                        .ExitCode.ShouldEqual(0);
                    DiagLog($"  commit [{i}] done at {testSw.Elapsed.TotalSeconds:F1}s");
                }

                DiagLog($"Phase 6 done in {testSw.Elapsed.TotalSeconds:F1}s");

                // 7. Verify commits are visible from main repo
                DiagLog("Phase 7: verify commits from main repo");
                for (int i = 0; i < count; i++)
                {
                    GitHelpers.InvokeGitAgainstGVFSRepo(
                        this.Enlistment.RepoRoot, $"log -1 --format=%s {branchNames[i]}")
                        .Output.ShouldContain(expectedSubstrings: new[] { $"commit from {i}" });
                }

                DiagLog($"Phase 7 done in {testSw.Elapsed.TotalSeconds:F1}s");

                // 8. Verify cross-worktree commit visibility (shared objects)
                DiagLog("Phase 8: cross-worktree visibility");
                for (int i = 0; i < count; i++)
                {
                    int other = (i + 1) % count;
                    GitHelpers.InvokeGitAgainstGVFSRepo(
                        worktreePaths[i], $"log -1 --format=%s {branchNames[other]}")
                        .Output.ShouldContain(expectedSubstrings: new[] { $"commit from {other}" });
                }

                DiagLog($"Phase 8 done in {testSw.Elapsed.TotalSeconds:F1}s");

                // 9. Remove all worktrees in parallel
                DiagLog($"Phase 9: concurrent worktree remove x{count}");
                ProcessResult[] removeResults = new ProcessResult[count];
                using (CountdownEvent barrier = new CountdownEvent(count))
                {
                    Thread[] threads = new Thread[count];
                    for (int i = 0; i < count; i++)
                    {
                        int idx = i;
                        threads[idx] = new Thread(() =>
                        {
                            barrier.Signal();
                            barrier.Wait();
                            removeResults[idx] = GitHelpers.InvokeGitAgainstGVFSRepo(
                                this.Enlistment.RepoRoot,
                                $"worktree remove --force \"{worktreePaths[idx]}\"");
                        });
                        threads[idx].Start();
                    }

                    foreach (Thread t in threads)
                    {
                        t.Join();
                    }
                }

                DiagLog($"Phase 9 done in {testSw.Elapsed.TotalSeconds:F1}s");

                for (int i = 0; i < count; i++)
                {
                    removeResults[i].ExitCode.ShouldEqual(0,
                        $"worktree remove [{i}] failed: {removeResults[i].Errors}");
                }

                // 10. Verify cleanup
                for (int i = 0; i < count; i++)
                {
                    Directory.Exists(worktreePaths[i]).ShouldBeFalse(
                        $"Worktree [{i}] directory should be deleted");
                }

                DiagLog($"Test COMPLETE in {testSw.Elapsed.TotalSeconds:F1}s");
            }
            finally
            {
                this.CleanupAllWorktrees(worktreePaths, branchNames, count);
            }
        }

        private void InitWorktreeArrays(int count, out string[] paths, out string[] branches)
        {
            paths = new string[count];
            branches = new string[count];
            for (int i = 0; i < count; i++)
            {
                string suffix = Guid.NewGuid().ToString("N").Substring(0, 8);
                paths[i] = Path.Combine(this.Enlistment.EnlistmentRoot, $"test-wt-{i}-{suffix}");
                branches[i] = $"worktree-test-branch-{i}-{suffix}";
            }
        }

        private ProcessResult[] ConcurrentWorktreeAdd(string[] paths, string[] branches, int count)
        {
            ProcessResult[] results = new ProcessResult[count];
            Stopwatch sw = Stopwatch.StartNew();
            using (CountdownEvent barrier = new CountdownEvent(count))
            {
                Thread[] threads = new Thread[count];
                for (int i = 0; i < count; i++)
                {
                    int idx = i;
                    threads[idx] = new Thread(() =>
                    {
                        barrier.Signal();
                        barrier.Wait();
                        Stopwatch addSw = Stopwatch.StartNew();
                        results[idx] = GitHelpers.InvokeGitAgainstGVFSRepo(
                            this.Enlistment.RepoRoot,
                            $"worktree add -b {branches[idx]} \"{paths[idx]}\"");
                        DiagLog($"  worktree add [{idx}] exit={results[idx].ExitCode} in {addSw.Elapsed.TotalSeconds:F1}s");
                    });
                    threads[idx].Start();
                }

                foreach (Thread t in threads)
                {
                    t.Join();
                }
            }

            DiagLog($"All {count} worktree adds completed in {sw.Elapsed.TotalSeconds:F1}s");
            return results;
        }

        /// <summary>
        /// Asserts that the GVFS mount for a worktree is running by probing
        /// the worktree-specific named pipe. This is the definitive signal
        /// that ProjFS projection is active — much stronger than File.Exists
        /// which depends on projection timing.
        /// </summary>
        private void AssertWorktreeMounted(string worktreePath, string label)
        {
            string basePipeName = GVFSPlatform.Instance.GetNamedPipeName(
                this.Enlistment.EnlistmentRoot);
            string suffix = GVFSEnlistment.GetWorktreePipeSuffix(worktreePath);

            Assert.IsNotNull(suffix,
                $"Could not determine pipe suffix for {label} at {worktreePath}. " +
                $"The worktree .git file may be missing or malformed.");

            string pipeName = basePipeName + suffix;

            using (NamedPipeClient client = new NamedPipeClient(pipeName))
            {
                if (!client.Connect(10000))
                {
                    string diagnostics = this.CaptureWorktreeDiagnostics(worktreePath);
                    Assert.Fail(
                        $"GVFS mount is NOT running for {label}.\n" +
                        $"Path: {worktreePath}\n" +
                        $"Pipe: {pipeName}\n" +
                        $"This indicates the post-hook 'gvfs mount' failed silently.\n" +
                        $"Diagnostics:\n{diagnostics}");
                }
            }
        }

        private string CaptureWorktreeDiagnostics(string worktreePath)
        {
            StringBuilder sb = new StringBuilder();

            sb.AppendLine($"  Directory exists: {Directory.Exists(worktreePath)}");
            if (Directory.Exists(worktreePath))
            {
                string dotGit = Path.Combine(worktreePath, ".git");
                sb.AppendLine($"  .git file exists: {File.Exists(dotGit)}");
                if (File.Exists(dotGit))
                {
                    try
                    {
                        sb.AppendLine($"  .git contents: {File.ReadAllText(dotGit).Trim()}");
                    }
                    catch (Exception ex)
                    {
                        sb.AppendLine($"  .git read failed: {ex.Message}");
                    }
                }

                try
                {
                    string[] entries = Directory.GetFileSystemEntries(worktreePath);
                    sb.AppendLine($"  Directory listing ({entries.Length} entries):");
                    foreach (string entry in entries)
                    {
                        sb.AppendLine($"    {Path.GetFileName(entry)}");
                    }
                }
                catch (Exception ex)
                {
                    sb.AppendLine($"  Directory listing failed: {ex.Message}");
                }
            }

            return sb.ToString();
        }

        private void CleanupAllWorktrees(string[] paths, string[] branches, int count)
        {
            for (int i = 0; i < count; i++)
            {
                this.ForceCleanupWorktree(paths[i], branches[i]);
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

        private static void DiagLog(string message)
        {
            Console.Error.WriteLine($"[CI-DEBUG] [WorktreeTests] {message}");
            Console.Error.Flush();
        }
    }
}
