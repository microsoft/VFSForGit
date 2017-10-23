using GVFS.FunctionalTests.Category;
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
    [Category(CategoryConstants.FastFetch)]
    public class FastFetchTests
    {
        private readonly string fastFetchRepoRoot = Settings.Default.FastFetchRoot;
        private readonly string fastFetchControlRoot = Settings.Default.FastFetchControl;

        [OneTimeSetUp]
        public void InitControlRepo()
        {
            Directory.CreateDirectory(this.fastFetchControlRoot);
            GitProcess.Invoke("C:\\", "clone -b " + Settings.Default.Commitish + " " + Settings.Default.RepoToClone + " " + this.fastFetchControlRoot);
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
            GitProcess.Invoke(this.fastFetchRepoRoot, "remote add origin " + Settings.Default.RepoToClone);
        }

        [TearDown]
        public void TearDownTests()
        {
            CmdRunner.DeleteDirectoryWithRetry(this.fastFetchRepoRoot);
        }

        [OneTimeTearDown]
        public void DeleteControlRepo()
        {
            CmdRunner.DeleteDirectoryWithRetry(this.fastFetchControlRoot);
        }
        
        [TestCase]
        public void CanFetchIntoEmptyGitRepoAndCheckoutWithGit()
        {
            this.RunFastFetch("-b " + Settings.Default.Commitish);

            this.GetRefTreeSha("remotes/origin/" + Settings.Default.Commitish).ShouldNotBeNull();

            ProcessResult checkoutResult = GitProcess.InvokeProcess(this.fastFetchRepoRoot, "checkout " + Settings.Default.Commitish);
            checkoutResult.Errors.ShouldEqual("Switched to a new branch '" + Settings.Default.Commitish + "'\r\n");
            checkoutResult.Output.ShouldEqual("Branch " + Settings.Default.Commitish + " set up to track remote branch " + Settings.Default.Commitish + " from origin.\n");

            // When checking out with git, must manually update shallow.
            ProcessResult updateRefResult = GitProcess.InvokeProcess(this.fastFetchRepoRoot, "update-ref shallow " + Settings.Default.Commitish);
            updateRefResult.ExitCode.ShouldEqual(0);
            updateRefResult.Errors.ShouldBeEmpty();
            updateRefResult.Output.ShouldBeEmpty();

            this.CurrentBranchShouldEqual(Settings.Default.Commitish);

            this.fastFetchRepoRoot.ShouldBeADirectory(FileSystemRunner.DefaultRunner)
                .WithDeepStructure(FileSystemRunner.DefaultRunner, this.fastFetchControlRoot, skipEmptyDirectories: false);
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

            this.AllFetchedFilePathsShouldPassCheck(path => path.StartsWith("GVFS", StringComparison.OrdinalIgnoreCase));
        }

        [TestCase]
        public void CanFetchAndCheckoutBranchIntoEmptyGitRepo()
        {
            this.RunFastFetch("--checkout -b " + Settings.Default.Commitish);

            this.CurrentBranchShouldEqual(Settings.Default.Commitish);

            this.fastFetchRepoRoot.ShouldBeADirectory(FileSystemRunner.DefaultRunner)
                .WithDeepStructure(FileSystemRunner.DefaultRunner, this.fastFetchControlRoot, skipEmptyDirectories: false);
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
                .WithDeepStructure(FileSystemRunner.DefaultRunner, this.fastFetchControlRoot, skipEmptyDirectories: false);
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
            string fastfetchoutput = this.RunFastFetch("--checkout --Allow-index-metadata-update-from-working-tree");
            Trace.WriteLine(fastfetchoutput); // Written to log file for manual investigation
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
            const string BaseCommit = "170b13ce1990c53944403a70e93c257061598ae0";
            const string UpdateCommit = "f2546f8e9ce7d7b1e3a0835932f0d6a6145665b1";

            GitProcess.Invoke(this.fastFetchRepoRoot, "config --local --add core.gvfs 1");

            this.RunFastFetch($"--checkout -c {BaseCommit}");
            string status = GitProcess.Invoke(this.fastFetchRepoRoot, "status --porcelain");
            status.ShouldBeEmpty("Status shows unexpected files changed");

            string output = this.RunFastFetch($"--checkout -c {UpdateCommit}");
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
                .WithDeepStructure(FileSystemRunner.DefaultRunner, this.fastFetchControlRoot, skipEmptyDirectories: false);
        }

        [TestCase]
        public void CanDetectAlreadyUpToDate()
        {
            this.RunFastFetch("--checkout -b " + Settings.Default.Commitish);
            this.CurrentBranchShouldEqual(Settings.Default.Commitish);
            
            this.RunFastFetch(" -b " + Settings.Default.Commitish).ShouldContain("\"TotalMissingObjects\":0");
            this.RunFastFetch("--checkout -b " + Settings.Default.Commitish).ShouldContain("\"RequiredBlobsCount\":0");

            this.CurrentBranchShouldEqual(Settings.Default.Commitish);
            this.fastFetchRepoRoot.ShouldBeADirectory(FileSystemRunner.DefaultRunner)
                .WithDeepStructure(FileSystemRunner.DefaultRunner, this.fastFetchControlRoot, skipEmptyDirectories: false);
        }

        [TestCase]
        public void SuccessfullyChecksOutCaseChanges()
        {
            // The delta between these two is the same as the UnitTest "caseChange.txt" data file.
            this.RunFastFetch("--checkout -c b5fd7d23706a18cff3e2b8225588d479f7e51138");
            this.RunFastFetch("--checkout -c fd4ae4312eb504fd40e78d2d4cf349004967a8b4");
            
            GitProcess.Invoke(this.fastFetchControlRoot, "checkout fd4ae4312eb504fd40e78d2d4cf349004967a8b4");

            try
            {
                this.fastFetchRepoRoot.ShouldBeADirectory(FileSystemRunner.DefaultRunner)
                    .WithDeepStructure(FileSystemRunner.DefaultRunner, this.fastFetchControlRoot, skipEmptyDirectories: false, ignoreCase: true);
            }
            finally
            {
                GitProcess.Invoke(this.fastFetchControlRoot, "checkout " + Settings.Default.Commitish);
            }
        }

        [TestCase]
        public void SuccessfullyChecksOutDirectoryToFileToDirectory()
        {
            // The delta between these two is:
            // renamed:    GVFlt_MoveFileTest/NoneToNone/from/subFolder/from.txt -> GVFlt_MoveFileTest/NoneToNone/from/subFolder
            //   where "subFolder" is a folder first, then becomes a file called "subFolder"
            this.RunFastFetch("--checkout -c b5fd7d23706a18cff3e2b8225588d479f7e51138");
            Path.Combine(this.fastFetchRepoRoot, "GVFlt_MoveFileTest\\NoneToNone\\from\\subFolder\\from.txt")
                .ShouldBeAFile(FileSystemRunner.DefaultRunner);

            // This commit is tracked by the "FunctionalTests/20171017_RenameDirectoryToFile" test branch 
            this.RunFastFetch("--checkout -c e6c02e421504313797b34eba50f8141472fb64ef");
            Path.Combine(this.fastFetchRepoRoot, "GVFlt_MoveFileTest\\NoneToNone\\from\\subFolder")
                .ShouldBeAFile(FileSystemRunner.DefaultRunner);

            // And back again
            this.RunFastFetch("--checkout -c b5fd7d23706a18cff3e2b8225588d479f7e51138");
            Path.Combine(this.fastFetchRepoRoot, "GVFlt_MoveFileTest\\NoneToNone\\from\\subFolder\\from.txt")
                .ShouldBeAFile(FileSystemRunner.DefaultRunner);
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

        private string RunFastFetch(string args)
        {
            args = args + " --verbose";

            string fastfetch = Path.Combine(TestContext.CurrentContext.TestDirectory, "fastfetch.exe");
            if (!File.Exists(fastfetch))
            {
                fastfetch = "fastfetch.exe";
            }

            Console.WriteLine($"Using {fastfetch}");

            ProcessStartInfo processInfo = new ProcessStartInfo(fastfetch);
            processInfo.Arguments = args;
            processInfo.WorkingDirectory = this.fastFetchRepoRoot;
            processInfo.UseShellExecute = false;
            processInfo.RedirectStandardOutput = true;
            processInfo.RedirectStandardError = true;
            
            ProcessResult result = ProcessHelper.Run(processInfo);
            result.Output.Contains("Error").ShouldEqual(false, result.Output);
            result.Errors.ShouldBeEmpty(result.Errors);
            result.ExitCode.ShouldEqual(0);
            return result.Output;
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
