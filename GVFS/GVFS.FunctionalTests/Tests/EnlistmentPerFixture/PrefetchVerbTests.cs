using GVFS.FunctionalTests.FileSystemRunners;
using GVFS.FunctionalTests.Should;
using GVFS.FunctionalTests.Tools;
using GVFS.Tests.Should;
using NUnit.Framework;
using System.IO;
using System.Threading;

namespace GVFS.FunctionalTests.Tests.EnlistmentPerFixture
{
    [TestFixture]
    [NonParallelizable]
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
            this.ExpectBlobCount(this.Enlistment.Prefetch($"--files {Path.Combine("GVFS", "GVFS", "Program.cs")}"), 1);
            this.ExpectBlobCount(this.Enlistment.Prefetch($"--files {Path.Combine("GVFS", "GVFS", "Program.cs")};{Path.Combine("GVFS", "GVFS.FunctionalTests", "GVFS.FunctionalTests.csproj")}"), 2);
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
            string output = this.Enlistment.Prefetch(
                $"--files {Path.Combine("GVFS", "GVFS", "Program.cs")};{Path.Combine("GVFS", "GVFS.FunctionalTests", "GVFS.FunctionalTests.csproj")} --hydrate");
            this.ExpectBlobCount(output, expectedCount);
            output.ShouldContain("Hydrated files:   " + expectedCount);
            output.ShouldContain("Downloaded:       0");
        }

        [TestCase, Order(6)]
        public void PrefetchFolders()
        {
            this.ExpectBlobCount(this.Enlistment.Prefetch($"--folders {Path.Combine("GVFS", "GVFS")}"), 17);
            this.ExpectBlobCount(this.Enlistment.Prefetch($"--folders {Path.Combine("GVFS", "GVFS")};{Path.Combine("GVFS", "GVFS.FunctionalTests")}"), 65);
        }

        [TestCase, Order(7)]
        public void PrefetchIsAllowedToDoNothing()
        {
            this.ExpectBlobCount(this.Enlistment.Prefetch("--files nonexistent.txt"), 0);
            this.ExpectBlobCount(this.Enlistment.Prefetch("--folders nonexistent_folder"), 0);
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
            File.Delete(tempFilePath);
        }

        [TestCase, Order(9)]
        public void PrefetchAll()
        {
            this.ExpectBlobCount(this.Enlistment.Prefetch("--files *"), 494);
            this.ExpectBlobCount(this.Enlistment.Prefetch("--folders /"), 494);
            this.ExpectBlobCount(this.Enlistment.Prefetch($"--folders {Path.DirectorySeparatorChar}"), 494);
        }

        // TODO(Mac): Handle that lock files are not deleted on Mac, they are simply unlocked
        [TestCase, Order(10)]
        [Category(Categories.MacTODO.M4)]
        public void PrefetchCleansUpStalePrefetchLock()
        {
            this.Enlistment.Prefetch("--commits");
            this.PostFetchJobShouldComplete();
            string prefetchCommitsLockFile = Path.Combine(this.Enlistment.GetObjectRoot(this.fileSystem), "pack", PrefetchCommitsAndTreesLock);
            prefetchCommitsLockFile.ShouldNotExistOnDisk(this.fileSystem);
            this.fileSystem.WriteAllText(prefetchCommitsLockFile, this.Enlistment.EnlistmentRoot);
            prefetchCommitsLockFile.ShouldBeAFile(this.fileSystem);

            this.Enlistment.Prefetch("--commits");
            this.PostFetchJobShouldComplete();
            prefetchCommitsLockFile.ShouldNotExistOnDisk(this.fileSystem);
        }

        private void ExpectBlobCount(string output, int expectedCount)
        {
            output.ShouldContain("Matched blobs:    " + expectedCount);
        }

        private void PostFetchJobShouldComplete()
        {
            string objectDir = this.Enlistment.GetObjectRoot(this.fileSystem);
            string postFetchLock = Path.Combine(objectDir, "post-fetch.lock");

            // Wait first, to hopefully ensure the background thread has
            // started before we check for the lock file.
            do
            {
                Thread.Sleep(500);
            }
            while (this.fileSystem.FileExists(postFetchLock));

            ProcessResult midxResult = GitProcess.InvokeProcess(this.Enlistment.RepoRoot, "midx --read --pack-dir=\"" + objectDir + "/pack\"");
            midxResult.ExitCode.ShouldEqual(0);
            midxResult.Output.ShouldContain("4d494458"); // Header from midx file.

            ProcessResult graphResult = GitProcess.InvokeProcess(this.Enlistment.RepoRoot, "commit-graph read --object-dir=\"" + objectDir + "\"");
            graphResult.ExitCode.ShouldEqual(0);
            graphResult.Output.ShouldContain("43475048"); // Header from commit-graph file.
        }
    }
}
