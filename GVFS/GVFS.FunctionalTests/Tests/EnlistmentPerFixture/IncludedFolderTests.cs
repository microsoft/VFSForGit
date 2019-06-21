using GVFS.FunctionalTests.FileSystemRunners;
using GVFS.FunctionalTests.Should;
using GVFS.FunctionalTests.Tools;
using GVFS.Tests.Should;
using NUnit.Framework;
using System.IO;

namespace GVFS.FunctionalTests.Tests.EnlistmentPerFixture
{
    [TestFixture]
    public class IncludedFolderTests : TestsWithEnlistmentPerFixture
    {
        private FileSystemRunner fileSystem = new SystemIORunner();

        [TestCase]
        public void BasicTestsAddingAndRemoving()
        {
            // directories before limiting them
            string[] allRootDirectories = Directory.GetDirectories(this.Enlistment.RepoRoot);
            string[] directoriesForParentAdd = Directory.GetDirectories(Path.Combine(this.Enlistment.RepoRoot, "GVFS", "GVFS"));

            GVFSProcess gvfsProcess = new GVFSProcess(this.Enlistment);
            gvfsProcess.AddIncludedFolders(Path.Combine("GVFS", "GVFS"));

            string[] directories = Directory.GetDirectories(this.Enlistment.RepoRoot);
            directories.Length.ShouldEqual(2);
            directories[0].ShouldEqual(Path.Combine(this.Enlistment.RepoRoot, ".git"));
            directories[1].ShouldEqual(Path.Combine(this.Enlistment.RepoRoot, "GVFS"));

            string folder = this.Enlistment.GetVirtualPathTo("GVFS", "GVFS");
            folder.ShouldBeADirectory(this.fileSystem);
            folder = this.Enlistment.GetVirtualPathTo("GVFS", "GVFS", "CommandLine");
            folder.ShouldBeADirectory(this.fileSystem);

            folder = this.Enlistment.GetVirtualPathTo("Scripts");
            folder.ShouldNotExistOnDisk(this.fileSystem);

            // Remove the last directory should make all folders appear again
            gvfsProcess.RemoveIncludedFolders(Path.Combine("GVFS", "GVFS"));
            directories = Directory.GetDirectories(this.Enlistment.RepoRoot);
            directories.ShouldMatchInOrder(allRootDirectories);

            // Add parent directory should make the parent recursive
            gvfsProcess.AddIncludedFolders(Path.Combine("GVFS", "GVFS", "CommandLine"));
            directories = Directory.GetDirectories(Path.Combine(this.Enlistment.RepoRoot, "GVFS", "GVFS"));
            directories.Length.ShouldEqual(1);
            directories[0].ShouldEqual(Path.Combine(this.Enlistment.RepoRoot, "GVFS", "GVFS", "CommandLine"));

            gvfsProcess.AddIncludedFolders(Path.Combine("GVFS", "GVFS"));
            directories = Directory.GetDirectories(Path.Combine(this.Enlistment.RepoRoot, "GVFS", "GVFS"));
            directories.ShouldMatchInOrder(directoriesForParentAdd);

            // Add and remove folder
            gvfsProcess.AddIncludedFolders("Scripts");
            folder.ShouldBeADirectory(this.fileSystem);

            gvfsProcess.RemoveIncludedFolders("Scripts");
            folder.ShouldNotExistOnDisk(this.fileSystem);

            // Add and remove sibling folder to GVFS/GVFS
            gvfsProcess.AddIncludedFolders(Path.Combine("GVFS", "FastFetch"));
            folder = this.Enlistment.GetVirtualPathTo("GVFS", "FastFetch");
            folder.ShouldBeADirectory(this.fileSystem);

            gvfsProcess.RemoveIncludedFolders(Path.Combine("GVFS", "FastFetch"));
            folder.ShouldNotExistOnDisk(this.fileSystem);
            folder = this.Enlistment.GetVirtualPathTo("GVFS", "GVFS");
            folder.ShouldBeADirectory(this.fileSystem);

            // Add subfolder of GVFS/GVFS and make sure it stays recursive
            gvfsProcess.AddIncludedFolders(Path.Combine("GVFS", "GVFS", "Properties"));
            folder = this.Enlistment.GetVirtualPathTo("GVFS", "GVFS", "Properties");
            folder.ShouldBeADirectory(this.fileSystem);

            folder = this.Enlistment.GetVirtualPathTo("GVFS", "GVFS", "CommandLine");
            folder.ShouldBeADirectory(this.fileSystem);
        }
    }
}
