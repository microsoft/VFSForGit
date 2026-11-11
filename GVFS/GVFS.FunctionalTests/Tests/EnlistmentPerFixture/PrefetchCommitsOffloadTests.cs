using GVFS.FunctionalTests.FileSystemRunners;
using GVFS.FunctionalTests.Should;
using GVFS.FunctionalTests.Tools;
using GVFS.Tests.Should;
using NUnit.Framework;
using System;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

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

        [TestCase]
        [Order(5)]
        [Timeout(60000)]
        public void PrefetchCommitsDoesNotAnimateRedirectedOutput()
        {
            string output = this.Enlistment.Prefetch("--commits");
            Assert.That(output, Does.Contain("Fetching commits and trees...Succeeded"));
            Assert.That(Regex.Matches(output, "Fetching commits and trees").Count, Is.EqualTo(1));
            Assert.That(Regex.IsMatch(output, @"\r(?!\n)"), Is.False);
        }

        [TestCase(false)]
        [TestCase(true)]
        [Order(6)]
        [Timeout(60000)]
        public async Task MountPrefetchProtocolSupportsLegacyAndProgressClients(bool reportProgress)
        {
            string pipeName = "GVFS_" + this.Enlistment.EnlistmentRoot.ToUpper().Replace(':', '_');
            using CancellationTokenSource cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(45));
            using NamedPipeClientStream client = new NamedPipeClientStream(
                ".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
            await client.ConnectAsync(10000, cancellation.Token);
            string request = reportProgress ? "PrefetchCommits|ReportProgress" : "PrefetchCommits";
            await client.WriteAsync(Encoding.UTF8.GetBytes(request + "\x03"), cancellation.Token);
            string response = await ReadPrefetchResponse(client, cancellation.Token);
            if (reportProgress)
            {
                Assert.That(response, Is.EqualTo("PrefetchCommitsStarted"));
                while (true)
                {
                    response = await ReadPrefetchResponse(client, cancellation.Token);
                    if (!response.StartsWith("PrefetchCommitsProgress|", StringComparison.Ordinal))
                    {
                        break;
                    }

                    Assert.That(response, Does.Contain("elapsed "));
                    Assert.That(Regex.IsMatch(response, @"\r|\n"), Is.False);
                }
            }

            const string CompleteHeader = "PrefetchCommitsComplete|";
            Assert.That(response, Does.StartWith(CompleteHeader));
            using JsonDocument result = JsonDocument.Parse(response.Substring(CompleteHeader.Length));
            Assert.That(result.RootElement.GetProperty("Success").GetBoolean(), Is.True, response);
        }

        private static async Task<string> ReadPrefetchResponse(NamedPipeClientStream client, CancellationToken cancellationToken)
        {
            using MemoryStream frame = new MemoryStream();
            byte[] next = new byte[1];
            while (await client.ReadAsync(next.AsMemory(), cancellationToken) != 0)
            {
                if (next[0] == 3)
                {
                    return Encoding.UTF8.GetString(frame.ToArray());
                }

                frame.WriteByte(next[0]);
                if (frame.Length > 4096)
                {
                    throw new InvalidDataException("Prefetch response exceeded the test frame limit");
                }
            }

            throw new EndOfStreamException("Mount closed the connection before the prefetch response ended");
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
