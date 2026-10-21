using GVFS.Common.Git;
using GVFS.Common.Tracing;
using System;
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
            return LoadFromGitConfig(tracer, enlistment.CreateGitProcess());
        }

        /// <summary>
        /// Invalid or unreadable retry settings use defaults and emit warnings.
        /// </summary>
        public static RetryConfig LoadFromGitConfig(ITracer tracer, GitProcess git)
        {
            ArgumentNullException.ThrowIfNull(git);
            int maxRetries = LoadIntOrDefault(
                tracer, git, GVFSConstants.GitConfig.MaxRetriesConfig, DefaultMaxRetries, 0, MaxConfiguredRetries);
            int timeoutSeconds = LoadIntOrDefault(
                tracer, git, GVFSConstants.GitConfig.TimeoutSecondsConfig, DefaultTimeoutSeconds, 1, MaxTimeoutSeconds);
            int readTimeoutSeconds = LoadIntOrDefault(
                tracer, git, GVFSConstants.GitConfig.ReadTimeoutSecondsConfig, DefaultReadTimeoutSeconds, 0, MaxTimeoutSeconds);
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
            ITracer tracer, GitProcess git, string configName, int defaultValue, int minValue, int maxValue)
        {
            string error;
            Exception readException = null;
            try
            {
                GitProcess.ConfigResult result = git.GetFromConfig(configName);
                if (result.TryParseAsInt(defaultValue, minValue, out int value, out error) && value <= maxValue)
                {
                    return value;
                }

                if (string.IsNullOrEmpty(error))
                {
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
