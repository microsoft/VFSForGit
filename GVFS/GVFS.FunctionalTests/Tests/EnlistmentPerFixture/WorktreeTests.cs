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
using ProcessHelper = GVFS.FunctionalTests.Tools.ProcessHelper;
using ProcessResult = GVFS.FunctionalTests.Tools.ProcessResult;

namespace GVFS.FunctionalTests.Tests.EnlistmentPerFixture
{
    [TestFixture]
    [Category(Categories.GitCommands)]
    public class WorktreeTests : TestsWithEnlistmentPerFixture
    {
        private const int MinWorktreeCount = 4;

        [TestCase]
        [Repeat(10)]
        public void ConcurrentWorktreeAddCommitRemove()
        {
            int count = Math.Max(Environment.ProcessorCount, MinWorktreeCount);
            string[] worktreePaths;
            string[] branchNames;

            // Adaptively scale down if concurrent adds overwhelm the primary
            // GVFS mount. CI runners with fewer resources may not handle as
            // many concurrent git operations as a developer workstation.
            while (true)
            {
                this.InitWorktreeArrays(count, out worktreePaths, out branchNames);
                ProcessResult[] addResults = this.ConcurrentWorktreeAdd(worktreePaths, branchNames, count);

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
                for (int i = 0; i < count; i++)
                {
                    this.AssertWorktreeMounted(worktreePaths[i], $"worktree [{i}]");
                }

                // 3. Verify projected files are visible (secondary assertion)
                for (int i = 0; i < count; i++)
                {
                    Directory.Exists(worktreePaths[i]).ShouldBeTrue(
                        $"Worktree [{i}] directory should exist");
                    File.Exists(Path.Combine(worktreePaths[i], "Readme.md")).ShouldBeTrue(
                        $"Readme.md should be projected in [{i}]");
                }

                // 4. Verify git status is clean in each worktree
                for (int i = 0; i < count; i++)
                {
                    ProcessResult status = GitHelpers.InvokeGitAgainstGVFSRepo(
                        worktreePaths[i], "status --porcelain");
                    status.ExitCode.ShouldEqual(0,
                        $"git status [{i}] failed: {status.Errors}");
                    status.Output.Trim().ShouldBeEmpty(
                        $"Worktree [{i}] should have clean status");
                }

                // 5. Verify worktree list shows all entries
                ProcessResult listResult = GitHelpers.InvokeGitAgainstGVFSRepo(
                    this.Enlistment.RepoRoot, "worktree list");
                listResult.ExitCode.ShouldEqual(0, $"worktree list failed: {listResult.Errors}");
                string listOutput = listResult.Output;
                for (int i = 0; i < count; i++)
                {
                    Assert.IsTrue(
                        listOutput.Contains(worktreePaths[i].Replace('\\', '/')),
                        $"worktree list should contain [{i}]. Output: {listOutput}");
                }

                // 6. Make commits in all worktrees
                for (int i = 0; i < count; i++)
                {
                    File.WriteAllText(
                        Path.Combine(worktreePaths[i], $"from-{i}.txt"),
                        $"created in worktree {i}");
                    GitHelpers.InvokeGitAgainstGVFSRepo(worktreePaths[i], $"add from-{i}.txt")
                        .ExitCode.ShouldEqual(0);
                    GitHelpers.InvokeGitAgainstGVFSRepo(
                        worktreePaths[i], $"commit -m \"commit from {i}\"")
                        .ExitCode.ShouldEqual(0);
                }

                // 7. Verify commits are visible from main repo
                for (int i = 0; i < count; i++)
                {
                    GitHelpers.InvokeGitAgainstGVFSRepo(
                        this.Enlistment.RepoRoot, $"log -1 --format=%s {branchNames[i]}")
                        .Output.ShouldContain(expectedSubstrings: new[] { $"commit from {i}" });
                }

                // 8. Verify cross-worktree commit visibility (shared objects)
                for (int i = 0; i < count; i++)
                {
                    int other = (i + 1) % count;
                    GitHelpers.InvokeGitAgainstGVFSRepo(
                        worktreePaths[i], $"log -1 --format=%s {branchNames[other]}")
                        .Output.ShouldContain(expectedSubstrings: new[] { $"commit from {other}" });
                }

                // 9. Remove all worktrees in parallel
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
            }
            finally
            {
                this.CleanupAllWorktrees(worktreePaths, branchNames, count);
            }
        }

        [TestCase]
        public void WorktreeOutsideEnlistmentTree()
        {
            string suffix = Guid.NewGuid().ToString("N").Substring(0, 8);
            string tempDir = Path.Combine(Path.GetTempPath(), $"gvfs-remote-wt-{suffix}");
            string worktreePath = Path.Combine(tempDir, "wt");
            string branchName = $"remote-wt-test-{suffix}";

            try
            {
                Directory.CreateDirectory(tempDir);

                // 1. Create worktree outside the enlistment tree
                ProcessResult addResult = GitHelpers.InvokeGitAgainstGVFSRepo(
                    this.Enlistment.RepoRoot,
                    $"worktree add -b {branchName} \"{worktreePath}\"");
                addResult.ExitCode.ShouldEqual(0,
                    $"worktree add failed: {addResult.Errors}");

                // 2. Verify GVFS mount is running
                this.AssertWorktreeMounted(worktreePath, "remote worktree");

                // 3. Verify git status works from the worktree root
                ProcessResult statusResult = GitHelpers.InvokeGitAgainstGVFSRepo(
                    worktreePath, "status --porcelain");
                statusResult.ExitCode.ShouldEqual(0,
                    $"git status from worktree root failed: {statusResult.Errors}");
                statusResult.Output.Trim().ShouldBeEmpty(
                    "Remote worktree should have clean status");

                // 4. Verify projected files are visible
                File.Exists(Path.Combine(worktreePath, "Readme.md")).ShouldBeTrue(
                    "Readme.md should be projected in remote worktree");

                // 5. Verify git status works from a subdirectory
                string subDir = Path.Combine(worktreePath, "GVFS");
                Directory.Exists(subDir).ShouldBeTrue(
                    "Subdirectory GVFS should be projected");
                ProcessResult subDirStatus = GitHelpers.InvokeGitAgainstGVFSRepo(
                    subDir, "status --porcelain");
                subDirStatus.ExitCode.ShouldEqual(0,
                    $"git status from subdirectory failed: {subDirStatus.Errors}");

                // 6. Verify commits work
                File.WriteAllText(
                    Path.Combine(worktreePath, "remote-test.txt"),
                    "created in remote worktree");
                GitHelpers.InvokeGitAgainstGVFSRepo(worktreePath, "add remote-test.txt")
                    .ExitCode.ShouldEqual(0);
                GitHelpers.InvokeGitAgainstGVFSRepo(
                    worktreePath, "commit -m \"commit from remote worktree\"")
                    .ExitCode.ShouldEqual(0);

                // 7. Verify commit is visible from primary repo
                GitHelpers.InvokeGitAgainstGVFSRepo(
                    this.Enlistment.RepoRoot, $"log -1 --format=%s {branchName}")
                    .Output.ShouldContain(expectedSubstrings: new[] { "commit from remote worktree" });

                // 8. Remove worktree
                ProcessResult removeResult = GitHelpers.InvokeGitAgainstGVFSRepo(
                    this.Enlistment.RepoRoot,
                    $"worktree remove --force \"{worktreePath}\"");
                removeResult.ExitCode.ShouldEqual(0,
                    $"worktree remove failed: {removeResult.Errors}");
                Directory.Exists(worktreePath).ShouldBeFalse(
                    "Remote worktree directory should be deleted");
            }
            finally
            {
                this.ForceCleanupWorktree(worktreePath, branchName);
                if (Directory.Exists(tempDir))
                {
                    try { Directory.Delete(tempDir, recursive: true); } catch { }
                }
            }
        }

        [TestCase]
        public void WorktreeUsesPerWorktreePlaceholderDatabase()
        {
            // Two commits where test4.txt has different content
            const string CommitA = "5d7a7d4db1734fb468a4094469ec58d26301b59d";
            const string CommitB = "fec239ea12de1eda6ae5329d4f345784d5b61ff9";
            string testFileRelativePath = Path.Combine(
                "Test_EPF_UpdatePlaceholderTests",
                "LockToPreventUpdateAndDelete",
                "test4.txt");
            const string ContentAtCommitA = "TestFileLockToPreventUpdateAndDelete4 \r\n";
            const string ContentAtCommitB = "Commit2LockToPreventUpdateAndDelete4 \r\n";

            string suffix = Guid.NewGuid().ToString("N").Substring(0, 8);
            string tempDir = Path.Combine(Path.GetTempPath(), $"gvfs-db-test-{suffix}");
            string worktreePath = Path.Combine(tempDir, "wt");
            string branchName = $"db-test-branch-{suffix}";

            try
            {
                Directory.CreateDirectory(tempDir);

                // 1. Checkout commitB in the primary and read the file to
                //    create a placeholder with version B content.
                GitHelpers.InvokeGitAgainstGVFSRepo(
                    this.Enlistment.RepoRoot, $"checkout {CommitB}")
                    .ExitCode.ShouldEqual(0, "checkout commitB in primary failed");

                string primaryFilePath = Path.Combine(this.Enlistment.RepoRoot, testFileRelativePath);
                File.ReadAllText(primaryFilePath).ShouldEqual(ContentAtCommitB,
                    "Primary should have version B content");

                // 2. Create a worktree at commitA and read the same file.
                //    Without the fix, the worktree mount writes the placeholder
                //    entry (SHA_A) to the primary's DB, overwriting SHA_B.
                ProcessResult addResult = GitHelpers.InvokeGitAgainstGVFSRepo(
                    this.Enlistment.RepoRoot,
                    $"worktree add -b {branchName} \"{worktreePath}\" {CommitA}");
                addResult.ExitCode.ShouldEqual(0,
                    $"worktree add failed: {addResult.Errors}");

                this.AssertWorktreeMounted(worktreePath, "db-test worktree");

                string worktreeFilePath = Path.Combine(worktreePath, testFileRelativePath);
                File.ReadAllText(worktreeFilePath).ShouldEqual(ContentAtCommitA,
                    "Worktree should have version A content");

                // 3. Checkout commitA in the primary.  UpdatePlaceholders reads
                //    the placeholder DB to find files that need updating.
                //    With a contaminated DB (SHA_A from worktree), the primary's
                //    placeholder (on-disk SHA_B) looks up-to-date (DB says SHA_A,
                //    index wants SHA_A — match) and the update is skipped.
                //    The file retains stale commitB content.
                GitHelpers.InvokeGitAgainstGVFSRepo(
                    this.Enlistment.RepoRoot, $"checkout {CommitA}")
                    .ExitCode.ShouldEqual(0, "checkout commitA in primary failed");

                string contentAfterCheckout = File.ReadAllText(primaryFilePath);
                contentAfterCheckout.ShouldEqual(ContentAtCommitA,
                    $"After checkout to commitA, primary should have version A content.\n" +
                    $"Got version B content instead — the worktree mount contaminated\n" +
                    $"the primary's placeholder DB, causing UpdatePlaceholders to skip\n" +
                    $"the update because the DB SHA matched the index SHA.");

                // 4. Verify the per-worktree placeholder DB exists (isolation check)
                GVFSEnlistment.WorktreeInfo wtInfo = GVFSEnlistment.TryGetWorktreeInfo(worktreePath);
                Assert.IsNotNull(wtInfo, "Should be able to resolve worktree info");
                string worktreeDbPath = Path.Combine(
                    wtInfo.WorktreeGitDir, ".gvfs", "databases", "VFSForGit.sqlite");

                bool dbExists = false;
                for (int i = 0; i < 20; i++)
                {
                    if (File.Exists(worktreeDbPath))
                    {
                        dbExists = true;
                        break;
                    }

                    System.Threading.Thread.Sleep(500);
                }

                Assert.IsTrue(dbExists,
                    $"Per-worktree VFSForGit.sqlite should exist at {worktreeDbPath}");
            }
            finally
            {
                this.ForceCleanupWorktree(worktreePath, branchName);
                if (Directory.Exists(tempDir))
                {
                    try { Directory.Delete(tempDir, recursive: true); } catch { }
                }
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void WorktreeDehydratePreservesPrimaryIndex(bool trailingSeparator)
        {
            string suffix = Guid.NewGuid().ToString("N").Substring(0, 8);
            string worktreeName = $"index-dehydrate-{suffix}";
            string worktreePath = Path.Combine(this.Enlistment.EnlistmentRoot, worktreeName);
            string branchName = $"index-dehydrate-branch-{suffix}";
            string backupParent = Path.Combine(this.Enlistment.EnlistmentRoot, "dehydrate_backup", worktreeName);

            try
            {
                ProcessResult addResult = GitHelpers.InvokeGitAgainstGVFSRepo(
                    this.Enlistment.RepoRoot,
                    $"worktree add -b {branchName} \"{worktreePath}\"");
                addResult.ExitCode.ShouldEqual(0, addResult.Errors);
                this.AssertWorktreeMounted(worktreePath, "index dehydrate worktree");

                File.WriteAllText(Path.Combine(worktreePath, "worktree-index-only.txt"), "Linked worktree content");
                GitHelpers.InvokeGitAgainstGVFSRepo(worktreePath, "add worktree-index-only.txt").ExitCode.ShouldEqual(0);
                GitHelpers.InvokeGitAgainstGVFSRepo(worktreePath, "commit -m \"Add worktree-only index entry\"").ExitCode.ShouldEqual(0);

                string worktreeIndex = this.GetGitIndexPath(worktreePath);
                string primaryIndex = this.GetGitIndexPath(this.Enlistment.RepoRoot);
                byte[] primaryIndexBefore = File.ReadAllBytes(primaryIndex);
                byte[] worktreeIndexBefore = File.ReadAllBytes(worktreeIndex);
                primaryIndexBefore.SequenceEqual(worktreeIndexBefore).ShouldBeFalse("The indexes must differ so the backup assertion detects the wrong source.");

                ProcessStartInfo startInfo = new ProcessStartInfo(GVFSTestConfig.PathToGVFS)
                {
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    WorkingDirectory = this.Enlistment.EnlistmentRoot,
                };
                startInfo.ArgumentList.Add("dehydrate");
                startInfo.ArgumentList.Add(worktreePath + (trailingSeparator ? Path.DirectorySeparatorChar.ToString() : string.Empty));
                startInfo.ArgumentList.Add("--confirm");
                startInfo.ArgumentList.Add("--folders");
                startInfo.ArgumentList.Add("GVFS");
                startInfo.ArgumentList.Add(TestConstants.InternalUseOnlyFlag);
                startInfo.ArgumentList.Add("{\"ServiceName\":\"" + this.Enlistment.ServiceName + "\"}");
                ProcessResult dehydrate = ProcessHelper.Run(startInfo, timeoutMs: 120000);
                dehydrate.ExitCode.ShouldEqual(0, dehydrate.Output + dehydrate.Errors);
                dehydrate.Output.ShouldContain("GVFS folder dehydrate successful.");
                CollectionAssert.AreEqual(primaryIndexBefore, File.ReadAllBytes(primaryIndex), "The primary index must not change.");

                string[] backups = Directory.GetDirectories(backupParent);
                backups.Length.ShouldEqual(1);
                CollectionAssert.AreEqual(
                    worktreeIndexBefore,
                    File.ReadAllBytes(Path.Combine(backups[0], ".git", "index")),
                    "The backup must contain the linked worktree index.");
            }
            finally
            {
                this.ForceCleanupWorktree(worktreePath, branchName);
                if (Directory.Exists(backupParent))
                {
                    Directory.Delete(backupParent, recursive: true);
                }
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void FullDehydrateRejectsLinkedWorktreesBeforeMutation(bool fromWorktree)
        {
            string suffix = Guid.NewGuid().ToString("N").Substring(0, 8);
            string worktreePath = Path.Combine(this.Enlistment.EnlistmentRoot, $"full-guard-{suffix}");
            string branchName = $"full-guard-branch-{suffix}";
            try
            {
                ProcessResult addResult = GitHelpers.InvokeGitAgainstGVFSRepo(
                    this.Enlistment.RepoRoot,
                    $"worktree add -b {branchName} \"{worktreePath}\"");
                addResult.ExitCode.ShouldEqual(0, addResult.Errors);
                this.AssertWorktreeMounted(worktreePath, "full dehydrate guard worktree");

                string primaryIndex = this.GetGitIndexPath(this.Enlistment.RepoRoot);
                string worktreeIndex = this.GetGitIndexPath(worktreePath);
                byte[] primaryIndexBefore = File.ReadAllBytes(primaryIndex);
                byte[] worktreeIndexBefore = File.ReadAllBytes(worktreeIndex);
                string dehydratePath = fromWorktree ? worktreePath : this.Enlistment.EnlistmentRoot;
                ProcessResult result = ProcessHelper.Run(
                    GVFSTestConfig.PathToGVFS,
                    $"dehydrate \"{dehydratePath}\" --full --confirm {TestConstants.InternalUseOnlyFlag} " +
                    GVFSHelpers.GetInternalParameter(serviceName: this.Enlistment.ServiceName),
                    workingDirectory: this.Enlistment.EnlistmentRoot,
                    timeoutMs: 120000);

                result.ExitCode.ShouldEqual(3, result.Output + result.Errors);
                result.Output.ShouldContain(fromWorktree
                    ? "Dehydrate --full is not supported for git worktrees."
                    : "Dehydrate --full is not supported while linked worktrees exist.");
                CollectionAssert.AreEqual(primaryIndexBefore, File.ReadAllBytes(primaryIndex));
                CollectionAssert.AreEqual(worktreeIndexBefore, File.ReadAllBytes(worktreeIndex));
                this.AssertWorktreeMounted(worktreePath, "worktree after rejected full dehydrate");
            }
            finally
            {
                this.ForceCleanupWorktree(worktreePath, branchName);
            }
        }

        [TestCase("missing")]
        [TestCase("empty")]
        [TestCase("unreadable")]
        public void FullDehydrateRejectsUsableWorktreeOmittedByGit(string pointerState)
        {
            string suffix = Guid.NewGuid().ToString("N").Substring(0, 8);
            string worktreePath = Path.Combine(this.Enlistment.EnlistmentRoot, $"hidden-guard-{suffix}");
            string branchName = $"hidden-guard-{suffix}";
            string reversePointer = null;
            byte[] originalPointer = null;
            FileStream lockedPointer = null;
            try
            {
                ProcessResult addResult = GitHelpers.InvokeGitAgainstGVFSRepo(
                    this.Enlistment.RepoRoot, $"worktree add -b {branchName} \"{worktreePath}\"");
                addResult.ExitCode.ShouldEqual(0, addResult.Errors);
                this.AssertWorktreeMounted(worktreePath, "worktree with incomplete reverse pointer");
                string primaryIndex = this.GetGitIndexPath(this.Enlistment.RepoRoot);
                string worktreeIndex = this.GetGitIndexPath(worktreePath);
                reversePointer = Path.Combine(Path.GetDirectoryName(worktreeIndex), "gitdir");
                originalPointer = File.ReadAllBytes(reversePointer);
                if (pointerState == "missing")
                {
                    File.Delete(reversePointer);
                }
                else if (pointerState == "empty")
                {
                    File.WriteAllBytes(reversePointer, Array.Empty<byte>());
                }
                else
                {
                    lockedPointer = new FileStream(reversePointer, FileMode.Open, FileAccess.Read, FileShare.None);
                }

                ProcessResult list = GitHelpers.InvokeGitAgainstGVFSRepo(this.Enlistment.RepoRoot, "worktree list --porcelain");
                list.ExitCode.ShouldEqual(0, list.Errors);
                list.Output.Contains(worktreePath.Replace('\\', '/')).ShouldBeFalse("Git must omit the worktree in this regression scenario.");
                ProcessResult status = GitHelpers.InvokeGitAgainstGVFSRepo(worktreePath, "status --porcelain=v1");
                status.ExitCode.ShouldEqual(0, status.Errors);

                byte[] primaryIndexBefore = File.ReadAllBytes(primaryIndex);
                byte[] worktreeIndexBefore = File.ReadAllBytes(worktreeIndex);
                ProcessResult result = ProcessHelper.Run(
                    GVFSTestConfig.PathToGVFS,
                    $"dehydrate \"{this.Enlistment.EnlistmentRoot}\" --full --confirm {TestConstants.InternalUseOnlyFlag} " +
                    GVFSHelpers.GetInternalParameter(serviceName: this.Enlistment.ServiceName),
                    workingDirectory: this.Enlistment.EnlistmentRoot,
                    timeoutMs: 120000);
                result.ExitCode.ShouldEqual(3, result.Output + result.Errors);
                result.Output.ShouldContain("Full dehydrate is blocked.");
                result.Output.ShouldContain(reversePointer);
                CollectionAssert.AreEqual(primaryIndexBefore, File.ReadAllBytes(primaryIndex));
                CollectionAssert.AreEqual(worktreeIndexBefore, File.ReadAllBytes(worktreeIndex));
                this.AssertWorktreeMounted(worktreePath, "worktree after blocked full dehydrate");
            }
            finally
            {
                lockedPointer?.Dispose();
                if (originalPointer != null)
                {
                    File.WriteAllBytes(reversePointer, originalPointer);
                }

                this.ForceCleanupWorktree(worktreePath, branchName);
            }
        }

        [TestCase]
        public void WorktreeDehydrateReportsBackupFailure()
        {
            string suffix = Guid.NewGuid().ToString("N").Substring(0, 8);
            string worktreeName = $"backup-failure-{suffix}";
            string worktreePath = Path.Combine(this.Enlistment.EnlistmentRoot, worktreeName);
            string branchName = $"backup-failure-branch-{suffix}";
            string backupParent = Path.Combine(this.Enlistment.EnlistmentRoot, "dehydrate_backup", worktreeName);
            try
            {
                ProcessResult addResult = GitHelpers.InvokeGitAgainstGVFSRepo(
                    this.Enlistment.RepoRoot,
                    $"worktree add -b {branchName} \"{worktreePath}\"");
                addResult.ExitCode.ShouldEqual(0, addResult.Errors);
                this.AssertWorktreeMounted(worktreePath, "backup failure worktree");
                string primaryIndex = this.GetGitIndexPath(this.Enlistment.RepoRoot);
                byte[] primaryIndexBefore = File.ReadAllBytes(primaryIndex);
                string worktreeIndex = this.GetGitIndexPath(worktreePath);
                using (FileStream locked = new FileStream(worktreeIndex, FileMode.Open, FileAccess.Read, FileShare.None))
                {
                    ProcessResult result = ProcessHelper.Run(
                        GVFSTestConfig.PathToGVFS,
                        $"dehydrate \"{worktreePath}\" --confirm --no-status --folders GVFS {TestConstants.InternalUseOnlyFlag} " +
                        GVFSHelpers.GetInternalParameter(serviceName: this.Enlistment.ServiceName),
                        workingDirectory: this.Enlistment.EnlistmentRoot,
                        timeoutMs: 120000);
                    result.ExitCode.ShouldEqual(3, result.Output + result.Errors);
                    result.Output.ShouldContain("Failed to back up");
                    result.Output.ShouldContain(worktreeIndex);
                }

                CollectionAssert.AreEqual(primaryIndexBefore, File.ReadAllBytes(primaryIndex));
            }
            finally
            {
                this.ForceCleanupWorktree(worktreePath, branchName);
                if (Directory.Exists(backupParent))
                {
                    Directory.Delete(backupParent, recursive: true);
                }
            }
        }

        private string GetGitIndexPath(string workingDirectory)
        {
            ProcessResult result = GitHelpers.InvokeGitAgainstGVFSRepo(
                workingDirectory,
                "rev-parse --path-format=absolute --git-path index");
            result.ExitCode.ShouldEqual(0, result.Errors);
            return Path.GetFullPath(result.Output.Trim());
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
                        results[idx] = GitHelpers.InvokeGitAgainstGVFSRepo(
                            this.Enlistment.RepoRoot,
                            $"worktree add -b {branches[idx]} \"{paths[idx]}\"");
                    });
                    threads[idx].Start();
                }

                foreach (Thread t in threads)
                {
                    t.Join();
                }
            }

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

            GVFSEnlistment.WorktreeInfo wtInfo = GVFSEnlistment.TryGetWorktreeInfo(worktreePath);
            if (wtInfo == null)
            {
                sb.AppendLine("  WorktreeInfo: null");
            }
            else
            {
                sb.AppendLine($"  WorktreeInfo.Name: {wtInfo.Name}");
                sb.AppendLine($"  WorktreeInfo.WorktreePath: {wtInfo.WorktreePath}");
                sb.AppendLine($"  WorktreeInfo.WorktreeGitDir: {wtInfo.WorktreeGitDir}");
                sb.AppendLine($"  WorktreeInfo.SharedGitDir: {wtInfo.SharedGitDir}");
                sb.AppendLine($"  WorktreeInfo.PipeSuffix: {wtInfo.PipeSuffix}");

                this.AppendDirectoryListing(sb, "worktree git dir", wtInfo.WorktreeGitDir);
                this.AppendDirectoryListing(
                    sb,
                    "worktree hooks dir",
                    Path.Combine(wtInfo.WorktreeGitDir, GVFSConstants.DotGit.Hooks.RootName));
                this.AppendDirectoryListing(
                    sb,
                    "worktree .gvfs dir",
                    Path.Combine(wtInfo.WorktreeGitDir, GVFSPlatform.Instance.Constants.DotGVFSRoot));
                this.AppendWorktreeMountLogs(sb, wtInfo);
            }

            this.AppendMountBootstrapTraces(sb, wtInfo == null ? null : wtInfo.Name);

            return sb.ToString();
        }

        private void AppendMountBootstrapTraces(StringBuilder sb, string worktreeName)
        {
            string dir = Path.Combine(
                Environment.GetEnvironmentVariable("GVFS_TEST_DIAGNOSTICS_DIR") ?? @"C:\temp\gvfs-ft-diagnostics",
                "mount-bootstrap");
            sb.AppendLine($"  mount bootstrap traces: {dir}");
            if (!Directory.Exists(dir))
            {
                sb.AppendLine("    exists: False");
                return;
            }

            try
            {
                foreach (FileInfo file in new DirectoryInfo(dir)
                    .GetFiles("mount_*.log")
                    .Where(f => f.LastWriteTimeUtc > DateTime.UtcNow.AddMinutes(-3))
                    .OrderBy(f => f.LastWriteTimeUtc))
                {
                    string contents = File.ReadAllText(file.FullName);
                    if (worktreeName != null && !contents.Contains(worktreeName))
                    {
                        continue;
                    }

                    sb.AppendLine($"    ----- {file.Name} -----");
                    sb.AppendLine(contents.Length <= 6000 ? contents : contents.Substring(contents.Length - 6000));
                }
            }
            catch (Exception ex)
            {
                sb.AppendLine($"    bootstrap trace capture failed: {ex}");
            }
        }

        private void AppendDirectoryListing(StringBuilder sb, string label, string path)
        {
            sb.AppendLine($"  {label}: {path}");
            sb.AppendLine($"    exists: {Directory.Exists(path)}");
            if (!Directory.Exists(path))
            {
                return;
            }

            try
            {
                string[] entries = Directory.GetFileSystemEntries(path);
                sb.AppendLine($"    entries ({entries.Length}):");
                foreach (string entry in entries)
                {
                    FileSystemInfo info = Directory.Exists(entry)
                        ? (FileSystemInfo)new DirectoryInfo(entry)
                        : new FileInfo(entry);
                    sb.AppendLine($"      {Path.GetFileName(entry)} ({info.Attributes})");
                }
            }
            catch (Exception ex)
            {
                sb.AppendLine($"    listing failed: {ex}");
            }
        }

        private void AppendWorktreeMountLogs(StringBuilder sb, GVFSEnlistment.WorktreeInfo wtInfo)
        {
            string logsRoot = Path.Combine(
                wtInfo.WorktreeGitDir,
                GVFSPlatform.Instance.Constants.DotGVFSRoot,
                GVFSConstants.DotGVFS.LogName);
            sb.AppendLine($"  worktree logs dir: {logsRoot}");
            sb.AppendLine($"    exists: {Directory.Exists(logsRoot)}");
            if (!Directory.Exists(logsRoot))
            {
                return;
            }

            try
            {
                FileInfo[] logFiles = new DirectoryInfo(logsRoot)
                    .GetFiles("*.log")
                    .OrderByDescending(file => file.LastWriteTimeUtc)
                    .Take(5)
                    .ToArray();

                foreach (FileInfo logFile in logFiles)
                {
                    sb.AppendLine($"    ----- {logFile.FullName} ({logFile.Length} bytes) -----");
                    sb.AppendLine(this.ReadTail(logFile.FullName, maxChars: 12000));
                }
            }
            catch (Exception ex)
            {
                sb.AppendLine($"    log capture failed: {ex}");
            }
        }

        private string ReadTail(string path, int maxChars)
        {
            string contents = File.ReadAllText(path);
            if (contents.Length <= maxChars)
            {
                return contents;
            }

            return contents.Substring(contents.Length - maxChars);
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
    }
}
