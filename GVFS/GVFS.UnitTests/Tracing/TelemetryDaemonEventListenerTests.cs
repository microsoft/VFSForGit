using System;
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
            const Keywords keyword = Keywords.Any;
            const string enlistmentId = "test-enlistmentId";
            const string mountId = "test-mountId";
            const string payload = "test-payload";
            Guid activityId = Guid.NewGuid();
            Guid parentActivityId = Guid.NewGuid();

            var expectedDict = new Dictionary<string, object>
            {
                ["version"] = vfsVersion,
                ["providerName"] = providerName,
                ["eventName"] = eventName,
                ["eventLevel"] = ((int)level).ToString(),
                ["eventOpcode"] = ((int)opcode).ToString(),
                ["payload"] = new Dictionary<string, string>
                {
                    ["enlistmentId"] = enlistmentId,
                    ["mountId"] = mountId,
                    ["json"] = payload,
                },
                ["etw.activityId"] = activityId.ToString("D"),
                ["etw.parentActivityId"] = parentActivityId.ToString("D"),
            };

            string json = TelemetryDaemonEventListener.CreateJsonMessage(
                vfsVersion,
                providerName,
                enlistmentId,
                mountId,
                eventName,
                activityId,
                parentActivityId,
                level,
                keyword,
                opcode,
                payload);

            var actualDict = JsonConvert.DeserializeObject<Dictionary<string, object>>(json);

            Assert.AreEqual(expectedDict.Count, actualDict.Count);
            Assert.AreEqual(expectedDict["version"], actualDict["version"]);
            Assert.AreEqual(expectedDict["providerName"], actualDict["providerName"]);
            Assert.AreEqual(expectedDict["eventName"], actualDict["eventName"]);
            Assert.AreEqual(expectedDict["eventLevel"], actualDict["eventLevel"]);
            Assert.AreEqual(expectedDict["eventOpcode"], actualDict["eventOpcode"]);
            Assert.AreEqual(expectedDict["etw.activityId"], actualDict["etw.activityId"]);
            Assert.AreEqual(expectedDict["etw.parentActivityId"], actualDict["etw.parentActivityId"]);

            var expectedPayloadDict = (Dictionary<string, string>)expectedDict["payload"];
            var actualPayloadDict = JsonConvert.DeserializeObject<Dictionary<string, string>>(actualDict["payload"].ToString());
            Assert.AreEqual(expectedPayloadDict.Count, actualPayloadDict.Count);
            Assert.AreEqual(expectedPayloadDict["enlistmentId"], actualPayloadDict["enlistmentId"]);
            Assert.AreEqual(expectedPayloadDict["mountId"], actualPayloadDict["mountId"]);
            Assert.AreEqual(expectedPayloadDict["json"], actualPayloadDict["json"]);
        }
    }
}
