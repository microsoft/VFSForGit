using GVFS.FunctionalTests.Tools;
using GVFS.Tests.Should;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace GVFS.FunctionalTests.Tests.EnlistmentPerFixture
{
    /// <summary>
    /// Functional tests for files that "git reset --soft" stages outside the sparse-index cone.
    /// No file system write stages them, so only the unstage preparation of the hook can bring
    /// them into the cone before Git unstages them.
    /// </summary>
    [TestFixture]
    public class AutoSparseIndexUnstageTests : AutoSparseIndexTestsBase
    {
        private const string ConeManagementPrefix = "gvfs cone-management:";
        private const string ChangedLine = "changed by a commit";

        // Files of HEAD, each in a different two-level directory outside the root-only cone
        private Queue<string> unusedFiles;
        private int branchCount;

        [OneTimeSetUp]
        public void SelectFiles()
        {
            this.unusedFiles = new Queue<string>(this.GroupByTwoLevelDirectory().Select(group => group.First()));
            this.unusedFiles.Count.ShouldBeAtLeast(4, "HEAD has too few directories that are two levels deep");
        }

        [TestCase]
        public void RestoreStagedPathKeepsTheChangeVisible()
        {
            this.UnstageAfterSoftResetShouldKeepTheChange(file => "restore --staged -- " + file);
        }

        [TestCase]
        public void RestoreStagedDotKeepsTheChangeVisible()
        {
            this.UnstageAfterSoftResetShouldKeepTheChange(file => "restore --staged .");
        }

        [TestCase]
        public void ResetPathKeepsTheChangeVisible()
        {
            this.UnstageAfterSoftResetShouldKeepTheChange(file => "reset --quiet -- " + file);
        }

        [TestCase]
        public void ResetKeepsTheChangeVisible()
        {
            this.UnstageAfterSoftResetShouldKeepTheChange(file => "reset --quiet");
        }

        [TestCase("restore --staged -- ")]
        [TestCase("reset --quiet -- ")]
        public void UnstageKeepsUnstagedEditsOfANewFile(string unstageCommand)
        {
            // The unstage preparation writes the staged content of an added file that has no
            // placeholder. It must not overwrite a file that the user created and edited again.
            string file = $"unstage-new-file-{this.branchCount++}.txt";
            string path = this.Enlistment.GetVirtualPathTo(file);
            try
            {
                File.WriteAllText(path, "staged\n");
                this.GitShouldSucceed("add -- " + file);
                File.AppendAllText(path, "not staged\n");

                this.GitShouldSucceed(unstageCommand + file);

                File.ReadAllText(path).ShouldEqual("staged\nnot staged\n");
                this.GitShouldSucceed("status --porcelain -- " + file).ShouldContain("?? " + file);
            }
            finally
            {
                GitProcess.InvokeProcess(this.Enlistment.RepoRoot, "reset --quiet -- " + file);
                File.Delete(path);
            }
        }

        /// <summary>
        /// Check out a commit that changes a file outside the cone, without a write to that file
        /// in the working tree. Then "git reset --soft HEAD~1" stages the change, and
        /// <paramref name="getUnstageCommand"/> unstages it. The change must stay in the working
        /// tree, and Git must report it.
        /// </summary>
        private void UnstageAfterSoftResetShouldKeepTheChange(Func<string, string> getUnstageCommand)
        {
            string file = this.unusedFiles.Dequeue();
            try
            {
                this.UnstageAfterSoftResetShouldKeepTheChange(file, getUnstageCommand(file));
            }
            finally
            {
                // Leave a clean working tree and index for the next test, also after a failure.
                GitProcess.InvokeProcess(this.Enlistment.RepoRoot, "reset --hard --quiet");
            }

            this.GitShouldSucceed("status --porcelain").ShouldNotContain(ignoreCase: true, file);
        }

        private void UnstageAfterSoftResetShouldKeepTheChange(string file, string unstageCommand)
        {
            this.CheckOutACommitThatChanges(file);
            this.SparseDirectoryEntries().ShouldBeNonEmpty();
            ConeLines(this.ReadConeFile())
                .ShouldNotContain(line => line.StartsWith("/" + TwoLevelDirectory(file) + "/", StringComparison.OrdinalIgnoreCase));

            ProcessResult softReset = this.GitWithTrace("reset --soft HEAD~1", out string softResetTrace);
            softReset.ExitCode.ShouldEqual(0, softReset.Errors);
            CountFullIndexExpansions(softResetTrace).ShouldEqual(0, "reset --soft expanded the whole index");
            this.GitShouldSucceed("diff --cached --name-only").ShouldContain(file);

            ProcessResult unstage = this.GitWithTrace(unstageCommand, out string unstageTrace);
            unstage.ExitCode.ShouldEqual(0, $"git {unstageCommand} failed: {unstage.Errors}");
            unstage.Errors.ShouldNotContain(ignoreCase: false, ConeManagementPrefix + " widen not applied", ConeManagementPrefix + " apply");
            CountFullIndexExpansions(unstageTrace).ShouldEqual(0, $"git {unstageCommand} expanded the whole index: {unstage.Errors}");

            this.GitShouldSucceed("diff --cached --name-only").ShouldNotContain(ignoreCase: true, file);
            ProcessResult status = this.GitWithTrace("status --porcelain", out string statusTrace);
            status.ExitCode.ShouldEqual(0, status.Errors);
            status.Output.ShouldContain(" M " + file);
            CountFullIndexExpansions(statusTrace).ShouldEqual(0, "status expanded the whole index");
            this.GitShouldSucceed("ls-files -v -- " + file).ShouldContain("H " + file);
            File.ReadAllText(this.Enlistment.GetVirtualPathTo(file)).ShouldContain(ChangedLine);
            this.SparseDirectoryEntries().ShouldBeNonEmpty();
        }

        /// <summary>
        /// Create a commit on top of HEAD that adds <see cref="ChangedLine"/> to
        /// <paramref name="file"/>, and check it out. The commit is made in a temporary index, so
        /// the working tree and the modified paths do not see the change before the checkout.
        /// </summary>
        private void CheckOutACommitThatChanges(string file)
        {
            string changedContent = this.GitShouldSucceed("cat-file blob HEAD:" + file) + ChangedLine + "\n";
            ProcessResult hash = GitProcess.InvokeProcess(
                this.Enlistment.RepoRoot,
                "hash-object -w --stdin",
                inputStream: new MemoryStream(Encoding.UTF8.GetBytes(changedContent)));
            hash.ExitCode.ShouldEqual(0, hash.Errors);
            string blob = hash.Output.Trim();

            string temporaryIndex = Path.Combine(this.Enlistment.EnlistmentRoot, $"unstage-test-index-{this.branchCount}");
            Dictionary<string, string> environment = new Dictionary<string, string> { { "GIT_INDEX_FILE", temporaryIndex } };
            string plainIndex = $"{BypassGVFS} -c index.sparse=false -c core.sparseCheckout=false";
            this.GitShouldSucceed(environment, $"{plainIndex} read-tree HEAD");
            this.GitShouldSucceed(environment, $"{plainIndex} update-index --cacheinfo 100644,{blob},{file}");
            string tree = this.GitShouldSucceed(environment, $"{plainIndex} write-tree").Trim();
            File.Delete(temporaryIndex);

            string commit = this.GitShouldSucceed($"commit-tree {tree} -p HEAD -m \"Change {file}\"").Trim();
            string branch = $"tests/functional/unstage-{this.branchCount++}";
            this.GitShouldSucceed($"branch {branch} {commit}");
            this.GitShouldSucceed("checkout " + branch);
            this.GitShouldSucceed("status --porcelain").ShouldNotContain(ignoreCase: true, file);
        }

        private string GitShouldSucceed(Dictionary<string, string> environment, string command)
        {
            ProcessResult result = GitProcess.InvokeProcess(this.Enlistment.RepoRoot, command, environment);
            result.ExitCode.ShouldEqual(0, $"git {command} failed: {result.Errors}");
            return result.Output;
        }
    }
}
