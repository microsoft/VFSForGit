using GVFS.FunctionalTests.FileSystemRunners;
using GVFS.FunctionalTests.Should;
using GVFS.FunctionalTests.Tools;
using GVFS.Tests.Should;
using NUnit.Framework;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;

namespace GVFS.FunctionalTests.Tests.EnlistmentPerFixture
{
    [TestFixtureSource(typeof(GitFilesTestsRunners), GitFilesTestsRunners.TestRunners)]
    public class GitFilesTests : TestsWithEnlistmentPerFixture
    {
        private const string ExcludeFileContentsBeforeChange =
@"# git ls-files --others --exclude-from=.git/info/exclude
# Lines that start with '#' are comments.
# For a project mostly in C, the following would be a good set of
# exclude patterns (uncomment them if you want to use them):
# *.[oa]
# *~
*
";
        private const string ExcludeFileContentsAfterChange =
@"# git ls-files --others --exclude-from=.git/info/exclude
# Lines that start with '#' are comments.
# For a project mostly in C, the following would be a good set of
# exclude patterns (uncomment them if you want to use them):
# *.[oa]
# *~
*
!/*
";

        private FileSystemRunner fileSystem;

        public GitFilesTests(FileSystemRunner fileSystem)
        {
            this.fileSystem = fileSystem;
        }

        [TestCase, Order(1)]
        public void CreateFileTest()
        {
            string virtualFile = Path.Combine(this.Enlistment.RepoRoot, "tempFile.txt");
            string excludeFile = Path.Combine(this.Enlistment.RepoRoot, GitHelpers.ExcludeFilePath);
            excludeFile.ShouldBeAFile(this.fileSystem).WithContents(ExcludeFileContentsBeforeChange.Replace("\r\n", "\n"));
            this.fileSystem.WriteAllText(virtualFile, "Some content here");

            this.Enlistment.WaitForBackgroundOperations().ShouldEqual(true, "Background operations failed to complete.");

            virtualFile.ShouldBeAFile(this.fileSystem).WithContents("Some content here");
            excludeFile.ShouldBeAFile(this.fileSystem).WithContents(ExcludeFileContentsAfterChange.Replace("\r\n", "\n"));
        }

        [TestCase, Order(2)]
        public void ReadingFileUpdatesTimestampsAndSizeInIndex()
        {
            string gitFileToCheck = "GVFS/GVFS.FunctionalTests/Category/CategoryConstants.cs";
            string virtualFile = Path.Combine(this.Enlistment.RepoRoot, gitFileToCheck.Replace('/', '\\'));
            ProcessResult initialResult = GitProcess.InvokeProcess(this.Enlistment.RepoRoot, "ls-files --debug -svmodc " + gitFileToCheck);
            initialResult.ShouldNotBeNull();
            initialResult.Output.ShouldNotBeNull();
            initialResult.Output.StartsWith("S ").ShouldEqual(true);
            initialResult.Output.ShouldContain("ctime: 0:0", "mtime: 0:0", "size: 0\t");

            using (FileStream fileStreamToRead = File.OpenRead(virtualFile))
            {
                fileStreamToRead.ReadByte();
            }

            this.Enlistment.WaitForBackgroundOperations().ShouldEqual(true, "Background operations did not complete.");

            ProcessResult afterUpdateResult = GitProcess.InvokeProcess(this.Enlistment.RepoRoot, "ls-files --debug -svmodc " + gitFileToCheck);
            afterUpdateResult.ShouldNotBeNull();
            afterUpdateResult.Output.ShouldNotBeNull();
            afterUpdateResult.Output.StartsWith("H ").ShouldEqual(true);
            afterUpdateResult.Output.ShouldNotContain("ctime: 0:0", "mtime: 0:0", "size: 0\t");
            afterUpdateResult.Output.ShouldContain("size: 161\t");
        }

        [TestCase, Order(3)]
        public void CreatedFileWillGetSkipworktreeBitCleared()
        {
            string fileToTest = "GVFS\\GVFS.Common\\RetryWrapper.cs";
            string fileToCreate = Path.Combine(this.Enlistment.RepoRoot, fileToTest);
            string gitFileToTest = fileToTest.Replace('\\', '/');
            this.VerifyWorktreeBit(gitFileToTest, LsFilesStatus.SkipWorktree);

            ManualResetEventSlim resetEvent = GitHelpers.AcquireGVFSLock(this.Enlistment);

            this.fileSystem.WriteAllText(fileToCreate, "Anything can go here");
            this.fileSystem.FileExists(fileToCreate).ShouldEqual(true);
            resetEvent.Set();

            this.Enlistment.WaitForBackgroundOperations().ShouldEqual(true, "Background operations did not complete.");

            string sparseCheckoutFile = Path.Combine(this.Enlistment.RepoRoot, TestConstants.DotGit.Info.SparseCheckout);
            sparseCheckoutFile.ShouldBeAFile(this.fileSystem).WithContents().ShouldContain(gitFileToTest);
            this.VerifyWorktreeBit(gitFileToTest, LsFilesStatus.Cached);
        }

        private void VerifyWorktreeBit(string path, char expectedStatus)
        {
            ProcessResult lsfilesResult = GitProcess.InvokeProcess(this.Enlistment.RepoRoot, "ls-files -svomdc " + path);
            lsfilesResult.ShouldNotBeNull();
            lsfilesResult.Output.ShouldNotBeNull();
            lsfilesResult.Output.Length.ShouldBeAtLeast(2);
            lsfilesResult.Output[0].ShouldEqual(expectedStatus);
        }

        private static class LsFilesStatus
        {
            public const char Cached = 'H';
            public const char SkipWorktree = 'S';
        }

        private class GitFilesTestsRunners
        {
            public const string TestRunners = "Runners";

            public static object[] Runners
            {
                get
                {
                    // Don't use the BashRunner for GitFilesTests as the BashRunner always strips off the last trailing newline (\n)
                    // and we expect there to be a trailing new line
                    List<object[]> runners = new List<object[]>();
                    foreach (object[] runner in FileSystemRunner.Runners.ToList())
                    {
                        if (!(runner.ToList().First() is BashRunner))
                        {
                            runners.Add(new object[] { runner.ToList().First() });
                        }
                    }

                    return runners.ToArray();
                }
            }
        }
    }
}
