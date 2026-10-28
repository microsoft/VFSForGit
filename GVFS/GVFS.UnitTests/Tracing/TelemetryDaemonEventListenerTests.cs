using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using GVFS.Common.Tracing;
using GVFS.Tests.Should;
using NUnit.Framework;
using System;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using GVFS.UnitTests.Mock.Common.Tracing;

namespace GVFS.UnitTests.Tracing
{
    [TestFixture]
    public class TelemetryDaemonEventListenerTests
    {
        [TestCase]
        public async Task RemoteBoundsSurviveARealPipeReconnectAndChildActivities()
        {
            string pipeName = "gvfs-telemetry-test-" + Guid.NewGuid().ToString("N");
            TelemetryAggregatorTests.TestClock clock = new TelemetryAggregatorTests.TestClock();
            using (CancellationTokenSource timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15)))
            using (ListenerFailureSink sink = new ListenerFailureSink())
            using (JsonTracer tracer = new JsonTracer("Microsoft-GVFS-Test", "Root", disableTelemetry: true))
            using (MockListener local = new MockListener(EventLevel.Verbose, Keywords.Any))
            using (TelemetryDaemonEventListener remote = new TelemetryDaemonEventListener("Microsoft.Git.GVFS", "synthetic-enlistment", "synthetic-mount", pipeName, sink, clock))
            {
                tracer.AddEventListener(local);
                tracer.AddEventListener(remote);
                using (NamedPipeServerStream server = CreateServer(pipeName))
                using (StreamReader reader = new StreamReader(server, Encoding.UTF8, false, 1024, leaveOpen: true))
                {
                    Task connection = server.WaitForConnectionAsync(timeout.Token);
                    TraceRejection(tracer);
                    await connection;
                    Assert.That((await ReadMessage(reader, timeout.Token)).EventName, Is.EqualTo("NetworkError"));

                    using (ITracer child = tracer.StartActivity("Child", EventLevel.Informational))
                    {
                        TraceRejection(child);
                    }

                    tracer.RelatedEvent(EventLevel.Informational, "BeforeDisconnect", null, Keywords.Telemetry);
                    Assert.That((await ReadMessage(reader, timeout.Token)).EventName, Is.EqualTo("BeforeDisconnect"));
                }

                tracer.RelatedEvent(EventLevel.Informational, "DetectBrokenPipe", null, Keywords.Telemetry);
                Assert.That(sink.Failed.Wait(TimeSpan.FromSeconds(5)), Is.True);

                using (NamedPipeServerStream server = CreateServer(pipeName))
                using (StreamReader reader = new StreamReader(server, Encoding.UTF8, false, 1024, leaveOpen: true))
                {
                    Task connection = server.WaitForConnectionAsync(timeout.Token);
                    tracer.RelatedEvent(EventLevel.Informational, "Reconnected", null, Keywords.Telemetry);
                    await connection;
                    Assert.That((await ReadMessage(reader, timeout.Token)).EventName, Is.EqualTo("Reconnected"));
                    TraceRejection(tracer);
                    clock.Advance(TimeSpan.FromSeconds(60));
                    tracer.RelatedEvent(EventLevel.Informational, "Flush", null, Keywords.Telemetry);
                    TelemetryDaemonEventListener.PipeMessage summary = await ReadMessage(reader, timeout.Token);
                    Assert.That(summary.EventName, Is.EqualTo("TelemetrySummary"));
                    Assert.That(summary.Payload.EnlistmentId, Is.EqualTo("synthetic-enlistment"));
                    Assert.That(summary.Payload.MountId, Is.EqualTo("synthetic-mount"));
                    using (JsonDocument payload = JsonDocument.Parse(summary.Payload.Json))
                    {
                        Assert.That(payload.RootElement.GetProperty("ObservedCount").GetInt64(), Is.EqualTo(3));
                        Assert.That(payload.RootElement.GetProperty("SampleCount").GetInt64(), Is.EqualTo(1));
                        Assert.That(payload.RootElement.GetProperty("SuppressedCount").GetInt64(), Is.EqualTo(2));
                    }

                    Assert.That((await ReadMessage(reader, timeout.Token)).EventName, Is.EqualTo("Flush"));
                    Assert.That(local.EventNamesRead.Count(name => name == "NetworkError"), Is.EqualTo(3));
                    remote.Dispose();
                }
            }
        }

        private static NamedPipeServerStream CreateServer(string pipeName)
        {
            return new NamedPipeServerStream(pipeName, PipeDirection.In, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
        }

        private static async Task<TelemetryDaemonEventListener.PipeMessage> ReadMessage(StreamReader reader, CancellationToken token)
        {
            return TelemetryDaemonEventListener.PipeMessage.FromJson(await reader.ReadLineAsync(token));
        }

        private static void TraceRejection(ITracer tracer)
        {
            EventMetadata metadata = new EventMetadata();
            metadata.SetTelemetryAggregation("Download", TelemetryFailureCategory.CircuitBreakerOpen, "Rejected");
            tracer.RelatedError(metadata, "Local rejection", Keywords.Network);
        }

        private sealed class ListenerFailureSink : IEventListenerEventSink, IDisposable
        {
            public ManualResetEventSlim Failed { get; } = new ManualResetEventSlim();

            public void OnListenerFailure(EventListener listener, string errorMessage)
            {
                this.Failed.Set();
            }

            public void OnListenerRecovery(EventListener listener)
            {
            }

            public void Dispose()
            {
                this.Failed.Dispose();
            }
        }

        [TestCase]
        public void TraceMessageDataIsCorrectFormat()
        {
            const string vfsVersion = "test-vfsVersion";
            const string vfsArchitecture = "test-architecture";
            const string providerName = "test-ProviderName";
            const string eventName = "test-eventName";
            const EventLevel level = EventLevel.Error;
            const EventOpcode opcode = EventOpcode.Start;
            const string enlistmentId = "test-enlistmentId";
            const string mountId = "test-mountId";
            const string gitCommandSessionId = "test_sessionId";
            const string payload = "test-payload";

            TelemetryDaemonEventListener.PipeMessage message = new TelemetryDaemonEventListener.PipeMessage
            {
                Version = vfsVersion,
                Architecture = vfsArchitecture,
                ProviderName = providerName,
                EventName = eventName,
                EventLevel = level,
                EventOpcode = opcode,
                Payload = new TelemetryDaemonEventListener.PipeMessage.PipeMessagePayload
                {
                    EnlistmentId = enlistmentId,
                    MountId = mountId,
                    GitCommandSessionId = gitCommandSessionId,
                    Json = payload
                },
            };

            string messageJson = message.ToJson();

            using (JsonDocument doc = JsonDocument.Parse(messageJson))
            {
                JsonElement root = doc.RootElement;
                root.EnumerateObject().Count().ShouldEqual(7);
                root.GetProperty("version").GetString().ShouldEqual(vfsVersion);
                root.GetProperty("architecture").GetString().ShouldEqual(vfsArchitecture);
                root.GetProperty("providerName").GetString().ShouldEqual(providerName);
                root.GetProperty("eventName").GetString().ShouldEqual(eventName);
                root.GetProperty("eventLevel").GetInt32().ShouldEqual((int)level);
                root.GetProperty("eventOpcode").GetInt32().ShouldEqual((int)opcode);

                JsonElement payloadElement = root.GetProperty("payload");
                payloadElement.EnumerateObject().Count().ShouldEqual(4);
                payloadElement.GetProperty("enlistmentId").GetString().ShouldEqual(enlistmentId);
                payloadElement.GetProperty("mountId").GetString().ShouldEqual(mountId);
                payloadElement.GetProperty("gitCommandSessionId").GetString().ShouldEqual(gitCommandSessionId);
                payloadElement.GetProperty("json").GetString().ShouldEqual(payload);
            }
        }
    }
}
