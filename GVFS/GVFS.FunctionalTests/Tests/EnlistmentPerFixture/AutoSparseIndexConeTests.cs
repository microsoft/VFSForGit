using GVFS.FunctionalTests.FileSystemRunners;
using GVFS.FunctionalTests.Tools;
using GVFS.Tests.Should;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace GVFS.FunctionalTests.Tests.EnlistmentPerFixture
{
    /// <summary>
    /// Functional tests for the mount's handling of the cone requests that the hooks send when
    /// <c>gvfs.auto-sparse-index</c> is true and the index is sparse.
    /// </summary>
    [TestFixture]
    public class AutoSparseIndexConeTests : TestsWithEnlistmentPerFixture
    {
        private const string BypassGVFS = "-c core.virtualfilesystem= -c core.hookspath=";
        private const string RootOnlyCone = "/*\n!/*/\n";
        private const string HookFailurePrefix = "gvfs cone-management: widen not applied";

        private static readonly UTF8Encoding Utf8NoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

        // Files three or more levels deep, one for each different "first/second" directory
        private List<string> deepFiles;

        // Two files in one directory that no other test uses
        private string deletedFile;
        private string siblingOfDeletedFile;
        private int traceCount;

        [OneTimeSetUp]
        public void EnableSparseIndex()
        {
            string buildOptions = GitProcess.Invoke(this.Enlistment.RepoRoot, "version --build-options");
            if (!buildOptions.Contains("feature: vfs-sparse-index"))
            {
                Assert.Ignore("This git does not support a sparse index with VFS (git version --build-options has no 'feature: vfs-sparse-index')");
            }

            List<string> headFiles = this.GitShouldSucceed("-c core.quotepath=false ls-tree -r --name-only HEAD")
                .Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Select(path => path.Trim())
                .Where(path => path.Count(character => character == '/') >= 2 && !path.Contains(' '))
                .ToList();
            List<IGrouping<string, string>> directories = headFiles
                .GroupBy(path => TwoLevelDirectory(path), StringComparer.OrdinalIgnoreCase)
                .ToList();
            directories.Count.ShouldBeAtLeast(9, "HEAD has too few directories that are two levels deep");
            this.deepFiles = directories.Take(8).Select(group => group.First()).ToList();

            List<string> pair = directories
                .Skip(8)
                .SelectMany(group => group.GroupBy(path => path.Substring(0, path.LastIndexOf('/')), StringComparer.OrdinalIgnoreCase))
                .First(group => group.Count() >= 2)
                .Take(2)
                .ToList();
            this.deletedFile = pair[0];
            this.siblingOfDeletedFile = pair[1];

            // Mount with the feature on, a root-only cone, and a collapsed index. The persisted
            // projection gets the same index, as it does when git collapses the index while the
            // repository is mounted.
            this.Enlistment.UnmountGVFS();
            this.GitShouldSucceed("-c core.hookspath= config gvfs.auto-sparse-index true");
            this.GitShouldSucceed("-c core.hookspath= config core.sparseCheckout true");
            this.GitShouldSucceed("-c core.hookspath= config core.sparseCheckoutCone true");
            this.GitShouldSucceed("-c core.hookspath= config index.sparse true");
            File.WriteAllText(this.SparseCheckoutPath, RootOnlyCone, Utf8NoBom);
            this.GitShouldSucceed($"{BypassGVFS} -c sparse.expectFilesOutsideOfPatterns=true update-index --force-write-index");
            this.SparseDirectoryEntries().ShouldBeNonEmpty();
            File.Copy(this.Enlistment.GetDotGitPath("index"), Path.Combine(this.Enlistment.DotGVFSRoot, "GVFS_projection"), overwrite: true);
            this.Enlistment.MountGVFS();
        }

        private string SparseCheckoutPath => this.Enlistment.GetDotGitPath("info", "sparse-checkout");

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
        public void WidenOfAFullIndexCollapsesTheIndex()
        {
            string modifiedFile = this.deepFiles[7];
            int fullEntryCount = this.GitShouldSucceed("ls-tree -r --name-only HEAD")
                .Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Length;

            // A clone writes a full index with skip-worktree bits. Git also writes a full index when
            // index.sparse is false.
            this.GitShouldSucceed("-c index.sparse=false update-index --force-write-index");
            this.IndexHeaderEntryCount().ShouldEqual(fullEntryCount, "The index is not full");

            File.WriteAllText(this.Enlistment.GetVirtualPathTo(modifiedFile), "modified by the test");
            ProcessResult add = this.GitWithTrace($"add -- {modifiedFile}", out _);

            // The mount applies the cone with its own Git process, which must write a sparse
            // index. Otherwise the index stays full until a user command writes it.
            add.ExitCode.ShouldEqual(0, add.Errors);
            add.Errors.ShouldNotContain(ignoreCase: false, HookFailurePrefix);
            this.GitShouldSucceed("diff --cached --name-only").ShouldContain(modifiedFile);
            int sparseEntryCount = this.IndexHeaderEntryCount();
            (sparseEntryCount < fullEntryCount).ShouldBeTrue($"The widen did not collapse the full index: {sparseEntryCount} of {fullEntryCount} entries");

            // The full index has an entry for the file that an earlier test deleted, so the widen
            // does not expand a directory to reach it, and Git does not check it out again.
            File.Exists(this.Enlistment.GetVirtualPathTo(this.deletedFile)).ShouldBeFalse("The widen restored the deleted file");
        }

        [TestCase]
        public void WidenDoesNotRestoreADeletedFile()
        {
            // Expanding the directory of a deleted file makes Git check the file out again, so
            // the mount must not widen the cone to that directory.
            File.Delete(this.Enlistment.GetVirtualPathTo(this.deletedFile));

            ProcessResult add = this.GitWithTrace($"add -- {this.siblingOfDeletedFile}", out _);

            add.ExitCode.ShouldEqual(0, add.Errors);
            add.Errors.ShouldContain(HookFailurePrefix + ": Rejected");
            File.Exists(this.Enlistment.GetVirtualPathTo(this.deletedFile)).ShouldBeFalse("The widen restored the deleted file");
        }

        private static string[] ConeLines(string cone)
        {
            return cone.Split('\n');
        }

        private string[] WaitForConeFile(Func<string[], bool> isExpected)
        {
            string[] lines = ConeLines(File.ReadAllText(this.SparseCheckoutPath));
            for (int attempt = 0; attempt < 50 && !isExpected(lines); attempt++)
            {
                System.Threading.Thread.Sleep(100);
                lines = ConeLines(File.ReadAllText(this.SparseCheckoutPath));
            }

            return lines;
        }

        private static bool IsConeLineFor(string line, string directory)
        {
            return line.Equals("/" + directory + "/", StringComparison.OrdinalIgnoreCase);
        }

        private static string TwoLevelDirectory(string path)
        {
            string[] segments = path.Split('/');
            return segments[0] + "/" + segments[1];
        }

        private static int CountFullIndexExpansions(string trace2EventFile)
        {
            File.Exists(trace2EventFile).ShouldBeTrue("git did not write the trace2 file " + trace2EventFile);
            return File.ReadLines(trace2EventFile)
                .Count(line => line.Contains("\"event\":\"region_enter\"") && line.Contains("\"label\":\"ensure_full_index\""));
        }

        private ProcessResult GitWithTrace(string command, out string trace2EventFile)
        {
            string traceFile = Path.Combine(this.Enlistment.EnlistmentRoot, $"cone-trace2-{System.Threading.Interlocked.Increment(ref this.traceCount)}.json");
            trace2EventFile = traceFile;
            return GitProcess.InvokeProcess(
                this.Enlistment.RepoRoot,
                command,
                new Dictionary<string, string>
                {
                    { "GIT_TRACE", "1" },
                    { "GIT_TRACE2_EVENT", traceFile },
                });
        }

        private string GitShouldSucceed(string command)
        {
            ProcessResult result = GitProcess.InvokeProcess(this.Enlistment.RepoRoot, command);
            result.ExitCode.ShouldEqual(0, $"git {command} failed: {result.Errors}");
            return result.Output;
        }

        private int IndexHeaderEntryCount()
        {
            // Bytes 8 to 11 of the index file are its entry count (big-endian). A sparse-directory
            // entry counts as one entry. ls-files cannot show the count on disk, because Git
            // collapses a full index in memory when it reads it with index.sparse true.
            byte[] header = new byte[12];
            using (FileStream stream = new FileStream(this.Enlistment.GetDotGitPath("index"), FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            {
                stream.ReadExactly(header, 0, header.Length);
            }

            return (header[8] << 24) | (header[9] << 16) | (header[10] << 8) | header[11];
        }

        private List<string> SparseDirectoryEntries()
        {
            return this.SparseIndexEntries().Where(line => line.EndsWith("/")).ToList();
        }

        private List<string> SparseIndexEntries()
        {
            // Without the virtual file system, git sees the projected folders outside the cone. By
            // default it then clears their skip-worktree bits and expands the index in memory.
            return this.GitShouldSucceed($"{BypassGVFS} -c sparse.expectFilesOutsideOfPatterns=true -c core.quotepath=false ls-files --sparse")
                .Split('\n')
                .Select(line => line.Trim())
                .Where(line => line.Length > 0)
                .ToList();
        }
    }
}
