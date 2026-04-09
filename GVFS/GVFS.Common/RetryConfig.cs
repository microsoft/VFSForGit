using GVFS.Common.Git;
using GVFS.Common.Tracing;
using System;
using System.Linq;

namespace GVFS.Common
{
    public class RetryConfig
    {
        public const int DefaultMaxRetries = 6;
        public const int DefaultTimeoutSeconds = 30;
        public const int FetchAndCloneTimeoutMinutes = 10;

        private const string EtwArea = nameof(RetryConfig);

        private const int MinRetries = 0;

        private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(DefaultTimeoutSeconds);

        public RetryConfig(int maxRetries = DefaultMaxRetries)
            : this(maxRetries, DefaultTimeout)
        {
        }

        public RetryConfig(int maxRetries, TimeSpan timeout)
        {
            this.MaxRetries = maxRetries;
            this.Timeout = timeout;
        }

        public int MaxRetries { get; }
        public int MaxAttempts
        {
            get { return this.MaxRetries + 1; }
        }

        public TimeSpan Timeout { get; set; }

        public static bool TryLoadFromGitConfig(ITracer tracer, Enlistment enlistment, out RetryConfig retryConfig, out string error)
        {
            return TryLoadFromGitConfig(tracer, new GitProcess(enlistment), out retryConfig, out error);
        }

        public static bool TryLoadFromGitConfig(ITracer tracer, GitProcess git, out RetryConfig retryConfig, out string error)
        {
            retryConfig = null;

            int maxRetries;
            if (!TryLoadMaxRetries(git, out maxRetries, out error))
            {
                if (tracer != null)
                {
                    tracer.RelatedError(
                        new EventMetadata
                        {
                            { "Area", EtwArea },
                            { "error", error }
                        },
                        "TryLoadConfig: TryLoadMaxRetries failed");
                }

                return false;
            }

            TimeSpan timeout;
            if (!TryLoadTimeout(git, out timeout, out error))
            {
                if (tracer != null)
                {
                    tracer.RelatedError(
                        new EventMetadata
                        {
                            { "Area", EtwArea },
                            { "maxRetries", maxRetries },
                            { "error", error }
                        },
                        "TryLoadConfig: TryLoadTimeout failed");
                }

                return false;
            }

            retryConfig = new RetryConfig(maxRetries, timeout);

            if (tracer != null)
            {
                tracer.RelatedEvent(
                    EventLevel.Informational,
                    "RetryConfig_LoadedRetryConfig",
                    new EventMetadata
                    {
                        { "Area", EtwArea },
                        { "Timeout", retryConfig.Timeout },
                        { "MaxRetries", retryConfig.MaxRetries },
                        { TracingConstants.MessageKey.InfoMessage, "RetryConfigLoaded" }
                    });
            }

            return true;
        }

        private static bool TryLoadMaxRetries(GitProcess git, out int attempts, out string error)
        {
            return TryGetFromGitConfig(
                git,
                GVFSConstants.GitConfig.MaxRetriesConfig,
                DefaultMaxRetries,
                MinRetries,
                out attempts,
                out error);
        }

        private static bool TryLoadTimeout(GitProcess git, out TimeSpan timeout, out string error)
        {
            timeout = TimeSpan.FromSeconds(0);
            int timeoutSeconds;
            if (!TryGetFromGitConfig(
                git,
                GVFSConstants.GitConfig.TimeoutSecondsConfig,
                DefaultTimeoutSeconds,
                0,
                out timeoutSeconds,
                out error))
            {
                return false;
            }

            timeout = TimeSpan.FromSeconds(timeoutSeconds);
            return true;
        }

        private static bool TryGetFromGitConfig(GitProcess git, string configName, int defaultValue, int minValue, out int value, out string error)
        {
            GitProcess.ConfigResult result = git.GetFromConfig(configName);
            return result.TryParseAsInt(defaultValue, minValue, out value, out error);
        }
    }
}
