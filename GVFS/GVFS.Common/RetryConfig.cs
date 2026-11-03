using GVFS.Common.Git;
using GVFS.Common.Tracing;
using System;
using System.Collections;
using System.ComponentModel;
using System.IO;

namespace GVFS.Common
{
    public class RetryConfig
    {
        public const int DefaultMaxRetries = 6;
        public const int DefaultTimeoutSeconds = 30;
        public const int DefaultReadTimeoutSeconds = 60;
        public const int FetchAndCloneTimeoutMinutes = 10;

        private const string EtwArea = nameof(RetryConfig);
        // HttpClient and the response stream accept signed millisecond deadlines.
        private const int MaxTimeoutSeconds = int.MaxValue / 1000;
        // Leave room for the initial attempt and two endpoint-routing attempts.
        private const int MaxConfiguredRetries = int.MaxValue - 3;

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

        public int MaxRetries { get; set; }
        public int MaxAttempts
        {
            get { return this.MaxRetries + 1; }
        }

        public TimeSpan Timeout { get; set; }

        public TimeSpan ReadTimeout { get; set; } = TimeSpan.FromSeconds(DefaultReadTimeoutSeconds);

        public static RetryConfig LoadFromGitConfig(ITracer tracer, Enlistment enlistment)
        {
            ArgumentNullException.ThrowIfNull(enlistment);
            return LoadFromGitConfig(tracer, CreateConfigReader(tracer, enlistment, Environment.GetEnvironmentVariables()));
        }

        /// <summary>
        /// Reads the settings with git.exe. Invalid or unreadable retry settings use defaults and
        /// emit warnings.
        /// </summary>
        public static RetryConfig LoadFromGitConfig(ITracer tracer, GitProcess git)
        {
            ArgumentNullException.ThrowIfNull(git);
            return LoadFromGitConfig(
                tracer,
                (string name, out string value, out string error) => git.GetFromConfig(name).TryParseAsString(out value, out error));
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
            // When the working directory does not exist, for example before clone creates it,
            // read only the global and system config. See LibGit2Repo for why GIT_DIR is ignored.
            string repoPath = Directory.Exists(workingDirectoryRoot) ? workingDirectoryRoot : null;
            return (string name, out string value, out string error) =>
                LibGit2Repo.TryGetConfigString(tracer, repoPath, name, out value, out error);
        }

        /// <summary>
        /// Reads with git.exe when libgit2 fails, for example because it rejects the repository's
        /// ownership or evaluates an include differently from git.exe. A transient file access
        /// failure is not read again, because libgit2 already used the GitConfigReadRetry budget for it.
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

        internal static RetryConfig LoadFromGitConfig(ITracer tracer, ConfigStringReader readConfig)
        {
            int maxRetries = LoadIntOrDefault(
                tracer, readConfig, GVFSConstants.GitConfig.MaxRetriesConfig, DefaultMaxRetries, 0, MaxConfiguredRetries);
            int timeoutSeconds = LoadIntOrDefault(
                tracer, readConfig, GVFSConstants.GitConfig.TimeoutSecondsConfig, DefaultTimeoutSeconds, 1, MaxTimeoutSeconds);
            int readTimeoutSeconds = LoadIntOrDefault(
                tracer, readConfig, GVFSConstants.GitConfig.ReadTimeoutSecondsConfig, DefaultReadTimeoutSeconds, 0, MaxTimeoutSeconds);
            RetryConfig retryConfig = new RetryConfig(maxRetries, TimeSpan.FromSeconds(timeoutSeconds))
            {
                ReadTimeout = readTimeoutSeconds == 0
                    ? System.Threading.Timeout.InfiniteTimeSpan
                    : TimeSpan.FromSeconds(readTimeoutSeconds),
            };

            if (tracer != null)
            {
                tracer.RelatedEvent(
                    EventLevel.Informational,
                    "RetryConfig_LoadedRetryConfig",
                    new EventMetadata
                    {
                        { "Area", EtwArea },
                        { "Timeout", retryConfig.Timeout },
                        { "ReadTimeout", retryConfig.ReadTimeout },
                        { "MaxRetries", retryConfig.MaxRetries },
                        { TracingConstants.MessageKey.InfoMessage, "RetryConfigLoaded" }
                    });
            }

            return retryConfig;
        }

        private static int LoadIntOrDefault(
            ITracer tracer, ConfigStringReader readConfig, string configName, int defaultValue, int minValue, int maxValue)
        {
            string error;
            Exception readException = null;
            try
            {
                if (readConfig(configName, out string valueString, out error) &&
                    GitConfigHelper.TryParseIntSetting(configName, valueString, defaultValue, minValue, out int value, out error))
                {
                    if (value <= maxValue)
                    {
                        return value;
                    }

                    error = $"Invalid value {value} for setting {configName}, value must be between {minValue} and {maxValue}";
                }
            }
            catch (Exception e) when (e is IOException || e is Win32Exception)
            {
                readException = e;
                error = e.Message;
            }

            EventMetadata metadata = new EventMetadata
            {
                { "Area", EtwArea },
                { "ConfigName", configName },
                { "DefaultValue", defaultValue },
                { "Error", error },
            };
            if (readException != null)
            {
                metadata.Add("Exception", readException.ToString());
            }

            tracer?.RelatedWarning(metadata, "The retry setting is invalid or unavailable. VFS for Git uses the default value.", Keywords.Telemetry);
            return defaultValue;
        }
    }
}
