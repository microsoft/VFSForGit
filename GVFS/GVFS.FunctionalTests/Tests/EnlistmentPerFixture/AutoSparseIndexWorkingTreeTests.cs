using GVFS.FunctionalTests.Tools;
using GVFS.Tests.Should;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace GVFS.FunctionalTests.Tests.EnlistmentPerFixture
{
    /// <summary>
    /// Functional tests for working-tree changes outside the sparse-index cone that no Git
    /// command names: Git must see them before it reads the index.
    /// </summary>
    [TestFixture]
    public class AutoSparseIndexWorkingTreeTests : AutoSparseIndexTestsBase
    {
        private const string ConeManagementPrefix = "gvfs cone-management:";

        private static readonly Func<string, string, bool> SamePath = (first, second) => first.Equals(second, StringComparison.OrdinalIgnoreCase);

        // Files of HEAD, each in a different two-level directory outside the root-only cone
        private Queue<string> unusedFiles;

        [OneTimeSetUp]
        public void SelectFiles()
        {
            this.unusedFiles = new Queue<string>(this.GroupByTwoLevelDirectory().Select(group => group.First()));
            this.unusedFiles.Count.ShouldBeAtLeast(12, "HEAD has too few directories that are two levels deep");
        }

        [TestCase]
        public void StatusAndAddSeeFilesThatWereEditedOutsideTheCone()
        {
            List<string> files = this.TakeFiles(3);
            foreach (string file in files)
            {
                this.AppendTo(file);
            }

            // This status takes the GVFS lock. The other tests run "git status" without it, as
            // an editor does.
            ProcessResult status = this.GitWithTrace("status --porcelain", out string statusTrace, takeOptionalLocks: true);
            status.ExitCode.ShouldEqual(0, status.Errors);
            ModifiedFilesIn(status.Output).ShouldContain(files, SamePath);
            this.ShouldNotExpandTheIndex(status, statusTrace);

            ProcessResult add = this.GitWithTrace("add .", out string addTrace);
            add.ExitCode.ShouldEqual(0, add.Errors);
            this.ShouldNotExpandTheIndex(add, addTrace);
            this.GitShouldSucceed("diff --cached --name-only").ShouldContain(files.ToArray());

            this.SparseDirectoryEntries().ShouldBeNonEmpty();
            this.CommitAndCheckThatTheConeNarrows(files, "commit -m \"Edited outside the cone\"");
        }

        [TestCase]
        public void CommitAllIncludesFilesThatWereEditedOutsideTheCone()
        {
            List<string> files = this.TakeFiles(3);
            foreach (string file in files)
            {
                this.AppendTo(file);
            }

            this.CommitAndCheckThatTheConeNarrows(files, "commit -a -m \"Edited outside the cone\"");
        }

        [TestCase]
        public void ConeNarrowsWhenAFileHasTheContentOfHeadAgainAndWidensAtTheNextEdit()
        {
            string file = this.TakeFiles(1).Single();
            string directory = ParentDirectory(file);
            string path = this.Enlistment.GetVirtualPathTo(file);
            byte[] headContent = File.ReadAllBytes(path);

            this.AppendTo(file);
            ModifiedFilesIn(this.Status()).ShouldContain(new[] { file }, SamePath);
            this.ConeShouldInclude(directory);

            // The change is inside the applied cone, so no request is pending. When changes stop
            // for a short time, the mount checks which modified files match HEAD.
            File.WriteAllBytes(path, headContent);
            ModifiedFilesIn(this.Status()).ShouldNotContain(new[] { file }, SamePath);
            this.WaitForConeFile(lines => !lines.Any(line => IsConeLineFor(line, directory)))
                .ShouldNotContain(line => IsConeLineFor(line, directory));

            this.AppendTo(file);
            ProcessResult status = this.GitWithTrace("status --porcelain", out string trace);
            ModifiedFilesIn(status.Output).ShouldContain(new[] { file }, SamePath);
            this.ShouldNotExpandTheIndex(status, trace);
            this.ConeShouldInclude(directory);
        }

        [TestCase]
        public void UnlockedDiffSeesAFileThatWasEditedOutsideTheCone()
        {
            string file = this.TakeFiles(1).Single();
            this.AppendTo(file);

            // git diff does not take the GVFS lock. The hook takes it to apply the pending change.
            ProcessResult diff = this.GitWithTrace("diff --name-only", out string trace);

            diff.ExitCode.ShouldEqual(0, diff.Errors);
            diff.Output.Split('\n').Select(line => line.Trim()).ShouldContain(line => line.Equals(file, StringComparison.OrdinalIgnoreCase));
            this.ShouldNotExpandTheIndex(diff, trace);
            this.ConeShouldInclude(ParentDirectory(file));
        }

        [TestCase]
        public void AddSeesANewFileOutsideTheCone()
        {
            string directory = ParentDirectory(this.TakeFiles(1).Single());
            string newFile = directory + "/new-file-outside-the-cone.txt";
            File.WriteAllText(this.Enlistment.GetVirtualPathTo(newFile), "new");

            ProcessResult add = this.GitWithTrace("add .", out string trace);

            add.ExitCode.ShouldEqual(0, add.Errors);
            this.ShouldNotExpandTheIndex(add, trace);
            this.GitShouldSucceed("diff --cached --name-only").ShouldContain(newFile);
            this.GitShouldSucceed("reset --quiet -- " + newFile);
            File.Delete(this.Enlistment.GetVirtualPathTo(newFile));
        }

        [TestCase]
        public void StatusAndCommitAllSeeAFileThatWasDeletedOutsideTheCone()
        {
            if (!this.GitChangesOnlyTheIndex)
            {
                // Depends on the Git change that makes "sparse-checkout reapply" change only the
                // index under a virtual file system. Older Git checks a deleted file out again.
                Assert.Ignore($"This git does not have '{IndexOnlySparsityFeature}'");
            }

            string file = this.TakeFiles(1).Single();
            File.Delete(this.Enlistment.GetVirtualPathTo(file));

            ProcessResult status = this.GitWithTrace("status --porcelain", out string trace);
            status.ExitCode.ShouldEqual(0, status.Errors);
            status.Output.ShouldContain(" D " + file);
            this.ShouldNotExpandTheIndex(status, trace);

            this.GitShouldSucceed("commit -a -m \"Deleted outside the cone\"");
            this.GitShouldSucceed("show --name-status --format= HEAD").ShouldContain("D\t" + file);
            File.Exists(this.Enlistment.GetVirtualPathTo(file)).ShouldBeFalse("Git restored the deleted file");
        }

        private static List<string> ModifiedFilesIn(string porcelainStatus)
        {
            return porcelainStatus
                .Split('\n')
                .Where(line => line.Length > 3)
                .Select(line => line.Substring(3).Trim())
                .ToList();
        }

        private string Status()
        {
            ProcessResult status = this.GitWithTrace("status --porcelain", out string trace);
            status.ExitCode.ShouldEqual(0, status.Errors);
            this.ShouldNotExpandTheIndex(status, trace);
            return status.Output;
        }

        private List<string> TakeFiles(int count)
        {
            List<string> files = new List<string>();
            for (int i = 0; i < count; i++)
            {
                files.Add(this.unusedFiles.Dequeue());
            }

            return files;
        }

        private void AppendTo(string file)
        {
            File.AppendAllText(this.Enlistment.GetVirtualPathTo(file), Environment.NewLine + "edited outside the cone" + Environment.NewLine);
        }

        private void ShouldNotExpandTheIndex(ProcessResult result, string trace)
        {
            result.Errors.ShouldNotContain(ignoreCase: false, ConeManagementPrefix + " widen not applied", ConeManagementPrefix + " apply");
            CountFullIndexExpansions(trace).ShouldEqual(0, "git expanded the whole index: " + result.Errors);
        }

        private void ConeShouldInclude(string directory)
        {
            ConeLines(File.ReadAllText(this.SparseCheckoutPath)).ShouldContain(line => IsConeLineFor(line, directory));
        }

        private void CommitAndCheckThatTheConeNarrows(List<string> files, string commitCommand)
        {
            ProcessResult commit = this.GitWithTrace(commitCommand, out string trace);
            commit.ExitCode.ShouldEqual(0, commit.Errors);
            this.ShouldNotExpandTheIndex(commit, trace);
            List<string> committed = this.GitShouldSucceed("show --name-only --format= HEAD")
                .Split('\n')
                .Select(line => line.Trim())
                .ToList();
            committed.ShouldContain(files, SamePath);

            // The committed files match HEAD, so the cone no longer needs their directories.
            List<string> directories = files.Select(ParentDirectory).ToList();
            string[] coneLines = this.WaitForConeFile(lines => !lines.Any(line => directories.Any(directory => IsConeLineFor(line, directory))));
            coneLines.ShouldNotContain(line => directories.Any(directory => IsConeLineFor(line, directory)));
            this.SparseDirectoryEntries().ShouldBeNonEmpty();
            ModifiedFilesIn(this.Status()).ShouldNotContain(files, SamePath);
        }
    }
}
