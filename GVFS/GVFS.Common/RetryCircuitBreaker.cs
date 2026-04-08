using System;
using System.Threading;

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

        /// <summary>
        /// Resets the circuit breaker to its initial state. Intended for testing.
        /// </summary>
        public static void Reset()
        {
            Volatile.Write(ref consecutiveFailures, 0);
            Volatile.Write(ref circuitOpenedAtUtcTicks, 0);
            Volatile.Write(ref failureThreshold, DefaultFailureThreshold);
            Volatile.Write(ref cooldownMs, DefaultCooldownMs);
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
