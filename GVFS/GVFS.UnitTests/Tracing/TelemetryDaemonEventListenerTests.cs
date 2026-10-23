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
