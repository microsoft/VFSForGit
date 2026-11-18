using GVFS.FunctionalTests.Tools;
using GVFS.Tests.Should;
using NUnit.Framework;
using System;
using System.Collections.Generic;
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
    /// and reports success. The pre-command hook must stop such a command before Git runs.
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
        private const string LocalChange = "local change outside the cone";

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
            this.unusedFiles.Count.ShouldBeAtLeast(12, "HEAD has too few leaf directories");

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
            GitProcess.InvokeProcess(this.Enlistment.RepoRoot, "reset --hard --quiet");
            GitProcess.InvokeProcess(this.Enlistment.RepoRoot, "checkout -q -f " + this.startBranch);
            GitProcess.InvokeProcess(this.Enlistment.RepoRoot, "reset --hard --quiet " + this.startCommit);
            GitProcess.InvokeProcess(this.Enlistment.RepoRoot, "stash clear");
        }

        [TestCase("add -- {0}", "widen")]
        [TestCase("add .", "apply")]
        [TestCase("add -u", "apply")]
        [TestCase("commit -a -m \"Edited outside the cone\"", "apply")]
        [TestCase("stash", "apply")]
        [TestCase("stash push -- {0}", "apply")]
        public void WorkingTreeCommandStopsAndRunsAfterTheFaultIsGone(string commandFormat, string request)
        {
            string file = this.unusedFiles.Dequeue();
            string command = string.Format(commandFormat, file);

            // Set the fault before the change, so that no other Git command applies it first.
            this.SetFaults(RefuseWidenAndApply);
            this.AppendLocalChange(file);

            this.ShouldStopBeforeGitRuns(command, $"({request}: Rejected). Run the command again.", file);

            this.SetFaults(string.Empty);
            ProcessResult retry = GitProcess.InvokeProcess(this.Enlistment.RepoRoot, command);
            retry.ExitCode.ShouldEqual(0, $"git {command} failed after the fault was removed: {retry.Output} {retry.Errors}");
            if (command.StartsWith("stash", StringComparison.Ordinal))
            {
                this.GitShouldSucceed("stash show --name-only").ShouldContain(file);
                this.HasLocalChange(file).ShouldBeFalse();
            }
            else if (command.StartsWith("commit", StringComparison.Ordinal))
            {
                this.GitShouldSucceed("diff --name-only HEAD~1 HEAD").ShouldContain(file);
            }
            else
            {
                this.GitShouldSucceed("diff --cached --name-only").ShouldContain(file);
            }
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
            this.SetFaults(RefuseWidenAndApply);
            this.AppendLocalChange(file);

            this.ShouldStopBeforeGitRuns(command, "(apply: Rejected). Run the command again.", file);

            this.SetFaults(string.Empty);
            ProcessResult retry = GitProcess.InvokeProcess(this.Enlistment.RepoRoot, command);
            retry.ExitCode.ShouldNotEqual(0, $"git {command} did not see the local change: {retry.Output}");
            this.GitShouldSucceed("rev-parse HEAD").Trim().ShouldEqual(this.startCommit);
            this.HasLocalChange(file).ShouldBeTrue();
        }

        [TestCase]
        public void StopMessageNamesTheGVFSLock()
        {
            string file = this.unusedFiles.Dequeue();
            this.SetFaults(RefuseApplyAsNotLockHolder);
            this.AppendLocalChange(file);

            this.ShouldStopBeforeGitRuns(
                "commit -a -m \"Edited outside the cone\"",
                "(apply: NotLockHolder).",
                file,
                "Another Git command holds the GVFS lock.");
        }

        [TestCase("status --porcelain")]
        [TestCase("diff --name-only")]
        [TestCase("commit --allow-empty -m \"Staged files only\"")]
        public void CommandThatDoesNotNeedTheChangeRunsWhenTheApplyFails(string command)
        {
            string file = this.unusedFiles.Dequeue();
            this.SetFaults(RefuseWidenAndApply);
            this.AppendLocalChange(file);

            ProcessResult result = GitProcess.InvokeProcess(this.Enlistment.RepoRoot, command);

            result.ExitCode.ShouldEqual(0, $"git {command} failed: {result.Output} {result.Errors}");
            (result.Output + result.Errors).ShouldNotContain(ignoreCase: false, StoppedMessage);
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
            output.ShouldContain(StoppedMessage + " " + expectedMessage);
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

        private void SetFaults(string faults)
        {
            File.WriteAllText(this.faultFile, faults);
        }

        private void AppendLocalChange(string file)
        {
            File.AppendAllText(this.Enlistment.GetVirtualPathTo(file), LocalChange + "\n");
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
