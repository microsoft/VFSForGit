using GVFS.Common;
using GVFS.Common.Git;
using GVFS.Tests.Should;
using GVFS.UnitTests.Mock.Common;
using GVFS.UnitTests.Mock.Git;
using NUnit.Framework;
using System;

namespace GVFS.UnitTests.Common
{
    [TestFixture]
    public class RetryConfigTests
    {
        private const string ReadConfigFailureMessage = "Failed to read config";
        [TestCase]
        public void TryLoadConfigFailsWhenGitFailsToReadConfig()
        {
            MockTracer tracer = new MockTracer();
            MockGitProcess gitProcess = new MockGitProcess();
            gitProcess.SetExpectedCommandResult("config gvfs.max-retries", () => new GitProcess.Result(string.Empty, ReadConfigFailureMessage, GitProcess.Result.GenericFailureCode));
            gitProcess.SetExpectedCommandResult("config gvfs.timeout-seconds", () => new GitProcess.Result(string.Empty, ReadConfigFailureMessage, GitProcess.Result.GenericFailureCode));

            RetryConfig config;
            string error;
            RetryConfig.TryLoadFromGitConfig(tracer, gitProcess, out config, out error).ShouldEqual(false);
            error.ShouldContain(ReadConfigFailureMessage);
        }

        [TestCase]
        public void TryLoadConfigUsesDefaultValuesWhenEntriesNotInConfig()
        {
            MockTracer tracer = new MockTracer();
            MockGitProcess gitProcess = new MockGitProcess();
            gitProcess.SetExpectedCommandResult("config gvfs.max-retries", () => new GitProcess.Result(string.Empty, string.Empty, GitProcess.Result.GenericFailureCode));
            gitProcess.SetExpectedCommandResult("config gvfs.timeout-seconds", () => new GitProcess.Result(string.Empty, string.Empty, GitProcess.Result.GenericFailureCode));
            gitProcess.SetExpectedCommandResult("config gvfs.credential-timeout-seconds", () => new GitProcess.Result(string.Empty, string.Empty, GitProcess.Result.GenericFailureCode));

            RetryConfig config;
            string error;
            RetryConfig.TryLoadFromGitConfig(tracer, gitProcess, out config, out error).ShouldEqual(true);
            error.ShouldEqual(string.Empty);
            config.MaxRetries.ShouldEqual(RetryConfig.DefaultMaxRetries);
            config.MaxAttempts.ShouldEqual(config.MaxRetries + 1);
            config.Timeout.ShouldEqual(TimeSpan.FromSeconds(RetryConfig.DefaultTimeoutSeconds));
            config.CredentialTimeoutMs.ShouldEqual(RetryConfig.DefaultCredentialTimeoutSeconds * 1000);
        }

        [TestCase]
        public void TryLoadConfigUsesDefaultValuesWhenEntriesAreBlank()
        {
            MockTracer tracer = new MockTracer();
            MockGitProcess gitProcess = new MockGitProcess();
            gitProcess.SetExpectedCommandResult("config gvfs.max-retries", () => new GitProcess.Result(string.Empty, string.Empty, GitProcess.Result.SuccessCode));
            gitProcess.SetExpectedCommandResult("config gvfs.timeout-seconds", () => new GitProcess.Result(string.Empty, string.Empty, GitProcess.Result.SuccessCode));
            gitProcess.SetExpectedCommandResult("config gvfs.credential-timeout-seconds", () => new GitProcess.Result(string.Empty, string.Empty, GitProcess.Result.SuccessCode));

            RetryConfig config;
            string error;
            RetryConfig.TryLoadFromGitConfig(tracer, gitProcess, out config, out error).ShouldEqual(true);
            error.ShouldEqual(string.Empty);
            config.MaxRetries.ShouldEqual(RetryConfig.DefaultMaxRetries);
            config.MaxAttempts.ShouldEqual(config.MaxRetries + 1);
            config.Timeout.ShouldEqual(TimeSpan.FromSeconds(RetryConfig.DefaultTimeoutSeconds));
            config.CredentialTimeoutMs.ShouldEqual(RetryConfig.DefaultCredentialTimeoutSeconds * 1000);
        }

        [TestCase]
        public void TryLoadConfigEnforcesMinimumValuesOnMaxRetries()
        {
            MockTracer tracer = new MockTracer();
            MockGitProcess gitProcess = new MockGitProcess();
            gitProcess.SetExpectedCommandResult("config gvfs.max-retries", () => new GitProcess.Result("-1", string.Empty, GitProcess.Result.SuccessCode));
            gitProcess.SetExpectedCommandResult("config gvfs.timeout-seconds", () => new GitProcess.Result("30", string.Empty, GitProcess.Result.SuccessCode));

            RetryConfig config;
            string error;
            RetryConfig.TryLoadFromGitConfig(tracer, gitProcess, out config, out error).ShouldEqual(false);
            error.ShouldContain("Invalid value -1 for setting gvfs.max-retries, value must be greater than or equal to 0");
        }

        [TestCase]
        public void TryLoadConfigEnforcesMinimumValuesOnTimeout()
        {
            MockTracer tracer = new MockTracer();
            MockGitProcess gitProcess = new MockGitProcess();
            gitProcess.SetExpectedCommandResult("config gvfs.max-retries", () => new GitProcess.Result("3", string.Empty, GitProcess.Result.SuccessCode));
            gitProcess.SetExpectedCommandResult("config gvfs.timeout-seconds", () => new GitProcess.Result("-1", string.Empty, GitProcess.Result.SuccessCode));

            RetryConfig config;
            string error;
            RetryConfig.TryLoadFromGitConfig(tracer, gitProcess, out config, out error).ShouldEqual(false);
            error.ShouldContain("Invalid value -1 for setting gvfs.timeout-seconds, value must be greater than or equal to 0");
        }

        [TestCase]
        public void TryLoadConfigUsesConfiguredValues()
        {
            int maxRetries = RetryConfig.DefaultMaxRetries + 1;
            int timeoutSeconds = RetryConfig.DefaultTimeoutSeconds + 1;
            int credentialTimeoutSeconds = RetryConfig.DefaultCredentialTimeoutSeconds + 1;

            MockTracer tracer = new MockTracer();
            MockGitProcess gitProcess = new MockGitProcess();
            gitProcess.SetExpectedCommandResult("config gvfs.max-retries", () => new GitProcess.Result(maxRetries.ToString(), string.Empty, GitProcess.Result.SuccessCode));
            gitProcess.SetExpectedCommandResult("config gvfs.timeout-seconds", () => new GitProcess.Result(timeoutSeconds.ToString(), string.Empty, GitProcess.Result.SuccessCode));
            gitProcess.SetExpectedCommandResult("config gvfs.credential-timeout-seconds", () => new GitProcess.Result(credentialTimeoutSeconds.ToString(), string.Empty, GitProcess.Result.SuccessCode));

            RetryConfig config;
            string error;
            RetryConfig.TryLoadFromGitConfig(tracer, gitProcess, out config, out error).ShouldEqual(true);
            error.ShouldEqual(string.Empty);
            config.MaxRetries.ShouldEqual(maxRetries);
            config.MaxAttempts.ShouldEqual(config.MaxRetries + 1);
            config.Timeout.ShouldEqual(TimeSpan.FromSeconds(timeoutSeconds));
            config.CredentialTimeoutMs.ShouldEqual(credentialTimeoutSeconds * 1000);
        }

        [TestCase]
        public void TryLoadConfigTreatsNonPositiveCredentialTimeoutAsUnbounded()
        {
            // A non-positive value is a deliberate escape hatch selecting the historical
            // unbounded wait, so it must be accepted rather than rejected as out of range.
            MockTracer tracer = new MockTracer();
            MockGitProcess gitProcess = new MockGitProcess();
            gitProcess.SetExpectedCommandResult("config gvfs.max-retries", () => new GitProcess.Result("3", string.Empty, GitProcess.Result.SuccessCode));
            gitProcess.SetExpectedCommandResult("config gvfs.timeout-seconds", () => new GitProcess.Result("30", string.Empty, GitProcess.Result.SuccessCode));
            gitProcess.SetExpectedCommandResult("config gvfs.credential-timeout-seconds", () => new GitProcess.Result("0", string.Empty, GitProcess.Result.SuccessCode));

            RetryConfig config;
            string error;
            RetryConfig.TryLoadFromGitConfig(tracer, gitProcess, out config, out error).ShouldEqual(true);
            config.CredentialTimeoutMs.ShouldEqual(-1, "0 seconds should select an unbounded wait");
        }

        [TestCase]
        public void RetryConfigDefaultsCredentialTimeoutWhenNotLoadedFromConfig()
        {
            // Requestors constructed with a hand-built RetryConfig (tests, FastFetch, profiling)
            // must still get a bounded credential fetch rather than an unbounded default.
            new RetryConfig().CredentialTimeoutMs.ShouldEqual(RetryConfig.DefaultCredentialTimeoutSeconds * 1000);
            new RetryConfig(3, TimeSpan.FromSeconds(30)).CredentialTimeoutMs.ShouldEqual(RetryConfig.DefaultCredentialTimeoutSeconds * 1000);
        }
    }
}
