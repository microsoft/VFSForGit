using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using GVFS.Common.Tracing;
using GVFS.Tests.Should;
using NUnit.Framework;
using GVFS.Common;
using System;
using System.IO;
using System.IO.Pipes;
using System.Threading;
using System.Threading.Tasks;

namespace GVFS.UnitTests.Tracing
{
    [TestFixture]
    public class TelemetryDaemonEventListenerTests
    {
        [TestCase]
        public async Task HydrationMetadataReachesTheCollectorWithNullsAndNestedCounts()
        {
            string pipeName = "GVFS.UnitTests.Hydration." + Guid.NewGuid().ToString("N");
            using (CancellationTokenSource deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10)))
            using (NamedPipeServerStream server = new NamedPipeServerStream(pipeName, PipeDirection.In, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous))
            {
                Task<string> received = ReadTelemetryAsync(server, deadline.Token);
                HydrationSummaryDiagnostics diagnostics = new HydrationSummaryDiagnostics
                {
                    TotalFileCount = 100,
                    ModifiedFileCount = 0,
                    Outcome = HydrationSummaryOutcome.Failed,
                    ExceptionType = typeof(IOException).FullName,
                };
                using (JsonTracer tracer = new JsonTracer("test", "test", disableTelemetry: true))
                {
                    tracer.AddEventListener(new TelemetryDaemonEventListener("test", "test-enlistment", "test-mount", pipeName, eventSink: null));
                    tracer.RelatedEvent(EventLevel.Warning, "HydrationSummaryDuration",
                        new EventMetadata { ["Summary"] = diagnostics.GetMetadata() }, Keywords.Telemetry);
                }

                string line = (await received).Split('\n')[0];
                TelemetryDaemonEventListener.PipeMessage envelope = TelemetryDaemonEventListener.PipeMessage.FromJson(line);
                Assert.AreEqual("HydrationSummaryDuration", envelope.EventName);
                Assert.AreEqual("test-enlistment", envelope.Payload.EnlistmentId);
                using (JsonDocument json = JsonDocument.Parse(envelope.Payload.Json))
                {
                    JsonElement summary = json.RootElement.GetProperty("Summary");
                    Assert.AreEqual(100, summary.GetProperty("TotalFileCount").GetInt32());
                    Assert.AreEqual(0, summary.GetProperty("ModifiedFileCount").GetInt32());
                    Assert.AreEqual(JsonValueKind.Null, summary.GetProperty("TotalFolderCount").ValueKind);
                    Assert.AreEqual("Failed", summary.GetProperty("Outcome").GetString());
                    Assert.AreEqual(diagnostics.CalculationId.ToString("N"), summary.GetProperty("CalculationId").GetString());
                }
            }
        }

        private static async Task<string> ReadTelemetryAsync(NamedPipeServerStream server, CancellationToken cancellationToken)
        {
            await server.WaitForConnectionAsync(cancellationToken).ConfigureAwait(false);
            using (StreamReader reader = new StreamReader(server))
            {
                return await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
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
