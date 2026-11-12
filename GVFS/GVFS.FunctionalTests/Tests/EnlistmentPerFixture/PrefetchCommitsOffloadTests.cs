using GVFS.Common.NamedPipes;
using GVFS.FunctionalTests.FileSystemRunners;
using GVFS.FunctionalTests.Should;
using GVFS.FunctionalTests.Tools;
using GVFS.Tests.Should;
using NUnit.Framework;
using System.IO;
using GVFSPlatform = GVFS.Common.GVFSPlatform;

namespace GVFS.FunctionalTests.Tests.EnlistmentPerFixture
{
    [TestFixture]
    [NonParallelizable]
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

        [SetUp]
        public void EnablePrefetchOffload()
        {
            GitProcess.InvokeProcess(this.Enlistment.RepoRoot, "config --local gvfs.prefetch-offload true")
                .ExitCode.ShouldEqual(0);
            GitProcess.InvokeProcess(this.Enlistment.RepoRoot, "config --local gvfs.trust-pack-indexes false")
                .ExitCode.ShouldEqual(0);
        }

        [TestCase, Order(1)]
        public void PrefetchCommitsMountedWithEmptyCacheRunsDirectly()
        {
            this.ReadPrefetchPackFileNames().Length.ShouldEqual(0);
            string pipeName = GVFSPlatform.Instance.GetNamedPipeName(this.Enlistment.EnlistmentRoot);
            using (NamedPipeClient client = new NamedPipeClient(pipeName))
            {
                client.Connect().ShouldEqual(true);
                client.SendRequest(new NamedPipeMessages.Message(NamedPipeMessages.PrefetchCommits.Request, null));
                client.ReadResponse().Header.ShouldEqual(NamedPipeMessages.PrefetchCommits.MountNotReadyResult);
            }

            this.ReadPrefetchPackFileNames().Length.ShouldEqual(0);
            string output = this.Enlistment.Prefetch("--commits --verbose");
            output.ShouldContain("Mount not ready, falling back to direct prefetch");
            this.PostFetchJobShouldComplete();

            string[] prefetchPacks = this.ReadPrefetchPackFileNames();
            prefetchPacks.Length.ShouldBeAtLeast(1, "There should be at least one prefetch pack after mounted prefetch");
            this.AllPrefetchPacksShouldHaveIdx(prefetchPacks);
        }

        [TestCase, Order(2)]
        public void PrefetchCommitsMountedWithCompletedCacheUsesOffload()
        {
            string[] packsBefore = this.ReadPrefetchPackFileNames();
            packsBefore.Length.ShouldBeAtLeast(1);

            string output = this.Enlistment.Prefetch("--commits --verbose");
            output.ShouldContain("Mount completed prefetch successfully");
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

            string output = this.Enlistment.Prefetch("--commits --verbose");
            output.ShouldContain("Mount completed prefetch successfully");
            this.PostFetchJobShouldComplete();

            string[] prefetchPacks = this.ReadPrefetchPackFileNames();
            prefetchPacks.Length.ShouldBeAtLeast(1, "There should be at least one prefetch pack after remount prefetch");
            this.AllPrefetchPacksShouldHaveIdx(prefetchPacks);
        }

        [TestCase, Order(5)]
        public void PrefetchCommitsWithOffloadDisabledRunsDirectly()
        {
            this.ReadPrefetchPackFileNames().Length.ShouldBeAtLeast(1);
            GitProcess.InvokeProcess(this.Enlistment.RepoRoot, "config --local gvfs.prefetch-offload false")
                .ExitCode.ShouldEqual(0);

            string output = this.Enlistment.Prefetch("--commits --verbose");
            Assert.That(output, Does.Not.Contain("TryPrefetchCommitsViaMountProcess"));
            output.ShouldContain("TryDownloadPrefetchPacks");
            this.PostFetchJobShouldComplete();
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
