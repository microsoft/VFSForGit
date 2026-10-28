using System;
using System.Collections.Generic;

namespace GVFS.Common.Tracing
{
    internal sealed class TelemetryAggregator
    {
        internal const int Capacity = 64;
        private static readonly TimeSpan Interval = TimeSpan.FromSeconds(60);

        private readonly object gate = new object();
        private readonly TimeProvider timeProvider;
        private readonly Dictionary<(string EventName, string Operation, string Category, string Result), Counter> counters = new Dictionary<(string, string, string, string), Counter>();
        private Counter overflow;
        private long intervalStarted;

        public TelemetryAggregator(TimeProvider timeProvider = null)
        {
            this.timeProvider = timeProvider ?? TimeProvider.System;
            this.intervalStarted = this.timeProvider.GetTimestamp();
        }

        public void Record(TraceEventMessage message, Action<TraceEventMessage> emit)
        {
            List<Counter> completed = null;
            DateTime now;
            bool sample = true;
            lock (this.gate)
            {
                now = this.timeProvider.GetUtcNow().UtcDateTime;
                if (this.timeProvider.GetElapsedTime(this.intervalStarted) >= Interval)
                {
                    completed = this.Drain();
                }

                TelemetryAggregation aggregation = message.TelemetryAggregation;
                if (aggregation != null)
                {
                    (string, string, string, string) key = (message.EventName, aggregation.Operation, aggregation.FailureCategory.ToString(), aggregation.Result);
                    if (!this.counters.TryGetValue(key, out Counter counter))
                    {
                        if (this.counters.Count < Capacity)
                        {
                            counter = new Counter(key, now);
                            this.counters.Add(key, counter);
                        }
                        else
                        {
                            this.overflow = this.overflow ?? new Counter(("Overflow", "Overflow", "Overflow", "Overflow"), now);
                            counter = this.overflow;
                        }
                    }

                    sample = counter.ObservedCount == 0 && counter != this.overflow;
                    counter.ObservedCount += aggregation.Count;
                    if (sample)
                    {
                        ++counter.SampleCount;
                    }
                }
            }

            EmitSummaries(completed, now, emit);
            if (sample)
            {
                emit(message);
            }
        }

        public void Flush(Action<TraceEventMessage> emit)
        {
            List<Counter> completed;
            lock (this.gate)
            {
                completed = this.Drain();
            }

            EmitSummaries(completed, this.timeProvider.GetUtcNow().UtcDateTime, emit);
        }

        private static void EmitSummaries(List<Counter> completed, DateTime now, Action<TraceEventMessage> emit)
        {
            if (completed == null)
            {
                return;
            }

            foreach (Counter counter in completed)
            {
                EventMetadata metadata = new EventMetadata
                {
                    { "TelemetrySchemaVersion", 1 },
                    { "TelemetryAggregation", "Summary" },
                    { "OriginalEventName", counter.Key.EventName },
                    { "Operation", counter.Key.Operation },
                    { "FailureCategory", counter.Key.Category },
                    { "Result", counter.Key.Result },
                    { "ObservedCount", counter.ObservedCount },
                    { "SampleCount", counter.SampleCount },
                    { "SuppressedCount", counter.ObservedCount - counter.SampleCount },
                    { "IntervalStartUtc", counter.StartedAtUtc.ToString("O") },
                    { "IntervalEndUtc", now.ToString("O") },
                    { "OverflowCount", counter.Key.EventName == "Overflow" ? counter.ObservedCount : 0L },
                };
                emit(new TraceEventMessage
                {
                    EventName = "TelemetrySummary",
                    Level = EventLevel.Informational,
                    Keywords = Keywords.Telemetry,
                    Opcode = EventOpcode.Info,
                    Payload = EventMetadataConverter.SerializeToString(metadata),
                });
            }
        }

        private List<Counter> Drain()
        {
            List<Counter> completed = new List<Counter>(this.counters.Values);
            if (this.overflow != null)
            {
                completed.Add(this.overflow);
                this.overflow = null;
            }

            this.counters.Clear();
            this.intervalStarted = this.timeProvider.GetTimestamp();
            return completed;
        }

        private sealed class Counter
        {
            public Counter((string EventName, string Operation, string Category, string Result) key, DateTime startedAtUtc)
            {
                this.Key = key;
                this.StartedAtUtc = startedAtUtc;
            }

            public (string EventName, string Operation, string Category, string Result) Key { get; }
            public DateTime StartedAtUtc { get; }
            public long ObservedCount { get; set; }
            public long SampleCount { get; set; }
        }
    }
}
