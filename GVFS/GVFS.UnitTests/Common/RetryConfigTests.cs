using GVFS.Common;
using GVFS.Common.FileSystem;
using GVFS.Common.Git;
using GVFS.CommandLine;
using GVFS.Common.Tracing;
using GVFS.Tests.Should;
using GVFS.UnitTests.Category;
using GVFS.UnitTests.Mock.Common;
using GVFS.UnitTests.Mock.Git;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;

namespace GVFS.UnitTests.Common
{
    [TestFixture]
    public class RetryConfigTests
    {
        [TestCase(GitProcess.Result.SuccessCode)]
        [TestCase(GitProcess.Result.GenericFailureCode)]
        public void MissingOrBlankSettingsUseDefaultsWithoutWarnings(int exitCode)
        {
            using MockTracer tracer = new MockTracer();
            MockGitProcess git = new MockGitProcess();
            foreach (string key in new[]
            {
                "gvfs.max-retries", "gvfs.timeout-seconds", "gvfs.read-timeout-seconds",
            })
            {
                git.SetExpectedCommandResult("config " + key,
                    () => new GitProcess.Result(string.Empty, string.Empty, exitCode));
            }

            RetryConfig config = RetryConfig.LoadFromGitConfig(tracer, git);
            config.MaxRetries.ShouldEqual(6);
            config.MaxAttempts.ShouldEqual(7);
            config.Timeout.ShouldEqual(TimeSpan.FromSeconds(30));
            config.ReadTimeout.ShouldEqual(TimeSpan.FromSeconds(60));
            tracer.RelatedWarningEvents.Count.ShouldEqual(0);
            tracer.RelatedErrorEvents.Count.ShouldEqual(0);
        }

        [TestCase]
        public void CompleteLoaderReadsAllSettings()
        {
            using MockTracer tracer = new MockTracer();
            MockGitProcess git = CreateGitProcess();
            RetryConfig config = RetryConfig.LoadFromGitConfig(tracer, git);
            config.MaxRetries.ShouldEqual(9);
            config.MaxAttempts.ShouldEqual(10);
            config.Timeout.ShouldEqual(TimeSpan.FromSeconds(45));
            config.ReadTimeout.ShouldEqual(TimeSpan.FromSeconds(90));
            git.CommandsRun.ShouldContain(command => command == "config gvfs.max-retries");
            git.CommandsRun.ShouldContain(command => command == "config gvfs.timeout-seconds");
            git.CommandsRun.ShouldContain(command => command == "config gvfs.read-timeout-seconds");
        }

        [TestCase("gvfs.max-retries", "-1", 6)]
        [TestCase("gvfs.max-retries", "invalid", 6)]
        [TestCase("gvfs.max-retries", "2147483647", 6)]
        [TestCase("gvfs.max-retries", "2147483645", 6)]
        [TestCase("gvfs.timeout-seconds", "-1", 30)]
        [TestCase("gvfs.timeout-seconds", "0", 30)]
        [TestCase("gvfs.timeout-seconds", "invalid", 30)]
        [TestCase("gvfs.timeout-seconds", "2147484", 30)]
        [TestCase("gvfs.read-timeout-seconds", "-1", 60)]
        [TestCase("gvfs.read-timeout-seconds", "invalid", 60)]
        [TestCase("gvfs.read-timeout-seconds", "2147484", 60)]
        [TestCase("gvfs.read-timeout-seconds", "2147483648", 60)]
        public void InvalidSettingUsesOnlyItsOwnDefault(string key, string value, int expectedDefault)
        {
            using MockTracer tracer = new MockTracer();
            MockGitProcess git = CreateGitProcess();
            git.SetExpectedCommandResult("config " + key,
                () => new GitProcess.Result(value, string.Empty, GitProcess.Result.SuccessCode));

            RetryConfig config = RetryConfig.LoadFromGitConfig(tracer, git);
            config.MaxRetries.ShouldEqual(key == "gvfs.max-retries" ? 6 : 9);
            config.Timeout.ShouldEqual(TimeSpan.FromSeconds(key == "gvfs.timeout-seconds" ? 30 : 45));
            config.ReadTimeout.ShouldEqual(TimeSpan.FromSeconds(key == "gvfs.read-timeout-seconds" ? 60 : 90));
            tracer.RelatedWarningEvents.Count.ShouldEqual(1);
            tracer.RelatedWarningEvents[0].ShouldContain(key);
            tracer.RelatedWarningEvents[0].ShouldContain("\"DefaultValue\":" + expectedDefault);
            tracer.RelatedErrorEvents.Count.ShouldEqual(0);
        }

        [TestCase("0", -1)]
        [TestCase("1", 1000)]
        [TestCase("2147483", 2147483000)]
        public void CompleteLoaderSupportsReadTimeoutBounds(string value, int expectedMilliseconds)
        {
            using MockTracer tracer = new MockTracer();
            MockGitProcess git = CreateGitProcess();
            git.SetExpectedCommandResult("config gvfs.read-timeout-seconds",
                () => new GitProcess.Result(value, string.Empty, GitProcess.Result.SuccessCode));
            RetryConfig config = RetryConfig.LoadFromGitConfig(tracer, git);
            config.ReadTimeout.ShouldEqual(TimeSpan.FromMilliseconds(expectedMilliseconds));
            config.MaxRetries.ShouldEqual(9);
            config.Timeout.ShouldEqual(TimeSpan.FromSeconds(45));
            tracer.RelatedWarningEvents.Count.ShouldEqual(0);
        }

        [TestCase]
        public void ReadFailuresUseDefaultsWithoutAborting()
        {
            using MockTracer tracer = new MockTracer();
            MockGitProcess git = CreateGitProcess();
            foreach (string key in new[] { "gvfs.max-retries", "gvfs.timeout-seconds", "gvfs.read-timeout-seconds" })
            {
                git.SetExpectedCommandResult("config " + key,
                    () => new GitProcess.Result(string.Empty, "Failed to read config", GitProcess.Result.GenericFailureCode));
            }

            RetryConfig config = RetryConfig.LoadFromGitConfig(tracer, git);
            config.MaxRetries.ShouldEqual(6);
            config.Timeout.ShouldEqual(TimeSpan.FromSeconds(30));
            config.ReadTimeout.ShouldEqual(TimeSpan.FromSeconds(60));
            tracer.RelatedWarningEvents.Count.ShouldEqual(3);
            tracer.RelatedErrorEvents.Count.ShouldEqual(0);
        }

        [TestCase(false)]
        [TestCase(true)]
        [NUnit.Framework.Category(CategoryConstants.ExceptionExpected)]
        public void ConfigReadExceptionsUseDefaultsAndKeepTheExceptionInDiagnostics(bool processError)
        {
            using MockTracer tracer = new MockTracer();
            MockGitProcess git = CreateGitProcess(new ThrowingGitProcess(processError));
            RetryConfig config = RetryConfig.LoadFromGitConfig(tracer, git);
            config.MaxRetries.ShouldEqual(9);
            config.Timeout.ShouldEqual(TimeSpan.FromSeconds(45));
            config.ReadTimeout.ShouldEqual(TimeSpan.FromSeconds(60));
            tracer.RelatedWarningEvents.Count.ShouldEqual(1);
            tracer.RelatedWarningEvents[0].ShouldContain("\"Exception\":");
            tracer.RelatedWarningEvents[0].ShouldContain(processError ? "Win32Exception" : "IOException");
            tracer.RelatedErrorEvents.Count.ShouldEqual(0);
        }

        [TestCase]
        public void LaterInvalidSettingsDoNotMutateAnExistingConfiguration()
        {
            using MockTracer tracer = new MockTracer();
            MockGitProcess git = CreateGitProcess();
            RetryConfig existing = RetryConfig.LoadFromGitConfig(tracer, git);
            git.SetExpectedCommandResult("config gvfs.read-timeout-seconds",
                () => new GitProcess.Result("invalid", string.Empty, GitProcess.Result.SuccessCode));

            RetryConfig reloaded = RetryConfig.LoadFromGitConfig(tracer, git);
            existing.ReadTimeout.ShouldEqual(TimeSpan.FromSeconds(90));
            reloaded.ReadTimeout.ShouldEqual(TimeSpan.FromSeconds(60));
            reloaded.MaxRetries.ShouldEqual(existing.MaxRetries);
            reloaded.Timeout.ShouldEqual(existing.Timeout);
        }

        [TestCase]
        public void CommandOverridesDoNotDiscardLoadedReadTimeout()
        {
            using MockTracer tracer = new MockTracer();
            MockGitProcess git = CreateGitProcess();
            RetryConfig config = new RetryConfigVerb().Load(
                tracer, new MockGVFSEnlistment(git), TimeSpan.FromMinutes(10));
            config.MaxRetries.ShouldEqual(9);
            config.MaxAttempts.ShouldEqual(10);
            config.Timeout.ShouldEqual(TimeSpan.FromMinutes(10));
            config.ReadTimeout.ShouldEqual(TimeSpan.FromSeconds(90));
        }

        [TestCase]
        public void LargestAcceptedValuesDoNotOverflowConsumerArithmetic()
        {
            using MockTracer tracer = new MockTracer();
            MockGitProcess git = CreateGitProcess();
            git.SetExpectedCommandResult("config gvfs.max-retries",
                () => new GitProcess.Result("2147483644", string.Empty, 0));
            git.SetExpectedCommandResult("config gvfs.timeout-seconds",
                () => new GitProcess.Result("2147483", string.Empty, 0));
            RetryConfig config = RetryConfig.LoadFromGitConfig(tracer, git);
            checked(config.MaxAttempts + 2).ShouldEqual(int.MaxValue);
            config.Timeout.ShouldEqual(TimeSpan.FromMilliseconds(2147483000));
            tracer.RelatedWarningEvents.Count.ShouldEqual(0);
        }

        [TestCase]
        [NUnit.Framework.Category(CategoryConstants.ExceptionExpected)]
        public void CompleteLoaderRejectsMissingDependencies()
        {
            Assert.Throws<ArgumentNullException>(() => RetryConfig.LoadFromGitConfig(null, (GitProcess)null));
            Assert.Throws<ArgumentNullException>(() => RetryConfig.LoadFromGitConfig(null, (Enlistment)null));
        }

        private class RetryConfigVerb : GVFSVerb
        {
            public override string EnlistmentRootPathParameter { get; set; }
            protected override string VerbName => "retry-config-test";
            public override void Execute()
            {
            }

            public RetryConfig Load(ITracer tracer, GVFSEnlistment enlistment, TimeSpan timeout)
            {
                return this.GetRetryConfig(tracer, enlistment, timeout);
            }
        }

        private static MockGitProcess CreateGitProcess(MockGitProcess git = null)
        {
            git = git ?? new MockGitProcess();
            foreach (KeyValuePair<string, string> setting in new Dictionary<string, string>
            {
                { "gvfs.max-retries", "9" },
                { "gvfs.timeout-seconds", "45" },
                { "gvfs.read-timeout-seconds", "90" },
            })
            {
                git.SetExpectedCommandResult("config " + setting.Key,
                    () => new GitProcess.Result(setting.Value, string.Empty, GitProcess.Result.SuccessCode));
            }

            return git;
        }

        private class ThrowingGitProcess : MockGitProcess
        {
            private readonly bool processError;

            public ThrowingGitProcess(bool processError)
            {
                this.processError = processError;
            }

            public override ConfigResult GetFromConfig(
                string settingName, bool forceOutsideEnlistment = false, PhysicalFileSystem fileSystem = null)
            {
                if (settingName == "gvfs.read-timeout-seconds")
                {
                    if (this.processError)
                    {
                        throw new Win32Exception("Could not start git");
                    }

                    throw new IOException("Could not read config");
                }

                return base.GetFromConfig(settingName, forceOutsideEnlistment, fileSystem);
            }
        }
    }
}
