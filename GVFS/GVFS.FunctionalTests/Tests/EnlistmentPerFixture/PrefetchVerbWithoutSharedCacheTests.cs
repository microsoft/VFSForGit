using GVFS.FunctionalTests.FileSystemRunners;
using GVFS.FunctionalTests.Should;
using GVFS.FunctionalTests.Tools;
using GVFS.Tests.Should;
using NUnit.Framework;
using System;
using System.IO;
using System.Threading;

namespace GVFS.FunctionalTests.Tests.EnlistmentPerFixture
{
    // TODO(#1219): Before these tests can be enabled PostFetchJobShouldComplete needs
    // to work on Mac (where post-fetch.lock is not removed from disk)
    [TestFixture]
    [Category(Categories.ExtraCoverage)]
    [Category(Categories.MacTODO.TestNeedsToLockFile)]
    public class PrefetchVerbWithoutSharedCacheTests : TestsWithEnlistmentPerFixture
    {
        private const string PrefetchPackPrefix = "prefetch";
        private const string TempPackFolder = "tempPacks";

        private FileSystemRunner fileSystem;

        // Set forcePerRepoObjectCache to true to avoid any of the tests inadvertently corrupting
        // the cache
        public PrefetchVerbWithoutSharedCacheTests()
            : base(forcePerRepoObjectCache: true, skipPrefetchDuringClone: true)
        {
            this.fileSystem = new SystemIORunner();
        }

        private string PackRoot
        {
            get
            {
                return this.Enlistment.GetPackRoot(this.fileSystem);
            }
        }

        private string TempPackRoot
        {
            get
            {
                return Path.Combine(this.PackRoot, TempPackFolder);
            }
        }

        [TestCase, Order(1)]
        public void PrefetchCommitsToEmptyCache()
        {
            this.Enlistment.Prefetch("--commits");
            this.PostFetchJobShouldComplete();

            // Verify prefetch pack(s) are in packs folder and have matching idx file
            string[] prefetchPacks = this.ReadPrefetchPackFileNames();
            this.AllPrefetchPacksShouldHaveIdx(prefetchPacks);

            // Verify tempPacks is empty
            this.TempPackRoot.ShouldBeADirectory(this.fileSystem).WithNoItems();
        }

        [TestCase, Order(2)]
        public void PrefetchBuildsIdxWhenMissingFromPrefetchPack()
        {
            string[] prefetchPacks = this.ReadPrefetchPackFileNames();
            prefetchPacks.Length.ShouldBeAtLeast(1, "There should be at least one prefetch pack");

            string idxPath = Path.ChangeExtension(prefetchPacks[0], ".idx");
            idxPath.ShouldBeAFile(this.fileSystem);
            File.SetAttributes(idxPath, FileAttributes.Normal);
            this.fileSystem.DeleteFile(idxPath);
            idxPath.ShouldNotExistOnDisk(this.fileSystem);

            // Prefetch should rebuild the missing idx
            this.Enlistment.Prefetch("--commits");
            this.PostFetchJobShouldComplete();

            idxPath.ShouldBeAFile(this.fileSystem);

            // All of the original prefetch packs should still be present
            string[] newPrefetchPacks = this.ReadPrefetchPackFileNames();
            newPrefetchPacks.ShouldContain(prefetchPacks, (item, expectedValue) => { return string.Equals(item, expectedValue); });
            this.AllPrefetchPacksShouldHaveIdx(newPrefetchPacks);
            this.TempPackRoot.ShouldBeADirectory(this.fileSystem).WithNoItems();
        }

        [TestCase, Order(3)]
        public void PrefetchCleansUpBadPrefetchPack()
        {
            string[] prefetchPacks = this.ReadPrefetchPackFileNames();
            long mostRecentPackTimestamp = this.GetMostRecentPackTimestamp(prefetchPacks);

            // Create a bad pack that is newer than the most recent pack
            string badContents = "BADPACK";
            string badPackPath = Path.Combine(this.PackRoot, $"{PrefetchPackPrefix}-{mostRecentPackTimestamp + 1}-{Guid.NewGuid().ToString("N")}.pack");
            this.fileSystem.WriteAllText(badPackPath, badContents);
            badPackPath.ShouldBeAFile(this.fileSystem).WithContents(badContents);

            // Prefetch should delete the bad pack
            this.Enlistment.Prefetch("--commits");
            this.PostFetchJobShouldComplete();

            badPackPath.ShouldNotExistOnDisk(this.fileSystem);

            // All of the original prefetch packs should still be present
            string[] newPrefetchPacks = this.ReadPrefetchPackFileNames();
            newPrefetchPacks.ShouldContain(prefetchPacks, (item, expectedValue) => { return string.Equals(item, expectedValue); });
            this.AllPrefetchPacksShouldHaveIdx(newPrefetchPacks);
            this.TempPackRoot.ShouldBeADirectory(this.fileSystem).WithNoItems();
        }

        [TestCase, Order(4)]
        public void PrefetchCleansUpOldPrefetchPack()
        {
            this.Enlistment.UnmountGVFS();

            string[] prefetchPacks = this.ReadPrefetchPackFileNames();
            long oldestPackTimestamp = this.GetOldestPackTimestamp(prefetchPacks);

            // Create a bad pack that is older than the oldest pack
            string badContents = "BADPACK";
            string badPackPath = Path.Combine(this.PackRoot, $"{PrefetchPackPrefix}-{oldestPackTimestamp - 1}-{Guid.NewGuid().ToString("N")}.pack");
            this.fileSystem.WriteAllText(badPackPath, badContents);
            badPackPath.ShouldBeAFile(this.fileSystem).WithContents(badContents);

            // Prefetch should delete the bad pack and all packs after it
            this.Enlistment.Prefetch("--commits");
            this.PostFetchJobShouldComplete();

            badPackPath.ShouldNotExistOnDisk(this.fileSystem);
            foreach (string packPath in prefetchPacks)
            {
                string idxPath = Path.ChangeExtension(packPath, ".idx");
                badPackPath.ShouldNotExistOnDisk(this.fileSystem);
                idxPath.ShouldNotExistOnDisk(this.fileSystem);
            }

            string[] newPrefetchPacks = this.ReadPrefetchPackFileNames();
            this.AllPrefetchPacksShouldHaveIdx(newPrefetchPacks);
            this.TempPackRoot.ShouldBeADirectory(this.fileSystem).WithNoItems();
        }

        [TestCase, Order(5)]
        public void PrefetchFailsWhenItCannotRemoveABadPrefetchPack()
        {
            this.Enlistment.UnmountGVFS();

            string[] prefetchPacks = this.ReadPrefetchPackFileNames();
            long mostRecentPackTimestamp = this.GetMostRecentPackTimestamp(prefetchPacks);

            // Create a bad pack that is newer than the most recent pack
            string badContents = "BADPACK";
            string badPackPath = Path.Combine(this.PackRoot, $"{PrefetchPackPrefix}-{mostRecentPackTimestamp + 1}-{Guid.NewGuid().ToString("N")}.pack");
            this.fileSystem.WriteAllText(badPackPath, badContents);
            badPackPath.ShouldBeAFile(this.fileSystem).WithContents(badContents);

            // Open a handle to the bad pack that will prevent prefetch from being able to delete it
            using (FileStream stream = new FileStream(badPackPath, FileMode.Open, FileAccess.Read, FileShare.None))
            {
                string output = this.Enlistment.Prefetch("--commits", failOnError: false);
                output.ShouldContain($"Unable to delete {badPackPath}");
            }

            // After handle is closed prefetch should succeed
            this.Enlistment.Prefetch("--commits");
            this.PostFetchJobShouldComplete();

            badPackPath.ShouldNotExistOnDisk(this.fileSystem);

            string[] newPrefetchPacks = this.ReadPrefetchPackFileNames();
            newPrefetchPacks.ShouldContain(prefetchPacks, (item, expectedValue) => { return string.Equals(item, expectedValue); });
            this.AllPrefetchPacksShouldHaveIdx(newPrefetchPacks);
            this.TempPackRoot.ShouldBeADirectory(this.fileSystem).WithNoItems();
        }

        [TestCase, Order(6)]
        public void PrefetchFailsWhenItCannotRemoveAPrefetchPackNewerThanBadPrefetchPack()
        {
            this.Enlistment.UnmountGVFS();

            string[] prefetchPacks = this.ReadPrefetchPackFileNames();
            long oldestPackTimestamp = this.GetOldestPackTimestamp(prefetchPacks);

            // Create a bad pack that is older than the oldest pack
            string badContents = "BADPACK";
            string badPackPath = Path.Combine(this.PackRoot, $"{PrefetchPackPrefix}-{oldestPackTimestamp - 1}-{Guid.NewGuid().ToString("N")}.pack");
            this.fileSystem.WriteAllText(badPackPath, badContents);
            badPackPath.ShouldBeAFile(this.fileSystem).WithContents(badContents);

            // Open a handle to a good pack that is newer than the bad pack, which will prevent prefetch from being able to delete it
            using (FileStream stream = new FileStream(prefetchPacks[0], FileMode.Open, FileAccess.Read, FileShare.None))
            {
                string output = this.Enlistment.Prefetch("--commits", failOnError: false);
                output.ShouldContain($"Unable to delete {prefetchPacks[0]}");
            }

            // After handle is closed prefetch should succeed
            this.Enlistment.Prefetch("--commits");
            this.PostFetchJobShouldComplete();

            // The bad pack and all packs newer than it should not be on disk
            badPackPath.ShouldNotExistOnDisk(this.fileSystem);

            string[] newPrefetchPacks = this.ReadPrefetchPackFileNames();
            newPrefetchPacks.ShouldNotContain(prefetchPacks, (item, expectedValue) => { return string.Equals(item, expectedValue); });
            this.AllPrefetchPacksShouldHaveIdx(newPrefetchPacks);
            this.TempPackRoot.ShouldBeADirectory(this.fileSystem).WithNoItems();
        }

        [TestCase, Order(7)]
        public void PrefetchFailsWhenItCannotRemoveAPrefetchIdxNewerThanBadPrefetchPack()
        {
            this.Enlistment.UnmountGVFS();

            string[] prefetchPacks = this.ReadPrefetchPackFileNames();
            long oldestPackTimestamp = this.GetOldestPackTimestamp(prefetchPacks);

            // Create a bad pack that is older than the oldest pack
            string badContents = "BADPACK";
            string badPackPath = Path.Combine(this.PackRoot, $"{PrefetchPackPrefix}-{oldestPackTimestamp - 1}-{Guid.NewGuid().ToString("N")}.pack");
            this.fileSystem.WriteAllText(badPackPath, badContents);
            badPackPath.ShouldBeAFile(this.fileSystem).WithContents(badContents);

            string newerIdxPath = Path.ChangeExtension(prefetchPacks[0], ".idx");
            newerIdxPath.ShouldBeAFile(this.fileSystem);

            // Open a handle to a good idx that is newer than the bad pack, which will prevent prefetch from being able to delete it
            using (FileStream stream = new FileStream(newerIdxPath, FileMode.Open, FileAccess.Read, FileShare.None))
            {
                string output = this.Enlistment.Prefetch("--commits", failOnError: false);
                output.ShouldContain($"Unable to delete {newerIdxPath}");
            }

            // After handle is closed prefetch should succeed
            this.Enlistment.Prefetch("--commits");
            this.PostFetchJobShouldComplete();

            // The bad pack and all packs newer than it should not be on disk
            badPackPath.ShouldNotExistOnDisk(this.fileSystem);
            newerIdxPath.ShouldNotExistOnDisk(this.fileSystem);

            string[] newPrefetchPacks = this.ReadPrefetchPackFileNames();
            newPrefetchPacks.ShouldNotContain(prefetchPacks, (item, expectedValue) => { return string.Equals(item, expectedValue); });
            this.AllPrefetchPacksShouldHaveIdx(newPrefetchPacks);
            this.TempPackRoot.ShouldBeADirectory(this.fileSystem).WithNoItems();
        }

        [TestCase, Order(8)]
        public void PrefetchCleansUpStaleTempPrefetchPacks()
        {
            this.Enlistment.UnmountGVFS();

            // Create stale packs and idxs  in the temp folder
            string stalePackContents = "StalePack";
            string stalePackPath = Path.Combine(this.TempPackRoot, $"{PrefetchPackPrefix}-123456-{Guid.NewGuid().ToString("N")}.pack");
            this.fileSystem.WriteAllText(stalePackPath, stalePackContents);
            stalePackPath.ShouldBeAFile(this.fileSystem).WithContents(stalePackContents);

            string staleIdxContents = "StaleIdx";
            string staleIdxPath = Path.ChangeExtension(stalePackPath, ".idx");
            this.fileSystem.WriteAllText(staleIdxPath, staleIdxContents);
            staleIdxPath.ShouldBeAFile(this.fileSystem).WithContents(staleIdxContents);

            string stalePackPath2 = Path.Combine(this.TempPackRoot, $"{PrefetchPackPrefix}-123457-{Guid.NewGuid().ToString("N")}.pack");
            this.fileSystem.WriteAllText(stalePackPath2, stalePackContents);
            stalePackPath2.ShouldBeAFile(this.fileSystem).WithContents(stalePackContents);

            string stalePack2TempIdx = Path.ChangeExtension(stalePackPath2, ".tempidx");
            this.fileSystem.WriteAllText(stalePack2TempIdx, staleIdxContents);
            stalePack2TempIdx.ShouldBeAFile(this.fileSystem).WithContents(staleIdxContents);

            // Create other unrelated file in the temp folder
            string otherFileContents = "Test file, don't delete me!";
            string otherFilePath = Path.Combine(this.TempPackRoot, "ReadmeAndDontDeleteMe.txt");
            this.fileSystem.WriteAllText(otherFilePath, otherFileContents);
            otherFilePath.ShouldBeAFile(this.fileSystem).WithContents(otherFileContents);

            this.Enlistment.Prefetch("--commits");
            this.PostFetchJobShouldComplete();

            // Validate stale prefetch packs are cleaned up
            Directory.GetFiles(this.TempPackRoot, $"{PrefetchPackPrefix}*.pack").ShouldBeEmpty("There should be no .pack files in the tempPack folder");
            Directory.GetFiles(this.TempPackRoot, $"{PrefetchPackPrefix}*.idx").ShouldBeEmpty("There should be no .idx files in the tempPack folder");
            Directory.GetFiles(this.TempPackRoot, $"{PrefetchPackPrefix}*.tempidx").ShouldBeEmpty("There should be no .tempidx files in the tempPack folder");

            // Validate other files are not impacted
            otherFilePath.ShouldBeAFile(this.fileSystem).WithContents(otherFileContents);
        }

        [TestCase, Order(9)]
        public void PrefetchCleansUpOphanedLockFiles()
        {
            // the commit-graph write happens only when the prefetch downloads at least one pack

            string graphPath = Path.Combine(this.Enlistment.GetObjectRoot(this.fileSystem), "info", "commit-graphs", "commit-graph-chain");
            string graphLockPath = graphPath + ".lock";

            this.fileSystem.CreateEmptyFile(graphLockPath);

            // Unmount so we can delete the files.
            this.Enlistment.UnmountGVFS();

            // Force deleting the prefetch packs to make the prefetch non-trivial.
            this.fileSystem.DeleteDirectory(this.PackRoot);
            this.fileSystem.CreateDirectory(this.PackRoot);

            // Re-mount so the post-fetch job runs
            this.Enlistment.MountGVFS();

            this.Enlistment.Prefetch("--commits");
            this.PostFetchJobShouldComplete();

            this.fileSystem.FileExists(graphLockPath).ShouldBeFalse(nameof(graphLockPath));
            this.fileSystem.FileExists(graphPath).ShouldBeTrue(nameof(graphPath));
        }

        private void PackShouldHaveIdxFile(string pathPath)
        {
            string idxPath = Path.ChangeExtension(pathPath, ".idx");
            idxPath.ShouldBeAFile(this.fileSystem).WithContents().Length.ShouldBeAtLeast(1, $"{idxPath} is unexepectedly empty");
        }

        private void AllPrefetchPacksShouldHaveIdx(string[] prefetchPacks)
        {
            prefetchPacks.Length.ShouldBeAtLeast(1, "There should be at least one prefetch pack");

            foreach (string prefetchPack in prefetchPacks)
            {
                this.PackShouldHaveIdxFile(prefetchPack);
            }
        }

        private string[] ReadPrefetchPackFileNames()
        {
            return Directory.GetFiles(this.PackRoot, $"{PrefetchPackPrefix}*.pack");
        }

        private long GetTimestamp(string preFetchPackName)
        {
            string filename = Path.GetFileName(preFetchPackName);
            filename.StartsWith(PrefetchPackPrefix).ShouldBeTrue($"'{preFetchPackName}' does not start with '{PrefetchPackPrefix}'");

            string[] parts = filename.Split('-');
            long parsed;

            parts.Length.ShouldBeAtLeast(1, $"'{preFetchPackName}' has less parts ({parts.Length}) than expected (1)");
            long.TryParse(parts[1], out parsed).ShouldBeTrue($"Failed to parse long from '{parts[1]}'");
            return parsed;
        }

        private long GetMostRecentPackTimestamp(string[] prefetchPacks)
        {
            prefetchPacks.Length.ShouldBeAtLeast(1, "prefetchPacks should have at least one item");

            long mostRecentPackTimestamp = -1;
            foreach (string prefetchPack in prefetchPacks)
            {
                long timestamp = this.GetTimestamp(prefetchPack);
                if (timestamp > mostRecentPackTimestamp)
                {
                    mostRecentPackTimestamp = timestamp;
                }
            }

            mostRecentPackTimestamp.ShouldBeAtLeast(1, "Failed to find the most recent pack");
            return mostRecentPackTimestamp;
        }

        private long GetOldestPackTimestamp(string[] prefetchPacks)
        {
            prefetchPacks.Length.ShouldBeAtLeast(1, "prefetchPacks should have at least one item");

            long oldestPackTimestamp = long.MaxValue;
            foreach (string prefetchPack in prefetchPacks)
            {
                long timestamp = this.GetTimestamp(prefetchPack);
                if (timestamp < oldestPackTimestamp)
                {
                    oldestPackTimestamp = timestamp;
                }
            }

            oldestPackTimestamp.ShouldBeAtMost(long.MaxValue - 1, "Failed to find the oldest pack");
            return oldestPackTimestamp;
        }

        private void PostFetchJobShouldComplete()
        {
            string objectDir = this.Enlistment.GetObjectRoot(this.fileSystem);
            string postFetchLock = Path.Combine(objectDir, "git-maintenance-step.lock");

            while (this.fileSystem.FileExists(postFetchLock))
            {
                Thread.Sleep(500);
            }

            ProcessResult graphResult = GitProcess.InvokeProcess(this.Enlistment.RepoRoot, "commit-graph verify --shallow --object-dir=\"" + objectDir + "\"");
            graphResult.ExitCode.ShouldEqual(0);
        }
    }
}
