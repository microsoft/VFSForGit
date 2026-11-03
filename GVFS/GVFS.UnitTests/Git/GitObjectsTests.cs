using GVFS.Common;
using GVFS.Common.Git;
using GVFS.Common.Http;
using GVFS.Common.Tracing;
using GVFS.Tests.Should;
using GVFS.UnitTests.Category;
using GVFS.UnitTests.Mock.Common;
using GVFS.UnitTests.Mock.FileSystem;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security;
using System.Threading;
using System.Threading.Tasks;
using System.Linq;

namespace GVFS.UnitTests.Git
{
    [TestFixture]
    public class GitObjectsTests
    {
        private const string EmptySha = "0000000000000000000000000000000000000000";
        private const string RealSha = "78981922613b2afb6025042ff6bd878ac1994e85";
        private readonly byte[] realData = new byte[]
        {
            0x78, 0x01, 0x4B, 0xCA, 0xC9, 0x4F, 0x52, 0x30, 0x62,
            0x48, 0xE4, 0x02, 0x00, 0x0E, 0x64, 0x02, 0x5D
        };
        private readonly List<string> openedPaths = new List<string>();
        private readonly Dictionary<string, MemoryStream> pathsToData = new Dictionary<string, MemoryStream>();

        [TestCase]
        public void WriteLooseObject_DetectsDataNotCompressed()
        {
            ITracer tracer = new MockTracer();
            GVFSEnlistment enlistment = new MockGVFSEnlistment();
            MockFileSystemWithCallbacks filesystem = new MockFileSystemWithCallbacks();
            GVFSContext context = new GVFSContext(tracer, filesystem, null, enlistment);

            GitObjects gitObjects = new GVFSGitObjects(context, null);

            this.openedPaths.Clear();
            filesystem.OnOpenFileStream = this.OnOpenFileStream;
            filesystem.OnFileExists = this.OnFileExists;

            bool foundException = false;

            try
            {
                using (Stream stream = new MemoryStream())
                {
                    stream.Write(new byte[] { 0, 1, 2, 3, 4 }, 0, 5);
                    stream.Position = 0;
                    gitObjects.WriteLooseObject(stream, EmptySha, true, new byte[128]);
                }
            }
            catch (RetryableException ex)
            {
                foundException = true;
                ex.Message.ShouldContain($"Requested object with hash {EmptySha} but received data that failed decompression.");
            }

            foundException.ShouldBeTrue("Failed to throw RetryableException");
            this.openedPaths.Count.ShouldEqual(1, "Incorrect number of opened paths (one to write temp file)");
            this.openedPaths[0].IndexOf(EmptySha.Substring(2)).ShouldBeAtMost(-1, "Should not have written to the loose object location");
        }

        [TestCase]
        public void WriteLooseObject_DetectsIncorrectData()
        {
            ITracer tracer = new MockTracer();
            GVFSEnlistment enlistment = new MockGVFSEnlistment();
            MockFileSystemWithCallbacks filesystem = new MockFileSystemWithCallbacks();
            GVFSContext context = new GVFSContext(tracer, filesystem, null, enlistment);

            GitObjects gitObjects = new GVFSGitObjects(context, null);

            this.openedPaths.Clear();
            filesystem.OnOpenFileStream = this.OnOpenFileStream;
            filesystem.OnFileExists = this.OnFileExists;

            bool foundException = false;

            try
            {
                using (Stream stream = new MemoryStream())
                {
                    stream.Write(this.realData, 0, this.realData.Length);
                    stream.Position = 0;
                    gitObjects.WriteLooseObject(stream, EmptySha, true, new byte[128]);
                }
            }
            catch (SecurityException ex)
            {
                foundException = true;
                ex.Message.ShouldContain($"Requested object with hash {EmptySha} but received object with hash");
            }

            foundException.ShouldBeTrue("Failed to throw SecurityException");
            this.openedPaths.Count.ShouldEqual(1, "Incorrect number of opened paths (one to write temp file)");
            this.openedPaths[0].IndexOf(EmptySha.Substring(2)).ShouldBeAtMost(-1, "Should not have written to the loose object location");
        }

        [TestCase]
        public void WriteLooseObject_Success()
        {
            ITracer tracer = new MockTracer();
            GVFSEnlistment enlistment = new MockGVFSEnlistment();
            MockFileSystemWithCallbacks filesystem = new MockFileSystemWithCallbacks();
            GVFSContext context = new GVFSContext(tracer, filesystem, null, enlistment);

            GitObjects gitObjects = new GVFSGitObjects(context, null);

            this.openedPaths.Clear();
            filesystem.OnOpenFileStream = this.OnOpenFileStream;
            filesystem.OnFileExists = this.OnFileExists;

            bool moved = false;
            filesystem.OnMoveFile = (path1, path2) => { moved = true; };

            using (Stream stream = new MemoryStream())
            {
                stream.Write(this.realData, 0, this.realData.Length);
                stream.Position = 0;
                gitObjects.WriteLooseObject(stream, RealSha, true, new byte[128]);
            }

            this.openedPaths.Count.ShouldEqual(2, "Incorrect number of opened paths");
            moved.ShouldBeTrue("File was not moved");
        }

        [TestCase]
        public void PrefetchFailureTelemetryReportsOnlyRequestAuthority()
        {
            const string RequestUrl = "https://user:secret@cache.example:8443/gvfs/prefetch?token=sensitive";
            MockTracer tracer = new MockTracer();
            MockGVFSEnlistment enlistment = new MockGVFSEnlistment();
            TestPrefetchRequestor requestor = new TestPrefetchRequestor(
                tracer,
                enlistment,
                HttpStatusCode.ServiceUnavailable,
                new Uri(RequestUrl));
            GitObjects gitObjects = new GVFSGitObjects(
                new GVFSContext(tracer, new MockFileSystemWithCallbacks(), null, enlistment),
                requestor);

            gitObjects.TryDownloadPrefetchPacks(
                gitProcess: null,
                latestTimestamp: 0,
                trustPackIndexes: false,
                out List<string> _)
                .ShouldEqual(false);

            tracer.StartActivityTracer.RelatedWarningEvents.Count.ShouldEqual(1);
            tracer.StartActivityTracer.RelatedWarningEvents[0].ShouldContain("\"PrefetchEndpointUrl\":\"cache.example:8443\"");
            tracer.StartActivityTracer.RelatedWarningEvents[0].IndexOf("user", StringComparison.Ordinal).ShouldEqual(-1);
            tracer.StartActivityTracer.RelatedWarningEvents[0].IndexOf("secret", StringComparison.Ordinal).ShouldEqual(-1);
            tracer.StartActivityTracer.RelatedWarningEvents[0].IndexOf("sensitive", StringComparison.Ordinal).ShouldEqual(-1);
        }

        [TestCase]
        public void UnsupportedPrefetchTelemetryReportsOnlyRequestAuthority()
        {
            const string RequestUrl = "https://user:secret@cache.example:8443/gvfs/prefetch?token=sensitive";
            MockTracer tracer = new MockTracer();
            MockGVFSEnlistment enlistment = new MockGVFSEnlistment();
            TestPrefetchRequestor requestor = new TestPrefetchRequestor(
                tracer,
                enlistment,
                HttpStatusCode.NotFound,
                new Uri(RequestUrl));
            GitObjects gitObjects = new GVFSGitObjects(
                new GVFSContext(tracer, new MockFileSystemWithCallbacks(), null, enlistment),
                requestor);

            gitObjects.TryDownloadPrefetchPacks(
                gitProcess: null,
                latestTimestamp: 0,
                trustPackIndexes: false,
                out List<string> _)
                .ShouldEqual(false);

            EventMetadata metadata = tracer.StartActivityTracer.RelatedEventMetadata[0];
            metadata["PrefetchEndpointUrl"].ShouldEqual("cache.example:8443");
        }

        [TestCase]
        public void PrefetchPackTelemetryCorrelatesResponseAndRetrySummary()
        {
            using MockTracer tracer = new MockTracer();
            MockGVFSEnlistment enlistment = new MockGVFSEnlistment();
            using StreamingPrefetchRequestor requestor = new StreamingPrefetchRequestor(tracer, enlistment);
            requestor.Streams.Enqueue(CreatePrefetchResponse(1));
            TestPrefetchGitObjects objects = new TestPrefetchGitObjects(tracer, enlistment, requestor);

            objects.TryDownloadPrefetchPacks(null, 0, true, out List<string> indexes).ShouldEqual(true);
            indexes.Count.ShouldEqual(1);
            MockTracer activity = tracer.StartActivityTracer;
            int packIndex = activity.RelatedEventNames.IndexOf("Receiving Pack/Index");
            (packIndex >= 0).ShouldEqual(true);
            EventMetadata pack = activity.RelatedEventMetadata[packIndex];
            pack["ResponseId"].ShouldEqual(requestor.ResponseId);
            pack["RequestId"].ShouldEqual(requestor.RequestId);
            pack["PackBytes"].ShouldEqual(1L);
            ((activity.RelatedEventKeywords[packIndex] & Keywords.Telemetry) != 0).ShouldEqual(true);
            EventMetadata start = tracer.StartMetadata.Single(metadata => metadata != null && metadata.ContainsKey("RequestId"));
            EventMetadata stop = activity.StopMetadata.Single();
            start["RequestId"].ShouldEqual(requestor.RequestId);
            stop["RequestId"].ShouldEqual(requestor.RequestId);
            stop["Success"].ShouldEqual(true);
            tracer.SnapshotRelatedEvents().Any(record => record.Name.StartsWith("NetworkBody", StringComparison.Ordinal))
                .ShouldEqual(false);
        }

        [TestCase]
        public async Task PrefetchHeaderFailureDrainsEarlierPackWorkBeforeRetry()
        {
            using MockTracer tracer = new MockTracer();
            MockGVFSEnlistment enlistment = new MockGVFSEnlistment();
            using StreamingPrefetchRequestor requestor = new StreamingPrefetchRequestor(tracer, enlistment);
            using MemoryStream initialResponse = CreatePrefetchResponse(2);
            using HeaderFailureStream firstResponse = new HeaderFailureStream(initialResponse.ToArray());
            requestor.Streams.Enqueue(firstResponse);
            requestor.Streams.Enqueue(CreatePrefetchResponse(0));
            TaskCompletionSource<bool> flush = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            TestPrefetchGitObjects objects = new TestPrefetchGitObjects(tracer, enlistment, requestor)
            {
                FirstFlush = flush.Task,
            };
            Task<bool> prefetch = Task.Run(() => objects.TryDownloadPrefetchPacks(null, 0, true, out List<string> _));
            try
            {
                await firstResponse.Failed.Task.WaitAsync(TimeSpan.FromSeconds(5));
                await Task.Delay(100);
                requestor.Requests.Count.ShouldEqual(1);
                prefetch.IsCompleted.ShouldEqual(false);
            }
            finally
            {
                flush.TrySetResult(true);
            }

            (await prefetch.WaitAsync(TimeSpan.FromSeconds(5))).ShouldEqual(true);
            requestor.Requests.Count.ShouldEqual(2);
            requestor.Requests[1].Query.ShouldEqual("?lastPackTimestamp=1");
            objects.Moves.ShouldEqual(2);
        }

        [TestCase]
        [Category(CategoryConstants.ExceptionExpected)]
        public void InterruptedResponseAlsoReportsPackFinalizationFailure()
        {
            using MockTracer tracer = new MockTracer();
            MockGVFSEnlistment enlistment = new MockGVFSEnlistment();
            using StreamingPrefetchRequestor requestor = new StreamingPrefetchRequestor(tracer, enlistment);
            using MemoryStream initial = CreatePrefetchResponse(2);
            requestor.Streams.Enqueue(new HeaderFailureStream(initial.ToArray()));
            requestor.Streams.Enqueue(CreatePrefetchResponse(0));
            TestPrefetchGitObjects objects = new TestPrefetchGitObjects(tracer, enlistment, requestor)
            {
                FirstFlush = Task.FromException(new IOException("pack finalization failed")),
            };

            objects.TryDownloadPrefetchPacks(null, 0, true, out List<string> _).ShouldEqual(true);
            tracer.StartActivityTracer.RelatedWarningEvents.ShouldContain(
                warning => warning.Contains("pack finalization failed", StringComparison.Ordinal) &&
                    warning.Contains("response was interrupted", StringComparison.Ordinal));
            requestor.Requests[1].Query.ShouldEqual("?lastPackTimestamp=0");
        }

        [TestCase]
        [Category(CategoryConstants.ExceptionExpected)]
        public void TemporaryPackCancellationPropagatesWithoutAFailureWarning()
        {
            using MockTracer tracer = new MockTracer();
            MockFileSystemWithCallbacks filesystem = new MockFileSystemWithCallbacks
            {
                OnOpenFileStream = (path, mode, access) => new MemoryStream(),
                OnFileExists = path => false,
            };
            GitObjects objects = new GVFSGitObjects(
                new GVFSContext(tracer, filesystem, null, new MockGVFSEnlistment()), null);
            using Stream canceled = new CanceledReadStream();

            Assert.Throws<OperationCanceledException>(() => objects.TryWriteTempFile(
                tracer, canceled, "mock:\\canceled.pack", out long _, out Task _, throwOnError: false));
            tracer.RelatedWarningEvents.Count.ShouldEqual(0);
        }

        private sealed class CanceledReadStream : MemoryStream
        {
            public override int Read(byte[] buffer, int offset, int count)
            {
                throw new OperationCanceledException();
            }
        }

        private static MemoryStream CreatePrefetchResponse(ushort declaredPacks)
        {
            MemoryStream response = new MemoryStream();
            using (BinaryWriter writer = new BinaryWriter(response, System.Text.Encoding.UTF8, leaveOpen: true))
            {
                writer.Write(new byte[] { (byte)'G', (byte)'P', (byte)'R', (byte)'E', (byte)' ', 1 });
                writer.Write(declaredPacks);
                if (declaredPacks > 0)
                {
                    writer.Write(1L);
                    writer.Write(1L);
                    writer.Write(1L);
                    writer.Write((byte)1);
                    writer.Write((byte)2);
                }
            }

            response.Position = 0;
            return response;
        }

        private class HeaderFailureStream : MemoryStream
        {
            public HeaderFailureStream(byte[] data) : base(data)
            {
            }

            public TaskCompletionSource<bool> Failed { get; } = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);

            public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
            {
                if (this.Position == this.Length)
                {
                    this.Failed.TrySetResult(true);
                    throw new IOException("Next prefetch header failed");
                }

                return base.ReadAsync(buffer, cancellationToken);
            }
        }

        private class StreamingPrefetchRequestor : GitObjectsHttpRequestor
        {
            public StreamingPrefetchRequestor(MockTracer tracer, MockGVFSEnlistment enlistment)
                : base(tracer, enlistment, new CacheServerInfo("mock://cache/server", "cache"), new RetryConfig(1))
            {
            }

            public Queue<Stream> Streams { get; } = new Queue<Stream>();
            public List<Uri> Requests { get; } = new List<Uri>();
            public string ResponseId { get; private set; }
            public long RequestId { get; private set; }

            protected override GitEndPointResponseData SendProtocolRequest(
                long requestId, Uri requestUri, HttpMethod httpMethod, string requestContent,
                CancellationToken cancellationToken, MediaTypeWithQualityHeaderValue acceptType = null)
            {
                this.RequestId = requestId;
                this.Requests.Add(requestUri);
                GitEndPointResponseData response = new GitEndPointResponseData(
                    HttpStatusCode.OK, GVFSConstants.MediaTypes.PrefetchPackFilesAndIndexesMediaType,
                    new HttpResponseStream(this.Streams.Dequeue(), this.RetryConfig.ReadTimeout,
                        cancellationToken, this.Tracer, new EventMetadata { { "RequestId", requestId } }),
                    null, null);
                this.ResponseId = response.ResponseId;
                return response;
            }
        }

        private class TestPrefetchGitObjects : GVFSGitObjects
        {
            private int moves;
            private int writes;

            public TestPrefetchGitObjects(MockTracer tracer, MockGVFSEnlistment enlistment, GitObjectsHttpRequestor requestor)
                : this(tracer, enlistment, requestor, new PrefetchFileSystem())
            {
            }

            private TestPrefetchGitObjects(MockTracer tracer, MockGVFSEnlistment enlistment,
                GitObjectsHttpRequestor requestor, MockFileSystemWithCallbacks filesystem)
                : base(new GVFSContext(tracer, filesystem, null, enlistment), requestor)
            {
                filesystem.OnOpenFileStream = (path, mode, access) => new MemoryStream();
                filesystem.OnMoveFile = (source, target) => Interlocked.Increment(ref this.moves);
                filesystem.OnFileExists = path => false;
            }

            public Task FirstFlush { get; set; } = Task.CompletedTask;
            public int Moves => this.moves;

            public override bool TryWriteTempFile(ITracer tracer, Stream source, string tempFilePath,
                out long fileLength, out Task flushTask, bool throwOnError = false)
            {
                fileLength = source.Length;
                source.CopyTo(Stream.Null);
                flushTask = ++this.writes == 1 ? this.FirstFlush : Task.CompletedTask;
                return true;
            }

            public override string[] ReadPackFileNames(string packFolderPath, string prefixFilter = "") => Array.Empty<string>();
        }

        private class PrefetchFileSystem : MockFileSystemWithCallbacks
        {
            public override void MoveAndOverwriteFile(string sourceFileName, string destinationFileName)
            {
                this.OnMoveFile(sourceFileName, destinationFileName);
            }
        }

        private Stream OnOpenFileStream(string path, FileMode mode, FileAccess access)
        {
            this.openedPaths.Add(path);

            if (this.pathsToData.TryGetValue(path, out MemoryStream stream))
            {
                this.pathsToData[path] = new MemoryStream(stream.ToArray());
            }
            else
            {
                this.pathsToData[path] = new MemoryStream();
            }

            return this.pathsToData[path];
        }

        private bool OnFileExists(string path)
        {
            return this.pathsToData.TryGetValue(path, out _);
        }

        private class TestPrefetchRequestor : GitObjectsHttpRequestor
        {
            private readonly HttpStatusCode statusCode;
            private readonly Uri requestUri;

            public TestPrefetchRequestor(
                ITracer tracer,
                Enlistment enlistment,
                HttpStatusCode statusCode,
                Uri requestUri)
                : base(tracer, enlistment, new CacheServerInfo("https://cache.example/server", "cache"), new RetryConfig(0))
            {
                this.statusCode = statusCode;
                this.requestUri = requestUri;
            }

            public override RetryWrapper<GitObjectTaskResult>.InvocationResult TrySendProtocolRequest(
                long requestId,
                Func<int, GitEndPointResponseData, RetryWrapper<GitObjectTaskResult>.CallbackResult> onSuccess,
                Action<RetryWrapper<GitObjectTaskResult>.ErrorEventArgs> onFailure,
                HttpMethod method,
                Func<Uri> endPointGenerator,
                Func<string> requestBodyGenerator,
                CancellationToken cancellationToken,
                MediaTypeWithQualityHeaderValue acceptType = null,
                bool retryOnFailure = true,
                Func<Uri> fallbackEndPointGenerator = null)
            {
                return new RetryWrapper<GitObjectTaskResult>.InvocationResult(
                    tryCount: 1,
                    new GitObjectsHttpException(this.statusCode, "Test failure"),
                    new GitObjectTaskResult(this.statusCode, this.requestUri));
            }
        }
    }
}
