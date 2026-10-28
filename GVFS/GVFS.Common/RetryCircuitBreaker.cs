using System;
using System.Threading;
using GVFS.Common.Tracing;

namespace GVFS.Common
{
    /// <summary>
    /// Global circuit breaker for retry operations. When too many consecutive failures
    /// occur (e.g., during system-wide resource exhaustion), the circuit opens and
    /// subsequent retry attempts fail fast instead of consuming connections and adding
    /// backoff delays that worsen the resource pressure.
    /// </summary>
    public static class RetryCircuitBreaker
    {
        public const int DefaultFailureThreshold = 15;
        public const int DefaultCooldownMs = 30_000;

        private static int failureThreshold = DefaultFailureThreshold;
        private static int cooldownMs = DefaultCooldownMs;
        private static int consecutiveFailures = 0;
        private static long circuitOpenedAtUtcTicks = 0;
        private static int observedState = -1;

        public static bool IsOpen
        {
            get
            {
                if (Volatile.Read(ref consecutiveFailures) < failureThreshold)
                {
                    return false;
                }

                long openedAt = Volatile.Read(ref circuitOpenedAtUtcTicks);
                return (DateTime.UtcNow.Ticks - openedAt) < TimeSpan.FromMilliseconds(cooldownMs).Ticks;
            }
        }

        public static int ConsecutiveFailures => Volatile.Read(ref consecutiveFailures);

        public static void RecordSuccess()
        {
            Interlocked.Exchange(ref consecutiveFailures, 0);
        }

        public static void RecordFailure()
        {
            int failures = Interlocked.Increment(ref consecutiveFailures);
            if (failures >= failureThreshold)
            {
                Volatile.Write(ref circuitOpenedAtUtcTicks, DateTime.UtcNow.Ticks);
            }
        }

        internal static void TraceState(ITracer tracer, bool isOpen, string trigger)
        {
            if (tracer == null)
            {
                return;
            }

            int state = isOpen ? 1 : (ConsecutiveFailures >= failureThreshold ? 2 : 0);
            int previousState = Interlocked.Exchange(ref observedState, state);
            if (previousState == state)
            {
                return;
            }

            string[] states = { "Accepting", "Rejecting", "CooldownElapsed" };
            EventMetadata metadata = new EventMetadata
            {
                { "PreviousState", previousState < 0 ? "Unknown" : states[previousState] },
                { "State", states[state] },
                { "Trigger", trigger },
                { "ObservedAtUtc", DateTime.UtcNow.ToString("O") },
            };
            metadata.SetTelemetryAggregation(nameof(RetryCircuitBreaker), TelemetryFailureCategory.BreakerState, states[state]);
            tracer.RelatedEvent(EventLevel.Informational, "RetryCircuitBreakerState", metadata, Keywords.Telemetry);
        }

        /// <summary>
        /// Resets the circuit breaker to its initial state. Intended for testing.
        /// </summary>
        public static void Reset()
        {
            Volatile.Write(ref consecutiveFailures, 0);
            Volatile.Write(ref circuitOpenedAtUtcTicks, 0);
            Volatile.Write(ref failureThreshold, DefaultFailureThreshold);
            Volatile.Write(ref cooldownMs, DefaultCooldownMs);
            Volatile.Write(ref observedState, -1);
        }

        /// <summary>
        /// Configures the circuit breaker thresholds. Intended for testing.
        /// </summary>
        public static void Configure(int threshold, int cooldownMilliseconds)
        {
            Volatile.Write(ref failureThreshold, threshold);
            Volatile.Write(ref cooldownMs, cooldownMilliseconds);
        }
    }
}
