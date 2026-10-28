using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using GVFS.Common.Tracing;
using NUnit.Framework;

namespace GVFS.UnitTests.Tracing
{
    [TestFixture]
    public class TelemetryAggregatorTests
    {
        [TestCase]
        public void StormRetainsOneSampleAndExactIntervalCounters()
        {
            TestClock clock = new TestClock();
            TelemetryAggregator aggregator = new TelemetryAggregator(clock);
            List<TraceEventMessage> output = new List<TraceEventMessage>();
            for (int i = 0; i < 10000; ++i)
            {
                TraceEventMessage message = CreateMessage("QueryFileSizes");
                message.Payload = "{\"RequestId\":" + i + "}";
                aggregator.Record(message, output.Add);
            }

            Assert.That(output.Count, Is.EqualTo(1));
            clock.Advance(TimeSpan.FromSeconds(60));
            aggregator.Record(new TraceEventMessage { EventName = "UnfamiliarFailure" }, output.Add);

            Assert.That(output.Select(message => message.EventName), Is.EqualTo(new[] { "NetworkError", "TelemetrySummary", "UnfamiliarFailure" }));
            using (JsonDocument summary = JsonDocument.Parse(output[1].Payload))
            {
                JsonElement root = summary.RootElement;
                AssertCounts(root, 10000, 1, 9999);
                Assert.That(root.GetProperty("Operation").GetString(), Is.EqualTo("QueryFileSizes"));
                Assert.That(root.GetProperty("IntervalStartUtc").GetDateTime(), Is.EqualTo(clock.UtcStart.UtcDateTime));
                Assert.That(root.GetProperty("IntervalEndUtc").GetDateTime(), Is.EqualTo(clock.UtcStart.AddMinutes(1).UtcDateTime));
                Assert.That(root.TryGetProperty("RequestId", out _), Is.False);
            }

            aggregator.Record(CreateMessage("QueryFileSizes"), output.Add);
            Assert.That(output.Count, Is.EqualTo(4), "The new interval permits a fresh sample.");
            aggregator.Flush(output.Add);
            aggregator.Flush(output.Add);
            Assert.That(output.Count, Is.EqualTo(5), "Repeated disposal flushes must not duplicate counters.");
        }

        [TestCase]
        public void CapacityOverflowIsBoundedAndCountedWithoutHidingUnknownFailures()
        {
            TelemetryAggregator aggregator = new TelemetryAggregator();
            List<TraceEventMessage> output = new List<TraceEventMessage>();
            for (int i = 0; i < TelemetryAggregator.Capacity + 100; ++i)
            {
                aggregator.Record(CreateMessage("Operation" + i), output.Add);
            }

            TraceEventMessage unknown = new TraceEventMessage { EventName = "NewNetworkFailure", Payload = "{\"HttpStatusCode\":401}" };
            aggregator.Record(unknown, output.Add);
            Assert.That(output.Count, Is.EqualTo(TelemetryAggregator.Capacity + 1));
            Assert.That(output.Last(), Is.SameAs(unknown));

            aggregator.Flush(output.Add);
            Assert.That(output.Count(message => message.EventName == "TelemetrySummary"), Is.EqualTo(TelemetryAggregator.Capacity + 1));
            using (JsonDocument overflow = JsonDocument.Parse(output.Last().Payload))
            {
                AssertCounts(overflow.RootElement, 100, 0, 100);
                Assert.That(overflow.RootElement.GetProperty("OverflowCount").GetInt64(), Is.EqualTo(100));
            }
        }

        [TestCase]
        public void ConcurrentObservationsDoNotDuplicateSamplesOrLoseCounts()
        {
            TelemetryAggregator aggregator = new TelemetryAggregator();
            ConcurrentQueue<TraceEventMessage> output = new ConcurrentQueue<TraceEventMessage>();
            Parallel.For(0, 10000, i => aggregator.Record(CreateMessage("Download"), output.Enqueue));
            Assert.That(output.Count, Is.EqualTo(1));

            aggregator.Flush(output.Enqueue);
            using (JsonDocument summary = JsonDocument.Parse(output.Last().Payload))
            {
                AssertCounts(summary.RootElement, 10000, 1, 9999);
            }
        }

        [TestCase]
        public void EvictionBatchWeightsKeepSuccessAndFailureSeparate()
        {
            TelemetryAggregator aggregator = new TelemetryAggregator();
            List<TraceEventMessage> output = new List<TraceEventMessage>();
            aggregator.Record(CreateMessage("EvictLruCommit", TelemetryFailureCategory.None, "Succeeded", 12), output.Add);
            aggregator.Record(CreateMessage("EvictLruCommit", TelemetryFailureCategory.EvictionUnavailable, "CouldNotEvict", 3), output.Add);
            aggregator.Flush(output.Add);

            using (JsonDocument success = JsonDocument.Parse(output[2].Payload))
            using (JsonDocument failure = JsonDocument.Parse(output[3].Payload))
            {
                AssertCounts(success.RootElement, 12, 1, 11);
                AssertCounts(failure.RootElement, 3, 1, 2);
                Assert.That(success.RootElement.GetProperty("Result").GetString(), Is.EqualTo("Succeeded"));
                Assert.That(failure.RootElement.GetProperty("Result").GetString(), Is.EqualTo("CouldNotEvict"));
            }
        }

        private static TraceEventMessage CreateMessage(
            string operation,
            TelemetryFailureCategory category = TelemetryFailureCategory.CircuitBreakerOpen,
            string result = "Rejected",
            long count = 1)
        {
            return new TraceEventMessage
            {
                EventName = "NetworkError",
                Level = EventLevel.Error,
                Keywords = Keywords.Telemetry,
                TelemetryAggregation = new TelemetryAggregation(operation, category, result, count),
            };
        }

        private static void AssertCounts(JsonElement root, long observed, long sampled, long suppressed)
        {
            Assert.That(root.GetProperty("ObservedCount").GetInt64(), Is.EqualTo(observed));
            Assert.That(root.GetProperty("SampleCount").GetInt64(), Is.EqualTo(sampled));
            Assert.That(root.GetProperty("SuppressedCount").GetInt64(), Is.EqualTo(suppressed));
        }

        internal sealed class TestClock : TimeProvider
        {
            private TimeSpan elapsed;

            public DateTimeOffset UtcStart { get; } = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
            public override long TimestampFrequency => TimeSpan.TicksPerSecond;

            public override DateTimeOffset GetUtcNow() => this.UtcStart + this.elapsed;
            public override long GetTimestamp() => this.elapsed.Ticks;

            public void Advance(TimeSpan interval)
            {
                this.elapsed += interval;
            }
        }
    }
}
