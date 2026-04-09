using GVFS.FunctionalTests.FileSystemRunners;
using GVFS.FunctionalTests.Properties;
using GVFS.FunctionalTests.Should;
using GVFS.FunctionalTests.Tools;
using GVFS.Tests.Should;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace GVFS.FunctionalTests.Tests
{
    [TestFixture]
    [Category(Categories.FastFetch)]
    [Category(Categories.ExtraCoverage)]
    public class FastFetchTests
    {
        private const string LsTreeTypeInPathBranchName = "FunctionalTests/20181105_LsTreeTypeInPath";

        private readonly string fastFetchRepoRoot = Settings.Default.FastFetchRoot;
        private readonly string fastFetchControlRoot = Settings.Default.FastFetchControl;
        private readonly string fastFetchBaseRoot = Settings.Default.FastFetchBaseRoot;

        [OneTimeSetUp]
        public void InitControlRepo()
        {
            Directory.CreateDirectory(this.fastFetchControlRoot);
            GitProcess.Invoke(this.fastFetchBaseRoot, "clone -b " + Settings.Default.Commitish + " " + GVFSTestConfig.RepoToClone + " " + this.fastFetchControlRoot);
        }

        [SetUp]
        public void InitRepo()
        {
            // Just in case Teardown did not run.  Say when debugging...
            if (Directory.Exists(this.fastFetchRepoRoot))
            {
                this.TearDownTests();
            }

            Directory.CreateDirectory(this.fastFetchRepoRoot);
            GitProcess.Invoke(this.fastFetchRepoRoot, "init");
            GitProcess.Invoke(this.fastFetchRepoRoot, "remote add origin " + GVFSTestConfig.RepoToClone);
        }

        [TearDown]
        public void TearDownTests()
        {
            RepositoryHelpers.DeleteTestDirectory(this.fastFetchRepoRoot);
        }

        [OneTimeTearDown]
        public void DeleteControlRepo()
        {
            RepositoryHelpers.DeleteTestDirectory(this.fastFetchControlRoot);
        }

        [TestCase]
        public void CanFetchIntoEmptyGitRepoAndCheckoutWithGit()
        {
            this.RunFastFetch("-b " + Settings.Default.Commitish);

            this.GetRefTreeSha("remotes/origin/" + Settings.Default.Commitish).ShouldNotBeNull();

            ProcessResult checkoutResult = GitProcess.InvokeProcess(this.fastFetchRepoRoot, "checkout " + Settings.Default.Commitish);
            checkoutResult.Errors.ShouldEqual("Switched to a new branch '" + Settings.Default.Commitish + "'\r\n");
            checkoutResult.Output.ShouldEqual("Branch '" + Settings.Default.Commitish + "' set up to track remote branch '" + Settings.Default.Commitish + "' from 'origin'.\n");

            // When checking out with git, must manually update shallow.
            ProcessResult updateRefResult = GitProcess.InvokeProcess(this.fastFetchRepoRoot, "update-ref shallow " + Settings.Default.Commitish);
            updateRefResult.ExitCode.ShouldEqual(0);
            updateRefResult.Errors.ShouldBeEmpty();
            updateRefResult.Output.ShouldBeEmpty();

            this.CurrentBranchShouldEqual(Settings.Default.Commitish);

            this.fastFetchRepoRoot.ShouldBeADirectory(FileSystemRunner.DefaultRunner)
                .WithDeepStructure(FileSystemRunner.DefaultRunner, this.fastFetchControlRoot);
        }

        [TestCase]
        public void CanFetchAndCheckoutASingleFolderIntoEmptyGitRepo()
        {
            this.RunFastFetch("--checkout --folders \"/GVFS\" -b " + Settings.Default.Commitish);

            this.CurrentBranchShouldEqual(Settings.Default.Commitish);

            this.fastFetchRepoRoot.ShouldBeADirectory(FileSystemRunner.DefaultRunner);
            List<string> dirs = Directory.EnumerateFileSystemEntries(this.fastFetchRepoRoot).ToList();
            dirs.SequenceEqual(new[]
            {
                Path.Combine(this.fastFetchRepoRoot, ".git"),
                Path.Combine(this.fastFetchRepoRoot, "GVFS"),
                Path.Combine(this.fastFetchRepoRoot, "GVFS.sln")
            });

            Directory.EnumerateFileSystemEntries(Path.Combine(this.fastFetchRepoRoot, "GVFS"), "*", SearchOption.AllDirectories)
                .Count()
                .ShouldEqual(345);

            this.AllFetchedFilePathsShouldPassCheck(path => path.StartsWith("GVFS", FileSystemHelpers.PathComparison));
        }

        [TestCase]
        public void CanFetchAndCheckoutMultipleTimesUsingForceCheckoutFlag()
        {
            this.RunFastFetch($"--checkout --folders \"/GVFS\" -b {Settings.Default.Commitish}");

            this.CurrentBranchShouldEqual(Settings.Default.Commitish);

            this.fastFetchRepoRoot.ShouldBeADirectory(FileSystemRunner.DefaultRunner);
            List<string> dirs = Directory.EnumerateFileSystemEntries(this.fastFetchRepoRoot).ToList();
            dirs.SequenceEqual(new[]
            {
                Path.Combine(this.fastFetchRepoRoot, ".git"),
                Path.Combine(this.fastFetchRepoRoot, "GVFS"),
                Path.Combine(this.fastFetchRepoRoot, "GVFS.sln")
            });

            Directory.EnumerateFileSystemEntries(Path.Combine(this.fastFetchRepoRoot, "GVFS"), "*", SearchOption.AllDirectories)
                .Count()
                .ShouldEqual(345);
            this.AllFetchedFilePathsShouldPassCheck(path => path.StartsWith("GVFS", FileSystemHelpers.PathComparison));

            // Run a second time in the same repo on the same branch with more folders.
            this.RunFastFetch($"--checkout --folders \"/GVFS;/Scripts\" -b {Settings.Default.Commitish} --force-checkout");
            dirs = Directory.EnumerateFileSystemEntries(this.fastFetchRepoRoot).ToList();
            dirs.SequenceEqual(new[]
            {
                Path.Combine(this.fastFetchRepoRoot, ".git"),
                Path.Combine(this.fastFetchRepoRoot, "GVFS"),
                Path.Combine(this.fastFetchRepoRoot, "Scripts"),
                Path.Combine(this.fastFetchRepoRoot, "GVFS.sln")
            });
            Directory.EnumerateFileSystemEntries(Path.Combine(this.fastFetchRepoRoot, "Scripts"), "*", SearchOption.AllDirectories)
                .Count()
                .ShouldEqual(5);
        }

        [TestCase]
        public void ForceCheckoutRequiresCheckout()
        {
            this.RunFastFetch($"--checkout --folders \"/Scripts\" -b {Settings.Default.Commitish}");

            // Run a second time in the same repo on the same branch with more folders but expect an error.
            ProcessResult result = this.RunFastFetch($"--force-checkout --folders \"/GVFS;/Scripts\" -b {Settings.Default.Commitish}");

            string[] expectedResults = new string[] { "Cannot use --force-checkout option without --checkout option." };
            result.Output.ShouldContain(expectedResults);
        }

        [TestCase]
        public void FastFetchFolderWithOnlyOneFile()
        {
            string folderPath = Path.Combine("GVFS", "GVFS", "Properties");
            this.RunFastFetch("--checkout --folders " + folderPath + " -b " + Settings.Default.Commitish);

            this.CurrentBranchShouldEqual(Settings.Default.Commitish);

            this.fastFetchRepoRoot.ShouldBeADirectory(FileSystemRunner.DefaultRunner);
            List<string> dirs = Directory.EnumerateFileSystemEntries(this.fastFetchRepoRoot).ToList();
            dirs.SequenceEqual(new[]
            {
                Path.Combine(this.fastFetchRepoRoot, ".git"),
                Path.Combine(this.fastFetchRepoRoot, "GVFS"),
                Path.Combine(this.fastFetchRepoRoot, "GVFS.sln")
            });

            dirs = Directory.EnumerateFileSystemEntries(Path.Combine(this.fastFetchRepoRoot, "GVFS"), "*", SearchOption.AllDirectories).ToList();
            dirs.SequenceEqual(new[]
            {
                Path.Combine(this.fastFetchRepoRoot, "GVFS", "GVFS"),
                Path.Combine(this.fastFetchRepoRoot, "GVFS", "GVFS", "Properties"),
                Path.Combine(this.fastFetchRepoRoot, "GVFS", "GVFS", "Properties", "AssemblyInfo.cs"),
            });

            this.AllFetchedFilePathsShouldPassCheck(path => path.StartsWith("GVFS", FileSystemHelpers.PathComparison));
        }

        [TestCase]
        public void CanFetchAndCheckoutBranchIntoEmptyGitRepo()
        {
            this.RunFastFetch("--checkout -b " + Settings.Default.Commitish);

            this.CurrentBranchShouldEqual(Settings.Default.Commitish);

            this.fastFetchRepoRoot.ShouldBeADirectory(FileSystemRunner.DefaultRunner)
                .WithDeepStructure(FileSystemRunner.DefaultRunner, this.fastFetchControlRoot);
        }

        [TestCase]
        public void CanUpdateIndex()
        {
            // Testing index versions 2, 3 and 4.  Not bothering to test version 1; it's not in use anymore.
            this.CanUpdateIndex(2, indexSigningOff: true);
            this.CanUpdateIndex(3, indexSigningOff: true);
            this.CanUpdateIndex(4, indexSigningOff: true);

            this.CanUpdateIndex(2, indexSigningOff: false);
            this.CanUpdateIndex(3, indexSigningOff: false);
            this.CanUpdateIndex(4, indexSigningOff: false);
        }

        [TestCase]
        public void CanFetchAndCheckoutAfterDeletingIndex()
        {
            this.RunFastFetch("--checkout -b " + Settings.Default.Commitish);

            File.Delete(Path.Combine(this.fastFetchRepoRoot, ".git", "index"));
            this.RunFastFetch("--checkout -b " + Settings.Default.Commitish);

            this.CurrentBranchShouldEqual(Settings.Default.Commitish);

            this.fastFetchRepoRoot.ShouldBeADirectory(FileSystemRunner.DefaultRunner)
                .WithDeepStructure(FileSystemRunner.DefaultRunner, this.fastFetchControlRoot);
        }

        public void CanUpdateIndex(int indexVersion, bool indexSigningOff)
        {
            // Initialize the repo
            GitProcess.Invoke(this.fastFetchRepoRoot, "config --local --add core.gvfs " + (indexSigningOff ? 1 : 0));
            this.CanFetchAndCheckoutBranchIntoEmptyGitRepo();
            string lsfilesAfterFirstFetch = GitProcess.Invoke(this.fastFetchRepoRoot, "ls-files --debug");
            lsfilesAfterFirstFetch.ShouldBeNonEmpty();

            // Reset the index and use 'git status' to get baseline.
            GitProcess.Invoke(this.fastFetchRepoRoot, $"-c index.version={indexVersion} read-tree HEAD");
            string lsfilesBeforeStatus = GitProcess.Invoke(this.fastFetchRepoRoot, "ls-files --debug");
            lsfilesBeforeStatus.ShouldBeNonEmpty();

            GitProcess.Invoke(this.fastFetchRepoRoot, "status");
            string lsfilesAfterStatus = GitProcess.Invoke(this.fastFetchRepoRoot, "ls-files --debug");
            lsfilesAfterStatus.ShouldBeNonEmpty();
            lsfilesAfterStatus.ShouldNotBeSameAs(lsfilesBeforeStatus, "Ensure 'git status' updates index");

            // Reset the index and use fastfetch to update the index. Compare against 'git status' baseline.
            GitProcess.Invoke(this.fastFetchRepoRoot, $"-c index.version= {indexVersion} read-tree HEAD");
            ProcessResult fastFetchResult = this.RunFastFetch("--checkout --Allow-index-metadata-update-from-working-tree");
            string lsfilesAfterUpdate = GitProcess.Invoke(this.fastFetchRepoRoot, "ls-files --debug");
            lsfilesAfterUpdate.ShouldEqual(lsfilesAfterStatus, "git status and fastfetch didn't result in the same index");

            // Don't reset the index and use 'git status' to update again.  Should be same results.
            this.RunFastFetch("--checkout --Allow-index-metadata-update-from-working-tree");
            string lsfilesAfterUpdate2 = GitProcess.Invoke(this.fastFetchRepoRoot, "ls-files --debug");
            lsfilesAfterUpdate2.ShouldEqual(lsfilesAfterUpdate, "Incremental update should not change index");

            // Verify that the final results are the same as the intial fetch results
            lsfilesAfterUpdate2.ShouldEqual(lsfilesAfterFirstFetch, "Incremental update should not change index");
        }

        [TestCase]
        public void IncrementalChangesLeaveGoodStatus()
        {
            // Specific commits taken from branch  FunctionalTests/20170206_Conflict_Source
            // These commits have adds, edits and removals
            const string BaseCommit = "db95d631e379d366d26d899523f8136a77441914";
            const string UpdateCommit = "51d15f7584e81d59d44c1511ce17d7c493903390";

            GitProcess.Invoke(this.fastFetchRepoRoot, "config --local --add core.gvfs 1");

            this.RunFastFetch($"--checkout -c {BaseCommit}");
            string status = GitProcess.Invoke(this.fastFetchRepoRoot, "status --porcelain");
            status.ShouldBeEmpty("Status shows unexpected files changed");

            this.RunFastFetch($"--checkout -c {UpdateCommit}");
            status = GitProcess.Invoke(this.fastFetchRepoRoot, "status --porcelain");
            status.ShouldBeEmpty("Status shows unexpected files changed");

            // Now that we have the content, verify that these commits meet our needs...
            string changes = GitProcess.Invoke(this.fastFetchRepoRoot, $"diff-tree -r --name-status {BaseCommit}..{UpdateCommit}");

            // There must be modified files in these commits.  Modified files must
            // be updated with valid metadata (times, sizes) or 'git status' will
            // show them as modified when they were not actually modified.
            Regex.IsMatch(changes, @"^M\s", RegexOptions.Multiline).ShouldEqual(true, "Data does not meet requirements");
        }

        [TestCase]
        public void CanFetchAndCheckoutBetweenTwoBranchesIntoEmptyGitRepo()
        {
            this.RunFastFetch("--checkout -b " + Settings.Default.Commitish);
            this.CurrentBranchShouldEqual(Settings.Default.Commitish);

            // Switch to another branch
            this.RunFastFetch("--checkout -b FunctionalTests/20170602");
            this.CurrentBranchShouldEqual("FunctionalTests/20170602");

            // And back
            this.RunFastFetch("--checkout -b " + Settings.Default.Commitish);
            this.CurrentBranchShouldEqual(Settings.Default.Commitish);

            this.fastFetchRepoRoot.ShouldBeADirectory(FileSystemRunner.DefaultRunner)
                .WithDeepStructure(FileSystemRunner.DefaultRunner, this.fastFetchControlRoot);
        }

        [TestCase]
        public void CanDetectAlreadyUpToDate()
        {
            this.RunFastFetch("--checkout -b " + Settings.Default.Commitish);
            this.CurrentBranchShouldEqual(Settings.Default.Commitish);

            this.RunFastFetch(" -b " + Settings.Default.Commitish).Output.ShouldContain("\"TotalMissingObjects\":0");
            this.RunFastFetch("--checkout -b " + Settings.Default.Commitish).Output.ShouldContain("\"RequiredBlobsCount\":0");

            this.CurrentBranchShouldEqual(Settings.Default.Commitish);
            this.fastFetchRepoRoot.ShouldBeADirectory(FileSystemRunner.DefaultRunner)
                .WithDeepStructure(FileSystemRunner.DefaultRunner, this.fastFetchControlRoot);
        }

        [TestCase]
        public void SuccessfullyChecksOutCaseChanges()
        {
            // The delta between these two is the same as the UnitTest "caseChange.txt" data file.
            this.RunFastFetch("--checkout -c b3ddcf43b997cba3fbf9d2341b297e22bf48601a");
            this.RunFastFetch("--checkout -c e637c874f6a914ae83cd5668bcdd07293fef961d");

            GitProcess.Invoke(this.fastFetchControlRoot, "checkout e637c874f6a914ae83cd5668bcdd07293fef961d");

            try
            {
                // Ignore case differences on case-insensitive filesystems
                this.fastFetchRepoRoot.ShouldBeADirectory(FileSystemRunner.DefaultRunner)
                    .WithDeepStructure(FileSystemRunner.DefaultRunner, this.fastFetchControlRoot, ignoreCase: !FileSystemHelpers.CaseSensitiveFileSystem);
            }
            finally
            {
                GitProcess.Invoke(this.fastFetchControlRoot, "checkout " + Settings.Default.Commitish);
            }
        }

        [TestCase]
        public void SuccessfullyChecksOutDirectoryToFileToDirectory()
        {
            // This test switches between two branches and verifies specific transitions occured
            this.RunFastFetch("--checkout -b FunctionalTests/20171103_DirectoryFileTransitionsPart1");

            // Delta of interest - Check initial state
            // renamed:    foo.cpp\foo.cpp -> foo.cpp
            //   where the top level "foo.cpp" is a folder with a file, then becomes just a file
            //   note that folder\file names picked illustrate a real example
            Path.Combine(this.fastFetchRepoRoot, "foo.cpp", "foo.cpp")
                .ShouldBeAFile(FileSystemRunner.DefaultRunner);

            // Delta of interest - Check initial state
            // renamed:    a\a <-> b && b <-> a
            //   where a\a contains "file contents one"
            //   and b contains "file contents two"
            //   This tests two types of renames crossing into each other
            Path.Combine(this.fastFetchRepoRoot, "a", "a")
                .ShouldBeAFile(FileSystemRunner.DefaultRunner).WithContents("file contents one");
            Path.Combine(this.fastFetchRepoRoot, "b")
                .ShouldBeAFile(FileSystemRunner.DefaultRunner).WithContents("file contents two");

            // Delta of interest - Check initial state
            // renamed:    c\c <-> d\c && d\d <-> c\d
            //   where c\c contains "file contents c"
            //   and d\d contains "file contents d"
            //   This tests two types of renames crossing into each other
            Path.Combine(this.fastFetchRepoRoot, "c", "c")
                .ShouldBeAFile(FileSystemRunner.DefaultRunner).WithContents("file contents c");
            Path.Combine(this.fastFetchRepoRoot, "d", "d")
                .ShouldBeAFile(FileSystemRunner.DefaultRunner).WithContents("file contents d");

            // Now switch to second branch, part2 and verify transitions
            this.RunFastFetch("--checkout -b FunctionalTests/20171103_DirectoryFileTransitionsPart2");

            // Delta of interest - Verify change
            // renamed:    foo.cpp\foo.cpp -> foo.cpp
            Path.Combine(this.fastFetchRepoRoot, "foo.cpp")
                .ShouldBeAFile(FileSystemRunner.DefaultRunner);

            // Delta of interest - Verify change
            // renamed:    a\a <-> b && b <-> a
            Path.Combine(this.fastFetchRepoRoot, "a")
                .ShouldBeAFile(FileSystemRunner.DefaultRunner).WithContents("file contents two");
            Path.Combine(this.fastFetchRepoRoot, "b")
                .ShouldBeAFile(FileSystemRunner.DefaultRunner).WithContents("file contents one");

            // Delta of interest - Verify change
            // renamed:    c\c <-> d\c && d\d <-> c\d
            Path.Combine(this.fastFetchRepoRoot, "c", "d")
                .ShouldBeAFile(FileSystemRunner.DefaultRunner).WithContents("file contents d");
            Path.Combine(this.fastFetchRepoRoot, "d", "c")
                .ShouldBeAFile(FileSystemRunner.DefaultRunner).WithContents("file contents c");
            Path.Combine(this.fastFetchRepoRoot, "c", "c")
                .ShouldNotExistOnDisk(FileSystemRunner.DefaultRunner);
            Path.Combine(this.fastFetchRepoRoot, "d", "d")
                .ShouldNotExistOnDisk(FileSystemRunner.DefaultRunner);

            // And back again
            this.RunFastFetch("--checkout -b FunctionalTests/20171103_DirectoryFileTransitionsPart1");

            // Delta of interest - Final validation
            // renamed:    foo.cpp\foo.cpp -> foo.cpp
            Path.Combine(this.fastFetchRepoRoot, "foo.cpp", "foo.cpp")
                .ShouldBeAFile(FileSystemRunner.DefaultRunner);

            // Delta of interest - Final validation
            // renamed:    a\a <-> b && b <-> a
            Path.Combine(this.fastFetchRepoRoot, "a", "a")
                .ShouldBeAFile(FileSystemRunner.DefaultRunner).WithContents("file contents one");
            Path.Combine(this.fastFetchRepoRoot, "b")
                .ShouldBeAFile(FileSystemRunner.DefaultRunner).WithContents("file contents two");

            // Delta of interest - Final validation
            // renamed:    c\c <-> d\c && d\d <-> c\d
            Path.Combine(this.fastFetchRepoRoot, "c", "c")
                .ShouldBeAFile(FileSystemRunner.DefaultRunner).WithContents("file contents c");
            Path.Combine(this.fastFetchRepoRoot, "d", "d")
                .ShouldBeAFile(FileSystemRunner.DefaultRunner).WithContents("file contents d");
            Path.Combine(this.fastFetchRepoRoot, "c", "d")
                .ShouldNotExistOnDisk(FileSystemRunner.DefaultRunner);
            Path.Combine(this.fastFetchRepoRoot, "d", "c")
                .ShouldNotExistOnDisk(FileSystemRunner.DefaultRunner);
        }

        [TestCase]
        public void CanFetchPathsWithLsTreeTypes()
        {
            this.RunFastFetch("--checkout -b " + LsTreeTypeInPathBranchName);
            Path.Combine(this.fastFetchRepoRoot, "Test_LsTree_Issues", "file with tree in name.txt")
                .ShouldBeAFile(FileSystemRunner.DefaultRunner).WithContents("File with \" tree \" in name caused issues with ls tree diff logic.");
            Path.Combine(this.fastFetchRepoRoot, "Test_LsTree_Issues", "directory with blob in path")
                .ShouldBeADirectory(FileSystemRunner.DefaultRunner);
            Path.Combine(this.fastFetchRepoRoot, "Test_LsTree_Issues", "directory with blob in path", "file with tree in name.txt")
                .ShouldBeAFile(FileSystemRunner.DefaultRunner).WithContents("File with \" tree \" in name caused issues with ls tree diff logic. This is another example.");
        }

        private void AllFetchedFilePathsShouldPassCheck(Func<string, bool> checkPath)
        {
            // Form a cache map of sha => path
            string[] allObjects = GitProcess.Invoke(this.fastFetchRepoRoot, "cat-file --batch-check --batch-all-objects").Split('\n');
            string[] gitlsLines = GitProcess.Invoke(this.fastFetchRepoRoot, "ls-tree -r HEAD").Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries);
            Dictionary<string, List<string>> allPaths = new Dictionary<string, List<string>>();
            foreach (string line in gitlsLines)
            {
                string sha = this.GetShaFromLsLine(line);
                string path = this.GetPathFromLsLine(line);

                if (!allPaths.ContainsKey(sha))
                {
                    allPaths.Add(sha, new List<string>());
                }

                allPaths[sha].Add(path);
            }

            foreach (string sha in allObjects.Where(line => line.Contains(" blob ")).Select(line => line.Substring(0, 40)))
            {
                allPaths.ContainsKey(sha).ShouldEqual(true, "Found a blob that wasn't in the tree: " + sha);

                // A single blob should map to multiple files, so if any pass for a single sha, we have to give a pass.
                allPaths[sha].Any(path => checkPath(path))
                    .ShouldEqual(true, "Downloaded extra paths:\r\n" + string.Join("\r\n", allPaths[sha]));
            }
        }

        private void CurrentBranchShouldEqual(string commitish)
        {
            // Ensure remote branch has been created
            this.GetRefTreeSha("remotes/origin/" + commitish).ShouldNotBeNull();

            // And head has been updated to local branch, which are both updated
            this.GetRefTreeSha("HEAD")
                .ShouldNotBeNull()
                .ShouldEqual(this.GetRefTreeSha(commitish));

            // Ensure no errors are thrown with git log
            GitHelpers.CheckGitCommand(this.fastFetchRepoRoot, "log");
        }

        private string GetRefTreeSha(string refName)
        {
            string headInfo = GitProcess.Invoke(this.fastFetchRepoRoot, "cat-file -p " + refName);
            if (string.IsNullOrEmpty(headInfo) || headInfo.EndsWith("missing"))
            {
                return null;
            }

            string[] headInfoLines = headInfo.Split('\n');
            headInfoLines[0].StartsWith("tree").ShouldEqual(true);
            int firstSpace = headInfoLines[0].IndexOf(' ');
            string headTreeSha = headInfoLines[0].Substring(firstSpace + 1);
            headTreeSha.Length.ShouldEqual(40);
            return headTreeSha;
        }

        private ProcessResult RunFastFetch(string args)
        {
            args = args + " --verbose";

            string fastfetch = Path.Combine(Settings.Default.CurrentDirectory, "FastFetch.exe");

            File.Exists(fastfetch).ShouldBeTrue($"{fastfetch} did not exist.");
            Console.WriteLine($"Using {fastfetch}");

            ProcessStartInfo processInfo = new ProcessStartInfo(fastfetch);
            processInfo.Arguments = args;
            processInfo.WorkingDirectory = this.fastFetchRepoRoot;
            processInfo.UseShellExecute = false;
            processInfo.RedirectStandardOutput = true;
            processInfo.RedirectStandardError = true;

            ProcessResult result = ProcessHelper.Run(processInfo);

            return result;
        }

        private string GetShaFromLsLine(string line)
        {
            string output = line.Substring(line.LastIndexOf('\t') - 40, 40);
            return output;
        }

        private string GetPathFromLsLine(string line)
        {
            int idx = line.LastIndexOf('\t') + 1;
            string output = line.Substring(idx, line.Length - idx);
            return output;
        }
    }
}
