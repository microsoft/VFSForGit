using GVFS.Common;
using GVFS.Common.Git;
using GVFS.Common.Http;
using GVFS.Common.Tracing;
using GVFS.Tests.Should;
using GVFS.UnitTests.Category;
using GVFS.UnitTests.Mock.Common;
using GVFS.UnitTests.Mock.FileSystem;
using GVFS.UnitTests.Mock.Git;
using NUnit.Framework;
using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace GVFS.FunctionalTests.Tests.LocalComponents
{
    [TestFixture]
    [NonParallelizable]
    public class GVFSGitObjectsTests
    {
        private readonly byte[] validTestObjectFileContents = new byte[]
        {
            0x78, 0x01, 0x4B, 0xCA, 0xC9, 0x4F, 0x52, 0x30, 0x62,
            0x48, 0xE4, 0x02, 0x00, 0x0E, 0x64, 0x02, 0x5D
        };

        [SetUp]
        public void SetUp()
        {
            RetryCircuitBreaker.Reset();
        }

        [TearDown]
        public void TearDown()
        {
            RetryCircuitBreaker.Reset();
        }

        [TestCase(false, false)]
        [TestCase(true, false)]
        [TestCase(false, true)]
        [Category(CategoryConstants.ExceptionExpected)]
        public async Task CoalescedWaiterDoesNotInheritTheDownloadOwnersCancellation(bool namedPipeWaiter, bool cancelWaiter)
        {
            const string ObjectSha = "78981922613b2afb6025042ff6bd878ac1994e85";
            using CancellationTokenSource ownerCancellation = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            using CancellationTokenSource waiterCancellation = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            using CancellationTokenSource serverCancellation = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            using ManualResetEventSlim bodyStarted = new ManualResetEventSlim();
            using CoalescedDownloadTracer tracer = new CoalescedDownloadTracer();
            using ManualResetEventSlim waiterJoined = tracer.WaiterJoined;
            using TcpListener listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            string endpoint = $"http://127.0.0.1:{((IPEndPoint)listener.LocalEndpoint).Port}";
            MockGVFSEnlistment enlistment = new MockGVFSEnlistment(
                "mock:\\src", endpoint, "mock:\\git", new MockGitProcess());
            enlistment.InitializeCachePathsFromKey("mock:\\.gvfs", "mock:\\.gvfs\\gitObjectCache");
            using GitObjectsHttpRequestor requestor = new GitObjectsHttpRequestor(
                tracer, enlistment, new CacheServerInfo(endpoint, "loopback"),
                new RetryConfig(0, TimeSpan.FromSeconds(5)));
            int objectAvailable = 0;
            MockFileSystemWithCallbacks fileSystem = new MockFileSystemWithCallbacks
            {
                OnFileExists = path => false,
                OnMoveFile = (source, target) => Volatile.Write(ref objectAvailable, 1),
                OnOpenFileStream = (path, mode, access) => access == FileAccess.Read
                    ? new MemoryStream(this.validTestObjectFileContents)
                    : new BodyReadMemoryStream(bodyStarted),
            };
            using GitRepo repository = new DownloadedBlobRepo(tracer, () => Volatile.Read(ref objectAvailable) != 0);
            GVFSGitObjects objects = new GVFSGitObjects(
                new GVFSContext(tracer, fileSystem, repository, enlistment), requestor);
            int requests = 0;
            Task server = this.ServeCanceledThenSuccessfulObject(
                listener, ObjectSha, () => Interlocked.Increment(ref requests), serverCancellation.Token);
            Task<bool> owner = null;
            Task<bool> waiter = null;
            try
            {
                owner = Task.Run(() => objects.TryCopyBlobContentStream(
                    ObjectSha, ownerCancellation.Token, GVFSGitObjects.RequestSource.FileStreamCallback,
                    (stream, length) => Assert.Fail("Canceled owner must not copy the blob."), out _));
                bodyStarted.Wait(TimeSpan.FromSeconds(5)).ShouldEqual(true);
                waiter = Task.Run(() =>
                {
                    if (namedPipeWaiter)
                    {
                        return objects.TryDownloadAndSaveObject(
                            ObjectSha, GVFSGitObjects.RequestSource.NamedPipeMessage)
                            == GitObjects.DownloadAndSaveObjectResult.Success;
                    }

                    return objects.TryCopyBlobContentStream(
                        ObjectSha, waiterCancellation.Token, GVFSGitObjects.RequestSource.FileStreamCallback,
                        (stream, length) =>
                        {
                            length.ShouldEqual(2L);
                            stream.ReadByte().ShouldEqual((int)'a');
                        }, out _);
                });
                tracer.WaiterJoined.Wait(TimeSpan.FromSeconds(5)).ShouldEqual(true);
                if (cancelWaiter)
                {
                    waiterCancellation.Cancel();
                }

                ownerCancellation.Cancel();
                Assert.CatchAsync<OperationCanceledException>(async () => await owner.WaitAsync(TimeSpan.FromSeconds(5)));
                if (cancelWaiter)
                {
                    Assert.CatchAsync<OperationCanceledException>(async () => await waiter.WaitAsync(TimeSpan.FromSeconds(5)));
                }
                else
                {
                    (await waiter.WaitAsync(TimeSpan.FromSeconds(5))).ShouldEqual(true);
                }

                requests.ShouldEqual(cancelWaiter ? 1 : 2);
                objects.inflightDownloads.Count.ShouldEqual(0);
                RetryCircuitBreaker.ConsecutiveFailures.ShouldEqual(0);
                tracer.SnapshotRelatedEvents().Count(
                    record => record.Name == "TryDownloadAndSaveObject_CoalescedCancellation")
                    .ShouldEqual(cancelWaiter ? 0 : 1);
            }
            finally
            {
                ownerCancellation.Cancel();
                waiterCancellation.Cancel();
                serverCancellation.Cancel();
                listener.Stop();
                await Task.WhenAll(
                    ObserveCanceledDownloadTask(owner ?? Task.CompletedTask),
                    ObserveCanceledDownloadTask(waiter ?? Task.CompletedTask),
                    ObserveCanceledDownloadTask(server));
            }
        }

        private async Task ServeCanceledThenSuccessfulObject(
            TcpListener listener, string objectSha, Action onRequest, CancellationToken cancellationToken)
        {
            for (int attempt = 0; attempt < 2; ++attempt)
            {
                using TcpClient client = await listener.AcceptTcpClientAsync(cancellationToken);
                using NetworkStream stream = client.GetStream();
                using (StreamReader reader = new StreamReader(stream, Encoding.ASCII, false, 1024, leaveOpen: true))
                {
                    string request = await reader.ReadLineAsync(cancellationToken);
                    request.ShouldContain(objectSha);
                    string line;
                    do
                    {
                        line = await reader.ReadLineAsync(cancellationToken);
                    }
                    while (!string.IsNullOrEmpty(line));
                }

                onRequest();
                byte[] headers = Encoding.ASCII.GetBytes(
                    $"HTTP/1.1 200 OK\r\nConnection: close\r\nContent-Type: application/x-git-loose-object\r\nContent-Length: {this.validTestObjectFileContents.Length}\r\n\r\n");
                await stream.WriteAsync(headers.AsMemory(), cancellationToken);
                if (attempt == 0)
                {
                    await stream.WriteAsync(this.validTestObjectFileContents.AsMemory(0, 8), cancellationToken);
                    try
                    {
                        (await stream.ReadAsync(new byte[1].AsMemory(), cancellationToken)).ShouldEqual(0);
                    }
                    catch (IOException e) when (e.InnerException is SocketException socketError &&
                        socketError.SocketErrorCode == SocketError.ConnectionReset)
                    {
                        // A canceled HTTP response can close the socket with a reset.
                    }
                }
                else
                {
                    await stream.WriteAsync(this.validTestObjectFileContents.AsMemory(), cancellationToken);
                }
            }
        }

        private static async Task ObserveCanceledDownloadTask(Task task)
        {
            try
            {
                await task.WaitAsync(TimeSpan.FromSeconds(5));
            }
            catch (OperationCanceledException)
            {
            }
        }

        private sealed class CoalescedDownloadTracer : MockTracer
        {
            public ManualResetEventSlim WaiterJoined { get; } = new ManualResetEventSlim();

            public override void RelatedEvent(EventLevel level, string name, EventMetadata metadata)
            {
                base.RelatedEvent(level, name, metadata);
                if (name == "TryDownloadAndSaveObject_CoalescedRequest")
                {
                    this.WaiterJoined.Set();
                }
            }
        }

        private sealed class BodyReadMemoryStream : MemoryStream
        {
            private readonly ManualResetEventSlim bodyStarted;

            public BodyReadMemoryStream(ManualResetEventSlim bodyStarted)
            {
                this.bodyStarted = bodyStarted;
            }

            public override void Write(byte[] buffer, int offset, int count)
            {
                base.Write(buffer, offset, count);
                if (count > 0)
                {
                    this.bodyStarted.Set();
                }
            }
        }

        private sealed class DownloadedBlobRepo : GitRepo
        {
            private readonly Func<bool> objectAvailable;

            public DownloadedBlobRepo(ITracer tracer, Func<bool> objectAvailable)
                : base(tracer)
            {
                this.objectAvailable = objectAvailable;
            }

            public override bool TryCopyBlobContentStream(string blobSha, Action<Stream, long> writeAction)
            {
                if (!this.objectAvailable())
                {
                    return false;
                }

                using MemoryStream stream = new MemoryStream(new byte[] { (byte)'a', (byte)'\n' });
                writeAction(stream, stream.Length);
                return true;
            }
        }
    }
}
