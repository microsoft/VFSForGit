using GVFS.Common.Tracing;
using System;
using System.Threading;

namespace GVFS.Common.Git
{
    internal static class GitConfigReadRetry
    {
        internal const int MaxAttempts = 5;
        internal const int InitialDelayMilliseconds = 20;

        public static T Invoke<T>(
            string configName,
            Func<T> readConfig,
            Func<T, bool> isTransientFailure,
            ITracer tracer,
            Action<TimeSpan> delay)
        {
            for (int attempt = 1; attempt <= MaxAttempts; ++attempt)
            {
                T result = readConfig();
                if (!isTransientFailure(result) || attempt == MaxAttempts)
                {
                    return result;
                }

                TimeSpan retryDelay = GetRetryDelay(attempt);
                TraceRetry(tracer, configName, attempt, retryDelay);
                delay(retryDelay);
            }

            throw new InvalidOperationException("Config read retry loop exited unexpectedly.");
        }

        public static T Invoke<T>(
            string configName,
            Func<T> readConfig,
            Func<Exception, bool> isTransientFailure,
            ITracer tracer,
            Action<TimeSpan> delay)
        {
            for (int attempt = 1; attempt <= MaxAttempts; ++attempt)
            {
                try
                {
                    return readConfig();
                }
                catch (Exception e) when (isTransientFailure(e) && attempt < MaxAttempts)
                {
                    TimeSpan retryDelay = GetRetryDelay(attempt);
                    TraceRetry(tracer, configName, attempt, retryDelay);
                    delay(retryDelay);
                }
            }

            throw new InvalidOperationException("Config read retry loop exited unexpectedly.");
        }

        public static bool IsTransientLibGit2Error(Exception exception)
        {
            return
                exception is LibGit2Exception &&
                IsTransientFileAccessError(exception.Message);
        }

        public static void Delay(TimeSpan delay)
        {
            Thread.Sleep(delay);
        }

        private static TimeSpan GetRetryDelay(int failedAttempt)
        {
            return TimeSpan.FromMilliseconds(InitialDelayMilliseconds * (1 << (failedAttempt - 1)));
        }

        public static bool IsTransientFileAccessError(string error)
        {
            return
                Contains(error, "Permission denied") ||
                Contains(error, "Device or resource busy") ||
                Contains(error, "being used by another process") ||
                Contains(error, "sharing violation");
        }

        public static bool Contains(string value, string expected)
        {
            return
                value != null &&
                value.IndexOf(expected, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static void TraceRetry(ITracer tracer, string configName, int failedAttempt, TimeSpan retryDelay)
        {
            EventMetadata metadata = new EventMetadata();
            metadata.Add("ConfigName", configName);
            metadata.Add("AttemptNumber", failedAttempt);
            metadata.Add("MaxAttempts", MaxAttempts);
            metadata.Add("RetryDelayMilliseconds", retryDelay.TotalMilliseconds);
            tracer.RelatedWarning(
                metadata,
                "Transient failure while reading git config. Retrying.",
                Keywords.Telemetry);
        }
    }
}
