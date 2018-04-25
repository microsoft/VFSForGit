using GVFS.FunctionalTests.FileSystemRunners;
using GVFS.FunctionalTests.Should;
using GVFS.Tests.Should;
using NUnit.Framework;
using System.IO;

namespace GVFS.FunctionalTests.Tests.EnlistmentPerFixture
{
    [TestFixture]
    public class PrefetchVerbTests : TestsWithEnlistmentPerFixture
    {
        private const string PrefetchCommitsAndTreesLock = "prefetch-commits-trees.lock";

        private FileSystemRunner fileSystem;

        public PrefetchVerbTests()
        {
            this.fileSystem = new SystemIORunner();
        }

        [TestCase, Order(1)]
        public void PrefetchAllMustBeExplicit()
        {
            this.Enlistment.Prefetch(string.Empty, failOnError: false).ShouldContain("Did you mean to fetch all blobs?");
        }

        [TestCase, Order(2)]
        public void PrefetchSpecificFiles()
        {
            this.ExpectBlobCount(this.Enlistment.Prefetch(@"--files GVFS\GVFS\Program.cs"), 1);
            this.ExpectBlobCount(this.Enlistment.Prefetch(@"--files GVFS\GVFS\Program.cs;GVFS\GVFS.FunctionalTests\GVFS.FunctionalTests.csproj"), 2);
        }

        [TestCase, Order(3)]
        public void PrefetchByFileExtension()
        {
            this.ExpectBlobCount(this.Enlistment.Prefetch("--files *.cs"), 199);
            this.ExpectBlobCount(this.Enlistment.Prefetch("--files *.cs;*.csproj"), 208);
        }

        [TestCase, Order(4)]
        public void PrefetchByFileExtensionWithHydrate()
        {
            int expectedCount = 3;
            string output = this.Enlistment.Prefetch("--files *.md --hydrate");
            this.ExpectBlobCount(output, expectedCount);
            output.ShouldContain("Hydrated files:   " + expectedCount);
        }

        [TestCase, Order(5)]
        public void PrefetchByFilesWithHydrateWhoseObjectsAreAlreadyDownloaded()
        {
            int expectedCount = 2;
            string output = this.Enlistment.Prefetch(@"--files GVFS\GVFS\Program.cs;GVFS\GVFS.FunctionalTests\GVFS.FunctionalTests.csproj --hydrate");
            this.ExpectBlobCount(output, expectedCount);
            output.ShouldContain("Hydrated files:   " + expectedCount);
            output.ShouldContain("Downloaded:       0");
        }

        [TestCase, Order(6)]
        public void PrefetchFolders()
        {
            this.ExpectBlobCount(this.Enlistment.Prefetch(@"--folders GVFS\GVFS"), 17);
            this.ExpectBlobCount(this.Enlistment.Prefetch(@"--folders GVFS\GVFS;GVFS\GVFS.FunctionalTests"), 65);
            this.PackDirShouldContainMidx(this.Enlistment.GetPackRoot(this.fileSystem));
        }

        [TestCase, Order(7)]
        public void PrefetchIsAllowedToDoNothing()
        {
            this.ExpectBlobCount(this.Enlistment.Prefetch("--files nonexistent.txt"), 0);
            this.ExpectBlobCount(this.Enlistment.Prefetch("--folders nonexistent_folder"), 0);
            this.PackDirShouldContainMidx(this.Enlistment.GetPackRoot(this.fileSystem));
        }

        [TestCase, Order(8)]
        public void PrefetchFolderListFromFile()
        {
            string tempFilePath = Path.Combine(Path.GetTempPath(), "temp.file");
            File.WriteAllLines(
                tempFilePath,
                new[]
                {
                    "# A comment",
                    " ",
                    "gvfs/",
                    "gvfs/gvfs",
                    "gvfs/"
                });

            this.ExpectBlobCount(this.Enlistment.Prefetch("--folders-list " + tempFilePath), 279);
            this.PackDirShouldContainMidx(this.Enlistment.GetPackRoot(this.fileSystem));
            File.Delete(tempFilePath);
        }

        [TestCase, Order(9)]
        public void PrefetchAll()
        {
            this.ExpectBlobCount(this.Enlistment.Prefetch("--files *"), 494);
            this.ExpectBlobCount(this.Enlistment.Prefetch("--folders /"), 494);
            this.ExpectBlobCount(this.Enlistment.Prefetch("--folders \\"), 494);
            this.PackDirShouldContainMidx(this.Enlistment.GetPackRoot(this.fileSystem));
        }

        [TestCase, Order(10)]
        public void PrefetchCleansUpStalePrefetchLock()
        {
            this.Enlistment.Prefetch("--commits");
            this.PackDirShouldContainMidx(this.Enlistment.GetPackRoot(this.fileSystem));
            string prefetchCommitsLockFile = Path.Combine(this.Enlistment.GetObjectRoot(this.fileSystem), "pack", PrefetchCommitsAndTreesLock);
            prefetchCommitsLockFile.ShouldNotExistOnDisk(this.fileSystem);
            this.fileSystem.WriteAllText(prefetchCommitsLockFile, this.Enlistment.EnlistmentRoot);
            prefetchCommitsLockFile.ShouldBeAFile(this.fileSystem);

            this.Enlistment.Prefetch("--commits");
            prefetchCommitsLockFile.ShouldNotExistOnDisk(this.fileSystem);
            this.PackDirShouldContainMidx(this.Enlistment.GetPackRoot(this.fileSystem));
        }

        private void ExpectBlobCount(string output, int expectedCount)
        {
            output.ShouldContain("Matched blobs:    " + expectedCount);
        }

        private void PackDirShouldContainMidx(string packDir)
        {
            string midxHead = packDir + "/midx-head";
            this.fileSystem.FileExists(midxHead).ShouldBeTrue();
            string midxHash = this.fileSystem.ReadAllText(midxHead).Substring(0, 40);
            string midxFile = packDir + "/midx-" + midxHash + ".midx";
            this.fileSystem.FileExists(midxFile).ShouldBeTrue();
        }
    }
}
