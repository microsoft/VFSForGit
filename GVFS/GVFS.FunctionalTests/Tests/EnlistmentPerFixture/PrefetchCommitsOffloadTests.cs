using GVFS.FunctionalTests.FileSystemRunners;
using GVFS.FunctionalTests.Should;
using GVFS.FunctionalTests.Tools;
using GVFS.Tests.Should;
using NUnit.Framework;
using System.IO;

namespace GVFS.FunctionalTests.Tests.EnlistmentPerFixture
{
    [TestFixture]
    public class PrefetchCommitsOffloadTests : TestsWithEnlistmentPerFixture
    {
        private const string PrefetchPackPrefix = "prefetch";

        private FileSystemRunner fileSystem;

        public PrefetchCommitsOffloadTests()
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

        [TestCase, Order(1)]
        public void PrefetchCommitsMountedUsesOffload()
        {
            // With the enlistment mounted, prefetch --commits should succeed
            // by offloading to the mount process (using its warm auth).
            this.Enlistment.Prefetch("--commits");
            this.PostFetchJobShouldComplete();

            string[] prefetchPacks = this.ReadPrefetchPackFileNames();
            prefetchPacks.Length.ShouldBeAtLeast(1, "There should be at least one prefetch pack after mounted prefetch");
            this.AllPrefetchPacksShouldHaveIdx(prefetchPacks);
        }

        [TestCase, Order(2)]
        public void PrefetchCommitsMountedIsIdempotent()
        {
            // Running prefetch --commits again while mounted should succeed
            // (may be a no-op if packs are already up to date).
            string[] packsBefore = this.ReadPrefetchPackFileNames();

            this.Enlistment.Prefetch("--commits");
            this.PostFetchJobShouldComplete();

            string[] packsAfter = this.ReadPrefetchPackFileNames();
            packsAfter.Length.ShouldBeAtLeast(packsBefore.Length, "Pack count should not decrease after idempotent prefetch");
            this.AllPrefetchPacksShouldHaveIdx(packsAfter);
        }

        [TestCase, Order(3)]
        public void PrefetchCommitsUnmountedFallsBackToDirectAuth()
        {
            // Unmount, then prefetch --commits should fall back to direct auth
            // and still succeed.
            this.Enlistment.UnmountGVFS();

            try
            {
                this.Enlistment.Prefetch("--commits");

                string[] prefetchPacks = this.ReadPrefetchPackFileNames();
                prefetchPacks.Length.ShouldBeAtLeast(1, "There should be at least one prefetch pack after unmounted prefetch");
                this.AllPrefetchPacksShouldHaveIdx(prefetchPacks);
            }
            finally
            {
                this.Enlistment.MountGVFS();
            }
        }

        [TestCase, Order(4)]
        public void PrefetchCommitsMountedAfterRemount()
        {
            // After unmount + remount, prefetch --commits should work via
            // the mount process again.
            this.Enlistment.UnmountGVFS();
            this.Enlistment.MountGVFS();

            this.Enlistment.Prefetch("--commits");
            this.PostFetchJobShouldComplete();

            string[] prefetchPacks = this.ReadPrefetchPackFileNames();
            prefetchPacks.Length.ShouldBeAtLeast(1, "There should be at least one prefetch pack after remount prefetch");
            this.AllPrefetchPacksShouldHaveIdx(prefetchPacks);
        }

        private string[] ReadPrefetchPackFileNames()
        {
            return Directory.GetFiles(this.PackRoot, $"{PrefetchPackPrefix}*.pack");
        }

        private void AllPrefetchPacksShouldHaveIdx(string[] prefetchPacks)
        {
            foreach (string prefetchPack in prefetchPacks)
            {
                string idxPath = Path.ChangeExtension(prefetchPack, ".idx");
                idxPath.ShouldBeAFile(this.fileSystem);
            }
        }

        private void PostFetchJobShouldComplete()
        {
            string objectDir = this.Enlistment.GetObjectRoot(this.fileSystem);
            string postFetchLock = Path.Combine(objectDir, "git-maintenance-step.lock");

            System.Diagnostics.Stopwatch timeout = System.Diagnostics.Stopwatch.StartNew();
            while (this.fileSystem.FileExists(postFetchLock))
            {
                timeout.Elapsed.TotalSeconds.ShouldBeAtMost(60, "Post-fetch lock file was not released within 60 seconds");
                System.Threading.Thread.Sleep(500);
            }

            ProcessResult graphResult = GitProcess.InvokeProcess(
                this.Enlistment.RepoRoot,
                "commit-graph verify --shallow --object-dir=\"" + objectDir + "\"");
            graphResult.ExitCode.ShouldEqual(0);
        }
    }
}
