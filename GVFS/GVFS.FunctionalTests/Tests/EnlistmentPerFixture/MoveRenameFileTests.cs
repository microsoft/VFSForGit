using GVFS.FunctionalTests.FileSystemRunners;
using GVFS.FunctionalTests.Should;
using GVFS.FunctionalTests.Tools;
using GVFS.Tests.Should;
using NUnit.Framework;
using System.IO;

namespace GVFS.FunctionalTests.Tests.EnlistmentPerFixture
{
    // TODO 452590 - Combine all of the MoveRenameTests into a single fixture, and have each use different
    // well known files
    [TestFixtureSource(typeof(FileSystemRunner), nameof(FileSystemRunner.Runners))]
    public class MoveRenameFileTests : TestsWithEnlistmentPerFixture
    {
        public const string TestFileContents =
@"using NUnitLite;
using System;
using System.Threading;

namespace GVFS.StressTests
{
    public class Program
    {
        public static void Main(string[] args)
        {
            string[] test_args = args;

            for (int i = 0; i < Properties.Settings.Default.TestRepeatCount; i++)
            {
                Console.WriteLine(""Starting pass {0}"", i + 1);
                DateTime now = DateTime.Now;
                new AutoRun().Execute(test_args);
                Console.WriteLine(""Completed pass {0} in {1}"", i + 1, DateTime.Now - now);
                Console.WriteLine();

                Thread.Sleep(TimeSpan.FromSeconds(1));
            }

            Console.WriteLine(""All tests completed.  Press Enter to exit."");
            Console.ReadLine();
        }
    }
}";

        private FileSystemRunner fileSystem;

        public MoveRenameFileTests(FileSystemRunner fileSystem)
        {
            this.fileSystem = fileSystem;
        }

        [TestCase]
        public void ChangeUnhydratedFileName()
        {
            string oldFilename = Path.Combine("Test_EPF_MoveRenameFileTests", "ChangeUnhydratedFileName", "Program.cs");
            string newFilename = Path.Combine("Test_EPF_MoveRenameFileTests", "ChangeUnhydratedFileName", "renamed_Program.cs");

            // Don't read oldFilename or check for its existence before calling MoveFile, because doing so
            // can cause the file to hydrate
            this.Enlistment.GetVirtualPathTo(newFilename).ShouldNotExistOnDisk(this.fileSystem);

            this.fileSystem.MoveFile(this.Enlistment.GetVirtualPathTo(oldFilename), this.Enlistment.GetVirtualPathTo(newFilename));
            this.Enlistment.GetVirtualPathTo(newFilename).ShouldBeAFile(this.fileSystem).WithContents(TestFileContents);
            this.Enlistment.GetVirtualPathTo(oldFilename).ShouldNotExistOnDisk(this.fileSystem);

            this.fileSystem.MoveFile(this.Enlistment.GetVirtualPathTo(newFilename), this.Enlistment.GetVirtualPathTo(oldFilename));
            this.Enlistment.GetVirtualPathTo(oldFilename).ShouldBeAFile(this.fileSystem).WithContents(TestFileContents);
            this.Enlistment.GetVirtualPathTo(newFilename).ShouldNotExistOnDisk(this.fileSystem);
        }

        [TestCase]
        public void ChangeUnhydratedFileNameCase()
        {
            string oldName = "Readme.md";
            string newName = "readme.md";

            string oldVirtualPath = this.Enlistment.GetVirtualPathTo(oldName);
            string newVirtualPath = this.Enlistment.GetVirtualPathTo(newName);

            this.ChangeUnhydratedFileCase(oldName, oldVirtualPath, newName, newVirtualPath, knownFileContents: null);
        }

        [TestCase]
        public void ChangeNestedUnhydratedFileNameCase()
        {
            string oldName = "Program.cs";
            string newName = "program.cs";
            string folderName = Path.Combine("Test_EPF_MoveRenameFileTests", "ChangeNestedUnhydratedFileNameCase");

            string oldVirtualPath = this.Enlistment.GetVirtualPathTo(Path.Combine(folderName, oldName));
            string newVirtualPath = this.Enlistment.GetVirtualPathTo(Path.Combine(folderName, newName));

            this.ChangeUnhydratedFileCase(oldName, oldVirtualPath, newName, newVirtualPath, TestFileContents);
        }

        [TestCase]
        public void MoveUnhydratedFileToDotGitFolder()
        {
            string targetFolderName = ".git";
            this.Enlistment.GetVirtualPathTo(targetFolderName).ShouldBeADirectory(this.fileSystem);

            string testFileName = "Program.cs";
            string testFileFolder = Path.Combine("Test_EPF_MoveRenameFileTests", "MoveUnhydratedFileToDotGitFolder");
            string testFilePathSubPath = Path.Combine(testFileFolder, testFileName);

            string newTestFileVirtualPath = Path.Combine(this.Enlistment.GetVirtualPathTo(targetFolderName), testFileName);

            this.fileSystem.MoveFile(this.Enlistment.GetVirtualPathTo(testFilePathSubPath), newTestFileVirtualPath);
            this.Enlistment.GetVirtualPathTo(testFilePathSubPath).ShouldNotExistOnDisk(this.fileSystem);
            newTestFileVirtualPath.ShouldBeAFile(this.fileSystem).WithContents(TestFileContents);

            this.fileSystem.DeleteFile(newTestFileVirtualPath);
            newTestFileVirtualPath.ShouldNotExistOnDisk(this.fileSystem);
        }

        [TestCase]
        public void MoveVirtualNTFSFileToOverwriteUnhydratedFile()
        {
            string targetFilename = ".gitattributes";

            string sourceFilename = "SourceFile.txt";
            string sourceFileContents = "The Source";

            this.fileSystem.WriteAllText(this.Enlistment.GetVirtualPathTo(sourceFilename), sourceFileContents);
            this.Enlistment.GetVirtualPathTo(sourceFilename).ShouldBeAFile(this.fileSystem).WithContents(sourceFileContents);

            this.fileSystem.ReplaceFile(this.Enlistment.GetVirtualPathTo(sourceFilename), this.Enlistment.GetVirtualPathTo(targetFilename));
            this.Enlistment.GetVirtualPathTo(targetFilename).ShouldBeAFile(this.fileSystem).WithContents(sourceFileContents);

            this.Enlistment.GetVirtualPathTo(sourceFilename).ShouldNotExistOnDisk(this.fileSystem);
        }

        private void ChangeUnhydratedFileCase(
            string oldName,
            string oldVirtualPath,
            string newName,
            string newVirtualPath,
            string knownFileContents)
        {
            this.fileSystem.MoveFile(oldVirtualPath, newVirtualPath);
            string fileContents = newVirtualPath.ShouldBeAFile(this.fileSystem).WithCaseMatchingName(newName).WithContents();
            fileContents.ShouldBeNonEmpty();
            if (knownFileContents != null)
            {
                fileContents.ShouldEqual(knownFileContents);
            }

            this.fileSystem.MoveFile(newVirtualPath, oldVirtualPath);
            oldVirtualPath.ShouldBeAFile(this.fileSystem).WithCaseMatchingName(oldName).WithContents(fileContents);
        }
    }
}
