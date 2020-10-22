using System.Collections.Generic;
using GVFS.Common.Tracing;
using GVFS.Tests.Should;
using Newtonsoft.Json;
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

            Dictionary<string, object> expectedDict = new Dictionary<string, object>
            {
                ["version"] = vfsVersion,
                ["providerName"] = providerName,
                ["eventName"] = eventName,
                ["eventLevel"] = (int)level,
                ["eventOpcode"] = (int)opcode,
                ["payload"] = new Dictionary<string, string>
                {
                    ["enlistmentId"] = enlistmentId,
                    ["mountId"] = mountId,
                    ["gitCommandSessionId"] = gitCommandSessionId,
                    ["json"] = payload,
                },
            };

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

            Dictionary<string, object> actualDict = JsonConvert.DeserializeObject<Dictionary<string, object>>(messageJson);

            actualDict.Count.ShouldEqual(expectedDict.Count);
            actualDict["version"].ShouldEqual(expectedDict["version"]);
            actualDict["providerName"].ShouldEqual(expectedDict["providerName"]);
            actualDict["eventName"].ShouldEqual(expectedDict["eventName"]);
            actualDict["eventLevel"].ShouldEqual(expectedDict["eventLevel"]);
            actualDict["eventOpcode"].ShouldEqual(expectedDict["eventOpcode"]);

            Dictionary<string, string> expectedPayloadDict = (Dictionary<string, string>)expectedDict["payload"];
            Dictionary<string, string> actualPayloadDict = JsonConvert.DeserializeObject<Dictionary<string, string>>(actualDict["payload"].ToString());
            actualPayloadDict.Count.ShouldEqual(expectedPayloadDict.Count);
            actualPayloadDict["enlistmentId"].ShouldEqual(expectedPayloadDict["enlistmentId"]);
            actualPayloadDict["mountId"].ShouldEqual(expectedPayloadDict["mountId"]);
            actualPayloadDict["gitCommandSessionId"].ShouldEqual(expectedPayloadDict["gitCommandSessionId"]);
            actualPayloadDict["json"].ShouldEqual(expectedPayloadDict["json"]);
        }
    }
}
