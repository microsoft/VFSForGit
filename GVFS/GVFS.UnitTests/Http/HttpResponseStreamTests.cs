using GVFS.Common;
using GVFS.Common.Http;
using GVFS.Common.Tracing;
using GVFS.Tests.Should;
using GVFS.UnitTests.Mock.Common;
using GVFS.UnitTests.Mock.Git;
using NUnit.Framework;
using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace GVFS.UnitTests.Http
{
    [TestFixture]
    public class HttpResponseStreamTests
    {
        [TestCase]
        public void DefaultReadTimeoutIsSixtySeconds()
        {
            new RetryConfig().ReadTimeout.ShouldEqual(TimeSpan.FromSeconds(60));
            new RetryConfig(1, TimeSpan.FromMinutes(10)).ReadTimeout.ShouldEqual(TimeSpan.FromSeconds(60));
            HttpResponseStream.DefaultProgressIntervalSeconds.ShouldEqual(30);
        }

        [TestCase]
        public void SynchronousReadTimesOut()
        {
            using (HttpResponseStream stream = CreateStream(new BlockingReadStream()))
            {
                IOException error = Assert.Throws<IOException>(() => stream.Read(new byte[8], 0, 8).ShouldEqual(0));
                error.Message.ShouldContain("No response body data arrived");
                stream.ReadFailed.ShouldEqual(true);
            }
        }

        [TestCase]
        public void ReadByteTimesOut()
        {
            using (HttpResponseStream stream = CreateStream(new BlockingReadStream()))
            {
                Assert.Throws<IOException>(() => stream.ReadByte());
                stream.ReadFailed.ShouldEqual(true);
            }
        }

        [TestCase]
        public void SpanReadTimesOut()
        {
            using (HttpResponseStream stream = CreateStream(new BlockingReadStream()))
            {
                Assert.Throws<IOException>(() => stream.Read(new byte[8].AsSpan()).ShouldEqual(0));
                stream.ReadFailed.ShouldEqual(true);
            }
        }

        [TestCase]
        public void ArrayReadAsyncTimesOut()
        {
            using (HttpResponseStream stream = CreateStream(new BlockingReadStream()))
            {
                Task<int> read = stream.ReadAsync(new byte[8], 0, 8, CancellationToken.None);
                Assert.ThrowsAsync<IOException>(async () => await read.WaitAsync(TimeSpan.FromSeconds(5)));
                stream.ReadFailed.ShouldEqual(true);
            }
        }

        [TestCase]
        public void MemoryReadAsyncTimesOut()
        {
            using (HttpResponseStream stream = CreateStream(new BlockingReadStream()))
            {
                Task<int> read = stream.ReadAsync(new byte[8].AsMemory()).AsTask();
                Assert.ThrowsAsync<IOException>(async () => await read.WaitAsync(TimeSpan.FromSeconds(5)));
                stream.ReadFailed.ShouldEqual(true);
            }
        }

        [TestCase]
        public async Task SlowTransferContinuesBeyondTheReadTimeout()
        {
            using MockTracer tracer = new MockTracer();
            using (HttpResponseStream stream = CreateStream(
                new SlowReadStream(12, TimeSpan.FromMilliseconds(50)),
                tracer,
                readTimeout: TimeSpan.FromMilliseconds(300)))
            {
                byte[] buffer = new byte[80 * 1024];
                int total = 0;
                int read;
                while ((read = await stream.ReadAsync(buffer.AsMemory())) != 0)
                {
                    total += read;
                }

                total.ShouldEqual(12);
                stream.ReadFailed.ShouldEqual(false);
                tracer.RelatedEventNames.ShouldNotContain(name => name == "NetworkBodyReadTimeout");
                EventMetadata summary = tracer.RelatedEventMetadata.Last();
                summary["Outcome"].ShouldEqual("Completed");
                summary["BytesRead"].ShouldEqual(12L);
                ((double)summary["ElapsedMs"] > 300).ShouldEqual(true);
            }
        }

        [TestCase]
        public async Task ConsumerPauseDoesNotTimeOutAndReportsNoPendingRead()
        {
            using MockTracer tracer = new MockTracer();
            using (HttpResponseStream stream = CreateStream(new MemoryStream(new byte[] { 1, 2 }), tracer))
            {
                stream.ReadByte().ShouldEqual(1);
                await Task.Delay(600);
                stream.ReadByte().ShouldEqual(2);
                stream.ReadByte().ShouldEqual(-1);
                stream.ReadFailed.ShouldEqual(false);
                tracer.RelatedEventNames.ShouldNotContain(name => name == "NetworkBodyReadTimeout");
                tracer.RelatedEventMetadata.ShouldContain(
                    metadata => (string)metadata["Outcome"] == "InProgress" &&
                    (bool)metadata["ReadPending"] == false &&
                    (long)metadata["BytesRead"] == 1);
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task CallerCancellationIsNotAReadFailure(bool cancelRequest)
        {
            using MockTracer tracer = new MockTracer();
            using (CancellationTokenSource cancellation = new CancellationTokenSource())
            using (BlockingReadStream innerStream = new BlockingReadStream())
            using (HttpResponseStream stream = new HttpResponseStream(
                innerStream,
                TimeSpan.FromSeconds(60),
                cancelRequest ? cancellation.Token : CancellationToken.None,
                tracer))
            {
                Task<int> read = stream.ReadAsync(
                    new byte[8].AsMemory(),
                    cancelRequest ? CancellationToken.None : cancellation.Token).AsTask();
                await innerStream.ReadStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
                cancellation.Cancel();
                Assert.CatchAsync<OperationCanceledException>(async () => await read.WaitAsync(TimeSpan.FromSeconds(5)));
                stream.ReadFailed.ShouldEqual(false);
                tracer.RelatedEventMetadata.Last()["Outcome"].ShouldEqual("Canceled");
                tracer.RelatedEventLevels.Last().ShouldEqual(EventLevel.Informational);
            }
        }

        [TestCase]
        public async Task DisposingCancelsPendingReadWithoutCallingItATimeout()
        {
            using MockTracer tracer = new MockTracer();
            using (BlockingReadStream innerStream = new BlockingReadStream())
            {
                HttpResponseStream stream = CreateStream(innerStream, tracer, TimeSpan.FromSeconds(60));
                Task<int> read = stream.ReadAsync(new byte[8].AsMemory()).AsTask();
                await innerStream.ReadStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
                stream.Dispose();
                Assert.CatchAsync<OperationCanceledException>(async () => await read.WaitAsync(TimeSpan.FromSeconds(5)));
                stream.ReadFailed.ShouldEqual(false);
                tracer.RelatedEventNames.ShouldNotContain(name => name == "NetworkBodyReadTimeout");
                tracer.RelatedEventMetadata.Last()["Outcome"].ShouldEqual("Closed");
            }
        }

        [TestCase]
        public void BlockedReadReportsProgressAndTimeoutWithMatchingResponseId()
        {
            using MockTracer tracer = new MockTracer();
            using (HttpResponseStream stream = CreateStream(new BlockingReadStream(), tracer))
            {
                Assert.ThrowsAsync<IOException>(
                    async () => await stream.ReadAsync(new byte[8].AsMemory()).AsTask().WaitAsync(TimeSpan.FromSeconds(5)));
                tracer.RelatedEventNames.ShouldContain(name => name == "NetworkBodyProgress");
                tracer.RelatedEventNames.Last().ShouldEqual("NetworkBodyReadTimeout");
                tracer.RelatedEventMetadata.ShouldContain(
                    metadata => (bool)metadata["ReadPending"] && (double)metadata["CurrentReadWaitMs"] > 0);
                foreach (EventMetadata metadata in tracer.RelatedEventMetadata)
                {
                    metadata["ResponseId"].ShouldEqual(stream.ResponseId);
                    ((long)metadata["SampleSequence"] > 0).ShouldEqual(true);
                    metadata["RequestId"].ShouldEqual(42L);
                    metadata["CacheName"].ShouldEqual("test-cache");
                    metadata["RequestAuthority"].ShouldEqual("cache.example:443");
                }

                EventMetadata timeout = tracer.RelatedEventMetadata.Last();
                timeout["Outcome"].ShouldEqual("TimedOut");
                timeout["BytesRead"].ShouldEqual(0L);
                tracer.RelatedEventLevels.Last().ShouldEqual(EventLevel.Warning);
                ((double)timeout["MaxReadWaitMs"] >= 200).ShouldEqual(true);
                ((double)timeout["ReadTimeMs"] >= 200).ShouldEqual(true);
                tracer.RelatedEventKeywords.ShouldContain(
                    keywords => (keywords & Keywords.Telemetry) == Keywords.Telemetry);
            }
        }

        [TestCase]
        public void ReadTimeoutReportsBytesReceivedBeforeTheStall()
        {
            using (MockTracer tracer = new MockTracer())
            using (HttpResponseStream stream = CreateStream(new BlockingReadStream(new byte[] { 1, 2, 3 }), tracer))
            {
                byte[] buffer = new byte[8];
                stream.Read(buffer, 0, buffer.Length).ShouldEqual(3);
                Assert.Throws<IOException>(() => stream.Read(buffer, 0, buffer.Length).ShouldEqual(0));
                EventMetadata timeout = tracer.RelatedEventMetadata.Last();
                timeout["Outcome"].ShouldEqual("TimedOut");
                timeout["BytesRead"].ShouldEqual(3L);
                ((double)timeout["MillisecondsSinceLastByte"] >= 200).ShouldEqual(true);
                ((string)timeout["Exception"]).ShouldContain("System.IO.IOException");
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task ProgressStopsAfterCompletionOrDisposal(bool readToEnd)
        {
            using MockTracer tracer = new MockTracer();
            HttpResponseStream stream = CreateStream(new MemoryStream(new byte[] { 1 }), tracer);
            stream.ReadByte().ShouldEqual(1);
            await Task.Delay(120);
            if (readToEnd)
            {
                stream.ReadByte().ShouldEqual(-1);
            }

            stream.Dispose();
            int eventCount = tracer.RelatedEventNames.Count;
            await Task.Delay(120);
            tracer.RelatedEventNames.Count.ShouldEqual(eventCount);
        }

        [TestCase]
        public async Task ZeroLengthReadsDoNotStartAReadOrCompleteTheTransfer()
        {
            using (BlockingReadStream innerStream = new BlockingReadStream())
            using (HttpResponseStream stream = CreateStream(innerStream))
            {
                stream.Read(new byte[1], 0, 0).ShouldEqual(0);
                (await stream.ReadAsync(Memory<byte>.Empty)).ShouldEqual(0);
                innerStream.ReadStarted.Task.IsCompleted.ShouldEqual(false);
                stream.ReadFailed.ShouldEqual(false);
                Assert.Throws<ArgumentNullException>(() => stream.Read(null, 0, 0).ShouldEqual(0));
                Assert.Throws<ArgumentOutOfRangeException>(() => stream.Read(new byte[1], 0, 2).ShouldEqual(0));
            }
        }

        [TestCase]
        public void FastTransfersDoNotEmitProgressEvents()
        {
            using MockTracer tracer = new MockTracer();
            using (HttpResponseStream stream = new HttpResponseStream(
                new MemoryStream(new byte[] { 1 }),
                TimeSpan.FromSeconds(60),
                CancellationToken.None,
                tracer))
            {
                stream.ReadByte().ShouldEqual(1);
                stream.ReadByte().ShouldEqual(-1);
            }

            tracer.RelatedEventNames.Count.ShouldEqual(0);
        }

        [TestCase]
        public async Task RealHttpBodyReadTimesOutAfterHeadersAndPartialData()
        {
            using (MockTracer tracer = new MockTracer())
            using (CancellationTokenSource serverCancellation = new CancellationTokenSource(TimeSpan.FromSeconds(10)))
            using (TcpListener listener = new TcpListener(IPAddress.Loopback, 0))
            using (HttpClient client = new HttpClient())
            {
                listener.Start();
                Task server = ServeStalledResponse(listener, serverCancellation.Token);
                try
                {
                    int port = ((IPEndPoint)listener.LocalEndpoint).Port;
                    using (HttpResponseMessage response = await client.GetAsync(
                        $"http://127.0.0.1:{port}/body",
                        HttpCompletionOption.ResponseHeadersRead).WaitAsync(TimeSpan.FromSeconds(5)))
                    using (HttpResponseStream stream = CreateStream(await response.Content.ReadAsStreamAsync(), tracer))
                    {
                        stream.ReadByte().ShouldEqual((int)'x');
                        Assert.ThrowsAsync<IOException>(
                            async () => await stream.ReadAsync(new byte[8].AsMemory()).AsTask().WaitAsync(TimeSpan.FromSeconds(5)));
                        stream.ReadFailed.ShouldEqual(true);
                        tracer.RelatedEventNames.Last().ShouldEqual("NetworkBodyReadTimeout");
                    }
                }
                finally
                {
                    serverCancellation.Cancel();
                    listener.Stop();
                    try
                    {
                        await server.WaitAsync(TimeSpan.FromSeconds(5));
                    }
                    catch (OperationCanceledException) when (serverCancellation.IsCancellationRequested)
                    {
                    }
                }
            }
        }

        [TestCase(HttpStatusCode.OK)]
        [TestCase(HttpStatusCode.ServiceUnavailable)]
        [TestCase(HttpStatusCode.Unauthorized)]
        [TestCase(HttpStatusCode.NotFound)]
        public async Task RequestorAppliesConfiguredReadTimeoutToSuccessAndErrorBodies(HttpStatusCode status)
        {
            using (MockTracer tracer = new MockTracer())
            using (CancellationTokenSource serverCancellation = new CancellationTokenSource(TimeSpan.FromSeconds(10)))
            using (TcpListener listener = new TcpListener(IPAddress.Loopback, 0))
            using (LoopbackHttpRequestor requestor = new LoopbackHttpRequestor(tracer))
            {
                listener.Start();
                Task server = ServeStalledResponse(listener, serverCancellation.Token, status);
                try
                {
                    int port = ((IPEndPoint)listener.LocalEndpoint).Port;
                    Uri uri = new Uri($"http://127.0.0.1:{port}/body?query-marker=not-for-telemetry");
                    if (status == HttpStatusCode.OK)
                    {
                        using (GitEndPointResponseData response = requestor.Get(uri))
                        {
                            response.Stream.ReadByte().ShouldEqual((int)'x');
                            Assert.Throws<IOException>(() => response.Stream.ReadByte());
                            response.StreamReadFailed.ShouldEqual(true);
                            response.ResponseId.ShouldEqual((string)tracer.RelatedEventMetadata
                                .Last(metadata => metadata.ContainsKey("ResponseId"))["ResponseId"]);
                        }
                    }
                    else
                    {
                        using (GitEndPointResponseData response = requestor.Get(uri))
                        {
                            response.StatusCode.ShouldEqual(status);
                            ((GitObjectsHttpException)response.Error).StatusCode.ShouldEqual(status);
                            response.Error.Message.ShouldContain("response body");
                            response.Error.Message.ShouldContain("No response body data arrived");
                            response.ShouldRetry.ShouldEqual(status == HttpStatusCode.ServiceUnavailable);
                        }
                    }

                    int timeoutIndex = tracer.RelatedEventNames.IndexOf("NetworkBodyReadTimeout");
                    (timeoutIndex >= 0).ShouldEqual(true);
                    EventMetadata timeout = tracer.RelatedEventMetadata[timeoutIndex];
                    timeout["RequestId"].ShouldEqual(42L);
                    timeout["StatusCode"].ShouldEqual(status);
                    timeout["CacheName"].ShouldEqual("test-cache");
                    timeout["RequestAuthority"].ShouldEqual($"127.0.0.1:{port}");
                    timeout["BytesRead"].ShouldEqual(1L);
                    timeout["ReadTimeoutMs"].ShouldEqual(300D);
                    timeout.ContainsKey("ContentType").ShouldEqual(false);
                    GVFSJsonOptions.Serialize(timeout).Contains("query-marker", StringComparison.Ordinal).ShouldEqual(false);
                }
                finally
                {
                    serverCancellation.Cancel();
                    listener.Stop();
                    try
                    {
                        await server.WaitAsync(TimeSpan.FromSeconds(5));
                    }
                    catch (OperationCanceledException) when (serverCancellation.IsCancellationRequested)
                    {
                    }
                }
            }
        }

        private static HttpResponseStream CreateStream(
            Stream innerStream,
            MockTracer tracer = null,
            TimeSpan? readTimeout = null)
        {
            return new HttpResponseStream(
                innerStream,
                readTimeout ?? TimeSpan.FromMilliseconds(300),
                CancellationToken.None,
                tracer,
                new EventMetadata
                {
                    { "RequestId", 42L },
                    { "CacheName", "test-cache" },
                    { "RequestAuthority", "cache.example:443" },
                },
                progressInterval: TimeSpan.FromMilliseconds(30));
        }

        [TestCase]
        public async Task StalledErrorBodyRetriesWithTheStatusAndReleasesItsConnectionPermit()
        {
            using MockTracer tracer = new MockTracer();
            using CancellationTokenSource cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            using TcpListener listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            string endpoint = $"http://127.0.0.1:{((IPEndPoint)listener.LocalEndpoint).Port}";
            Task server = ServeRetriedError(listener, cancellation.Token);
            using GitObjectsHttpRequestor requestor = new GitObjectsHttpRequestor(
                tracer, new MockGVFSEnlistment(), new CacheServerInfo(endpoint, "test"),
                new RetryConfig(1) { ReadTimeout = TimeSpan.FromMilliseconds(100) });
            try
            {
                RetryWrapper<GitObjectsHttpRequestor.GitObjectTaskResult>.InvocationResult result =
                    requestor.TrySendProtocolRequest(
                        123,
                        (attempt, response) =>
                        {
                            response.Stream.ReadByte().ShouldEqual(-1);
                            return new RetryWrapper<GitObjectsHttpRequestor.GitObjectTaskResult>.CallbackResult(
                                new GitObjectsHttpRequestor.GitObjectTaskResult(true));
                        },
                        onFailure: null,
                        method: HttpMethod.Get,
                        endPointGenerator: () => new Uri(endpoint),
                        requestBodyGenerator: () => null,
                        cancellationToken: CancellationToken.None);
                result.Succeeded.ShouldEqual(true);
                result.Attempts.ShouldEqual(2);
                EventMetadata[] responses = tracer.RelatedEventMetadata
                    .Where(metadata => metadata.ContainsKey("availableConnections")).ToArray();
                responses.Length.ShouldEqual(2);
                responses[0]["StatusCode"].ShouldEqual(HttpStatusCode.ServiceUnavailable);
                responses[1]["StatusCode"].ShouldEqual(HttpStatusCode.OK);
                responses[1]["availableConnections"].ShouldEqual(responses[0]["availableConnections"]);
            }
            finally
            {
                cancellation.Cancel();
                listener.Stop();
                try
                {
                    await server.WaitAsync(TimeSpan.FromSeconds(5));
                }
                catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
                {
                }
            }
        }

        [TestCase]
        public async Task ConfigProbeRetainsUnauthorizedStatusWhenItsBodyStalls()
        {
            using MockTracer tracer = new MockTracer();
            using CancellationTokenSource cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            using TcpListener listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            string endpoint = $"http://127.0.0.1:{((IPEndPoint)listener.LocalEndpoint).Port}";
            Task server = ServeStalledResponse(listener, cancellation.Token, HttpStatusCode.Unauthorized);
            using ConfigHttpRequestor requestor = new ConfigHttpRequestor(
                tracer, new MockGVFSEnlistment("mock:\\repo", endpoint, "mock:\\git", new MockGitProcess()),
                new RetryConfig(0) { ReadTimeout = TimeSpan.FromMilliseconds(100) });
            try
            {
                requestor.TryQueryGVFSConfig(false, out ServerGVFSConfig _, out HttpStatusCode? status, out string error)
                    .ShouldEqual(false);
                status.ShouldEqual(HttpStatusCode.Unauthorized);
                error.ShouldContain("401");
                error.ShouldContain("No response body data arrived");
            }
            finally
            {
                cancellation.Cancel();
                listener.Stop();
                try
                {
                    await server.WaitAsync(TimeSpan.FromSeconds(5));
                }
                catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
                {
                }
            }
        }

        private static async Task ServeRetriedError(TcpListener listener, CancellationToken cancellationToken)
        {
            for (int attempt = 0; attempt < 2; ++attempt)
            {
                using TcpClient client = await listener.AcceptTcpClientAsync(cancellationToken);
                using NetworkStream stream = client.GetStream();
                using (StreamReader reader = new StreamReader(stream, Encoding.ASCII, false, 1024, leaveOpen: true))
                {
                    string line;
                    do
                    {
                        line = await reader.ReadLineAsync(cancellationToken);
                    }
                    while (!string.IsNullOrEmpty(line));
                }

                byte[] response = Encoding.ASCII.GetBytes(attempt == 0
                    ? "HTTP/1.1 503 Unavailable\r\nContent-Length: 2\r\n\r\nx"
                    : "HTTP/1.1 200 OK\r\nContent-Length: 0\r\n\r\n");
                await stream.WriteAsync(response.AsMemory(), cancellationToken);
                if (attempt == 0)
                {
                    (await stream.ReadAsync(new byte[1].AsMemory(), cancellationToken)).ShouldEqual(0);
                }
            }
        }

        [TestCase]
        public async Task DisabledReadTimeoutStillHonorsCallerCancellation()
        {
            using CancellationTokenSource cancellation = new CancellationTokenSource();
            using HttpResponseStream stream = new HttpResponseStream(
                new BlockingReadStream(), Timeout.InfiniteTimeSpan, cancellation.Token);
            Task<int> read = stream.ReadAsync(new byte[1].AsMemory()).AsTask();
            await Task.Delay(400);
            read.IsCompleted.ShouldEqual(false);
            cancellation.Cancel();
            Assert.CatchAsync<OperationCanceledException>(async () => await read.WaitAsync(TimeSpan.FromSeconds(5)));
            stream.ReadFailed.ShouldEqual(false);
        }

        [TestCase]
        public async Task OverlappingAndDisposedReadsFailWithoutOverwritingPendingRead()
        {
            using BlockingReadStream innerStream = new BlockingReadStream();
            HttpResponseStream stream = CreateStream(innerStream, readTimeout: TimeSpan.FromSeconds(60));
            Task<int> read = stream.ReadAsync(new byte[1].AsMemory()).AsTask();
            await innerStream.ReadStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.ThrowsAsync<InvalidOperationException>(async () => (await stream.ReadAsync(new byte[1].AsMemory())).ShouldEqual(0));
            stream.Dispose();
            Assert.CatchAsync<OperationCanceledException>(async () => await read.WaitAsync(TimeSpan.FromSeconds(5)));
            Assert.ThrowsAsync<ObjectDisposedException>(async () => (await stream.ReadAsync(new byte[1].AsMemory())).ShouldEqual(0));
        }

        [TestCase]
        public async Task BlockedProgressListenerDoesNotOwnTheReadStateGate()
        {
            using ManualResetEventSlim reporting = new ManualResetEventSlim();
            using ManualResetEventSlim release = new ManualResetEventSlim();
            using ManualResetEventSlim reported = new ManualResetEventSlim();
            using CallbackTracer tracer = new CallbackTracer();
            bool responseClosed = false;
            tracer.Callback = name =>
            {
                if (name == "NetworkBodyProgress")
                {
                    reporting.Set();
                    try
                    {
                        release.Wait(TimeSpan.FromSeconds(10)).ShouldEqual(true);
                        if (responseClosed)
                        {
                            throw new ObjectDisposedException("tracer");
                        }
                    }
                    finally
                    {
                        reported.Set();
                    }
                }
            };
            using HttpResponseStream stream = CreateStream(new BlockingReadStream(new byte[] { 1 }), tracer);
            try
            {
                stream.ReadByte().ShouldEqual(1);
                reporting.Wait(TimeSpan.FromSeconds(5)).ShouldEqual(true);
                Assert.ThrowsAsync<IOException>(
                    async () => await stream.ReadAsync(new byte[1].AsMemory()).AsTask().WaitAsync(TimeSpan.FromSeconds(5)));
                stream.ReadFailed.ShouldEqual(true);
                stream.Dispose();
                responseClosed = true;
            }
            finally
            {
                release.Set();
                reported.Wait(TimeSpan.FromSeconds(5)).ShouldEqual(true);
            }

            await Task.Delay(50);
        }

        [TestCase]
        public void RawResponseStreamTracksReadFailuresAndIdentity()
        {
            using GitEndPointResponseData response = new GitEndPointResponseData(
                HttpStatusCode.OK, "application/json", new FailingReadStream(), null, null);
            string.IsNullOrEmpty(response.ResponseId).ShouldEqual(false);
            Assert.Throws<IOException>(() => response.Stream.ReadByte());
            response.StreamReadFailed.ShouldEqual(true);
        }

        [TestCase]
        public void ResponseDisposalReleasesPermitWhenFinalDiagnosticsThrow()
        {
            using CallbackTracer tracer = new CallbackTracer();
            tracer.Callback = name =>
            {
                if (name != "NetworkBodyProgress")
                {
                    throw new InvalidOperationException("diagnostic failure");
                }
            };
            MemoryStream innerStream = new MemoryStream(new byte[] { 1 });
            HttpResponseStream stream = CreateStream(innerStream, tracer);
            int releases = 0;
            GitEndPointResponseData response = new GitEndPointResponseData(
                HttpStatusCode.OK, "application/json", stream, new HttpResponseMessage(), () => ++releases);
            stream.ReadByte().ShouldEqual(1);
            Thread.Sleep(40);
            Assert.Throws<InvalidOperationException>(() => response.Dispose());
            innerStream.CanRead.ShouldEqual(false);
            response.Dispose();
            releases.ShouldEqual(1);
        }

        [TestCase]
        public async Task ProgressReportsCountOnlyBytesSinceThePreviousReport()
        {
            using MockTracer tracer = new MockTracer();
            using HttpResponseStream stream = CreateStream(new MemoryStream(new byte[] { 1, 2 }), tracer);
            stream.ReadByte().ShouldEqual(1);
            await Task.Delay(150);
            stream.ReadByte().ShouldEqual(2);
            await Task.Delay(150);
            stream.ReadByte().ShouldEqual(-1);
            EventMetadata[] reports = tracer.RelatedEventMetadata.ToArray();
            reports.Select(report => (long)report["SampleSequence"]).Distinct().Count().ShouldEqual(reports.Length);
            reports.Sum(report => (long)report["BytesSinceLastProgress"]).ShouldEqual(2L);
            reports.Count(report => (long)report["BytesSinceLastProgress"] == 1).ShouldEqual(2);
            reports.OrderBy(report => (long)report["SampleSequence"]).Last()["Outcome"].ShouldEqual("Completed");
        }

        private class CallbackTracer : MockTracer
        {
            public Action<string> Callback { get; set; }

            public override void RelatedEvent(EventLevel level, string eventName, EventMetadata metadata, Keywords keyword)
            {
                this.Callback?.Invoke(eventName);
                base.RelatedEvent(level, eventName, metadata, keyword);
            }
        }

        private class FailingReadStream : MemoryStream
        {
            public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
            {
                throw new IOException("read failed");
            }
        }

        private static async Task ServeStalledResponse(
            TcpListener listener,
            CancellationToken cancellationToken,
            HttpStatusCode status = HttpStatusCode.OK)
        {
            using (TcpClient connection = await listener.AcceptTcpClientAsync(cancellationToken))
            using (NetworkStream stream = connection.GetStream())
            {
                using (StreamReader request = new StreamReader(stream, Encoding.ASCII, false, 1024, leaveOpen: true))
                {
                    string line;
                    do
                    {
                        line = await request.ReadLineAsync(cancellationToken);
                    }
                    while (!string.IsNullOrEmpty(line));
                }

                byte[] response = Encoding.ASCII.GetBytes(
                    $"HTTP/1.1 {(int)status} Test\r\nContent-Length: 2\r\nX-Cache-Name: test-cache\r\n\r\nx");
                await stream.WriteAsync(response.AsMemory(), cancellationToken);
                await Task.Delay(Timeout.Infinite, cancellationToken);
            }
        }

        private class LoopbackHttpRequestor : HttpRequestor
        {
            public LoopbackHttpRequestor(MockTracer tracer)
                : base(
                    tracer,
                    new RetryConfig { ReadTimeout = TimeSpan.FromMilliseconds(300) },
                    new MockGVFSEnlistment())
            {
            }

            public GitEndPointResponseData Get(Uri uri)
            {
                return this.SendRequest(42, uri, HttpMethod.Get, null, CancellationToken.None);
            }
        }

        private class BlockingReadStream : MemoryStream
        {
            public BlockingReadStream(byte[] prefix = null)
                : base(prefix ?? Array.Empty<byte>())
            {
            }

            public TaskCompletionSource<bool> ReadStarted { get; } =
                new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
            {
                this.ReadStarted.TrySetResult(true);
                if (this.Position < this.Length)
                {
                    return await base.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
                }

                await Task.Delay(Timeout.Infinite, cancellationToken).ConfigureAwait(false);
                return 0;
            }
        }

        private class SlowReadStream : MemoryStream
        {
            private readonly TimeSpan delay;
            private int remaining;

            public SlowReadStream(int bytes, TimeSpan delay)
            {
                this.remaining = bytes;
                this.delay = delay;
            }

            public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
            {
                if (this.remaining == 0)
                {
                    return 0;
                }

                await Task.Delay(this.delay, cancellationToken).ConfigureAwait(false);
                buffer.Span[0] = 1;
                --this.remaining;
                return 1;
            }
        }
    }
}
