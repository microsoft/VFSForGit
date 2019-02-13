using GVFS.FunctionalTests.FileSystemRunners;
using GVFS.FunctionalTests.Should;
using GVFS.Tests.Should;
using NUnit.Framework;
using System.IO;

namespace GVFS.FunctionalTests.Tests.EnlistmentPerFixture
{
    [TestFixtureSource(typeof(FileSystemRunner), nameof(FileSystemRunner.Runners))]
    public class MoveRenameFolderTests : TestsWithEnlistmentPerFixture
    {
        private const string TestFileContents =
@"// dllmain.cpp : Defines the entry point for the DLL application.
#include ""stdafx.h""

BOOL APIENTRY DllMain( HMODULE hModule,
                       DWORD  ul_reason_for_call,
                       LPVOID lpReserved
                     )
{
    UNREFERENCED_PARAMETER(hModule);
    UNREFERENCED_PARAMETER(lpReserved);

    switch (ul_reason_for_call)
    {
    case DLL_PROCESS_ATTACH:
    case DLL_THREAD_ATTACH:
    case DLL_THREAD_DETACH:
    case DLL_PROCESS_DETACH:
        break;
    }
    return TRUE;
}

";
        private FileSystemRunner fileSystem;

        public MoveRenameFolderTests(FileSystemRunner fileSystem)
        {
            this.fileSystem = fileSystem;
        }

        // WindowsOnly because renames of partial folders are blocked only on Windows
        [TestCase]
        [Category(Categories.WindowsOnly)]
        public void RenameFolderShouldFail()
        {
            string testFileName = "RenameFolderShouldFail.cpp";
            string oldFolderName = Path.Combine("Test_EPF_MoveRenameFolderTests", "RenameFolderShouldFail", "source");
            string newFolderName = Path.Combine("Test_EPF_MoveRenameFolderTests", "RenameFolderShouldFail", "sourcerenamed");
            this.Enlistment.GetVirtualPathTo(newFolderName).ShouldNotExistOnDisk(this.fileSystem);

            this.fileSystem.MoveDirectory_RequestShouldNotBeSupported(this.Enlistment.GetVirtualPathTo(oldFolderName), this.Enlistment.GetVirtualPathTo(newFolderName));

            this.Enlistment.GetVirtualPathTo(oldFolderName).ShouldBeADirectory(this.fileSystem);
            this.Enlistment.GetVirtualPathTo(newFolderName).ShouldNotExistOnDisk(this.fileSystem);

            this.Enlistment.GetVirtualPathTo(Path.Combine(newFolderName, testFileName)).ShouldNotExistOnDisk(this.fileSystem);
            this.Enlistment.GetVirtualPathTo(Path.Combine(oldFolderName, testFileName)).ShouldBeAFile(this.fileSystem).WithContents(TestFileContents);
        }

        // MacOnly because renames of partial folders are blocked on Windows
        [TestCase]
        [Category(Categories.MacOnly)]
        public void ChangeUnhydratedFolderName()
        {
            string testFileName = "ChangeUnhydratedFolderName.cpp";
            string oldFolderName = Path.Combine("Test_EPF_MoveRenameFolderTests", "ChangeUnhydratedFolderName", "source");
            string newFolderName = Path.Combine("Test_EPF_MoveRenameFolderTests", "ChangeUnhydratedFolderName", "source_renamed");
            this.Enlistment.GetVirtualPathTo(newFolderName).ShouldNotExistOnDisk(this.fileSystem);

            this.fileSystem.MoveDirectory(this.Enlistment.GetVirtualPathTo(oldFolderName), this.Enlistment.GetVirtualPathTo(newFolderName));

            this.Enlistment.GetVirtualPathTo(newFolderName).ShouldBeADirectory(this.fileSystem);
            this.Enlistment.GetVirtualPathTo(oldFolderName).ShouldNotExistOnDisk(this.fileSystem);

            this.Enlistment.GetVirtualPathTo(Path.Combine(oldFolderName, testFileName)).ShouldNotExistOnDisk(this.fileSystem);
            this.Enlistment.GetVirtualPathTo(Path.Combine(newFolderName, testFileName)).ShouldBeAFile(this.fileSystem).WithContents(TestFileContents);
        }

        // MacOnly because renames of partial folders are blocked on Windows
        [TestCase]
        [Category(Categories.MacOnly)]
        public void MoveUnhydratedFolderToNewFolder()
        {
            string testFileName = "MoveUnhydratedFolderToVirtualNTFSFolder.cpp";
            string oldFolderName = Path.Combine("Test_EPF_MoveRenameFolderTests", "MoveUnhydratedFolderToVirtualNTFSFolder");

            string newFolderName = "NewPerFixtureParent";
            this.Enlistment.GetVirtualPathTo(newFolderName).ShouldNotExistOnDisk(this.fileSystem);
            this.fileSystem.CreateDirectory(this.Enlistment.GetVirtualPathTo(newFolderName));
            this.Enlistment.GetVirtualPathTo(newFolderName).ShouldBeADirectory(this.fileSystem);

            string movedFolderPath = Path.Combine(newFolderName, "EnlistmentPerFixture");
            this.fileSystem.MoveDirectory(this.Enlistment.GetVirtualPathTo(oldFolderName), this.Enlistment.GetVirtualPathTo(movedFolderPath));

            this.Enlistment.GetVirtualPathTo(movedFolderPath).ShouldBeADirectory(this.fileSystem);
            this.Enlistment.GetVirtualPathTo(oldFolderName).ShouldNotExistOnDisk(this.fileSystem);

            this.Enlistment.GetVirtualPathTo(Path.Combine(oldFolderName, testFileName)).ShouldNotExistOnDisk(this.fileSystem);
            this.Enlistment.GetVirtualPathTo(Path.Combine(movedFolderPath, testFileName)).ShouldBeAFile(this.fileSystem).WithContents(TestFileContents);
        }

        // MacOnly because renames of partial folders are blocked on Windows
        [TestCase]
        [Category(Categories.MacOnly)]
        public void MoveUnhydratedFolderToFullFolderInDotGitFolder()
        {
            string testFileName = "MoveUnhydratedFolderToFullFolderInDotGitFolder.cpp";
            string oldFolderName = Path.Combine("Test_EPF_MoveRenameFolderTests", "MoveUnhydratedFolderToFullFolderInDotGitFolder");

            string newFolderName = Path.Combine(".git", "NewPerFixtureParent");
            this.Enlistment.GetVirtualPathTo(newFolderName).ShouldNotExistOnDisk(this.fileSystem);
            this.fileSystem.CreateDirectory(this.Enlistment.GetVirtualPathTo(newFolderName));
            this.Enlistment.GetVirtualPathTo(newFolderName).ShouldBeADirectory(this.fileSystem);

            string movedFolderPath = Path.Combine(newFolderName, "Should");
            this.fileSystem.MoveDirectory(this.Enlistment.GetVirtualPathTo(oldFolderName), this.Enlistment.GetVirtualPathTo(movedFolderPath));

            this.Enlistment.GetVirtualPathTo(movedFolderPath).ShouldBeADirectory(this.fileSystem);
            this.Enlistment.GetVirtualPathTo(oldFolderName).ShouldNotExistOnDisk(this.fileSystem);
            this.Enlistment.GetVirtualPathTo(Path.Combine(oldFolderName, testFileName)).ShouldNotExistOnDisk(this.fileSystem);
            this.Enlistment.GetVirtualPathTo(Path.Combine(movedFolderPath, testFileName)).ShouldBeAFile(this.fileSystem).WithContents(TestFileContents);
        }

        [TestCase]
        public void MoveFullFolderToFullFolderInDotGitFolder()
        {
            string fileContents = "Test contents for MoveFullFolderToFullFolderInDotGitFolder";
            string testFileName = "MoveFullFolderToFullFolderInDotGitFolder.txt";
            string oldFolderPath = this.Enlistment.GetVirtualPathTo("MoveFullFolderToFullFolderInDotGitFolder");
            oldFolderPath.ShouldNotExistOnDisk(this.fileSystem);
            this.fileSystem.CreateDirectory(oldFolderPath);
            oldFolderPath.ShouldBeADirectory(this.fileSystem);

            string oldFilePath = Path.Combine(oldFolderPath, testFileName);
            this.fileSystem.WriteAllText(oldFilePath, fileContents);
            oldFilePath.ShouldBeAFile(this.fileSystem).WithContents(fileContents);

            string newFolderName = "NewMoveFullFolderToFullFolderInDotGitFolder";
            string newFolderPath = this.Enlistment.GetVirtualPathTo(".git", newFolderName);
            newFolderPath.ShouldNotExistOnDisk(this.fileSystem);
            this.fileSystem.CreateDirectory(newFolderPath);
            newFolderPath.ShouldBeADirectory(this.fileSystem);

            string movedFolderPath = Path.Combine(newFolderPath, "Should");
            this.fileSystem.MoveDirectory(oldFolderPath, movedFolderPath);

            Path.Combine(movedFolderPath).ShouldBeADirectory(this.fileSystem);
            oldFolderPath.ShouldNotExistOnDisk(this.fileSystem);
            Path.Combine(movedFolderPath, testFileName).ShouldBeAFile(this.fileSystem).WithContents(fileContents);
        }

        // MacOnly because renames of partial folders are blocked on Windows
        [TestCase]
        [Category(Categories.MacOnly)]
        public void MoveAndRenameUnhydratedFolderToNewFolder()
        {
            string testFileName = "MoveAndRenameUnhydratedFolderToNewFolder.cpp";
            string oldFolderName = Path.Combine("Test_EPF_MoveRenameFolderTests", "MoveAndRenameUnhydratedFolderToNewFolder");

            string newFolderName = "NewPerTestCaseParent";
            this.Enlistment.GetVirtualPathTo(newFolderName).ShouldNotExistOnDisk(this.fileSystem);
            this.fileSystem.CreateDirectory(this.Enlistment.GetVirtualPathTo(newFolderName));
            this.Enlistment.GetVirtualPathTo(newFolderName).ShouldBeADirectory(this.fileSystem);

            string movedFolderPath = Path.Combine(newFolderName, "MoveAndRenameUnhydratedFolderToNewFolder_renamed");
            this.fileSystem.MoveDirectory(this.Enlistment.GetVirtualPathTo(oldFolderName), this.Enlistment.GetVirtualPathTo(movedFolderPath));

            this.Enlistment.GetVirtualPathTo(movedFolderPath).ShouldBeADirectory(this.fileSystem);
            this.Enlistment.GetVirtualPathTo(oldFolderName).ShouldNotExistOnDisk(this.fileSystem);

            this.Enlistment.GetVirtualPathTo(Path.Combine(oldFolderName, testFileName)).ShouldNotExistOnDisk(this.fileSystem);
            this.Enlistment.GetVirtualPathTo(Path.Combine(movedFolderPath, testFileName)).ShouldBeAFile(this.fileSystem).WithContents(TestFileContents);
        }

        // MacOnly because renames of partial folders are blocked on Windows
        [TestCase]
        [Category(Categories.MacOnly)]
        public void MoveFolderWithUnhydratedAndFullContents()
        {
            string testFileName = "MoveFolderWithUnhydratedAndFullContents.cpp";
            string oldFolderName = Path.Combine("Test_EPF_MoveRenameFolderTests", "MoveFolderWithUnhydratedAndFullContents");

            string newFile = "TestFile.txt";
            string newFileContents = "Contents of TestFile.txt";
            this.fileSystem.WriteAllText(this.Enlistment.GetVirtualPathTo(Path.Combine(oldFolderName, newFile)), newFileContents);

            string newFolderName = "New_MoveFolderWithUnhydratedAndFullContents";
            this.Enlistment.GetVirtualPathTo(newFolderName).ShouldNotExistOnDisk(this.fileSystem);
            this.fileSystem.CreateDirectory(this.Enlistment.GetVirtualPathTo(newFolderName));
            this.Enlistment.GetVirtualPathTo(newFolderName).ShouldBeADirectory(this.fileSystem);

            string movedFolderPath = Path.Combine(newFolderName, "MoveFolderWithUnhydratedAndFullContents_renamed");
            this.fileSystem.MoveDirectory(this.Enlistment.GetVirtualPathTo(oldFolderName), this.Enlistment.GetVirtualPathTo(movedFolderPath));

            this.Enlistment.GetVirtualPathTo(movedFolderPath).ShouldBeADirectory(this.fileSystem);
            this.Enlistment.GetVirtualPathTo(oldFolderName).ShouldNotExistOnDisk(this.fileSystem);

            // Test file should have been moved
            this.Enlistment.GetVirtualPathTo(Path.Combine(oldFolderName, testFileName)).ShouldNotExistOnDisk(this.fileSystem);
            this.Enlistment.GetVirtualPathTo(Path.Combine(movedFolderPath, testFileName)).ShouldBeAFile(this.fileSystem).WithContents(TestFileContents);

            // New file should have been moved
            this.Enlistment.GetVirtualPathTo(Path.Combine(oldFolderName, newFile)).ShouldNotExistOnDisk(this.fileSystem);
            this.Enlistment.GetVirtualPathTo(Path.Combine(movedFolderPath, newFile)).ShouldBeAFile(this.fileSystem).WithContents(newFileContents);
        }

        // MacOnly because renames of partial folders are blocked on Windows
        [TestCase]
        [Category(Categories.MacOnly)]
        public void MoveFolderWithUnexpandedChildFolders()
        {
            string oldFolderPath = this.Enlistment.GetVirtualPathTo("Test_EPF_MoveRenameFileTests");
            string newFolderName = "Test_EPF_MoveRenameFileTests_Renamed";
            string newFolderPath = this.Enlistment.GetVirtualPathTo(newFolderName);
            this.fileSystem.MoveDirectory(oldFolderPath, newFolderPath);
            oldFolderPath.ShouldNotExistOnDisk(this.fileSystem);

            newFolderPath.ShouldBeADirectory(this.fileSystem);
            this.Enlistment.GetVirtualPathTo(newFolderName, "ChangeNestedUnhydratedFileNameCase", "Program.cs")
                .ShouldBeAFile(this.fileSystem)
                .WithContents(MoveRenameFileTests.TestFileContents);

            this.Enlistment.GetVirtualPathTo(newFolderName, "ChangeUnhydratedFileName", "Program.cs")
                .ShouldBeAFile(this.fileSystem)
                .WithContents(MoveRenameFileTests.TestFileContents);

            this.Enlistment.GetVirtualPathTo(newFolderName, "MoveUnhydratedFileToDotGitFolder", "Program.cs")
                .ShouldBeAFile(this.fileSystem)
                .WithContents(MoveRenameFileTests.TestFileContents);

            // Test moving a folder with a very deep unhydrated child tree
            oldFolderPath = this.Enlistment.GetVirtualPathTo("Test_EPF_WorkingDirectoryTests");

            // But expand the folder we will be renaming (so that only the children have not been expanded)
            oldFolderPath.ShouldBeADirectory(this.fileSystem).WithDirectories().ShouldContain(dir => dir.Name.Equals("1"));

            newFolderName = "Test_EPF_WorkingDirectoryTests_Renamed";
            newFolderPath = this.Enlistment.GetVirtualPathTo(newFolderName);
            this.fileSystem.MoveDirectory(oldFolderPath, newFolderPath);
            oldFolderPath.ShouldNotExistOnDisk(this.fileSystem);
            this.Enlistment.GetVirtualPathTo(newFolderName, "1", "2", "3", "4", "ReadDeepProjectedFile.cpp")
                .ShouldBeAFile(this.fileSystem)
                .WithContents(WorkingDirectoryTests.TestFileContents);
        }
    }
}
