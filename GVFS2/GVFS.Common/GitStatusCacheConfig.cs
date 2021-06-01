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
            GitProcess.ConfigResult result = git.GetFromConfig(configName);
            return result.TryParseAsInt(defaultValue, minValue, out value, out error);
        }
    }
}
