using GVFS.Common.Tracing;
using System;
using System.Collections.Generic;

namespace GVFS.Service
{
    public sealed class InstallationTelemetryHealth
    {
        public enum FailureReason
        {
            StoreRejected,
            DeliveryFailed,
            WorkerStopped,
        }

        private readonly ITracer tracer;
        private readonly TimeProvider clock;
        private readonly Dictionary<FailureReason, FailureState> failures = new Dictionary<FailureReason, FailureState>();

        public InstallationTelemetryHealth(ITracer tracer, TimeProvider clock)
        {
            this.tracer = tracer;
            this.clock = clock;
        }

        public void ReportFailure(FailureReason reason, string localDetail)
        {
            if (!this.failures.TryGetValue(reason, out FailureState state))
            {
                state = new FailureState();
                this.failures.Add(reason, state);
            }

            state.Count++;
            DateTimeOffset now = this.clock.GetUtcNow();
            if (reason != FailureReason.WorkerStopped && now < state.NextReportUtc)
            {
                return;
            }

            this.tracer.RelatedWarning(nameof(InstallationTelemetryMonitor) + ": " + localDetail);
            this.Report(reason, state, now);
        }

        public void Flush()
        {
            DateTimeOffset now = this.clock.GetUtcNow();
            foreach (KeyValuePair<FailureReason, FailureState> failure in this.failures)
            {
                if (failure.Value.Count > 0 && now >= failure.Value.NextReportUtc)
                {
                    this.Report(failure.Key, failure.Value, now);
                }
            }
        }

        private void Report(FailureReason reason, FailureState state, DateTimeOffset now)
        {
            this.tracer.RelatedEvent(
                EventLevel.Warning,
                "InstallationTelemetryFailure",
                new EventMetadata { { "Reason", reason.ToString() }, { "FailureCount", state.Count } },
                Keywords.Telemetry);
            state.Count = 0;
            state.NextReportUtc = now.AddMinutes(5);
        }

        private sealed class FailureState
        {
            public long Count { get; set; }
            public DateTimeOffset NextReportUtc { get; set; }
        }
    }
}
