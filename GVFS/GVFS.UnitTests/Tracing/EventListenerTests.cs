using System;
using GVFS.Common.Tracing;
using NUnit.Framework;

namespace GVFS.UnitTests.Tracing
{
    [TestFixture]
    public class EventListenerTests
    {
        [TestCase]
        public void EventListener_TryRecordMessage_ExceptionThrownInternally_ReturnsFalseAndErrorMessage()
        {
            string expectedErrorMessage = "test error message";

            var message = new TraceEventMessage { Level = EventLevel.Error, Keywords = Keywords.None };
            var listener = new TestEventListener(EventLevel.Informational, Keywords.Any)
            {
                RecordMessageInternalCallback = _ => throw new Exception(expectedErrorMessage)
            };

            string actualErrorMessage;
            bool? actualResult = listener.TryRecordMessage(message, out actualErrorMessage);

            Assert.IsFalse(actualResult);
            Assert.IsTrue(actualErrorMessage.Contains(expectedErrorMessage));
        }

        [TestCase]
        public void EventListener_TryRecordMessage_ListenerNotEnabledForMessage_ReturnsNull()
        {
            var message = new TraceEventMessage { Level = EventLevel.Informational, Keywords = Keywords.None };
            var listener = new TestEventListener(EventLevel.Critical, Keywords.Telemetry);

            string actualErrorMessage;
            bool? actualResult = listener.TryRecordMessage(message, out actualErrorMessage);

            Assert.IsNull(actualResult);
            Assert.IsNull(actualErrorMessage);
        }

        [TestCase]
        public void EventListener_TryRecordMessage_MessageSentSuccessfully_ReturnsTrue()
        {
            var message = new TraceEventMessage { Level = EventLevel.Error, Keywords = Keywords.None };
            var listener = new TestEventListener(EventLevel.Informational, Keywords.Any);

            string actualErrorMessage;
            bool? actualResult = listener.TryRecordMessage(message, out actualErrorMessage);

            Assert.IsTrue(actualResult);
            Assert.IsNull(actualErrorMessage);
        }

        private class TestEventListener : EventListener
        {
            public TestEventListener(EventLevel maxVerbosity, Keywords keywordFilter)
                : base(maxVerbosity, keywordFilter)
            {
            }

            public Action<TraceEventMessage> RecordMessageInternalCallback { get; set; }

            protected override void RecordMessageInternal(TraceEventMessage message)
            {
                this.RecordMessageInternalCallback?.Invoke(message);
            }
        }
    }
}
