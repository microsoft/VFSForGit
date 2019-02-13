using System.Collections.Generic;
using GVFS.Common.Tracing;
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
                    ["json"] = payload,
                },
            };

            TelemetryDaemonEventListener.TelemetryMessage message = new TelemetryDaemonEventListener.TelemetryMessage
            {
                Version = vfsVersion,
                ProviderName = providerName,
                EventName = eventName,
                EventLevel = level,
                EventOpcode = opcode,
                Payload = new TelemetryDaemonEventListener.TelemetryMessage.TelemetryMessagePayload
                {
                    EnlistmentId = enlistmentId,
                    MountId = mountId,
                    Json = payload
                },
            };

            string messageJson = message.ToJson();

            Dictionary<string, object> actualDict = JsonConvert.DeserializeObject<Dictionary<string, object>>(messageJson);

            Assert.AreEqual(expectedDict.Count, actualDict.Count);
            Assert.AreEqual(expectedDict["version"], actualDict["version"]);
            Assert.AreEqual(expectedDict["providerName"], actualDict["providerName"]);
            Assert.AreEqual(expectedDict["eventName"], actualDict["eventName"]);
            Assert.AreEqual(expectedDict["eventLevel"], actualDict["eventLevel"]);
            Assert.AreEqual(expectedDict["eventOpcode"], actualDict["eventOpcode"]);

            Dictionary<string, string> expectedPayloadDict = (Dictionary<string, string>)expectedDict["payload"];
            Dictionary<string, string> actualPayloadDict = JsonConvert.DeserializeObject<Dictionary<string, string>>(actualDict["payload"].ToString());
            Assert.AreEqual(expectedPayloadDict.Count, actualPayloadDict.Count);
            Assert.AreEqual(expectedPayloadDict["enlistmentId"], actualPayloadDict["enlistmentId"]);
            Assert.AreEqual(expectedPayloadDict["mountId"], actualPayloadDict["mountId"]);
            Assert.AreEqual(expectedPayloadDict["json"], actualPayloadDict["json"]);
        }
    }
}
