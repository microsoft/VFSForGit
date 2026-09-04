using GVFS.Common;
using GVFS.Common.Git;
using GVFS.Common.Http;
using GVFS.Common.Tracing;
using GVFS.Tests.Should;
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
