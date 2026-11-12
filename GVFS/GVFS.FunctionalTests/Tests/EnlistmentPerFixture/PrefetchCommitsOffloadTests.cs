using GVFS.FunctionalTests.FileSystemRunners;
using GVFS.FunctionalTests.Should;
using GVFS.FunctionalTests.Tools;
using GVFS.Tests.Should;
using NUnit.Framework;
using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;

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

        [TestCase("git-maintenance-step.lock")]
        [TestCase("prefetch-commits-trees.lock")]
        [Order(6)]
        public void PrefetchCommitsFallsBackWhileCacheOrPrefetchIsBusy(string lockName)
        {
            string lockRoot = lockName == "git-maintenance-step.lock"
                ? this.Enlistment.GetObjectRoot(this.fileSystem)
                : this.PackRoot;
            string lockPath = Path.Combine(lockRoot, lockName);
            using (ManualResetEventSlim declined = new ManualResetEventSlim())
            using (Process prefetch = new Process
            {
                StartInfo = new ProcessStartInfo(GVFSTestConfig.PathToGVFS, "prefetch --commits --verbose")
                {
                    UseShellExecute = false,
                    WorkingDirectory = this.Enlistment.EnlistmentRoot,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                },
            })
            {
                StringBuilder output = new StringBuilder();
                prefetch.OutputDataReceived += (_, args) =>
                {
                    if (args.Data != null)
                    {
                        output.AppendLine(args.Data);
                        if (args.Data.Contains("Mount not ready, falling back to direct prefetch"))
                        {
                            declined.Set();
                        }
                    }
                };
                FileStream heldLock = new FileStream(lockPath, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None);
                try
                {
                    prefetch.Start().ShouldEqual(true);
                    prefetch.BeginOutputReadLine();
                    prefetch.BeginErrorReadLine();
                    Assert.That(declined.Wait(TimeSpan.FromSeconds(30)), Is.True, "Offload did not decline while the lock was held.");
                }
                finally
                {
                    heldLock.Dispose();
                    File.Delete(lockPath);
                    if (!prefetch.WaitForExit(60000))
                    {
                        prefetch.Kill(entireProcessTree: true);
                        prefetch.WaitForExit();
                        Assert.Fail("Direct prefetch did not finish after lock release.");
                    }

                    prefetch.WaitForExit();
                }

                prefetch.ExitCode.ShouldEqual(0, output.ToString());
            }

            this.PostFetchJobShouldComplete();
        }

        [TestCase, Order(7)]
        public void PrefetchCommitsWithOnlyIncompletePacksRunsDirectly()
        {
            string[] packs = this.ReadPrefetchPackFileNames();
            packs.Length.ShouldBeAtLeast(1);
            try
            {
                foreach (string pack in packs)
                {
                    File.WriteAllText(Path.ChangeExtension(pack, ".incomplete"), string.Empty);
                }

                string output = this.Enlistment.Prefetch("--commits --verbose");
                output.ShouldContain("Mount not ready, falling back to direct prefetch");
                this.PostFetchJobShouldComplete();
                this.AllPrefetchPacksShouldHaveIdx(this.ReadPrefetchPackFileNames());
            }
            finally
            {
                foreach (string pack in packs)
                {
                    File.Delete(Path.ChangeExtension(pack, ".incomplete"));
                }
            }
        }

        [TestCase, Order(8)]
        public void PrefetchCommitsWithAnUnindexedOlderPackRunsDirectly()
        {
            this.ReadPrefetchPackFileNames().Length.ShouldBeAtLeast(1);
            string badPack = Path.Combine(this.PackRoot, "prefetch-1-" + Guid.NewGuid().ToString("N") + ".pack");
            try
            {
                File.WriteAllText(badPack, "BADPACK");
                string output = this.Enlistment.Prefetch("--commits --verbose");
                output.ShouldContain("Mount not ready, falling back to direct prefetch");
                this.PostFetchJobShouldComplete();
                Assert.That(File.Exists(badPack), Is.False);
                this.AllPrefetchPacksShouldHaveIdx(this.ReadPrefetchPackFileNames());
            }
            finally
            {
                File.Delete(badPack);
                File.Delete(Path.ChangeExtension(badPack, ".idx"));
            }
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
