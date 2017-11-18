using RGFS.FunctionalTests.FileSystemRunners;
using RGFS.FunctionalTests.Should;
using RGFS.Tests.Should;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace RGFS.FunctionalTests.Tests.LongRunningEnlistment
{
    [TestFixtureSource(typeof(FileSystemRunner), FileSystemRunner.TestRunners)]
    public class GitObjectManipulationTests : TestsWithLongRunningEnlistment
    {
        private FileSystemRunner fileSystem;
        public GitObjectManipulationTests(FileSystemRunner fileSystem)
        {
            this.fileSystem = fileSystem;
        }

        [TestCase]
        public void PackFileWritesAreRedirectedToLocalAlternate()
        {
            string filename = "pack-e145421ff608e7f956de40e77ef948d26432913c.pack";
            string virtualPath = Path.Combine(this.Enlistment.VirtualRepoRoot, ".git", "objects", "pack", filename);
            string physicalPath = Path.Combine(this.Enlistment.LocalAlternateRoot, ".git", "objects", "pack", filename);

            virtualPath.ShouldNotExistOnDisk(this.fileSystem);
            physicalPath.ShouldNotExistOnDisk(this.fileSystem);
            this.fileSystem.WriteAllText(virtualPath, "any ol' contents");

            virtualPath.ShouldBeAFile(this.fileSystem);
            physicalPath.ShouldBeAFile(this.fileSystem);

            string repoPath = Path.Combine(this.Enlistment.PhysicalRepoRoot, ".git", "objects", "pack", filename);
            repoPath.ShouldNotExistOnDisk(this.fileSystem);

            this.fileSystem.DeleteFile(virtualPath);
        }

        [TestCase]
        public void LooseObjectWritesAreRedirectedToLocalAlternate()
        {
            string firstTwoletters = "e1";
            string rest = "45421ff608e7f956de40e77ef948d26432913c";

            // Assert that creating a two letter folder in .git\objects ends up only in the local alternate
            string virtualFolder = Path.Combine(this.Enlistment.VirtualRepoRoot, ".git", "objects", firstTwoletters);
            string physicalFolder = Path.Combine(this.Enlistment.LocalAlternateRoot, ".git", "objects", firstTwoletters);

            virtualFolder.ShouldNotExistOnDisk(this.fileSystem);
            physicalFolder.ShouldNotExistOnDisk(this.fileSystem);
            this.fileSystem.CreateDirectory(virtualFolder);

            virtualFolder.ShouldBeAFile(this.fileSystem);
            physicalFolder.ShouldBeAFile(this.fileSystem);

            string repoFolder = Path.Combine(this.Enlistment.PhysicalRepoRoot, ".git", "objects", "pack", firstTwoletters, rest);
            repoFolder.ShouldNotExistOnDisk(this.fileSystem);
            
            // Assert that creating a file in the folder above ends up only in the local alternate
            string virtualPath = Path.Combine(virtualFolder, rest);
            string physicalPath = Path.Combine(physicalFolder, rest);

            virtualPath.ShouldNotExistOnDisk(this.fileSystem);
            physicalPath.ShouldNotExistOnDisk(this.fileSystem);
            this.fileSystem.WriteAllText(virtualPath, "any ol' contents");

            virtualPath.ShouldBeAFile(this.fileSystem);
            physicalPath.ShouldBeAFile(this.fileSystem);

            Path.Combine(repoFolder, rest).ShouldNotExistOnDisk(this.fileSystem);
            
            this.fileSystem.DeleteFile(virtualPath);
            this.fileSystem.DeleteDirectory(virtualFolder);
        }

        [TestCase]
        public void NormalFileWritesAreNotRedirectedToLocalAlternate()
        {
            string filename = "AWeirdRandom.GitObjectsfile";
            string virtualPath = Path.Combine(this.Enlistment.VirtualRepoRoot, ".git", "objects", filename);
            string alternatePath = Path.Combine(this.Enlistment.LocalAlternateRoot, ".git", "objects", filename);
            string repoPath = Path.Combine(this.Enlistment.PhysicalRepoRoot, ".git", "objects", filename);

            virtualPath.ShouldNotExistOnDisk(this.fileSystem);
            alternatePath.ShouldNotExistOnDisk(this.fileSystem);
            repoPath.ShouldNotExistOnDisk(this.fileSystem);

            this.fileSystem.WriteAllText(virtualPath, "any ol' contents");

            virtualPath.ShouldBeAFile(this.fileSystem);
            alternatePath.ShouldNotExistOnDisk(this.fileSystem);
            repoPath.ShouldBeAFile(this.fileSystem);
            
            this.fileSystem.DeleteFile(virtualPath);
        }
    }
}
