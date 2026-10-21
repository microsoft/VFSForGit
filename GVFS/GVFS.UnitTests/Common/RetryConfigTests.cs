using GVFS.Common;
using GVFS.Common.Git;
using GVFS.Common.Tracing;
using GVFS.Tests.Should;
using GVFS.UnitTests.Mock.Common;
using GVFS.UnitTests.Mock.Git;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.IO;

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

            RetryConfig config;
            string error;
            RetryConfig.TryLoadFromGitConfig(tracer, gitProcess, out config, out error).ShouldEqual(true);
            error.ShouldEqual(string.Empty);
            config.MaxRetries.ShouldEqual(RetryConfig.DefaultMaxRetries);
            config.MaxAttempts.ShouldEqual(config.MaxRetries + 1);
            config.Timeout.ShouldEqual(TimeSpan.FromSeconds(RetryConfig.DefaultTimeoutSeconds));
        }

        [TestCase]
        public void TryLoadConfigUsesDefaultValuesWhenEntriesAreBlank()
        {
            MockTracer tracer = new MockTracer();
            MockGitProcess gitProcess = new MockGitProcess();
            gitProcess.SetExpectedCommandResult("config gvfs.max-retries", () => new GitProcess.Result(string.Empty, string.Empty, GitProcess.Result.SuccessCode));
            gitProcess.SetExpectedCommandResult("config gvfs.timeout-seconds", () => new GitProcess.Result(string.Empty, string.Empty, GitProcess.Result.SuccessCode));

            RetryConfig config;
            string error;
            RetryConfig.TryLoadFromGitConfig(tracer, gitProcess, out config, out error).ShouldEqual(true);
            error.ShouldEqual(string.Empty);
            config.MaxRetries.ShouldEqual(RetryConfig.DefaultMaxRetries);
            config.MaxAttempts.ShouldEqual(config.MaxRetries + 1);
            config.Timeout.ShouldEqual(TimeSpan.FromSeconds(RetryConfig.DefaultTimeoutSeconds));
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

            MockTracer tracer = new MockTracer();
            MockGitProcess gitProcess = new MockGitProcess();
            gitProcess.SetExpectedCommandResult("config gvfs.max-retries", () => new GitProcess.Result(maxRetries.ToString(), string.Empty, GitProcess.Result.SuccessCode));
            gitProcess.SetExpectedCommandResult("config gvfs.timeout-seconds", () => new GitProcess.Result(timeoutSeconds.ToString(), string.Empty, GitProcess.Result.SuccessCode));

            RetryConfig config;
            string error;
            RetryConfig.TryLoadFromGitConfig(tracer, gitProcess, out config, out error).ShouldEqual(true);
            error.ShouldEqual(string.Empty);
            config.MaxRetries.ShouldEqual(maxRetries);
            config.MaxAttempts.ShouldEqual(config.MaxRetries + 1);
            config.Timeout.ShouldEqual(TimeSpan.FromSeconds(timeoutSeconds));
        }

        [TestCase]
        public void TryLoadConfigWithLibGit2UsesRepositoryValues()
        {
            using (TestRepository repo = new TestRepository("[gvfs]\n\tmax-retries = 7\n\ttimeout-seconds = 31\n"))
            {
                RetryConfig.TryLoadFromGitConfig(new MockTracer(), RetryConfig.CreateLibGit2Reader(new MockTracer(), repo.WorkingDirectory), out RetryConfig config, out string error)
                    .ShouldBeTrue(error);

                error.ShouldEqual(string.Empty);
                config.MaxRetries.ShouldEqual(7);
                config.Timeout.ShouldEqual(TimeSpan.FromSeconds(31));
            }
        }

        [TestCase]
        public void TryLoadConfigWithLibGit2UsesDefaultValuesWhenEntriesAreBlank()
        {
            using (TestRepository repo = new TestRepository("[gvfs]\n\tmax-retries =\n\ttimeout-seconds\n"))
            {
                RetryConfig.TryLoadFromGitConfig(new MockTracer(), RetryConfig.CreateLibGit2Reader(new MockTracer(), repo.WorkingDirectory), out RetryConfig config, out string error)
                    .ShouldBeTrue(error);

                config.MaxRetries.ShouldEqual(RetryConfig.DefaultMaxRetries);
                config.Timeout.ShouldEqual(TimeSpan.FromSeconds(RetryConfig.DefaultTimeoutSeconds));
            }
        }

        [TestCase]
        public void TryLoadConfigWithLibGit2ReportsTheSameParseErrorAsGit()
        {
            using (TestRepository repo = new TestRepository("[gvfs]\n\tmax-retries = abc\n"))
            {
                RetryConfig.TryLoadFromGitConfig(new MockTracer(), RetryConfig.CreateLibGit2Reader(new MockTracer(), repo.WorkingDirectory), out RetryConfig _, out string error)
                    .ShouldBeFalse();

                error.ShouldEqual("Misconfigured config setting gvfs.max-retries, could not parse value `abc` as an int");
            }
        }

        [TestCase]
        public void TryLoadConfigWithLibGit2EnforcesMinimumValuesOnTimeout()
        {
            using (TestRepository repo = new TestRepository("[gvfs]\n\tmax-retries = 3\n\ttimeout-seconds = -1\n"))
            {
                MockTracer tracer = new MockTracer();
                RetryConfig.TryLoadFromGitConfig(tracer, RetryConfig.CreateLibGit2Reader(tracer, repo.WorkingDirectory), out RetryConfig _, out string error)
                    .ShouldBeFalse();

                error.ShouldEqual("Invalid value -1 for setting gvfs.timeout-seconds, value must be greater than or equal to 0");
                tracer.RelatedErrorEvents.Count.ShouldEqual(1);
                tracer.RelatedErrorEvents[0].ShouldContain("TryLoadTimeout failed");
            }
        }

        [TestCase]
        public void TryLoadConfigWithLibGit2FailsWhenConfigIsCorrupt()
        {
            using (TestRepository repo = new TestRepository("[gvfs\n"))
            {
                MockTracer tracer = new MockTracer();
                RetryConfig.TryLoadFromGitConfig(tracer, RetryConfig.CreateLibGit2Reader(tracer, repo.WorkingDirectory), out RetryConfig config, out string error)
                    .ShouldBeFalse();

                config.ShouldBeNull();
                error.ShouldContain("Error while reading 'gvfs.max-retries' from config");
                error.ShouldContain("failed to parse config file");
                tracer.RelatedErrorEvents[0].ShouldContain("TryLoadMaxRetries failed");
            }
        }

        [TestCase]
        public void GitFallbackReadsWithGitWhenLibGit2Fails()
        {
            MockTracer tracer = new MockTracer();
            List<string> gitReads = new List<string>();
            RetryConfig.ConfigStringReader readConfig = RetryConfig.WithGitFallback(
                tracer,
                (string name, out string value, out string error) =>
                {
                    value = null;
                    error = "Error while reading '" + name + "' from config: Couldn't open repo";
                    return false;
                },
                (string name, out string value, out string error) =>
                {
                    gitReads.Add(name);
                    value = "4";
                    error = string.Empty;
                    return true;
                });

            RetryConfig.TryLoadFromGitConfig(tracer, readConfig, out RetryConfig config, out string loadError).ShouldBeTrue(loadError);

            config.MaxRetries.ShouldEqual(4);
            config.Timeout.ShouldEqual(TimeSpan.FromSeconds(4));
            gitReads.ShouldMatchInOrder("gvfs.max-retries", "gvfs.timeout-seconds");
            int readWithGitEvent = tracer.RelatedEventNames.IndexOf("RetryConfig_ReadWithGit");
            readWithGitEvent.ShouldEqual(0);
            tracer.RelatedEventNames.FindAll(eventName => eventName == "RetryConfig_ReadWithGit").Count.ShouldEqual(2);
            EventMetadata metadata = tracer.RelatedEventMetadata[readWithGitEvent];
            metadata["Area"].ShouldEqual("RetryConfig");
            metadata["ConfigName"].ShouldEqual("gvfs.max-retries");
            ((string)metadata["LibGit2Error"]).ShouldContain("Couldn't open repo");
            tracer.RelatedWarningEvents.Count.ShouldEqual(0);
        }

        [TestCase]
        public void GitFallbackDoesNotReadAgainAfterATransientFailure()
        {
            int gitReads = 0;
            RetryConfig.ConfigStringReader readConfig = RetryConfig.WithGitFallback(
                new MockTracer(),
                (string name, out string value, out string error) =>
                {
                    value = null;
                    error = "Error while reading '" + name + "' from config: could not open 'C:/repo/.git/config': " +
                        "The process cannot access the file because it is being used by another process.";
                    return false;
                },
                (string name, out string value, out string error) =>
                {
                    ++gitReads;
                    value = null;
                    error = null;
                    return true;
                });

            RetryConfig.TryLoadFromGitConfig(new MockTracer(), readConfig, out RetryConfig _, out string loadError).ShouldBeFalse();

            gitReads.ShouldEqual(0);
            loadError.ShouldContain("being used by another process");
        }

        [TestCase]
        public void GitFallbackReportsTheGitErrorWhenBothReadersFail()
        {
            RetryConfig.ConfigStringReader readConfig = RetryConfig.WithGitFallback(
                new MockTracer(),
                (string name, out string value, out string error) =>
                {
                    value = null;
                    error = "libgit2 error";
                    return false;
                },
                (string name, out string value, out string error) =>
                {
                    value = null;
                    error = "git error";
                    return false;
                });

            RetryConfig.TryLoadFromGitConfig(new MockTracer(), readConfig, out RetryConfig config, out string loadError).ShouldBeFalse();

            config.ShouldBeNull();
            loadError.ShouldEqual("git error");
        }

        [TestCase]
        public void GitFallbackDoesNotReadWithGitWhenLibGit2Succeeds()
        {
            MockTracer tracer = new MockTracer();
            int gitReads = 0;
            RetryConfig.ConfigStringReader readConfig = RetryConfig.WithGitFallback(
                tracer,
                (string name, out string value, out string error) =>
                {
                    value = null;
                    error = null;
                    return true;
                },
                (string name, out string value, out string error) =>
                {
                    ++gitReads;
                    value = null;
                    error = null;
                    return true;
                });

            RetryConfig.TryLoadFromGitConfig(tracer, readConfig, out RetryConfig config, out string _).ShouldBeTrue();

            config.MaxRetries.ShouldEqual(RetryConfig.DefaultMaxRetries);
            gitReads.ShouldEqual(0);
            tracer.RelatedEventNames.Contains("RetryConfig_ReadWithGit").ShouldBeFalse();
        }

        [TestCase]
        public void LibGit2ReaderReadsGlobalConfigBeforeTheRepositoryExists()
        {
            using (IsolatedLibGit2Config machineConfig = new IsolatedLibGit2Config())
            {
                File.WriteAllText(machineConfig.GlobalConfigPath, "[gvfs]\n\tmax-retries = 9\n\ttimeout-seconds = 41\n");
                string missingWorkingDirectory = Path.Combine(Path.GetTempPath(), "RetryConfigTests_" + Guid.NewGuid().ToString("N"));

                RetryConfig.TryLoadFromGitConfig(new MockTracer(), RetryConfig.CreateLibGit2Reader(new MockTracer(), missingWorkingDirectory), out RetryConfig config, out string error)
                    .ShouldBeTrue(error);

                config.MaxRetries.ShouldEqual(9);
                config.Timeout.ShouldEqual(TimeSpan.FromSeconds(41));
            }
        }

        [TestCase]
        public void LibGit2ReaderRejectsAGlobalIncludeConditionItIgnores()
        {
            using (IsolatedLibGit2Config machineConfig = new IsolatedLibGit2Config())
            {
                File.WriteAllText(
                    machineConfig.GlobalConfigPath,
                    "[includeIf \"hasconfig:remote.*.url:https://example.com/**\"]\n\tpath = C:/does-not-matter.gitconfig\n");
                string missingWorkingDirectory = Path.Combine(Path.GetTempPath(), "RetryConfigTests_" + Guid.NewGuid().ToString("N"));

                RetryConfig.CreateLibGit2Reader(new MockTracer(), missingWorkingDirectory)("gvfs.max-retries", out string _, out string error)
                    .ShouldBeFalse();

                error.ShouldContain("includeIf condition that libgit2 does not evaluate: hasconfig:");
            }
        }

        [TestCase]
        public void LibGit2AndGitReadTheSameRepositoryValues()
        {
            string gitPath = GetGitPathOrIgnore();
            using (TestRepository repo = new TestRepository("[gvfs]\n\tmax-retries = 7\n\tmax-retries = 8\n\ttimeout-seconds = 31\n"))
            {
                RetryConfig.TryLoadFromGitConfig(new MockTracer(), RetryConfig.CreateLibGit2Reader(new MockTracer(), repo.WorkingDirectory), out RetryConfig libGit2Config, out string libGit2Error)
                    .ShouldBeTrue(libGit2Error);
                RetryConfig.TryLoadFromGitConfig(new MockTracer(), new GitProcess(gitPath, repo.WorkingDirectory), out RetryConfig gitConfig, out string gitError)
                    .ShouldBeTrue(gitError);

                libGit2Config.MaxRetries.ShouldEqual(8);
                gitConfig.MaxRetries.ShouldEqual(8);
                libGit2Config.Timeout.ShouldEqual(TimeSpan.FromSeconds(31));
                gitConfig.Timeout.ShouldEqual(TimeSpan.FromSeconds(31));
            }
        }

        [TestCase]
        public void ConfigWithAnIncludeConditionLibGit2IgnoresIsReadWithGit()
        {
            string gitPath = GetGitPathOrIgnore();
            using (TestRepository repo = new TestRepository(string.Empty))
            {
                string includedPath = Path.Combine(repo.WorkingDirectory, "included.gitconfig");
                File.WriteAllText(includedPath, "[gvfs]\n\tmax-retries = 12\n");
                File.AppendAllText(
                    Path.Combine(repo.WorkingDirectory, ".git", "config"),
                    "[remote \"origin\"]\n\turl = https://example.com/repo\n" +
                    "[includeIf \"hasconfig:remote.*.url:https://example.com/**\"]\n\tpath = " + includedPath.Replace('\\', '/') + "\n");

                MockTracer tracer = new MockTracer();
                RetryConfig.ConfigStringReader readWithLibGit2 = RetryConfig.CreateLibGit2Reader(tracer, repo.WorkingDirectory);
                GitProcess git = new GitProcess(gitPath, repo.WorkingDirectory);
                RetryConfig.ConfigStringReader readWithGit = (string name, out string value, out string error) =>
                    git.GetFromConfig(name).TryParseAsString(out value, out error);

                readWithLibGit2("gvfs.max-retries", out string _, out string libGit2Error).ShouldBeFalse();
                libGit2Error.ShouldContain("includeIf condition that libgit2 does not evaluate: hasconfig:remote.*.url:https://example.com/**");

                RetryConfig.TryLoadFromGitConfig(tracer, RetryConfig.WithGitFallback(tracer, readWithLibGit2, readWithGit), out RetryConfig config, out string loadError)
                    .ShouldBeTrue(loadError);
                config.MaxRetries.ShouldEqual(12);
            }
        }

        [TestCase("gitdir:**/")]
        [TestCase("gitdir/i:**/")]
        [TestCase("onbranch:main")]
        public void LibGit2ReadsConfigWithIncludeConditionsItEvaluates(string condition)
        {
            using (TestRepository repo = new TestRepository(string.Empty))
            {
                string includedPath = Path.Combine(repo.WorkingDirectory, "included.gitconfig");
                File.WriteAllText(includedPath, "[gvfs]\n\tmax-retries = 13\n");
                File.AppendAllText(
                    Path.Combine(repo.WorkingDirectory, ".git", "config"),
                    "[includeIf \"" + condition + "\"]\n\tpath = " + includedPath.Replace('\\', '/') + "\n");

                RetryConfig.CreateLibGit2Reader(new MockTracer(), repo.WorkingDirectory)("gvfs.max-retries", out string value, out string error)
                    .ShouldBeTrue(error);
                value.ShouldEqual("13");
            }
        }

        private static string GetGitPathOrIgnore()
        {
            string gitPath = "C:\\Program Files\\Git\\cmd\\git.exe";
            if (!File.Exists(gitPath))
            {
                Assert.Ignore("Git not found at expected path — skipping integration test");
            }

            return gitPath;
        }

        // A minimal repository that libgit2 can open without git.exe. libgit2 does not read the
        // machine's config while the repository exists. The non-ASCII path checks the UTF-8
        // marshalling of the repository path.
        private sealed class TestRepository : IDisposable
        {
            private readonly IsolatedLibGit2Config machineConfig = new IsolatedLibGit2Config();

            public TestRepository(string extraConfig)
            {
                this.WorkingDirectory = Path.Combine(Path.GetTempPath(), "RetryConfigTests_ü_" + Guid.NewGuid().ToString("N"));
                string dotGit = Path.Combine(this.WorkingDirectory, ".git");
                Directory.CreateDirectory(Path.Combine(dotGit, "objects"));
                Directory.CreateDirectory(Path.Combine(dotGit, "refs", "heads"));
                File.WriteAllText(Path.Combine(dotGit, "HEAD"), "ref: refs/heads/main\n");
                File.WriteAllText(
                    Path.Combine(dotGit, "config"),
                    "[core]\n\trepositoryformatversion = 0\n\tbare = false\n" + extraConfig);
            }

            public string WorkingDirectory { get; }

            public void Dispose()
            {
                Directory.Delete(this.WorkingDirectory, recursive: true);
                this.machineConfig.Dispose();
            }
        }
    }
}
