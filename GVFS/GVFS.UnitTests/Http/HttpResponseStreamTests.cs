using GVFS.Common;
using GVFS.Common.Http;
using GVFS.Common.Tracing;
using GVFS.Tests.Should;
using GVFS.UnitTests.Category;
using GVFS.UnitTests.Mock.Common;
using Moq;
using NUnit.Framework;
using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using static GVFS.UnitTests.Http.HttpResponseStreamTestSupport;

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
        [Category(CategoryConstants.ExceptionExpected)]
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
        [Category(CategoryConstants.ExceptionExpected)]
        public void ReadByteTimesOut()
        {
            using (HttpResponseStream stream = CreateStream(new BlockingReadStream()))
            {
                Assert.Throws<IOException>(() => stream.ReadByte());
                stream.ReadFailed.ShouldEqual(true);
            }
        }

        [TestCase]
        [Category(CategoryConstants.ExceptionExpected)]
        public void SpanReadTimesOut()
        {
            using (HttpResponseStream stream = CreateStream(new BlockingReadStream()))
            {
                Assert.Throws<IOException>(() => stream.Read(new byte[8].AsSpan()).ShouldEqual(0));
                stream.ReadFailed.ShouldEqual(true);
            }
        }

        [TestCase]
        [Category(CategoryConstants.ExceptionExpected)]
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
        [Category(CategoryConstants.ExceptionExpected)]
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
                tracer.SnapshotRelatedEvents().ShouldNotContain(record => record.Name == "NetworkBodyReadTimeout");
                EventMetadata summary = LastBodyEvent(tracer, "Completed").Metadata;
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
                tracer.SnapshotRelatedEvents().ShouldNotContain(record => record.Name == "NetworkBodyReadTimeout");
                tracer.SnapshotRelatedEvents().ShouldContain(
                    record => (string)record.Metadata["Outcome"] == "InProgress" &&
                    (bool)record.Metadata["ReadPending"] == false &&
                    (long)record.Metadata["BytesRead"] == 1);
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        [Category(CategoryConstants.ExceptionExpected)]
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
                LastBodyEvent(tracer, "Canceled").Level.ShouldEqual(EventLevel.Informational);
            }
        }

        [TestCase]
        [Category(CategoryConstants.ExceptionExpected)]
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
                tracer.SnapshotRelatedEvents().ShouldNotContain(record => record.Name == "NetworkBodyReadTimeout");
                LastBodyEvent(tracer, "Closed").Metadata["Outcome"].ShouldEqual("Closed");
            }
        }

        [TestCase]
        [Category(CategoryConstants.ExceptionExpected)]
        public void BlockedReadReportsProgressAndTimeoutWithMatchingResponseId()
        {
            using MockTracer tracer = new MockTracer();
            using (HttpResponseStream stream = CreateStream(new BlockingReadStream(), tracer))
            {
                Assert.ThrowsAsync<IOException>(
                    async () => await stream.ReadAsync(new byte[8].AsMemory()).AsTask().WaitAsync(TimeSpan.FromSeconds(5)));
                MockTracer.RecordedEvent[] records = tracer.SnapshotRelatedEvents();
                records.ShouldContain(record => record.Name == "NetworkBodyProgress");
                LastBodyEvent(tracer, "TimedOut").Name.ShouldEqual("NetworkBodyReadTimeout");
                records.ShouldContain(
                    record => (bool)record.Metadata["ReadPending"] && (double)record.Metadata["CurrentReadWaitMs"] > 0);
                foreach (EventMetadata metadata in records.Select(record => record.Metadata))
                {
                    metadata["ResponseId"].ShouldEqual(stream.ResponseId);
                    ((long)metadata["SampleSequence"] > 0).ShouldEqual(true);
                    metadata["RequestId"].ShouldEqual(42L);
                    metadata["CacheName"].ShouldEqual("test-cache");
                    metadata["RequestAuthority"].ShouldEqual("cache.example:443");
                }

                EventMetadata timeout = LastBodyEvent(tracer, "TimedOut").Metadata;
                timeout["Outcome"].ShouldEqual("TimedOut");
                timeout["BytesRead"].ShouldEqual(0L);
                LastBodyEvent(tracer, "TimedOut").Level.ShouldEqual(EventLevel.Warning);
                ((double)timeout["MaxReadWaitMs"] >= 200).ShouldEqual(true);
                ((double)timeout["ReadTimeMs"] >= 200).ShouldEqual(true);
                records.ShouldContain(
                    record => (record.Keyword & Keywords.Telemetry) == Keywords.Telemetry);
            }
        }

        [TestCase]
        [Category(CategoryConstants.ExceptionExpected)]
        public void ReadTimeoutReportsBytesReceivedBeforeTheStall()
        {
            using (MockTracer tracer = new MockTracer())
            using (HttpResponseStream stream = CreateStream(new BlockingReadStream(new byte[] { 1, 2, 3 }), tracer))
            {
                byte[] buffer = new byte[8];
                stream.Read(buffer, 0, buffer.Length).ShouldEqual(3);
                Assert.Throws<IOException>(() => stream.Read(buffer, 0, buffer.Length).ShouldEqual(0));
                EventMetadata timeout = LastBodyEvent(tracer, "TimedOut").Metadata;
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
            long terminalSequence = (long)LastBodyEvent(tracer, readToEnd ? "Completed" : "Closed").Metadata["SampleSequence"];
            await Task.Delay(120);
            tracer.SnapshotRelatedEvents().Max(record => (long)record.Metadata["SampleSequence"])
                .ShouldEqual(terminalSequence);
        }

        [TestCase]
        [Category(CategoryConstants.ExceptionExpected)]
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

            tracer.SnapshotRelatedEvents().Length.ShouldEqual(0);
        }

        [TestCase]
        [Category(CategoryConstants.ExceptionExpected)]
        public void DisposedRootTracerDoesNotChangeTheReadFailureOrExitTheProcess()
        {
            using JsonTracer tracer = new JsonTracer(GVFSConstants.GVFSEtwProviderName, "body-test", disableTelemetry: true);
            using HttpResponseStream stream = new HttpResponseStream(
                new BlockingReadStream(), TimeSpan.FromMilliseconds(200), CancellationToken.None,
                tracer, progressInterval: TimeSpan.FromMilliseconds(20));
            tracer.Dispose();
            IOException failure = Assert.ThrowsAsync<IOException>(
                async () => await stream.ReadAsync(new byte[1].AsMemory()).AsTask().WaitAsync(TimeSpan.FromSeconds(5)));
            failure.Message.ShouldContain("No response body data arrived");
            stream.ReadFailed.ShouldEqual(true);
        }

        [TestCase]
        [Category(CategoryConstants.ExceptionExpected)]
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
        [Category(CategoryConstants.ExceptionExpected)]
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
        [Category(CategoryConstants.ExceptionExpected)]
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
        [Category(CategoryConstants.ExceptionExpected)]
        public void RawResponseStreamTracksReadFailuresAndIdentity()
        {
            using GitEndPointResponseData response = new GitEndPointResponseData(
                HttpStatusCode.OK, "application/json", new FailingReadStream(), null, null);
            string.IsNullOrEmpty(response.ResponseId).ShouldEqual(false);
            Assert.Throws<IOException>(() => response.Stream.ReadByte());
            response.StreamReadFailed.ShouldEqual(true);
        }

        [TestCase]
        [Category(CategoryConstants.ExceptionExpected)]
        public void ResponseDisposalReleasesPermitWhenFinalDiagnosticsFail()
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
            response.Dispose();
            innerStream.CanRead.ShouldEqual(false);
            response.Dispose();
            releases.ShouldEqual(1);
            tracer.RelatedWarningEvents.Count.ShouldEqual(1);
        }

        [TestCase(false)]
        [TestCase(true)]
        [Category(CategoryConstants.ExceptionExpected)]
        public void DiagnosticFailureDoesNotReplaceTheReadTimeout(bool failReportConstruction)
        {
            Mock<ITracer> tracer = new Mock<ITracer>();
            using ManualResetEventSlim warningReported = new ManualResetEventSlim();
            int reports = 0;
            EventMetadata warningMetadata = null;
            string warningMessage = null;
            tracer.Setup(item => item.RelatedEvent(
                It.IsAny<EventLevel>(), It.IsAny<string>(), It.IsAny<EventMetadata>(), It.IsAny<Keywords>()))
                .Callback(() =>
                {
                    Interlocked.Increment(ref reports);
                    throw new InvalidOperationException("diagnostic failure");
                });
            tracer.Setup(item => item.RelatedWarning(
                It.IsAny<EventMetadata>(), It.IsAny<string>(), It.IsAny<Keywords>()))
                .Callback<EventMetadata, string, Keywords>((metadata, message, keyword) =>
                {
                    warningMetadata = metadata;
                    warningMessage = message;
                    warningReported.Set();
                });

            using HttpResponseStream stream = new HttpResponseStream(
                new BlockingReadStream(), TimeSpan.FromMilliseconds(200), CancellationToken.None, tracer.Object,
                failReportConstruction ? new EventMetadata { { "ResponseId", "duplicate" } } : null,
                TimeSpan.FromMilliseconds(20));
            IOException error = Assert.Throws<IOException>(() => stream.ReadByte());
            error.Message.ShouldContain("No response body data arrived");
            stream.ReadFailed.ShouldEqual(true);
            warningReported.Wait(TimeSpan.FromSeconds(5)).ShouldEqual(true);
            warningMetadata["ResponseId"].ShouldEqual(stream.ResponseId);
            warningMessage.ShouldContain("Further reports for this response are disabled.");
            reports.ShouldEqual(failReportConstruction ? 0 : 1);
            tracer.Verify(item => item.RelatedWarning(
                It.IsAny<EventMetadata>(), It.IsAny<string>(), It.IsAny<Keywords>()), Times.Once);
        }

        [TestCase]
        [Category(CategoryConstants.ExceptionExpected)]
        public void DiagnosticFailureDoesNotStopSubsequentReads()
        {
            Mock<ITracer> tracer = new Mock<ITracer>();
            using ManualResetEventSlim warningReported = new ManualResetEventSlim();
            tracer.Setup(item => item.RelatedEvent(
                It.IsAny<EventLevel>(), It.IsAny<string>(), It.IsAny<EventMetadata>(), It.IsAny<Keywords>()))
                .Throws(new InvalidOperationException("diagnostic failure"));
            tracer.Setup(item => item.RelatedWarning(
                It.IsAny<EventMetadata>(), It.IsAny<string>(), It.IsAny<Keywords>()))
                .Callback(() => warningReported.Set());
            using HttpResponseStream stream = new HttpResponseStream(
                new MemoryStream(new byte[] { 1, 2 }), TimeSpan.FromSeconds(1), CancellationToken.None,
                tracer.Object, progressInterval: TimeSpan.FromMilliseconds(20));
            stream.ReadByte().ShouldEqual(1);
            warningReported.Wait(TimeSpan.FromSeconds(5)).ShouldEqual(true);
            stream.ReadByte().ShouldEqual(2);
            stream.ReadByte().ShouldEqual(-1);
            stream.ReadFailed.ShouldEqual(false);
            tracer.Verify(item => item.RelatedEvent(
                It.IsAny<EventLevel>(), It.IsAny<string>(), It.IsAny<EventMetadata>(), It.IsAny<Keywords>()), Times.Once);
        }

        [TestCase]
        [Category(CategoryConstants.ExceptionExpected)]
        public void FinalReportConstructionFailurePreservesTheReadError()
        {
            using MockTracer tracer = new MockTracer();
            using HttpResponseStream stream = new HttpResponseStream(
                new FailingReadStream(), TimeSpan.FromSeconds(1), CancellationToken.None, tracer,
                new EventMetadata { { "ResponseId", "duplicate" } });
            Assert.Throws<IOException>(() => stream.ReadByte()).Message.ShouldEqual("read failed");
            tracer.RelatedWarningEvents.Count.ShouldEqual(1);
            tracer.SnapshotRelatedEvents().Length.ShouldEqual(0);
        }

        [TestCase]
        [NonParallelizable]
        [Category(CategoryConstants.ExceptionExpected)]
        public void DiagnosticWarningFailureUsesStandardErrorWithoutChangingTheRead()
        {
            Mock<ITracer> tracer = new Mock<ITracer>();
            tracer.Setup(item => item.RelatedEvent(
                It.IsAny<EventLevel>(), It.IsAny<string>(), It.IsAny<EventMetadata>(), It.IsAny<Keywords>()))
                .Throws(new InvalidOperationException("diagnostic failure"));
            tracer.Setup(item => item.RelatedWarning(
                It.IsAny<EventMetadata>(), It.IsAny<string>(), It.IsAny<Keywords>()))
                .Throws(new IOException("warning failure"));
            TextWriter previousError = Console.Error;
            using StringWriter errorOutput = new StringWriter();
            try
            {
                Console.SetError(errorOutput);
                using HttpResponseStream stream = new HttpResponseStream(
                    new FailingReadStream(), TimeSpan.FromSeconds(1), CancellationToken.None, tracer.Object);
                Assert.Throws<IOException>(() => stream.ReadByte()).Message.ShouldEqual("read failed");
                errorOutput.ToString().ShouldContain("Response body diagnostics failed.");
            }
            finally
            {
                Console.SetError(previousError);
            }
        }

        [TestCase]
        [Category(CategoryConstants.ExceptionExpected)]
        public void FailedDiagnosticListenerDoesNotInterruptTheTransfer()
        {
            using JsonTracer tracer = new JsonTracer(GVFSConstants.GVFSEtwProviderName, "body-test", disableTelemetry: true);
            using ManualResetEventSlim progress = new ManualResetEventSlim();
            tracer.AddEventListener(new FailingBodyListener(tracer, progress));
            using HttpResponseStream stream = new HttpResponseStream(
                new MemoryStream(new byte[] { 1 }), TimeSpan.FromSeconds(1), CancellationToken.None,
                tracer, progressInterval: TimeSpan.FromMilliseconds(20));
            stream.ReadByte().ShouldEqual(1);
            progress.Wait(TimeSpan.FromSeconds(5)).ShouldEqual(true);
            stream.ReadByte().ShouldEqual(-1);
            stream.ReadFailed.ShouldEqual(false);
        }

        private sealed class FailingBodyListener : GVFS.Common.Tracing.EventListener
        {
            private readonly ManualResetEventSlim progress;

            public FailingBodyListener(IEventListenerEventSink sink, ManualResetEventSlim progress)
                : base(EventLevel.Informational, Keywords.Any, sink)
            {
                this.progress = progress;
            }

            protected override void RecordMessageInternal(TraceEventMessage message)
            {
                if (message.EventName == "NetworkBodyProgress")
                {
                    this.progress.Set();
                    throw new IOException("diagnostic output failed");
                }
            }
        }

        [TestCase]
        public void ProgressReportsCountOnlyBytesSinceThePreviousReport()
        {
            using CallbackTracer tracer = new CallbackTracer();
            using ManualResetEventSlim first = new ManualResetEventSlim();
            using ManualResetEventSlim second = new ManualResetEventSlim();
            tracer.EventCallback = (name, metadata) =>
            {
                if (name == "NetworkBodyProgress")
                {
                    if ((long)metadata["BytesRead"] == 1)
                    {
                        first.Set();
                    }
                    else if ((long)metadata["BytesRead"] == 2)
                    {
                        second.Set();
                    }
                }
            };
            using HttpResponseStream stream = CreateStream(new MemoryStream(new byte[] { 1, 2 }), tracer);
            stream.ReadByte().ShouldEqual(1);
            first.Wait(TimeSpan.FromSeconds(5)).ShouldEqual(true);
            stream.ReadByte().ShouldEqual(2);
            second.Wait(TimeSpan.FromSeconds(5)).ShouldEqual(true);
            stream.ReadByte().ShouldEqual(-1);
            EventMetadata[] reports = tracer.SnapshotRelatedEvents().Select(record => record.Metadata).ToArray();
            reports.Select(report => (long)report["SampleSequence"]).Distinct().Count().ShouldEqual(reports.Length);
            reports.Sum(report => (long)report["BytesSinceLastProgress"]).ShouldEqual(2L);
            reports.Count(report => (long)report["BytesSinceLastProgress"] == 1).ShouldEqual(2);
            reports.OrderBy(report => (long)report["SampleSequence"]).Last()["Outcome"].ShouldEqual("Completed");
        }

        private class FailingReadStream : MemoryStream
        {
            public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
            {
                throw new IOException("read failed");
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
