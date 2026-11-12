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

        [SetUp]
        public void EnablePrefetchOffload()
        {
            GitProcess.InvokeProcess(this.Enlistment.RepoRoot, "config --local gvfs.prefetch-offload true")
                .ExitCode.ShouldEqual(0);
        }

        [TestCase, Order(1)]
        public void PrefetchCommitsMountedUsesOffload()
        {
            // With the enlistment mounted, prefetch --commits should succeed
            // by offloading to the mount process (using its warm auth).
            this.Enlistment.Prefetch("--commits").ShouldContain("Fetching commits and trees...Succeeded");
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

            this.Enlistment.Prefetch("--commits").ShouldContain("Fetching commits and trees...Succeeded");
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

            this.Enlistment.Prefetch("--commits").ShouldContain("Fetching commits and trees...Succeeded");
            this.PostFetchJobShouldComplete();

            string[] prefetchPacks = this.ReadPrefetchPackFileNames();
            prefetchPacks.Length.ShouldBeAtLeast(1, "There should be at least one prefetch pack after remount prefetch");
            this.AllPrefetchPacksShouldHaveIdx(prefetchPacks);
        }

        [TestCase(false)]
        [TestCase(true)]
        [Order(5)]
        [Timeout(60000)]
        public void PrefetchCommitsDoesNotAnimateRedirectedOutput(bool offload)
        {
            GitProcess.InvokeProcess(this.Enlistment.RepoRoot, $"config --local gvfs.prefetch-offload {offload.ToString().ToLowerInvariant()}")
                .ExitCode.ShouldEqual(0);
            string output = this.Enlistment.Prefetch("--commits");
            string source = offload ? string.Empty : @" from (?:cache server|origin \(no cache server\))";
            Assert.That(Regex.IsMatch(output, @"^Fetching commits and trees" + source + @"\.\.\.Succeeded\r?$", RegexOptions.Multiline), Is.True, output);
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

        [TestCase(false)]
        [TestCase(true)]
        [Order(7)]
        [Timeout(120000)]
        public async Task UnmountWaitsForAcceptedPrefetchAndKeepsMountAliveOnTimeout(bool waitForTimeout)
        {
            this.Enlistment.Prefetch("--commits");
            string pipeName = "GVFS_" + this.Enlistment.EnlistmentRoot.ToUpper().Replace(':', '_');
            using CancellationTokenSource cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(100));
            using NamedPipeClientStream prefetch = new NamedPipeClientStream(
                ".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
            using NamedPipeClientStream unmount = new NamedPipeClientStream(
                ".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
            using FileStream prefetchLock = new FileStream(
                Path.Combine(this.PackRoot, "prefetch-commits-trees.lock"),
                FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            bool unmountStarted = false;
            bool unmountCompleted = false;
            bool unmountTerminalReceived = false;
            bool prefetchCompleted = false;
            try
            {
                await prefetch.ConnectAsync(10000, cancellation.Token);
                await prefetch.WriteAsync(Encoding.UTF8.GetBytes("PrefetchCommits|ReportProgress\x03"), cancellation.Token);
                Assert.That(await ReadPrefetchResponse(prefetch, cancellation.Token), Is.EqualTo("PrefetchCommitsStarted"));
                Assert.That(await ReadPrefetchResponse(prefetch, cancellation.Token), Does.StartWith("PrefetchCommitsProgress|"));
                await unmount.ConnectAsync(10000, cancellation.Token);
                await unmount.WriteAsync(Encoding.UTF8.GetBytes("Unmount|ReportProgress\x03"), cancellation.Token);
                unmountStarted = true;
                Assert.That(await ReadPrefetchResponse(unmount, cancellation.Token), Is.EqualTo("Ack"));
                string waiting = await ReadPrefetchResponse(unmount, cancellation.Token);
                Assert.That(waiting, Does.StartWith("UnmountWaiting|").And.Contain("commit prefetch").And.Contain($"requester PID {Environment.ProcessId}"));

                using (NamedPipeClientStream rejected = new NamedPipeClientStream(
                    ".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous))
                {
                    await rejected.ConnectAsync(10000, cancellation.Token);
                    await rejected.WriteAsync(Encoding.UTF8.GetBytes("PrefetchCommits\x03"), cancellation.Token);
                    Assert.That(await ReadPrefetchResponse(rejected, cancellation.Token), Is.EqualTo("MountNotReady"));
                }

                if (waitForTimeout)
                {
                    string timeout = await ReadPrefetchResponse(unmount, cancellation.Token);
                    unmountTerminalReceived = true;
                    Assert.That(timeout, Does.StartWith("UnmountTimedOut|").And.Contain("30 seconds").And.Contain("stays mounted").And.Contain($"requester PID {Environment.ProcessId}"));
                    Assert.That(this.Enlistment.IsMounted(), Is.True);
                }

                prefetchLock.Dispose();
                string response;
                do
                {
                    response = await ReadPrefetchResponse(prefetch, cancellation.Token);
                }
                while (response.StartsWith("PrefetchCommitsProgress|", StringComparison.Ordinal));

                prefetchCompleted = true;
                Assert.That(response, Does.StartWith("PrefetchCommitsComplete|"));
                using JsonDocument result = JsonDocument.Parse(response.Substring("PrefetchCommitsComplete|".Length));
                Assert.That(result.RootElement.GetProperty("Success").GetBoolean(), Is.True, response);
                if (waitForTimeout)
                {
                    this.Enlistment.UnmountGVFS();
                }
                else
                {
                    string completion = await ReadPrefetchResponse(unmount, cancellation.Token);
                    unmountTerminalReceived = true;
                    unmountCompleted = completion == "Complete";
                    Assert.That(completion, Is.EqualTo("Complete"));
                }

                unmountCompleted = true;
            }
            finally
            {
                prefetchLock.Dispose();
                // Release the paused operation even when an assertion fails.
                if (!prefetchCompleted && prefetch.IsConnected)
                {
                    string response;
                    do
                    {
                        response = await ReadPrefetchResponse(prefetch, cancellation.Token);
                    }
                    while (response.StartsWith("PrefetchCommitsProgress|", StringComparison.Ordinal));
                }

                if (unmountStarted && !unmountTerminalReceived && unmount.IsConnected)
                {
                    string response;
                    do
                    {
                        response = await ReadPrefetchResponse(unmount, cancellation.Token);
                    }
                    while (response.StartsWith("UnmountWaiting|", StringComparison.Ordinal));
                    unmountCompleted = response == "Complete";
                }

                if (unmountCompleted)
                {
                    Assert.That(SpinWait.SpinUntil(() => !this.Enlistment.IsMounted(), TimeSpan.FromSeconds(10)), Is.True);
                    this.Enlistment.MountGVFS();
                }
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
