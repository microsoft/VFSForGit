using GVFS.FunctionalTests.FileSystemRunners;
using GVFS.FunctionalTests.Should;
using NUnit.Framework;
using System.IO;

namespace GVFS.FunctionalTests.Tests.EnlistmentPerFixture
{
    [TestFixtureSource(typeof(FileSystemRunner), FileSystemRunner.TestRunners)]
    public class MoveRenameFolderTests : TestsWithEnlistmentPerFixture
    {       
        public const string TestFileContents =
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

        [TestCase]
        public void RenameFolderShouldFail()
        {
            string testFileName = "RenameFolderShouldFail.cpp";
            string oldFolderName = "Test_EPF_MoveRenameFolderTests\\RenameFolderShouldFail\\source";
            string newFolderName = "Test_EPF_MoveRenameFolderTests\\RenameFolderShouldFail\\sourcerenamed";
            this.Enlistment.GetVirtualPathTo(newFolderName).ShouldNotExistOnDisk(this.fileSystem);

            this.fileSystem.MoveDirectory_RequestShouldNotBeSupported(this.Enlistment.GetVirtualPathTo(oldFolderName), this.Enlistment.GetVirtualPathTo(newFolderName));

            this.Enlistment.GetVirtualPathTo(oldFolderName).ShouldBeADirectory(this.fileSystem);
            this.Enlistment.GetVirtualPathTo(newFolderName).ShouldNotExistOnDisk(this.fileSystem);

            this.Enlistment.GetVirtualPathTo(Path.Combine(newFolderName, testFileName)).ShouldNotExistOnDisk(this.fileSystem);
            this.Enlistment.GetVirtualPathTo(Path.Combine(oldFolderName, testFileName)).ShouldBeAFile(this.fileSystem).WithContents(TestFileContents);
        }

        [TestCase]
        [Ignore("Disabled until moving partial folders is supported")]
        public void ChangeUnhydratedFolderName()
        {
            string testFileName = "ChangeUnhydratedFolderName.cpp";
            string oldFolderName = "Test_EPF_MoveRenameFolderTests\\ChangeUnhydratedFolderName\\source";
            string newFolderName = "Test_EPF_MoveRenameFolderTests\\ChangeUnhydratedFolderName\\source_renamed";
            this.Enlistment.GetVirtualPathTo(newFolderName).ShouldNotExistOnDisk(this.fileSystem);

            this.fileSystem.MoveDirectory(this.Enlistment.GetVirtualPathTo(oldFolderName), this.Enlistment.GetVirtualPathTo(newFolderName));

            this.Enlistment.GetVirtualPathTo(newFolderName).ShouldBeADirectory(this.fileSystem);
            this.Enlistment.GetVirtualPathTo(oldFolderName).ShouldNotExistOnDisk(this.fileSystem);

            this.Enlistment.GetVirtualPathTo(Path.Combine(oldFolderName, testFileName)).ShouldNotExistOnDisk(this.fileSystem);
            this.Enlistment.GetVirtualPathTo(Path.Combine(newFolderName, testFileName)).ShouldBeAFile(this.fileSystem).WithContents(TestFileContents);
        }

        [TestCase]
        [Ignore("Disabled until moving partial folders is supported")]
        public void MoveUnhydratedFolderToNewFolder()
        {
            string testFileName = "MoveUnhydratedFolderToVirtualNTFSFolder.cpp";
            string oldFolderName = "Test_EPF_MoveRenameFolderTests\\MoveUnhydratedFolderToVirtualNTFSFolder";

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

        [TestCase]
        [Ignore("Disabled until moving partial folders is supported")]
        public void MoveUnhydratedFolderToFullFolderInDotGitFolder()
        {
            string testFileName = "MoveUnhydratedFolderToFullFolderInDotGitFolder.cpp";
            string oldFolderName = "Test_EPF_MoveRenameFolderTests\\MoveUnhydratedFolderToFullFolderInDotGitFolder";

            string newFolderName = ".git\\NewPerFixtureParent";
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
            string newFolderPath = this.Enlistment.GetVirtualPathTo(".git\\" + newFolderName);
            newFolderPath.ShouldNotExistOnDisk(this.fileSystem);
            this.fileSystem.CreateDirectory(newFolderPath);
            newFolderPath.ShouldBeADirectory(this.fileSystem);

            string movedFolderPath = Path.Combine(newFolderPath, "Should");
            this.fileSystem.MoveDirectory(oldFolderPath, movedFolderPath);

            Path.Combine(movedFolderPath).ShouldBeADirectory(this.fileSystem);
            oldFolderPath.ShouldNotExistOnDisk(this.fileSystem);
            Path.Combine(movedFolderPath, testFileName).ShouldBeAFile(this.fileSystem).WithContents(fileContents);
        }

        [TestCase]
        [Ignore("Disabled until moving partial folders is supported")]
        public void MoveAndRenameUnhydratedFolderToNewFolder()
        {
            string testFileName = "MoveAndRenameUnhydratedFolderToNewFolder.cpp";
            string oldFolderName = "Test_EPF_MoveRenameFolderTests\\MoveAndRenameUnhydratedFolderToNewFolder";

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

        [TestCase]
        [Ignore("Disabled until moving partial folders is supported")]
        public void MoveFolderWithUnhydratedAndFullContents()
        {
            string testFileName = "MoveFolderWithUnhydratedAndFullContents.cs";
            string oldFolderName = "Test_EPF_MoveRenameFolderTests\\MoveFolderWithUnhydratedAndFullContents";

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
    }
}
