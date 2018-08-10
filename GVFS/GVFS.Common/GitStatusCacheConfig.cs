using GVFS.Common.Git;
using GVFS.Common.Tracing;
using System;
using System.Linq;

namespace GVFS.Common
{
    /// <summary>
    /// Manage the reading of GitStatusCache configuration data from git config.
    /// </summary>
    public class GitStatusCacheConfig
    {
        private const string EtwArea = nameof(GitStatusCacheConfig);

        private static readonly TimeSpan DefaultBackoffTime = TimeSpan.FromSeconds(2);

        public GitStatusCacheConfig(TimeSpan backOffTime)
        {
            this.BackoffTime = backOffTime;
        }

        public static GitStatusCacheConfig DefaultConfig { get; } = new GitStatusCacheConfig(DefaultBackoffTime);

        public TimeSpan BackoffTime { get; private set; }

        public static bool TryLoadFromGitConfig(ITracer tracer, Enlistment enlistment, out GitStatusCacheConfig gitStatusCacheConfig, out string error)
        {
            return TryLoadFromGitConfig(tracer, new GitProcess(enlistment), out gitStatusCacheConfig, out error);
        }

        public static bool TryLoadFromGitConfig(ITracer tracer, GitProcess git, out GitStatusCacheConfig gitStatusCacheConfig, out string error)
        {
            gitStatusCacheConfig = DefaultConfig;

            int backOffTimeSeconds = (int)DefaultBackoffTime.TotalSeconds;
            if (!TryLoadBackOffTime(git, out backOffTimeSeconds, out error))
            {
                if (tracer != null)
                {
                    tracer.RelatedError(
                        new EventMetadata
                        {
                            { "Area", EtwArea },
                            { "error", error }
                        },
                        $"{nameof(GitStatusCacheConfig.TryLoadFromGitConfig)}: TryLoadBackOffTime failed");
                }

                return false;
            }

            gitStatusCacheConfig = new GitStatusCacheConfig(TimeSpan.FromSeconds(backOffTimeSeconds));

            if (tracer != null)
            {
                tracer.RelatedEvent(
                    EventLevel.Informational,
                    "GitStatusCacheConfig_Loaded",
                    new EventMetadata
                    {
                        { "Area", EtwArea },
                        { "BackOffTime", gitStatusCacheConfig.BackoffTime },
                        { TracingConstants.MessageKey.InfoMessage, "GitStatusCacheConfigLoaded" }
                    });
            }

            return true;
        }

        private static bool TryLoadBackOffTime(GitProcess git, out int backoffTimeSeconds, out string error)
        {
            bool returnVal = TryGetFromGitConfig(
                git: git,
                configName: GVFSConstants.GitConfig.GitStatusCacheBackoffConfig,
                defaultValue: (int)DefaultBackoffTime.TotalSeconds,
                minValue: 0,
                value: out backoffTimeSeconds,
                error: out error);

            return returnVal;
        }

        private static bool TryGetFromGitConfig(GitProcess git, string configName, int defaultValue, int minValue, out int value, out string error)
        {
            value = defaultValue;
            error = string.Empty;

            GitProcess.Result result = git.GetFromConfig(configName);
            if (result.HasErrors)
            {
                if (result.Errors.Any())
                {
                    error = "Error while reading '" + configName + "' from config: " + result.Errors;
                    return false;
                }

                // Git returns non-zero for non-existent settings and errors.
                return true;
            }

            string valueString = result.Output.TrimEnd('\n');
            if (string.IsNullOrWhiteSpace(valueString))
            {
                // Use default value
                return true;
            }

            if (!int.TryParse(valueString, out value))
            {
                error = string.Format("Misconfigured config setting {0}, could not parse value {1}", configName, valueString);
                return false;
            }

            if (value < minValue)
            {
                error = string.Format("Invalid value {0} for setting {1}, value must be greater than or equal to {2}", value, configName, minValue);
                return false;
            }

            return true;
        }
    }
}
