using GVFS.FunctionalTests.FileSystemRunners;
using GVFS.FunctionalTests.Tools;
using GVFS.Tests.Should;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace GVFS.FunctionalTests.Tests.EnlistmentPerFixture
{
    /// <summary>
    /// Functional tests for the mount's handling of the cone requests that the hooks send when
    /// <c>gvfs.auto-sparse-index</c> is true and the index is sparse.
    /// </summary>
    [TestFixture]
    public class AutoSparseIndexConeTests : AutoSparseIndexTestsBase
    {
        private const string HookFailurePrefix = "gvfs cone-management: widen not applied";

        // Files three or more levels deep, one for each different "first/second" directory
        private List<string> deepFiles;

        // Two files in one directory that no other test uses
        private string deletedFile;
        private string siblingOfDeletedFile;

        [OneTimeSetUp]
        public void SelectFiles()
        {
            List<IGrouping<string, string>> directories = this.GroupByTwoLevelDirectory();
            directories.Count.ShouldBeAtLeast(8, "HEAD has too few directories that are two levels deep");
            this.deepFiles = directories.Take(7).Select(group => group.First()).ToList();

            List<string> pair = directories
                .Skip(7)
                .SelectMany(group => group.GroupBy(path => ParentDirectory(path), StringComparer.OrdinalIgnoreCase))
                .First(group => group.Count() >= 2)
                .Take(2)
                .ToList();
            this.deletedFile = pair[0];
            this.siblingOfDeletedFile = pair[1];
        }

        [TestCase]
        public void GitAddWidensTheConeWithoutExpandingTheIndexAndTheConeNarrowsAfterwards()
        {
            string modifiedFile = this.deepFiles[0];
            string unmodifiedFile = this.deepFiles[1];
            string laterFile = this.deepFiles[2];
            string unmodifiedDirectory = TwoLevelDirectory(unmodifiedFile);
            File.WriteAllText(this.Enlistment.GetVirtualPathTo(modifiedFile), "modified by the test");

            // The command names two files outside the cone. Only the first one is modified.
            ProcessResult add = this.GitWithTrace($"add -- {modifiedFile} {unmodifiedFile}", out string trace);

            add.ExitCode.ShouldEqual(0, add.Errors);
            add.Errors.ShouldNotContain(ignoreCase: false, HookFailurePrefix);
            CountFullIndexExpansions(trace).ShouldEqual(0, "git expanded the whole index: " + add.Errors);
            this.GitShouldSucceed("diff --cached --name-only").ShouldContain(modifiedFile);

            // The cone keeps the directory of the modified file, but not the directory that the
            // command only named. The index is still sparse, and it still has the entries of
            // that directory until the next widen.
            string cone = File.ReadAllText(this.SparseCheckoutPath);
            ConeLines(cone).ShouldContain(line => IsConeLineFor(line, TwoLevelDirectory(modifiedFile)));
            ConeLines(cone).ShouldNotContain(line => IsConeLineFor(line, unmodifiedDirectory));
            this.SparseDirectoryEntries().ShouldBeNonEmpty();
            this.SparseIndexEntries().ShouldContain(entry => entry.Equals(unmodifiedFile, StringComparison.OrdinalIgnoreCase));

            // The next widen makes the index match the cone again, so the directory collapses.
            ProcessResult laterAdd = this.GitWithTrace($"add -- {laterFile}", out string laterTrace);

            laterAdd.ExitCode.ShouldEqual(0, laterAdd.Errors);
            laterAdd.Errors.ShouldNotContain(ignoreCase: false, HookFailurePrefix);
            CountFullIndexExpansions(laterTrace).ShouldEqual(0, "git expanded the whole index: " + laterAdd.Errors);
            this.SparseIndexEntries().ShouldNotContain(entry => entry.StartsWith(unmodifiedDirectory + "/", StringComparison.OrdinalIgnoreCase) && !entry.EndsWith("/"));
            File.ReadAllText(this.SparseCheckoutPath).ShouldEqual(cone);

            // Applying the cone to the index wrote no working-tree file and deleted nothing.
            // GVFS records any such write or delete in the modified paths.
            GVFSHelpers.ModifiedPathsShouldNotContain(this.Enlistment, FileSystemRunner.DefaultRunner, unmodifiedFile, unmodifiedDirectory + "/", laterFile);
            this.GitShouldSucceed($"hash-object -- {unmodifiedFile}").Trim()
                .ShouldEqual(this.GitShouldSucceed($"rev-parse HEAD:{unmodifiedFile}").Trim());
        }

        [TestCase]
        public void ConcurrentGitCommandsThatNameOutOfConePathsSucceed()
        {
            string firstFile = this.deepFiles[3];
            string secondFile = this.deepFiles[4];
            string readOnlyFile = this.deepFiles[5];
            File.WriteAllText(this.Enlistment.GetVirtualPathTo(firstFile), "first");
            File.WriteAllText(this.Enlistment.GetVirtualPathTo(secondFile), "second");

            // The two adds take the GVFS lock one after the other. The diff does not take the
            // lock, so it can run while an add holds it.
            Task<ProcessResult> firstAdd = Task.Run(() => this.GitWithTrace($"add -- {firstFile}", out _));
            Task<ProcessResult> secondAdd = Task.Run(() => this.GitWithTrace($"add -- {secondFile}", out _));
            Task<ProcessResult> diff = Task.Run(() => this.GitWithTrace($"diff --stat -- {readOnlyFile}", out _));
            Task.WaitAll(firstAdd, secondAdd, diff);

            firstAdd.Result.ExitCode.ShouldEqual(0, firstAdd.Result.Errors);
            secondAdd.Result.ExitCode.ShouldEqual(0, secondAdd.Result.Errors);
            diff.Result.ExitCode.ShouldEqual(0, diff.Result.Errors);
            this.GitShouldSucceed("diff --cached --name-only").ShouldContain(firstFile, secondFile);

            // When all commands are done, the cone has only the modified directories. A narrow
            // can reach the mount after its command ended, so the test waits for it.
            string[] coneLines = this.WaitForConeFile(lines => !lines.Any(line => IsConeLineFor(line, TwoLevelDirectory(readOnlyFile))));
            coneLines.ShouldContain(line => IsConeLineFor(line, TwoLevelDirectory(firstFile)));
            coneLines.ShouldContain(line => IsConeLineFor(line, TwoLevelDirectory(secondFile)));
            coneLines.ShouldNotContain(line => IsConeLineFor(line, TwoLevelDirectory(readOnlyFile)));
            this.SparseDirectoryEntries().ShouldBeNonEmpty();
        }

        [TestCase]
        public void UnlockedCommandDoesNotChangeTheCone()
        {
            string file = this.deepFiles[6];
            string coneBefore = File.ReadAllText(this.SparseCheckoutPath);

            // git diff does not take the GVFS lock, so the mount must not replace the index for it.
            ProcessResult diff = this.GitWithTrace($"diff --stat -- {file}", out _);

            diff.ExitCode.ShouldEqual(0, diff.Errors);
            diff.Errors.ShouldContain(HookFailurePrefix + ": Rejected");
            File.ReadAllText(this.SparseCheckoutPath).ShouldEqual(coneBefore);
        }

        [TestCase]
        public void WidenDoesNotRestoreADeletedFile()
        {
            File.Delete(this.Enlistment.GetVirtualPathTo(this.deletedFile));

            ProcessResult add = this.GitWithTrace($"add -- {this.siblingOfDeletedFile}", out _);

            add.ExitCode.ShouldEqual(0, add.Errors);
            if (this.GitChangesOnlyTheIndex)
            {
                // Git expands the directory of the deleted file without a checkout.
                add.Errors.ShouldNotContain(ignoreCase: false, HookFailurePrefix);
                this.GitShouldSucceed("status --porcelain").ShouldContain(" D " + this.deletedFile);
            }
            else
            {
                // An older Git checks the deleted file out again when it expands the directory,
                // so the mount must not widen the cone to that directory.
                add.Errors.ShouldContain(HookFailurePrefix + ": Rejected");
            }

            File.Exists(this.Enlistment.GetVirtualPathTo(this.deletedFile)).ShouldBeFalse("The widen restored the deleted file");
        }
    }
}
