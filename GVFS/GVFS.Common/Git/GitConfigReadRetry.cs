using GVFS.Common.Tracing;
using System;
using System.Threading;

namespace GVFS.Common.Git
{
    internal static class GitConfigReadRetry
    {
        internal const int MaxAttempts = 5;
        internal const int InitialDelayMilliseconds = 20;

        private static readonly string[] TransientReasons =
        {
            "Permission denied",
            "Device or resource busy",
            "being used by another process",
            "sharing violation",
        };

        internal static T InvokeResult<T>(
            string configName,
            Func<T> readConfig,
            Func<T, bool> isTransientFailure,
            ITracer tracer,
            Action<TimeSpan> delay,
            int maxAttempts = MaxAttempts)
        {
            for (int attempt = 1; ; ++attempt)
            {
                T result = readConfig();
                if (!isTransientFailure(result))
                {
                    return result;
                }

                if (attempt >= maxAttempts)
                {
                    TraceExhausted(tracer, configName, attempt);
                    return result;
                }

                TimeSpan retryDelay = GetRetryDelay(attempt);
                TraceRetry(tracer, configName, attempt, maxAttempts, retryDelay);
                delay(retryDelay);
            }
        }

        internal static T InvokeCatching<T>(
            string configName,
            Func<T> readConfig,
            Func<Exception, bool> isTransientFailure,
            ITracer tracer,
            Action<TimeSpan> delay,
            int maxAttempts = MaxAttempts)
        {
            for (int attempt = 1; ; ++attempt)
            {
                try
                {
                    return readConfig();
                }
                catch (Exception e) when (isTransientFailure(e))
                {
                    if (attempt >= maxAttempts)
                    {
                        TraceExhausted(tracer, configName, attempt);
                        throw;
                    }

                    TimeSpan retryDelay = GetRetryDelay(attempt);
                    TraceRetry(tracer, configName, attempt, maxAttempts, retryDelay);
                    delay(retryDelay);
                }
            }
        }

        internal static bool IsTransientLibGit2Error(Exception exception)
        {
            return
                exception is LibGit2Exception &&
                IsTransientFileAccessError(exception.Message);
        }

        /// <summary>
        /// Both git.exe and libgit2 report a file failure as <c>... '&lt;path&gt;': &lt;reason&gt;</c>.
        /// Only the reason after the final <c>': </c> is tested, so a path or key that contains
        /// a transient phrase does not match.
        /// </summary>
        internal static bool IsTransientFileAccessError(string error, string lineMustContain = null)
        {
            if (string.IsNullOrEmpty(error))
            {
                return false;
            }

            foreach (string line in error.Split('\n'))
            {
                if (lineMustContain != null && !ContainsOrdinalIgnoreCase(line, lineMustContain))
                {
                    continue;
                }

                int reasonSeparator = line.LastIndexOf("': ", StringComparison.Ordinal);
                if (reasonSeparator < 0)
                {
                    continue;
                }

                string reason = line.Substring(reasonSeparator + 3);
                foreach (string transientReason in TransientReasons)
                {
                    if (ContainsOrdinalIgnoreCase(reason, transientReason))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        internal static bool ContainsOrdinalIgnoreCase(string value, string expected)
        {
            return
                value != null &&
                value.IndexOf(expected, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        internal static void Delay(TimeSpan delay)
        {
            Thread.Sleep(delay);
        }

        private static TimeSpan GetRetryDelay(int failedAttempt)
        {
            return TimeSpan.FromMilliseconds(InitialDelayMilliseconds * (1 << (failedAttempt - 1)));
        }

        // A repository URL with embedded credentials can become part of a deprecated cache
        // server config key name.
        private static string GetTraceableConfigName(string configName)
        {
            return configName != null && configName.IndexOf('@') >= 0
                ? "<redacted>"
                : configName;
        }

        private static void TraceRetry(ITracer tracer, string configName, int failedAttempt, int maxAttempts, TimeSpan retryDelay)
        {
            EventMetadata metadata = new EventMetadata();
            metadata.Add("ConfigName", GetTraceableConfigName(configName));
            metadata.Add("AttemptNumber", failedAttempt);
            metadata.Add("MaxAttempts", maxAttempts);
            metadata.Add("RetryDelayMilliseconds", retryDelay.TotalMilliseconds);
            tracer.RelatedWarning(
                metadata,
                "Transient failure while reading git config. Retrying.",
                Keywords.Telemetry);
        }

        private static void TraceExhausted(ITracer tracer, string configName, int attempts)
        {
            EventMetadata metadata = new EventMetadata();
            metadata.Add("ConfigName", GetTraceableConfigName(configName));
            metadata.Add("Attempts", attempts);
            tracer.RelatedWarning(
                metadata,
                "Transient failure while reading git config persisted after all attempts.",
                Keywords.Telemetry);
        }
    }
}
