using GVFS.FunctionalTests.FileSystemRunners;
using GVFS.FunctionalTests.Should;
using GVFS.Tests.Should;
using NUnit.Framework;
using System;
using System.Linq;

namespace GVFS.FunctionalTests.Tests.EnlistmentPerTestCase
{
    [TestFixture]
    public class PersistedSparseExcludeTests : TestsWithEnlistmentPerTestCase
    {
        private const string FileToAdd = @"GVFS\TestAddFile.txt";
        private const string FileToUpdate = @"GVFS\GVFS\Program.cs";
        private const string FileToDelete = "Readme.md";
        private const string FolderToCreate = "PersistedSparseExcludeTests_NewFolder";
        private const string ExcludeFilePath = @".git\info\exclude";
        private const string SparseCheckoutFilePath = @".git\info\sparse-checkout";
        private static string[] expectedExcludeFileContents = new string[] 
        {
            "!/*",
            "!/GVFS",
            "!/GVFS/*",
            "*",
            "!/PersistedSparseExcludeTests_NewFolder",
            "!/PersistedSparseExcludeTests_NewFolder/*"
        };
        private static string[] expectedSparseFileContents = new string[] 
        {
            "/.gitattributes",
            "/GVFS/GVFS/Program.cs",
            "/GVFS/TestAddFile.txt",
            "/Readme.md",
            "/PersistedSparseExcludeTests_NewFolder/"
        };

        [TestCaseSource(typeof(FileSystemRunner), FileSystemRunner.TestRunners)]
        public void ExcludeSparseFileSavedAfterRemount(FileSystemRunner fileSystem)
        {
            string fileToAdd = this.Enlistment.GetVirtualPathTo(FileToAdd);
            fileSystem.WriteAllText(fileToAdd, "Contents for the new file");

            string fileToUpdate = this.Enlistment.GetVirtualPathTo(FileToUpdate);
            fileSystem.AppendAllText(fileToUpdate, "// Testing");

            string fileToDelete = this.Enlistment.GetVirtualPathTo(FileToDelete);
            fileSystem.DeleteFile(fileToDelete);

            string folderToCreate = this.Enlistment.GetVirtualPathTo(FolderToCreate);
            fileSystem.CreateDirectory(folderToCreate);

            // Remount
            this.Enlistment.UnmountGVFS();
            this.Enlistment.MountGVFS();

            string excludeFile = this.Enlistment.GetVirtualPathTo(ExcludeFilePath);
            string excludeFileContents = excludeFile.ShouldBeAFile(fileSystem).WithContents();

            excludeFileContents.Split(new char[] { '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Where(x => !x.StartsWith("#")) // Exclude comments
                .OrderBy(x => x)
                .ShouldMatchInOrder(expectedExcludeFileContents.OrderBy(x => x));

            string sparseFile = this.Enlistment.GetVirtualPathTo(SparseCheckoutFilePath);
            sparseFile.ShouldBeAFile(fileSystem).WithContents()
                .Split(new char[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Where(x => expectedSparseFileContents.Contains(x)) // Exclude extra entries for files hydrated during test
                .OrderBy(x => x)
                .ShouldMatchInOrder(expectedSparseFileContents.OrderBy(x => x));
        }      
    }
}
