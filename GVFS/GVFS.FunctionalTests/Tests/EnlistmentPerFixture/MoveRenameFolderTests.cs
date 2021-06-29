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

        [TestCase]
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
    }
}
