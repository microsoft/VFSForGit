using GVFS.FunctionalTests.Tools;
using GVFS.Tests.Should;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace GVFS.FunctionalTests.Tests.EnlistmentPerFixture
{
    /// <summary>
    /// Functional tests for commands that write the index when the mount cannot widen the
    /// sparse-index cone or apply a pending working-tree change. Without the cone change, Git has
    /// no index entry for a file outside the cone: it fails, or it does not see a local change
    /// and reports success. The pre-command hook must stop such a command before Git runs, and
    /// let other commands run with a warning.
    /// </summary>
    /// <remarks>
    /// The fixture mounts with the test-only fault file of the mount (see
    /// <c>ConeRequestFaultInjection</c>), so that a test can make the mount refuse a request.
    /// </remarks>
    [TestFixture]
    public class AutoSparseIndexFailClosedTests : AutoSparseIndexTestsBase
    {
        private const string FaultFileEnvironmentVariable = "GVFS_TEST_CONE_REQUEST_FAULT_FILE";
        private const string RefuseWidenAndApply = "ConeWiden F\nConeApply F\n";
        private const string RefuseApplyAsNotLockHolder = "ConeApply NL\n";
        private const string StoppedMessage = "GVFS could not prepare the sparse index";
        private const string UnstageStoppedMessage = "VFS for Git could not add the staged files to the sparse-index cone";
        private const string StaleIndexWarning = "GVFS: the sparse index is not up to date (apply: Rejected); this command can miss changes outside the sparse-checkout cone. Run it again.";
        private const string LocalChange = "local change outside the cone";
        private const string ConeChangePendingFile = "ConeChangePending";

        // The test cases of this fixture take this many files.
        private const int FilesUsed = 28;

        // Git refuses a command that would overwrite a local change with one of these messages.
        private static readonly string[] LocalChangeRefusals =
        {
            "would be overwritten",
            "You have unstaged changes",
            "not uptodate",
        };

        // Files of HEAD, each in a different leaf directory outside the root-only cone
        private Queue<string> unusedFiles;
        private string faultFile;
        private string startBranch;
        private string startCommit;
        private int branchCount;

        [OneTimeSetUp]
        public void MountWithTheFaultFile()
        {
            List<IGrouping<string, string>> parents = this.DeepHeadFiles
                .GroupBy(path => ParentDirectory(path), StringComparer.OrdinalIgnoreCase)
                .ToList();
            this.unusedFiles = new Queue<string>(parents
                .Where(group => !parents.Any(other => other.Key.StartsWith(group.Key + "/", StringComparison.OrdinalIgnoreCase)))
                .Select(group => group.First()));
            this.unusedFiles.Count.ShouldBeAtLeast(FilesUsed, "HEAD has too few leaf directories");

            this.faultFile = Path.Combine(this.Enlistment.EnlistmentRoot, "cone-request-faults.txt");
            this.SetFaults(string.Empty);

            // The mount reads the variable when it starts.
            Environment.SetEnvironmentVariable(FaultFileEnvironmentVariable, this.faultFile);
            try
            {
                this.Enlistment.UnmountGVFS();
                this.Enlistment.MountGVFS();
            }
            finally
            {
                Environment.SetEnvironmentVariable(FaultFileEnvironmentVariable, null);
            }

            this.startBranch = this.GitShouldSucceed("rev-parse --abbrev-ref HEAD").Trim();
            this.startCommit = this.GitShouldSucceed("rev-parse HEAD").Trim();
        }

        [TearDown]
        public void ResetToTheStart()
        {
            this.SetFaults(string.Empty);
            this.GitShouldSucceed("reset --hard --quiet");
            this.GitShouldSucceed("checkout -q -f " + this.startBranch);
            this.GitShouldSucceed("reset --hard --quiet " + this.startCommit);
            this.GitShouldSucceed("stash clear");
        }

        [TestCase("add -- {0}", "widen", "staged")]
        [TestCase("add .", "apply", "staged")]
        [TestCase("add -A", "apply", "staged")]
        [TestCase("add -u", "apply", "staged")]
        [TestCase("rm --cached -q -- {0}", "widen", "removed")]
        [TestCase("commit -a -m \"Edited outside the cone\"", "apply", "committed")]
        [TestCase("commit -m \"Edited outside the cone\" -- {0}", "widen", "committed")]
        [TestCase("stash", "apply", "stashed")]
        [TestCase("stash push -- {0}", "apply", "stashed")]
        [TestCase("checkout -- {0}", "widen", "discarded")]
        [TestCase("restore -- {0}", "widen", "discarded")]
        public void WorkingTreeCommandStopsAndRunsAfterTheFaultIsGone(string commandFormat, string request, string effect)
        {
            string file = this.unusedFiles.Dequeue();
            string command = string.Format(commandFormat, file);
            this.EditWhileTheMountRefuses(file, RefuseWidenAndApply);

            this.ShouldStopBeforeGitRuns(command, $"{StoppedMessage} ({request}: Rejected). Run the command again.", file);

            this.SetFaults(string.Empty);
            ProcessResult retry = GitProcess.InvokeProcess(this.Enlistment.RepoRoot, command);
            retry.ExitCode.ShouldEqual(0, $"git {command} failed after the fault was removed: {retry.Output} {retry.Errors}");
            switch (effect)
            {
                case "staged":
                    this.GitShouldSucceed("diff --cached --name-only").ShouldContain(file);
                    break;

                case "removed":
                    this.GitShouldSucceed("diff --cached --name-status").ShouldContain("D\t" + file);
                    break;

                case "committed":
                    this.GitShouldSucceed("diff --name-only HEAD~1 HEAD").ShouldContain(file);
                    break;

                case "stashed":
                    this.GitShouldSucceed("stash show --name-only").ShouldContain(file);
                    this.HasLocalChange(file).ShouldBeFalse();
                    break;

                case "discarded":
                    this.HasLocalChange(file).ShouldBeFalse();
                    break;

                default:
                    Assert.Fail("Unknown effect " + effect);
                    break;
            }
        }

        [TestCase("restore --staged -- {0}", false)]
        [TestCase("checkout HEAD -- {0}", true)]
        public void UnstageCommandStopsForAFailedWidenAndRunsAfterTheFaultIsGone(string commandFormat, bool discardsTheChange)
        {
            // An unstage writes the index, so a failed widen stops it, as it stops a command that
            // writes the index.
            string file = this.unusedFiles.Dequeue();
            string command = string.Format(commandFormat, file);
            this.EditWhileTheMountRefuses(file, RefuseWidenAndApply);

            this.ShouldStopBeforeGitRuns(command, UnstageStoppedMessage + " (widen: Rejected).", file);

            this.SetFaults(string.Empty);
            ProcessResult retry = GitProcess.InvokeProcess(this.Enlistment.RepoRoot, command);
            retry.ExitCode.ShouldEqual(0, $"git {command} failed after the fault was removed: {retry.Output} {retry.Errors}");
            this.HasLocalChange(file).ShouldEqual(!discardsTheChange);
            this.GitShouldSucceed("diff --cached --name-only").ShouldNotContain(ignoreCase: true, file);
        }
        [TestCase("checkout -q {0}")]
        [TestCase("switch -q {0}")]
        [TestCase("merge --no-edit {0}")]
        [TestCase("cherry-pick {0}")]
        [TestCase("rebase {0}")]
        [TestCase("reset -q --keep {0}")]
        public void HistoryCommandStopsAndGitRefusesAfterTheFaultIsGone(string commandFormat)
        {
            // A branch changes the file, and the file has a local change. Git must refuse to
            // move HEAD, because it would leave the file without the change of the branch.
            string file = this.unusedFiles.Dequeue();
            string command = string.Format(commandFormat, this.CreateBranchThatChanges(file));
            this.EditWhileTheMountRefuses(file, RefuseWidenAndApply);

            this.ShouldStopBeforeGitRuns(command, StoppedMessage + " (apply: Rejected). Run the command again.", file);
            this.GitShouldRefuseAfterTheFaultIsGone(command, file);
        }

        [TestCase]
        public void RevertStopsAndGitRefusesAfterTheFaultIsGone()
        {
            // HEAD changes the file, and the file has a local change that the revert would lose.
            string file = this.unusedFiles.Dequeue();
            this.GitShouldSucceed("checkout -q " + this.CreateBranchThatChanges(file));
            this.EditWhileTheMountRefuses(file, RefuseWidenAndApply);

            this.ShouldStopBeforeGitRuns("revert --no-edit HEAD", StoppedMessage + " (apply: Rejected). Run the command again.", file);
            this.GitShouldRefuseAfterTheFaultIsGone("revert --no-edit HEAD", file);
        }

        [TestCase]
        public void StopMessageNamesTheGVFSLock()
        {
            string file = this.unusedFiles.Dequeue();
            this.EditWhileTheMountRefuses(file, RefuseApplyAsNotLockHolder);

            this.ShouldStopBeforeGitRuns(
                "commit -a -m \"Edited outside the cone\"",
                StoppedMessage + " (apply: NotLockHolder).",
                file,
                "This Git command does not hold the GVFS lock.");
        }

        [TestCase("status --porcelain", true)]
        [TestCase("diff --name-only", true)]
        [TestCase("diff --cached --name-only", true)]
        [TestCase("ls-files -- {0}", true)]
        [TestCase("blame -- {0}", true)]
        [TestCase("log -1 -p -- {0}", false)]
        public void ReadOnlyCommandRunsWithAWarningWhenTheApplyFails(string commandFormat, bool sendsAnApply)
        {
            // "git log" does not read the index, so the hook does not apply the change for it.
            string file = this.unusedFiles.Dequeue();
            string command = string.Format(commandFormat, file);
            this.EditWhileTheMountRefuses(file, RefuseWidenAndApply);

            ProcessResult result = GitProcess.InvokeProcess(this.Enlistment.RepoRoot, command);

            result.ExitCode.ShouldEqual(0, $"git {command} failed: {result.Output} {result.Errors}");
            result.Output.ShouldNotContain(ignoreCase: false, StoppedMessage);
            if (sendsAnApply)
            {
                result.Errors.ShouldContain(StaleIndexWarning);
            }
            else
            {
                result.Errors.ShouldNotContain(ignoreCase: false, StaleIndexWarning);
            }

            this.ConeChangeIsPending().ShouldBeTrue("the refused apply must keep the change pending");
            this.HasLocalChange(file).ShouldBeTrue();
        }

        [TestCase]
        public void CommitOfTheStagedFilesOnlyRunsWithAWarningWhenTheApplyFails()
        {
            // The commit does not read the local change, so the hook lets it run.
            string stagedFile = $"fail-closed-staged-{this.branchCount++}.txt";
            File.WriteAllText(this.Enlistment.GetVirtualPathTo(stagedFile), "staged\n");
            this.GitShouldSucceed("add -- " + stagedFile);
            string file = this.unusedFiles.Dequeue();
            this.EditWhileTheMountRefuses(file, RefuseWidenAndApply);

            ProcessResult result = GitProcess.InvokeProcess(this.Enlistment.RepoRoot, "commit -m \"Staged files only\"");

            result.ExitCode.ShouldEqual(0, $"git commit failed: {result.Output} {result.Errors}");
            result.Errors.ShouldContain(StaleIndexWarning);
            string committed = this.GitShouldSucceed("diff --name-only HEAD~1 HEAD");
            committed.ShouldContain(stagedFile);
            committed.ShouldNotContain(ignoreCase: true, file);
            this.HasLocalChange(file).ShouldBeTrue();
        }

        private static string Sha256(string path)
        {
            using (FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            {
                return Convert.ToHexString(SHA256.HashData(stream));
            }
        }

        /// <summary>
        /// Make the mount refuse requests as <paramref name="faults"/> says, then edit
        /// <paramref name="file"/> and wait until the mount makes the cone change pending. The
        /// fault comes first, so that no other Git command applies the change.
        /// </summary>
        private void EditWhileTheMountRefuses(string file, string faults)
        {
            this.SetFaults(faults);
            File.AppendAllText(this.Enlistment.GetVirtualPathTo(file), LocalChange + "\n");

            Stopwatch stopwatch = Stopwatch.StartNew();
            while (!this.ConeChangeIsPending() && stopwatch.ElapsedMilliseconds < 10000)
            {
                System.Threading.Thread.Sleep(50);
            }

            this.ConeChangeIsPending().ShouldBeTrue("the mount did not make the cone change pending");
        }

        /// <summary>
        /// Run <paramref name="command"/> while the fault file refuses requests, and check that the
        /// hook stopped it before Git ran.
        /// </summary>
        private void ShouldStopBeforeGitRuns(string command, string expectedMessage, string changedFile, string expectedSecondLine = null)
        {
            string indexPath = this.Enlistment.GetDotGitPath("index");
            string head = this.GitShouldSucceed("rev-parse HEAD").Trim();
            string stashes = this.GitShouldSucceed("stash list");
            string indexBefore = Sha256(indexPath);

            ProcessResult result = GitProcess.InvokeProcess(this.Enlistment.RepoRoot, command);

            string output = result.Output + result.Errors;
            result.ExitCode.ShouldNotEqual(0, $"git {command} was not stopped: {output}");
            output.ShouldContain(expectedMessage);
            if (expectedSecondLine != null)
            {
                output.ShouldContain(expectedSecondLine);
            }

            // Git did not run: no index lock, the same index, HEAD, stashes, and working tree.
            File.Exists(indexPath + ".lock").ShouldBeFalse("git left an index lock");
            Sha256(indexPath).ShouldEqual(indexBefore, "the index changed");
            this.GitShouldSucceed("rev-parse HEAD").Trim().ShouldEqual(head);
            this.GitShouldSucceed("stash list").ShouldEqual(stashes);
            this.HasLocalChange(changedFile).ShouldBeTrue();
        }

        /// <summary>
        /// Remove the fault, run <paramref name="command"/> again, and check that Git (not the
        /// hook) refused it because of the local change.
        /// </summary>
        private void GitShouldRefuseAfterTheFaultIsGone(string command, string changedFile)
        {
            string head = this.GitShouldSucceed("rev-parse HEAD").Trim();
            this.SetFaults(string.Empty);

            ProcessResult retry = GitProcess.InvokeProcess(this.Enlistment.RepoRoot, command);

            string output = retry.Output + retry.Errors;
            retry.ExitCode.ShouldNotEqual(0, $"git {command} did not see the local change: {output}");
            output.ShouldNotContain(ignoreCase: false, StoppedMessage);
            LocalChangeRefusals.Any(refusal => output.Contains(refusal)).ShouldBeTrue($"git {command} failed for another reason: {output}");
            this.GitShouldSucceed("rev-parse HEAD").Trim().ShouldEqual(head);
            this.HasLocalChange(changedFile).ShouldBeTrue();
        }

        private bool ConeChangeIsPending()
        {
            return File.Exists(Path.Combine(this.Enlistment.DotGVFSRoot, ConeChangePendingFile));
        }

        private void SetFaults(string faults)
        {
            File.WriteAllText(this.faultFile, faults);
        }

        private bool HasLocalChange(string file)
        {
            return File.ReadAllText(this.Enlistment.GetVirtualPathTo(file)).Contains(LocalChange);
        }

        /// <summary>
        /// Create a branch with a commit on top of HEAD that changes <paramref name="file"/>. The
        /// commit is made in a temporary index, so the working tree does not change.
        /// </summary>
        private string CreateBranchThatChanges(string file)
        {
            string changedContent = this.GitShouldSucceed("cat-file blob HEAD:" + file) + "changed by a commit\n";
            ProcessResult hash = GitProcess.InvokeProcess(
                this.Enlistment.RepoRoot,
                "hash-object -w --stdin",
                inputStream: new MemoryStream(Encoding.UTF8.GetBytes(changedContent)));
            hash.ExitCode.ShouldEqual(0, hash.Errors);

            string temporaryIndex = Path.Combine(this.Enlistment.EnlistmentRoot, $"fail-closed-test-index-{this.branchCount}");
            Dictionary<string, string> environment = new Dictionary<string, string> { { "GIT_INDEX_FILE", temporaryIndex } };
            string plainIndex = $"{BypassGVFS} -c index.sparse=false -c core.sparseCheckout=false";
            this.GitShouldSucceed(environment, $"{plainIndex} read-tree HEAD");
            this.GitShouldSucceed(environment, $"{plainIndex} update-index --cacheinfo 100644,{hash.Output.Trim()},{file}");
            string tree = this.GitShouldSucceed(environment, $"{plainIndex} write-tree").Trim();
            File.Delete(temporaryIndex);

            string commit = this.GitShouldSucceed($"commit-tree {tree} -p HEAD -m \"Change {file}\"").Trim();
            string branch = $"tests/functional/fail-closed-{this.branchCount++}";
            this.GitShouldSucceed($"branch {branch} {commit}");
            return branch;
        }

        private string GitShouldSucceed(Dictionary<string, string> environment, string command)
        {
            ProcessResult result = GitProcess.InvokeProcess(this.Enlistment.RepoRoot, command, environment);
            result.ExitCode.ShouldEqual(0, $"git {command} failed: {result.Errors}");
            return result.Output;
        }
    }
}
