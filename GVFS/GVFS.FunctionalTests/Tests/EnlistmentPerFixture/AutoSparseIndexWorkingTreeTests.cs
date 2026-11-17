using GVFS.FunctionalTests.Tools;
using GVFS.Tests.Should;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;

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
            this.unusedFiles.Count.ShouldBeAtLeast(18, "HEAD has too few directories that are two levels deep");
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
        public void ConeNarrowsAfterResetHardDiscardsAnEdit()
        {
            string file = this.TakeFiles(1).Single();
            string directory = ParentDirectory(file);
            this.AppendTo(file);
            ModifiedFilesIn(this.Status()).ShouldContain(new[] { file }, SamePath);
            this.ConeShouldInclude(directory);

            // The reset rebuilds the projection. The file has the content of HEAD again.
            this.GitShouldSucceed("reset --hard --quiet");

            this.WaitForConeFile(lines => !lines.Any(line => IsConeLineFor(line, directory)))
                .ShouldNotContain(line => IsConeLineFor(line, directory));
        }

        [TestCase]
        public void ConeNarrowsWhenACreatedFileIsIgnored()
        {
            string directory = ParentDirectory(this.TakeFiles(1).Single());
            string ignoredFile = directory + "/created.vfs-cone-ignored";
            File.AppendAllText(this.Enlistment.GetDotGitPath("info", "exclude"), "\n*.vfs-cone-ignored\n");
            File.WriteAllText(this.Enlistment.GetVirtualPathTo(ignoredFile), "ignored");

            // The status applies the change. Git does not list an ignored file, so the
            // check finds it clean, and the cone leaves its directory out.
            this.Status().ShouldNotContain(ignoreCase: true, ignoredFile);
            this.WaitForConeFile(lines => !lines.Any(line => IsConeLineFor(line, directory)))
                .ShouldNotContain(line => IsConeLineFor(line, directory));
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
        public void AddSeesARenameOverSaveAfterAFolderDehydrate()
        {
            // An earlier change, then a folder dehydrate, which rewrites the index and the cone.
            string earlierFile = this.TakeFiles(1).Single();
            this.AppendTo(earlierFile);
            this.GitShouldSucceed("add -- " + earlierFile);
            this.GitShouldSucceed("restore --staged --worktree -- " + earlierFile);
            this.DehydrateFolder(ParentDirectory(earlierFile));

            // An editor writes a temporary file and renames it over the file.
            string file = this.TakeFiles(1).Single();
            string path = this.Enlistment.GetVirtualPathTo(file);
            string temporaryPath = path + ".tmp~";
            File.WriteAllText(temporaryPath, File.ReadAllText(path) + "edited outside the cone\n");
            File.Move(temporaryPath, path, overwrite: true);

            ProcessResult add = this.GitWithTrace("add -- " + file, out string trace);

            add.ExitCode.ShouldEqual(0, add.Errors);
            this.ShouldNotExpandTheIndex(add, trace);
            this.GitShouldSucceed("diff --cached --name-only").ShouldContain(file);
            this.GitShouldSucceed("restore --staged --worktree -- " + file);
        }

        [TestCase]
        public void StatusAndCommitAllSeeAFileThatWasDeletedOutsideTheCone()
        {
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

        [TestCase("checkout --detach")]
        [TestCase("reset --merge")]
        public void SwitchingCommitsKeepsOrRefusesToOverwriteChangesOutsideTheCone(string switchCommand)
        {
            List<string> files = this.TakeFiles(2);
            string editedFile = files[0];
            string deletedFile = files[1];
            string editedPath = this.Enlistment.GetVirtualPathTo(editedFile);
            string deletedPath = this.Enlistment.GetVirtualPathTo(deletedFile);
            string addedFile = ParentDirectory(editedFile) + "/added-next-to-an-edit.txt";
            string startCommit = this.GitShouldSucceed("rev-parse HEAD").Trim();
            string branch = this.GitShouldSucceed("symbolic-ref --short HEAD").Trim();

            // The sibling commit changes the directories of both files, but not the files. The
            // other commit changes the edited file.
            string siblingCommit = this.CommitWithoutTheWorkingTree(
                startCommit,
                (addedFile, "added"),
                (ParentDirectory(deletedFile) + "/added-next-to-a-delete.txt", "added"));
            string overwriteCommit = this.CommitWithoutTheWorkingTree(startCommit, (editedFile, "changed by a commit"));

            this.GitShouldSucceed("checkout --detach");
            try
            {
                this.AppendTo(editedFile);
                string editedContent = File.ReadAllText(editedPath);
                File.Delete(deletedPath);
                string status = this.Status();
                status.ShouldContain(" M " + editedFile, " D " + deletedFile);

                // Git keeps both changes, as it does with a full index.
                ProcessResult keep = this.GitWithTrace($"{switchCommand} {siblingCommit}", out _);
                keep.ExitCode.ShouldEqual(0, keep.Errors);
                keep.Errors.ShouldNotContain(ignoreCase: false, ConeManagementPrefix);
                this.GitShouldSucceed("rev-parse HEAD").Trim().ShouldEqual(siblingCommit);
                File.Exists(this.Enlistment.GetVirtualPathTo(addedFile)).ShouldBeTrue("Git did not check out the new file of the commit");
                File.ReadAllText(editedPath).ShouldEqual(editedContent, "Git lost the edit");
                File.Exists(deletedPath).ShouldBeFalse("Git restored the deleted file");
                this.Status().ShouldContain(" M " + editedFile, " D " + deletedFile);

                // Git refuses to overwrite the edit.
                ProcessResult refuse = this.GitWithTrace($"{switchCommand} {overwriteCommit}", out _);
                refuse.ExitCode.ShouldNotEqual(0, "Git overwrote a file that was edited outside the cone");
                refuse.Errors.ShouldContain(editedFile);
                this.GitShouldSucceed("rev-parse HEAD").Trim().ShouldEqual(siblingCommit);
                File.ReadAllText(editedPath).ShouldEqual(editedContent, "Git lost the edit");
                File.Exists(deletedPath).ShouldBeFalse("Git restored the deleted file");
            }
            finally
            {
                GitProcess.InvokeProcess(this.Enlistment.RepoRoot, $"{switchCommand} {startCommit}");
                GitProcess.InvokeProcess(this.Enlistment.RepoRoot, $"checkout {startCommit} -- {editedFile} {deletedFile}");
                GitProcess.InvokeProcess(this.Enlistment.RepoRoot, $"checkout {branch}");
            }

            this.GitShouldSucceed("status --porcelain").ShouldNotContain(ignoreCase: true, editedFile, deletedFile);
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

        private void DehydrateFolder(string directory)
        {
            ProcessStartInfo processInfo = new ProcessStartInfo(GVFSTestConfig.PathToGVFS);
            processInfo.Arguments = $"dehydrate --confirm --no-status --folders {directory} {TestConstants.InternalUseOnlyFlag} {GVFSHelpers.GetInternalParameter()}";
            processInfo.WorkingDirectory = this.Enlistment.EnlistmentRoot;
            processInfo.UseShellExecute = false;
            processInfo.RedirectStandardOutput = true;
            ProcessResult dehydrate = ProcessHelper.Run(processInfo);
            dehydrate.ExitCode.ShouldEqual(0, dehydrate.Output + dehydrate.Errors);
        }

        /// <summary>
        /// Commit <paramref name="files"/> on top of <paramref name="parent"/> with a separate
        /// index and without the virtual file system, so that the working tree, the index, and the
        /// cone do not change.
        /// </summary>
        private string CommitWithoutTheWorkingTree(string parent, params (string Path, string Content)[] files)
        {
            string indexFile = Path.Combine(this.Enlistment.EnlistmentRoot, "commit-" + Guid.NewGuid().ToString("N") + ".index");
            string plainGit = BypassGVFS + " -c core.sparseCheckout=false -c index.sparse=false";
            Dictionary<string, string> environment = new Dictionary<string, string> { { "GIT_INDEX_FILE", indexFile } };
            try
            {
                this.PlainGitShouldSucceed($"{plainGit} read-tree {parent}", environment);
                foreach ((string path, string content) in files)
                {
                    string blob;
                    using (MemoryStream input = new MemoryStream(Encoding.UTF8.GetBytes(content + "\n")))
                    {
                        // hash-object rejects the empty paths of the other options, and it does
                        // not read the index.
                        blob = this.PlainGitShouldSucceed("hash-object -w --no-filters --stdin", environment, input).Trim();
                    }

                    this.PlainGitShouldSucceed($"{plainGit} update-index --add --cacheinfo 100644,{blob},{path}", environment);
                }

                string tree = this.PlainGitShouldSucceed($"{plainGit} write-tree", environment).Trim();
                return this.PlainGitShouldSucceed($"{plainGit} commit-tree {tree} -p {parent} -m \"Commit beside the working tree\"", environment).Trim();
            }
            finally
            {
                File.Delete(indexFile);
            }
        }

        private string PlainGitShouldSucceed(string command, Dictionary<string, string> environment, Stream input = null)
        {
            ProcessResult result = GitProcess.InvokeProcess(this.Enlistment.RepoRoot, command, environment, input);
            result.ExitCode.ShouldEqual(0, $"git {command} failed: {result.Errors}");
            return result.Output;
        }

        private void ShouldNotExpandTheIndex(ProcessResult result, string trace)
        {
            result.Errors.ShouldNotContain(ignoreCase: false, ConeManagementPrefix + " widen not applied", ConeManagementPrefix + " apply");
            CountFullIndexExpansions(trace).ShouldEqual(0, "git expanded the whole index: " + result.Errors);
        }

        private void ConeShouldInclude(string directory)
        {
            ConeLines(this.ReadConeFile()).ShouldContain(line => IsConeLineFor(line, directory));
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
