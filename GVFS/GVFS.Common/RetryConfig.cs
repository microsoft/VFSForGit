using GVFS.Common.Git;
using GVFS.Common.Tracing;
using System;
using System.Collections;
using System.IO;
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

        internal delegate bool ConfigStringReader(string name, out string value, out string error);

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
            return TryLoadFromGitConfig(
                tracer,
                CreateConfigReader(tracer, enlistment, Environment.GetEnvironmentVariables()),
                out retryConfig,
                out error);
        }

        internal static bool TryLoadFromGitConfig(ITracer tracer, GitProcess git, out RetryConfig retryConfig, out string error)
        {
            return TryLoadFromGitConfig(
                tracer,
                (string name, out string value, out string readError) => git.GetFromConfig(name).TryParseAsString(out value, out readError),
                out retryConfig,
                out error);
        }

        /// <summary>
        /// Reads the settings in process with libgit2, and with git.exe when libgit2 cannot give
        /// the value that git.exe would give.
        /// </summary>
        internal static ConfigStringReader CreateConfigReader(ITracer tracer, Enlistment enlistment, IDictionary environment)
        {
            GitProcess git = null;
            ConfigStringReader readWithGit = (string name, out string value, out string error) =>
            {
                git = git ?? enlistment.CreateGitProcess(tracer);
                return git.GetFromConfig(name).TryParseAsString(out value, out error);
            };

            return LibGit2Repo.EnvironmentChangesGitConfig(environment)
                ? readWithGit
                : WithGitFallback(tracer, CreateLibGit2Reader(tracer, enlistment.WorkingDirectoryBackingRoot), readWithGit);
        }

        internal static ConfigStringReader CreateLibGit2Reader(ITracer tracer, string workingDirectoryRoot)
        {
            // GitProcess.GetFromConfig also reads only the global and system config when the
            // working directory does not exist, for example before clone creates it.
            string repoPath = Directory.Exists(workingDirectoryRoot) ? workingDirectoryRoot : null;
            return (string name, out string value, out string error) =>
                LibGit2Repo.TryGetConfigString(tracer, repoPath, name, out value, out error);
        }

        /// <summary>
        /// Reads with git.exe when libgit2 fails, for example because it rejects the repository's
        /// ownership or skips an includeIf condition. A transient file access failure is not read
        /// again, because libgit2 already used the GitConfigReadRetry budget for it.
        /// </summary>
        internal static ConfigStringReader WithGitFallback(ITracer tracer, ConfigStringReader readWithLibGit2, ConfigStringReader readWithGit)
        {
            return (string name, out string value, out string error) =>
            {
                if (readWithLibGit2(name, out value, out error))
                {
                    return true;
                }

                if (GitConfigReadRetry.IsTransientFileAccessError(error))
                {
                    return false;
                }

                tracer?.RelatedEvent(
                    EventLevel.Informational,
                    "RetryConfig_ReadWithGit",
                    new EventMetadata
                    {
                        { "Area", EtwArea },
                        { "ConfigName", name },
                        { "LibGit2Error", error },
                    });

                return readWithGit(name, out value, out error);
            };
        }

        internal static bool TryLoadFromGitConfig(ITracer tracer, ConfigStringReader readConfig, out RetryConfig retryConfig, out string error)
        {
            retryConfig = null;

            int maxRetries;
            if (!TryLoadMaxRetries(readConfig, out maxRetries, out error))
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
            if (!TryLoadTimeout(readConfig, out timeout, out error))
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

        private static bool TryLoadMaxRetries(ConfigStringReader readConfig, out int attempts, out string error)
        {
            return TryGetFromGitConfig(
                readConfig,
                GVFSConstants.GitConfig.MaxRetriesConfig,
                DefaultMaxRetries,
                MinRetries,
                out attempts,
                out error);
        }

        private static bool TryLoadTimeout(ConfigStringReader readConfig, out TimeSpan timeout, out string error)
        {
            timeout = TimeSpan.FromSeconds(0);
            int timeoutSeconds;
            if (!TryGetFromGitConfig(
                readConfig,
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

        private static bool TryGetFromGitConfig(ConfigStringReader readConfig, string configName, int defaultValue, int minValue, out int value, out string error)
        {
            value = defaultValue;
            if (!readConfig(configName, out string valueString, out error))
            {
                return false;
            }

            return GitConfigHelper.TryParseIntSetting(configName, valueString, defaultValue, minValue, out value, out error);
        }
    }
}
