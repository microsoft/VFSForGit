using System;
using GVFS.Common.Tracing;
using Moq;
using NUnit.Framework;

namespace GVFS.UnitTests.Tracing
{
    [TestFixture]
    public class EventListenerTests
    {
        [TestCase]
        public void EventListener_RecordMessage_ExceptionThrownInternally_RaisesFailureEventWithErrorMessage()
        {
            string expectedErrorMessage = $"test error message unique={Guid.NewGuid():N}";

            Mock<IEventListenerEventSink> eventSink = new Mock<IEventListenerEventSink>();

            TraceEventMessage message = new TraceEventMessage { Level = EventLevel.Error, Keywords = Keywords.None };
            TestEventListener listener = new TestEventListener(EventLevel.Informational, Keywords.Any, eventSink.Object)
            {
                RecordMessageInternalCallback = _ => throw new Exception(expectedErrorMessage)
            };

            listener.RecordMessage(message);

            eventSink.Verify(
                x => x.OnListenerFailure(listener, It.Is<string>(msg => msg.Contains(expectedErrorMessage))),
                times: Times.Once);
        }

        private class TestEventListener : EventListener
        {
            public TestEventListener(EventLevel maxVerbosity, Keywords keywordFilter, IEventListenerEventSink eventSink)
                : base(maxVerbosity, keywordFilter, eventSink)
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
