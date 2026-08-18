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

        /// <summary>
        /// Default bound for a runtime credential fetch. Deliberately generous: the mount's
        /// requestor is shared by the background maintenance prefetch, interactive on-demand
        /// hydration, and the user-initiated prefetch/clone verbs, where a human may take
        /// longer than the 30s request timeout to answer a GCM cold-start / MFA / smartcard
        /// prompt. It still bounds the indefinite hang.
        /// </summary>
        public const int DefaultCredentialTimeoutSeconds = 120;

        private const string EtwArea = nameof(RetryConfig);

        private const int MinRetries = 0;

        private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(DefaultTimeoutSeconds);

        public RetryConfig(int maxRetries = DefaultMaxRetries)
            : this(maxRetries, DefaultTimeout)
        {
        }

        public RetryConfig(int maxRetries, TimeSpan timeout)
            : this(maxRetries, timeout, DefaultCredentialTimeoutSeconds * 1000)
        {
        }

        public RetryConfig(int maxRetries, TimeSpan timeout, int credentialTimeoutMs)
        {
            this.MaxRetries = maxRetries;
            this.Timeout = timeout;
            this.CredentialTimeoutMs = credentialTimeoutMs;
        }

        public int MaxRetries { get; }
        public int MaxAttempts
        {
            get { return this.MaxRetries + 1; }
        }

        public TimeSpan Timeout { get; set; }

        /// <summary>
        /// How long a runtime credential fetch may block waiting on the credential manager.
        /// A negative value waits indefinitely, which is the historical behavior and reopens
        /// the hang this bound was added to prevent.
        /// </summary>
        public int CredentialTimeoutMs { get; }

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

            int credentialTimeoutMs;
            if (!TryLoadCredentialTimeoutMs(git, out credentialTimeoutMs, out error))
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
                        "TryLoadConfig: TryLoadCredentialTimeoutMs failed");
                }

                return false;
            }

            retryConfig = new RetryConfig(maxRetries, timeout, credentialTimeoutMs);

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
                        { "CredentialTimeoutMs", retryConfig.CredentialTimeoutMs },
                        { TracingConstants.MessageKey.InfoMessage, "RetryConfigLoaded" }
                    });
            }

            return true;
        }

        private static bool TryLoadMaxRetries(GitProcess git, out int attempts, out string error)        {
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

        /// <summary>
        /// Reads the credential-fetch bound, in seconds, from git config. A configured value of
        /// 0 or less selects an unbounded wait, so this deliberately allows non-positive values
        /// rather than treating them as out of range.
        /// </summary>
        private static bool TryLoadCredentialTimeoutMs(GitProcess git, out int credentialTimeoutMs, out string error)
        {
            credentialTimeoutMs = DefaultCredentialTimeoutSeconds * 1000;

            int credentialTimeoutSeconds;
            if (!TryGetFromGitConfig(
                git,
                GVFSConstants.GitConfig.CredentialTimeoutSeconds,
                DefaultCredentialTimeoutSeconds,
                int.MinValue,
                out credentialTimeoutSeconds,
                out error))
            {
                return false;
            }

            credentialTimeoutMs = credentialTimeoutSeconds <= 0 ? -1 : credentialTimeoutSeconds * 1000;
            return true;
        }

        private static bool TryGetFromGitConfig(GitProcess git, string configName, int defaultValue, int minValue, out int value, out string error)
        {
            GitProcess.ConfigResult result = git.GetFromConfig(configName);
            return result.TryParseAsInt(defaultValue, minValue, out value, out error);
        }
    }
}
