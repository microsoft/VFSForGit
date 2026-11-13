using GVFS.Common.Http;
using GVFS.Common.Tracing;
using GVFS.UnitTests.Mock.Common;
using System;
using System.IO;
using System.Linq;
using System.Threading;

namespace GVFS.UnitTests.Http
{
    internal static class HttpResponseStreamTestSupport
    {
        internal static HttpResponseStream CreateStream(
            Stream innerStream,
            MockTracer tracer = null,
            TimeSpan? readTimeout = null)
        {
            return new HttpResponseStream(
                innerStream,
                readTimeout ?? TimeSpan.FromMilliseconds(300),
                CancellationToken.None,
                tracer,
                new EventMetadata
                {
                    { "RequestId", 42L },
                    { "CacheName", "test-cache" },
                    { "RequestAuthority", "cache.example:443" },
                },
                progressInterval: TimeSpan.FromMilliseconds(30));
        }

        internal static MockTracer.RecordedEvent LastBodyEvent(MockTracer tracer, string outcome)
        {
            return tracer.SnapshotRelatedEvents()
                .Where(record => record.Metadata != null &&
                    record.Metadata.TryGetValue("Outcome", out object value) && (string)value == outcome)
                .OrderBy(record => (long)record.Metadata["SampleSequence"])
                .Last();
        }

        internal class CallbackTracer : MockTracer
        {
            public Action<string> Callback { get; set; }
            public Action<string, EventMetadata> EventCallback { get; set; }

            public override void RelatedEvent(EventLevel level, string eventName, EventMetadata metadata)
            {
                this.RelatedEvent(level, eventName, metadata, Keywords.None);
            }

            public override void RelatedEvent(EventLevel level, string eventName, EventMetadata metadata, Keywords keyword)
            {
                base.RelatedEvent(level, eventName, metadata, keyword);
                this.EventCallback?.Invoke(eventName, metadata);
                this.Callback?.Invoke(eventName);
            }
        }
    }
}
