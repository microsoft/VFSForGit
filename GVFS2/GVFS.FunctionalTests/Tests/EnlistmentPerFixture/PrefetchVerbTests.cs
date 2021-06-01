using GVFS.FunctionalTests.FileSystemRunners;
using GVFS.FunctionalTests.Should;
using GVFS.FunctionalTests.Tools;
using GVFS.Tests.Should;
using NUnit.Framework;
using System;
using System.IO;
using System.Linq;
using System.Threading;

namespace GVFS.FunctionalTests.Tests.EnlistmentPerFixture
{
    [TestFixture]
    [NonParallelizable]
    public class PrefetchVerbTests : TestsWithEnlistmentPerFixture
    {
        private const string PrefetchCommitsAndTreesLock = "prefetch-commits-trees.lock";
        private const string LsTreeTypeInPathBranchName = "FunctionalTests/20201014_LsTreeTypeInPath";

        // on case-insensitive filesystems, test case-blind matching in
        // folder lists using "gvfs/" instead of "GVFS/"
        private static readonly string PrefetchGVFSFolder = FileSystemHelpers.CaseSensitiveFileSystem ? "GVFS" : "gvfs";
        private static readonly string PrefetchGVFSFolderPath = PrefetchGVFSFolder + "/";
        private static readonly string[] PrefetchFolderList = new string[]
        {
            "# A comment",
            " ",
            PrefetchGVFSFolderPath, // "GVFS/" or "gvfs/"
            PrefetchGVFSFolderPath + PrefetchGVFSFolder, // "GVFS/GVFS" or "gvfs/gvfs"
            PrefetchGVFSFolderPath,
        };

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
            File.WriteAllLines(tempFilePath, PrefetchFolderList);
            this.ExpectBlobCount(this.Enlistment.Prefetch("--folders-list \"" + tempFilePath + "\""), 279);
            File.Delete(tempFilePath);
        }

        [TestCase, Order(9)]
        public void PrefetchAll()
        {
            this.ExpectBlobCount(this.Enlistment.Prefetch("--files *"), 494);
            this.ExpectBlobCount(this.Enlistment.Prefetch($"--folders {Path.DirectorySeparatorChar}"), 494);
        }

        [TestCase, Order(10)]
        public void NoopPrefetch()
        {
            this.ExpectBlobCount(this.Enlistment.Prefetch("--files *"), 494);

            this.Enlistment.Prefetch("--files *").ShouldContain("Nothing new to prefetch.");
        }

        // TODO(#1219): Handle that lock files are not deleted on Mac, they are simply unlocked
        [TestCase, Order(11)]
        [Category(Categories.MacTODO.TestNeedsToLockFile)]
        public void PrefetchCleansUpStalePrefetchLock()
        {
            this.Enlistment.Prefetch("--commits");
            this.PostFetchStepShouldComplete();
            string prefetchCommitsLockFile = Path.Combine(this.Enlistment.GetObjectRoot(this.fileSystem), "pack", PrefetchCommitsAndTreesLock);
            prefetchCommitsLockFile.ShouldNotExistOnDisk(this.fileSystem);
            this.fileSystem.WriteAllText(prefetchCommitsLockFile, this.Enlistment.EnlistmentRoot);
            prefetchCommitsLockFile.ShouldBeAFile(this.fileSystem);

            this.fileSystem
                .EnumerateDirectory(this.Enlistment.GetPackRoot(this.fileSystem))
                .Split()
                .Where(file => string.Equals(Path.GetExtension(file), ".keep", FileSystemHelpers.PathComparison))
                .Count()
                .ShouldEqual(1, "Incorrect number of .keep files in pack directory");

            this.Enlistment.Prefetch("--commits");
            this.PostFetchStepShouldComplete();
            prefetchCommitsLockFile.ShouldNotExistOnDisk(this.fileSystem);
        }

        [TestCase, Order(12)]
        public void PrefetchFilesFromFileListFile()
        {
            string tempFilePath = Path.Combine(Path.GetTempPath(), "temp.file");
            try
            {
                File.WriteAllLines(
                    tempFilePath,
                    new[]
                    {
                        Path.Combine("GVFS", "GVFS", "Program.cs"),
                        Path.Combine("GVFS", "GVFS.FunctionalTests", "GVFS.FunctionalTests.csproj")
                    });

                this.ExpectBlobCount(this.Enlistment.Prefetch($"--files-list \"{tempFilePath}\""), 2);
            }
            finally
            {
                File.Delete(tempFilePath);
            }
        }

        [TestCase, Order(13)]
        public void PrefetchFilesFromFileListStdIn()
        {
            // on case-insensitive filesystems, test case-blind matching
            // using "App.config" instead of "app.config"
            string input = string.Join(
                Environment.NewLine,
                new[]
                {
                    Path.Combine("GVFS", "GVFS", "packages.config"),
                    Path.Combine("GVFS", "GVFS.FunctionalTests", FileSystemHelpers.CaseSensitiveFileSystem ? "app.config" : "App.config")
                });

            this.ExpectBlobCount(this.Enlistment.Prefetch("--stdin-files-list", standardInput: input), 2);
        }

        [TestCase, Order(14)]
        public void PrefetchFolderListFromStdin()
        {
            string input = string.Join(Environment.NewLine, PrefetchFolderList);
            this.ExpectBlobCount(this.Enlistment.Prefetch("--stdin-folders-list", standardInput: input), 279);
        }

        public void PrefetchPathsWithLsTreeTypeInPath()
        {
            ProcessResult checkoutResult = GitProcess.InvokeProcess(this.Enlistment.RepoRoot, "checkout " + LsTreeTypeInPathBranchName);

            this.ExpectBlobCount(this.Enlistment.Prefetch("--files *"), 496);
        }

        private void ExpectBlobCount(string output, int expectedCount)
        {
            output.ShouldContain("Matched blobs:    " + expectedCount);
        }

        private void PostFetchStepShouldComplete()
        {
            string objectDir = this.Enlistment.GetObjectRoot(this.fileSystem);
            string objectCacheLock = Path.Combine(objectDir, "git-maintenance-step.lock");

            // Wait first, to hopefully ensure the background thread has
            // started before we check for the lock file.
            do
            {
                Thread.Sleep(500);
            }
            while (this.fileSystem.FileExists(objectCacheLock));

            // A commit graph is not always generated, but if it is, then we want to ensure it is in a good state
            if (this.fileSystem.FileExists(Path.Combine(objectDir, "info", "commit-graphs", "commit-graph-chain")))
            {
                ProcessResult graphResult = GitProcess.InvokeProcess(this.Enlistment.RepoRoot, "commit-graph verify --shallow --object-dir=\"" + objectDir + "\"");
                graphResult.ExitCode.ShouldEqual(0);
            }
        }
    }
}
