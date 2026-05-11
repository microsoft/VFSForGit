using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using GVFS.Common.Tracing;
using GVFS.Tests.Should;
using NUnit.Framework;

namespace GVFS.UnitTests.Tracing
{
    [TestFixture]
    public class TelemetryDaemonEventListenerTests
    {
        [TestCase]
        public void TraceMessageDataIsCorrectFormat()
        {
            const string vfsVersion = "test-vfsVersion";
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
                root.EnumerateObject().Count().ShouldEqual(6);
                root.GetProperty("version").GetString().ShouldEqual(vfsVersion);
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
