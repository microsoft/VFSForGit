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
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using GVFS.UnitTests.Mock.Common.Tracing;
using GVFS.CommandLine;
using GVFS.Common.FileSystem;
using GVFS.Common.NamedPipes;
using GVFS.UnitTests.Mock.Common;
using GVFS.UnitTests.Category;

namespace GVFS.UnitTests.Tracing
{
    [TestFixture]
    public class TelemetryDaemonEventListenerTests
    {
        [TestCase(false)]
        [TestCase(true)]
        [Category(CategoryConstants.ExceptionExpected)]
        public async Task HydrationMetadataReachesTheCollectorWithNullsAndNestedCounts(bool cacheHit)
        {
            string pipeName = "GVFS.UnitTests.Hydration." + Guid.NewGuid().ToString("N");
            using (CancellationTokenSource deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10)))
            using (NamedPipeServerStream server = new NamedPipeServerStream(pipeName, PipeDirection.In, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous))
            {
                Task<string> received = ReadTelemetryAsync(server, deadline.Token);
                using (JsonTracer tracer = new JsonTracer("test", "health", disableTelemetry: true))
                using (StringWriter output = new StringWriter())
                {
                    tracer.AddEventListener(new TelemetryDaemonEventListener("test", "test-enlistment", "test-mount", pipeName, eventSink: null));
                    CollectorHealthVerb verb = new CollectorHealthVerb { StatusOnly = true, CacheHit = cacheHit, Output = output };
                    verb.ExecuteWithCompletion(null, tracer, Guid.NewGuid());
                }

                string[] lines = (await received).Split('\n', StringSplitOptions.RemoveEmptyEntries);
                Assert.AreEqual(cacheHit ? 1 : 2, lines.Length);
                TelemetryDaemonEventListener.PipeMessage health = TelemetryDaemonEventListener.PipeMessage.FromJson(lines.Last());
                Assert.AreEqual("health", health.EventName);
                Assert.AreEqual("test-enlistment", health.Payload.EnlistmentId);
                using (JsonDocument json = JsonDocument.Parse(health.Payload.Json))
                {
                    JsonElement root = json.RootElement;
                    Assert.AreEqual(cacheHit ? "Success" : "Failed", root.GetProperty("Outcome").GetString());
                    if (cacheHit)
                    {
                        Assert.AreEqual(0, root.GetProperty("Cache").GetProperty("Counts").GetProperty("ModifiedFileCount").GetInt32());
                        Assert.IsFalse(root.TryGetProperty("Summary", out _));
                    }
                    else
                    {
                        JsonElement summary = root.GetProperty("Summary");
                        Assert.AreEqual(100, summary.GetProperty("TotalFileCount").GetInt32());
                        Assert.AreEqual(JsonValueKind.Null, summary.GetProperty("ModifiedFileCount").ValueKind);
                        Assert.AreEqual(JsonValueKind.Null, summary.GetProperty("TotalFolderCount").ValueKind);
                        Assert.AreEqual("Failed", summary.GetProperty("Outcome").GetString());
                        TelemetryDaemonEventListener.PipeMessage duration = TelemetryDaemonEventListener.PipeMessage.FromJson(lines[0]);
                        Assert.AreEqual("HydrationSummaryDuration", duration.EventName);
                        using (JsonDocument calculation = JsonDocument.Parse(duration.Payload.Json))
                        {
                            Assert.AreEqual(summary.GetRawText(), calculation.RootElement.GetRawText());
                            Assert.AreEqual(root.GetProperty("CommandId").GetString(), summary.GetProperty("CommandId").GetString());
                        }
                    }
                }

                Assert.That(string.Join("\n", lines), Does.Not.Contain("private"));
            }
        }

        private sealed class CollectorHealthVerb : HealthVerb
        {
            public bool CacheHit { get; set; }

            internal override CacheLookupResult TryGetCachedHydrationStatus(GVFSEnlistment enlistment)
            {
                return ParseCachedResponse(this.CacheHit
                    ? new NamedPipeMessages.Message("S", "0,0,0,0,100,10")
                    : new NamedPipeMessages.Message("NA", null));
            }

            internal override EnlistmentHydrationSummary CreateHydrationSummary(GVFSEnlistment enlistment, ITracer tracer, HydrationSummaryDiagnostics diagnostics)
            {
                return EnlistmentHydrationSummary.CreateSummary(new MockGVFSEnlistment(), new HeaderFileSystem(), tracer,
                    () => 10, new FailingPlaceholderPaths(), diagnostics: diagnostics);
            }
        }

        private sealed class HeaderFileSystem : PhysicalFileSystem
        {
            public override Stream OpenFileStream(string path, FileMode fileMode, FileAccess fileAccess, FileShare shareMode, FileOptions options, bool flushesToDisk)
            {
                return new MemoryStream(new byte[] { 68, 73, 82, 67, 0, 0, 0, 4, 0, 0, 0, 100 });
            }
        }

        private sealed class FailingPlaceholderPaths : EnlistmentPathData
        {
            public override void LoadPlaceholdersFromDatabase(GVFSEnlistment enlistment, HydrationSummaryDiagnostics diagnostics = null)
            {
                throw new IOException("mock:\\private\\database");
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
        [Category(CategoryConstants.ExceptionExpected)]
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

        [TestCase]
        public async Task DisposalDeliversPendingSummaryAndPreservesLegacyResult()
        {
            string pipeName = "gvfs-telemetry-test-" + Guid.NewGuid().ToString("N");
            using (CancellationTokenSource timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15)))
            using (NamedPipeServerStream server = CreateServer(pipeName))
            using (StreamReader reader = new StreamReader(server, Encoding.UTF8, false, 1024, leaveOpen: true))
            using (JsonTracer tracer = new JsonTracer("Microsoft-GVFS-Test", "Root", disableTelemetry: true))
            using (TelemetryDaemonEventListener remote = new TelemetryDaemonEventListener("Microsoft.Git.GVFS", "synthetic-enlistment", "synthetic-mount", pipeName, null))
            {
                tracer.AddEventListener(remote);
                Task connection = server.WaitForConnectionAsync(timeout.Token);
                for (int i = 0; i < 2; ++i)
                {
                    EventMetadata metadata = new EventMetadata { { "result", "0xC0000467" } };
                    metadata.SetTelemetryAggregation("StartDirectoryEnumerationAsyncHandler", TelemetryFailureCategory.SizesUnavailable, "Failed");
                    tracer.RelatedError(metadata, "Synthetic size unavailable");
                }

                await connection;
                TelemetryDaemonEventListener.PipeMessage sample = await ReadMessage(reader, timeout.Token);
                using (JsonDocument payload = JsonDocument.Parse(sample.Payload.Json))
                {
                    Assert.That(payload.RootElement.GetProperty("result").GetString(), Is.EqualTo("0xC0000467"));
                    Assert.That(payload.RootElement.GetProperty("AggregationResult").GetString(), Is.EqualTo("Failed"));
                    Assert.That(payload.RootElement.GetProperty("TelemetrySchemaVersion").GetInt32(), Is.EqualTo(3));
                    Assert.That(payload.RootElement.GetProperty("TelemetryAggregation").GetString(), Is.EqualTo("Sample"));
                    Assert.That(payload.RootElement.TryGetProperty("Result", out _), Is.False);
                }

                Task disposal = Task.Run(remote.Dispose);
                TelemetryDaemonEventListener.PipeMessage summary = await ReadMessage(reader, timeout.Token);
                Assert.That(summary.EventName, Is.EqualTo("TelemetrySummary"));
                using (JsonDocument payload = JsonDocument.Parse(summary.Payload.Json))
                {
                    Assert.That(payload.RootElement.GetProperty("ObservedCount").GetInt64(), Is.EqualTo(2));
                    Assert.That(payload.RootElement.GetProperty("SampleCount").GetInt64(), Is.EqualTo(1));
                    Assert.That(payload.RootElement.GetProperty("SuppressedCount").GetInt64(), Is.EqualTo(1));
                    Assert.That(payload.RootElement.GetProperty("AggregationResult").GetString(), Is.EqualTo("Failed"));
                }

                await disposal.WaitAsync(timeout.Token);
                remote.Dispose();
            }
        }

        [TestCase]
        public async Task CrossCommandSummaryOmitsSessionButSamplesRetainIt()
        {
            string pipeName = "gvfs-telemetry-test-" + Guid.NewGuid().ToString("N");
            TelemetryAggregatorTests.TestClock clock = new TelemetryAggregatorTests.TestClock();
            using (CancellationTokenSource timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15)))
            using (NamedPipeServerStream server = CreateServer(pipeName))
            using (StreamReader reader = new StreamReader(server, Encoding.UTF8, false, 1024, leaveOpen: true))
            using (JsonTracer tracer = new JsonTracer("Microsoft-GVFS-Test", "Root", disableTelemetry: true))
            using (TelemetryDaemonEventListener remote = new TelemetryDaemonEventListener("Microsoft.Git.GVFS", "synthetic-enlistment", "synthetic-mount", pipeName, null, clock))
            {
                tracer.AddEventListener(remote);
                Task connection = server.WaitForConnectionAsync(timeout.Token);
                tracer.SetGitCommandSessionId("session-A");
                TraceRejection(tracer);
                await connection;
                Assert.That((await ReadMessage(reader, timeout.Token)).Payload.GitCommandSessionId, Is.EqualTo("session-A"));

                tracer.SetGitCommandSessionId("session-B");
                TraceRejection(tracer);
                tracer.SetGitCommandSessionId("session-C");
                clock.Advance(TimeSpan.FromSeconds(60));
                tracer.RelatedEvent(EventLevel.Informational, "HealthyEvent", null, Keywords.Telemetry);
                string summaryJson = await reader.ReadLineAsync(timeout.Token);
                using (JsonDocument envelope = JsonDocument.Parse(summaryJson))
                {
                    Assert.That(envelope.RootElement.GetProperty("payload").TryGetProperty("gitCommandSessionId", out _), Is.False);
                }

                TelemetryDaemonEventListener.PipeMessage summary = TelemetryDaemonEventListener.PipeMessage.FromJson(summaryJson);
                Assert.That(summary.EventName, Is.EqualTo("TelemetrySummary"));
                Assert.That(summary.Payload.EnlistmentId, Is.EqualTo("synthetic-enlistment"));
                Assert.That(summary.Payload.MountId, Is.EqualTo("synthetic-mount"));
                using (JsonDocument payload = JsonDocument.Parse(summary.Payload.Json))
                {
                    Assert.That(payload.RootElement.GetProperty("ObservedCount").GetInt64(), Is.EqualTo(2));
                    Assert.That(payload.RootElement.GetProperty("SampleCount").GetInt64(), Is.EqualTo(1));
                    Assert.That(payload.RootElement.GetProperty("SuppressedCount").GetInt64(), Is.EqualTo(1));
                }

                TelemetryDaemonEventListener.PipeMessage healthy = await ReadMessage(reader, timeout.Token);
                Assert.That(healthy.EventName, Is.EqualTo("HealthyEvent"));
                Assert.That(healthy.Payload.GitCommandSessionId, Is.EqualTo("session-C"));
            }
        }

        [TestCase]
        public async Task BreakerRecoverySampleSurvivesInitialAcceptingAndRepeatedTransitions()
        {
            RetryCircuitBreaker.Reset();
            try
            {
                string pipeName = "gvfs-telemetry-test-" + Guid.NewGuid().ToString("N");
                TelemetryAggregatorTests.TestClock clock = new TelemetryAggregatorTests.TestClock();
                using (CancellationTokenSource timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15)))
                using (NamedPipeServerStream server = CreateServer(pipeName))
                using (StreamReader reader = new StreamReader(server, Encoding.UTF8, false, 1024, leaveOpen: true))
                using (JsonTracer tracer = new JsonTracer("Microsoft-GVFS-Test", "Root", disableTelemetry: true))
                using (TelemetryDaemonEventListener remote = new TelemetryDaemonEventListener("Microsoft.Git.GVFS", "synthetic-enlistment", "synthetic-mount", pipeName, null, clock))
                {
                    tracer.AddEventListener(remote);
                    Task connection = server.WaitForConnectionAsync(timeout.Token);
                    RetryWrapper<bool> wrapper = new RetryWrapper<bool>(1, CancellationToken.None, tracer: tracer);
                    wrapper.Invoke(attempt => new RetryWrapper<bool>.CallbackResult(true));
                    wrapper.Invoke(attempt =>
                    {
                        RetryCircuitBreaker.Configure(1, 30000);
                        return new RetryWrapper<bool>.CallbackResult(new IOException("Synthetic failure"), shouldRetry: true);
                    });
                    RetryCircuitBreaker.Configure(1, 0);
                    wrapper.Invoke(attempt => new RetryWrapper<bool>.CallbackResult(true));
                    await connection;

                    string[] previousStates = { "Unknown", "Accepting", "Rejecting", "CooldownElapsed" };
                    string[] states = { "Accepting", "Rejecting", "CooldownElapsed", "Accepting" };
                    for (int i = 0; i < states.Length; ++i)
                    {
                        TelemetryDaemonEventListener.PipeMessage sample = await ReadMessage(reader, timeout.Token);
                        Assert.That(sample.EventName, Is.EqualTo("RetryCircuitBreakerState"));
                        using (JsonDocument payload = JsonDocument.Parse(sample.Payload.Json))
                        {
                            Assert.That(payload.RootElement.GetProperty("PreviousState").GetString(), Is.EqualTo(previousStates[i]));
                            Assert.That(payload.RootElement.GetProperty("State").GetString(), Is.EqualTo(states[i]));
                            Assert.That(payload.RootElement.GetProperty("AggregationResult").GetString(), Is.EqualTo(states[i]));
                        }
                    }

                    clock.Advance(TimeSpan.FromSeconds(10));
                    wrapper.Invoke(attempt =>
                    {
                        RetryCircuitBreaker.Configure(1, 30000);
                        return new RetryWrapper<bool>.CallbackResult(new IOException("Synthetic failure"), shouldRetry: true);
                    });
                    RetryCircuitBreaker.Configure(1, 0);
                    clock.Advance(TimeSpan.FromSeconds(2));
                    wrapper.Invoke(attempt =>
                    {
                        clock.Advance(TimeSpan.FromSeconds(1));
                        return new RetryWrapper<bool>.CallbackResult(true);
                    });
                    tracer.RelatedEvent(EventLevel.Informational, "RepeatedTransitionsComplete", null, Keywords.Telemetry);
                    Assert.That((await ReadMessage(reader, timeout.Token)).EventName, Is.EqualTo("RepeatedTransitionsComplete"));

                    clock.Advance(TimeSpan.FromSeconds(60));
                    tracer.RelatedEvent(EventLevel.Informational, "Flush", null, Keywords.Telemetry);
                    for (int i = 0; i < states.Length; ++i)
                    {
                        TelemetryDaemonEventListener.PipeMessage summary = await ReadMessage(reader, timeout.Token);
                        Assert.That(summary.EventName, Is.EqualTo("TelemetrySummary"));
                        using (JsonDocument payload = JsonDocument.Parse(summary.Payload.Json))
                        {
                            Assert.That(payload.RootElement.GetProperty("PreviousState").GetString(), Is.EqualTo(previousStates[i]));
                            Assert.That(payload.RootElement.GetProperty("AggregationResult").GetString(), Is.EqualTo(states[i]));
                            Assert.That(payload.RootElement.GetProperty("ObservedCount").GetInt64(), Is.EqualTo(i == 0 ? 1 : 2));
                            Assert.That(payload.RootElement.GetProperty("SampleCount").GetInt64(), Is.EqualTo(1));
                            Assert.That(payload.RootElement.GetProperty("SuppressedCount").GetInt64(), Is.EqualTo(i == 0 ? 0 : 1));
                            int[] lastSeconds = { 0, 10, 12, 13 };
                            Assert.That(payload.RootElement.GetProperty("LastObservedAtUtc").GetDateTime(), Is.EqualTo(clock.UtcStart.AddSeconds(lastSeconds[i]).UtcDateTime));
                        }
                    }

                    Assert.That((await ReadMessage(reader, timeout.Token)).EventName, Is.EqualTo("Flush"));
                }
            }
            finally
            {
                RetryCircuitBreaker.Reset();
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
