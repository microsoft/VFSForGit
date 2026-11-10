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

            // The enlistment's working directory does not exist, so libgit2 reads the isolated
            // global config. git.exe reads the same values when the environment changes Git config.
            using IsolatedLibGit2Config machineConfig = new IsolatedLibGit2Config();
            File.WriteAllText(machineConfig.GlobalConfigPath, "[gvfs]\n\tmax-retries = 9\n\ttimeout-seconds = 45\n\tread-timeout-seconds = 90\n");
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

        [TestCase]
        public void LibGit2ReaderReadsRepositoryValues()
        {
            using (TestRepository repo = new TestRepository("[gvfs]\n\tmax-retries = 7\n\ttimeout-seconds = 31\n\tread-timeout-seconds = 0\n"))
            {
                MockTracer tracer = new MockTracer();
                RetryConfig config = RetryConfig.LoadFromGitConfig(tracer, RetryConfig.CreateLibGit2Reader(tracer, repo.WorkingDirectory));

                config.MaxRetries.ShouldEqual(7);
                config.Timeout.ShouldEqual(TimeSpan.FromSeconds(31));
                config.ReadTimeout.ShouldEqual(System.Threading.Timeout.InfiniteTimeSpan);
                tracer.RelatedWarningEvents.Count.ShouldEqual(0);
            }
        }

        [TestCase]
        public void LibGit2ReaderUsesDefaultValuesWhenEntriesAreBlank()
        {
            using (TestRepository repo = new TestRepository("[gvfs]\n\tmax-retries =\n\ttimeout-seconds\n"))
            {
                MockTracer tracer = new MockTracer();
                RetryConfig config = RetryConfig.LoadFromGitConfig(tracer, RetryConfig.CreateLibGit2Reader(tracer, repo.WorkingDirectory));

                config.MaxRetries.ShouldEqual(RetryConfig.DefaultMaxRetries);
                config.Timeout.ShouldEqual(TimeSpan.FromSeconds(RetryConfig.DefaultTimeoutSeconds));
                config.ReadTimeout.ShouldEqual(TimeSpan.FromSeconds(RetryConfig.DefaultReadTimeoutSeconds));
                tracer.RelatedWarningEvents.Count.ShouldEqual(0);
            }
        }

        [TestCase("max-retries = abc", "gvfs.max-retries", "Misconfigured config setting gvfs.max-retries, could not parse value `abc` as an int")]
        [TestCase("timeout-seconds = 0", "gvfs.timeout-seconds", "Invalid value 0 for setting gvfs.timeout-seconds, value must be greater than or equal to 1")]
        [TestCase("read-timeout-seconds = 2147484", "gvfs.read-timeout-seconds", "Invalid value 2147484 for setting gvfs.read-timeout-seconds, value must be between 0 and 2147483")]
        public void LibGit2ReaderReportsTheSameInvalidValueErrorsAsGit(string setting, string configName, string expectedError)
        {
            using (TestRepository repo = new TestRepository("[gvfs]\n\t" + setting + "\n"))
            {
                MockTracer tracer = new MockTracer();
                RetryConfig.LoadFromGitConfig(tracer, RetryConfig.CreateLibGit2Reader(tracer, repo.WorkingDirectory));

                tracer.RelatedWarningEvents.Count.ShouldEqual(1);
                tracer.RelatedWarningEvents[0].ShouldContain("\"ConfigName\":\"" + configName + "\"", expectedError.Replace("`", "\\u0060"));
            }
        }

        [TestCase]
        public void LibGit2ReaderUsesDefaultsWhenConfigIsCorrupt()
        {
            using (TestRepository repo = new TestRepository("[gvfs\n"))
            {
                MockTracer tracer = new MockTracer();
                RetryConfig config = RetryConfig.LoadFromGitConfig(tracer, RetryConfig.CreateLibGit2Reader(tracer, repo.WorkingDirectory));

                config.MaxRetries.ShouldEqual(RetryConfig.DefaultMaxRetries);
                config.Timeout.ShouldEqual(TimeSpan.FromSeconds(RetryConfig.DefaultTimeoutSeconds));
                List<string> settingWarnings = tracer.RelatedWarningEvents.FindAll(warning => warning.Contains("The retry setting is invalid or unavailable"));
                settingWarnings.Count.ShouldEqual(3);
                settingWarnings[0].ShouldContain("Error while reading", "failed to parse config file");
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

            RetryConfig config = RetryConfig.LoadFromGitConfig(tracer, readConfig);

            config.MaxRetries.ShouldEqual(4);
            config.Timeout.ShouldEqual(TimeSpan.FromSeconds(4));
            config.ReadTimeout.ShouldEqual(TimeSpan.FromSeconds(4));
            gitReads.ShouldMatchInOrder("gvfs.max-retries", "gvfs.timeout-seconds", "gvfs.read-timeout-seconds");
            int readWithGitEvent = tracer.RelatedEventNames.IndexOf("RetryConfig_ReadWithGit");
            readWithGitEvent.ShouldEqual(0);
            tracer.RelatedEventNames.FindAll(eventName => eventName == "RetryConfig_ReadWithGit").Count.ShouldEqual(3);
            EventMetadata metadata = tracer.RelatedEventMetadata[readWithGitEvent];
            metadata["Area"].ShouldEqual("RetryConfig");
            metadata["ConfigName"].ShouldEqual("gvfs.max-retries");
            ((string)metadata["LibGit2Error"]).ShouldContain("Couldn't open repo");
            tracer.RelatedWarningEvents.Count.ShouldEqual(0);
        }

        [TestCase]
        public void GitFallbackDoesNotReadAgainAfterATransientFailure()
        {
            MockTracer tracer = new MockTracer();
            int gitReads = 0;
            RetryConfig.ConfigStringReader readConfig = RetryConfig.WithGitFallback(
                tracer,
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

            RetryConfig config = RetryConfig.LoadFromGitConfig(tracer, readConfig);

            gitReads.ShouldEqual(0);
            config.MaxRetries.ShouldEqual(RetryConfig.DefaultMaxRetries);
            tracer.RelatedWarningEvents.Count.ShouldEqual(3);
            tracer.RelatedWarningEvents[0].ShouldContain("being used by another process");
        }

        [TestCase]
        public void GitFallbackReportsTheGitErrorWhenBothReadersFail()
        {
            MockTracer tracer = new MockTracer();
            RetryConfig.ConfigStringReader readConfig = RetryConfig.WithGitFallback(
                tracer,
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

            RetryConfig config = RetryConfig.LoadFromGitConfig(tracer, readConfig);

            config.MaxRetries.ShouldEqual(RetryConfig.DefaultMaxRetries);
            tracer.RelatedWarningEvents.Count.ShouldEqual(3);
            tracer.RelatedWarningEvents[0].ShouldContain("\"Error\":\"git error\"");
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

            RetryConfig config = RetryConfig.LoadFromGitConfig(tracer, readConfig);

            config.MaxRetries.ShouldEqual(RetryConfig.DefaultMaxRetries);
            gitReads.ShouldEqual(0);
            tracer.RelatedEventNames.Contains("RetryConfig_ReadWithGit").ShouldBeFalse();
        }

        [TestCase]
        public void LibGit2ReaderReadsGlobalConfigBeforeTheRepositoryExists()
        {
            using (IsolatedLibGit2Config machineConfig = new IsolatedLibGit2Config())
            {
                File.WriteAllText(machineConfig.GlobalConfigPath, "[gvfs]\n\tmax-retries = 9\n\ttimeout-seconds = 41\n\tread-timeout-seconds = 42\n");
                string missingWorkingDirectory = Path.Combine(Path.GetTempPath(), "RetryConfigTests_" + Guid.NewGuid().ToString("N"));

                MockTracer tracer = new MockTracer();
                RetryConfig config = RetryConfig.LoadFromGitConfig(tracer, RetryConfig.CreateLibGit2Reader(tracer, missingWorkingDirectory));

                config.MaxRetries.ShouldEqual(9);
                config.Timeout.ShouldEqual(TimeSpan.FromSeconds(41));
                config.ReadTimeout.ShouldEqual(TimeSpan.FromSeconds(42));
                tracer.RelatedWarningEvents.Count.ShouldEqual(0);
            }
        }

        [TestCase]
        public void UserConfigThatGitDoesNotReadIsReadWithGit()
        {
            string gitPath = GetGitPathOrIgnore();
            string[] variables = { "HOMEDRIVE", "HOMEPATH", "USERPROFILE" };
            string[] originalValues = Array.ConvertAll(variables, Environment.GetEnvironmentVariable);
            using (IsolatedLibGit2Config machineConfig = new IsolatedLibGit2Config())
            {
                // git reads only HOME\.gitconfig. libgit2 also falls back to USERPROFILE\.gitconfig.
                string emptyHome = Path.Combine(machineConfig.Directory, "home");
                string profile = Path.Combine(machineConfig.Directory, "profile");
                Directory.CreateDirectory(emptyHome);
                Directory.CreateDirectory(profile);
                File.WriteAllText(Path.Combine(profile, ".gitconfig"), "[gvfs]\n\ttimeout-seconds = 1\n");
                try
                {
                    Environment.SetEnvironmentVariable("HOME", emptyHome);
                    Environment.SetEnvironmentVariable("HOMEDRIVE", null);
                    Environment.SetEnvironmentVariable("HOMEPATH", null);
                    Environment.SetEnvironmentVariable("USERPROFILE", profile);
                    IsolatedLibGit2Config.ResetUserSearchPathsFromEnvironment();

                    string missingWorkingDirectory = Path.Combine(Path.GetTempPath(), "RetryConfigTests_" + Guid.NewGuid().ToString("N"));
                    MockTracer tracer = new MockTracer();
                    RetryConfig.ConfigStringReader readWithLibGit2 = RetryConfig.CreateLibGit2Reader(tracer, missingWorkingDirectory);
                    GitProcess git = new GitProcess(gitPath, workingDirectoryRoot: null);
                    RetryConfig.ConfigStringReader readWithGit = (string name, out string value, out string error) =>
                        git.GetFromConfig(name).TryParseAsString(out value, out error);

                    readWithLibGit2("gvfs.timeout-seconds", out string _, out string libGit2Error).ShouldBeFalse();
                    libGit2Error.ShouldContain("libgit2 reads the global config file", Path.Combine(profile, ".gitconfig"));
                    readWithGit("gvfs.timeout-seconds", out string gitValue, out string gitError).ShouldBeTrue(gitError);
                    gitValue.ShouldBeNull();

                    RetryConfig config = RetryConfig.LoadFromGitConfig(tracer, RetryConfig.WithGitFallback(tracer, readWithLibGit2, readWithGit));
                    config.Timeout.ShouldEqual(TimeSpan.FromSeconds(RetryConfig.DefaultTimeoutSeconds));
                }
                finally
                {
                    for (int i = 0; i < variables.Length; ++i)
                    {
                        Environment.SetEnvironmentVariable(variables[i], originalValues[i]);
                    }
                }
            }
        }

        [TestCase]
        public void DisposingARepositoryWithoutNativeStateKeepsTheIsolatedConfig()
        {
            using (IsolatedLibGit2Config machineConfig = new IsolatedLibGit2Config())
            {
                File.WriteAllText(machineConfig.GlobalConfigPath, "[gvfs]\n\tmax-retries = 9\n");

                // This repository never initialized libgit2, so disposing it must not shut libgit2 down.
                new MockLibGit2Repo(new MockTracer()).Dispose();

                string missingWorkingDirectory = Path.Combine(Path.GetTempPath(), "RetryConfigTests_" + Guid.NewGuid().ToString("N"));
                RetryConfig.CreateLibGit2Reader(new MockTracer(), missingWorkingDirectory)("gvfs.max-retries", out string value, out string error)
                    .ShouldBeTrue(error);
                value.ShouldEqual("9");
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

                error.ShouldContain("libgit2 does not evaluate like git: includeif.hasconfig:");
            }
        }

        [TestCase]
        public void LibGit2AndGitReadTheSameRepositoryValues()
        {
            string gitPath = GetGitPathOrIgnore();
            using (TestRepository repo = new TestRepository("[gvfs]\n\tmax-retries = 7\n\tmax-retries = 8\n\ttimeout-seconds = 31\n\tread-timeout-seconds = 32\n"))
            {
                RetryConfig libGit2Config = RetryConfig.LoadFromGitConfig(new MockTracer(), RetryConfig.CreateLibGit2Reader(new MockTracer(), repo.WorkingDirectory));
                RetryConfig gitConfig = RetryConfig.LoadFromGitConfig(new MockTracer(), new GitProcess(gitPath, repo.WorkingDirectory));

                libGit2Config.MaxRetries.ShouldEqual(8);
                gitConfig.MaxRetries.ShouldEqual(8);
                libGit2Config.Timeout.ShouldEqual(TimeSpan.FromSeconds(31));
                gitConfig.Timeout.ShouldEqual(TimeSpan.FromSeconds(31));
                libGit2Config.ReadTimeout.ShouldEqual(TimeSpan.FromSeconds(32));
                gitConfig.ReadTimeout.ShouldEqual(TimeSpan.FromSeconds(32));
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
                libGit2Error.ShouldContain("libgit2 does not evaluate like git: includeif.hasconfig:remote.*.url:https://example.com/**.path");

                RetryConfig config = RetryConfig.LoadFromGitConfig(tracer, RetryConfig.WithGitFallback(tracer, readWithLibGit2, readWithGit));
                config.MaxRetries.ShouldEqual(12);
                tracer.RelatedWarningEvents.Count.ShouldEqual(0);
            }
        }

        // In each layout, libgit2 evaluates the include differently from git.exe.
        [TestCase("onbranch:main", false)]
        [TestCase("onbranch:main", true)]
        [TestCase("gitdir:<git dir without drive>", false)]
        [TestCase("gitdir/i:<git dir without drive>", false)]
        public void IncludeLayoutsLibGit2EvaluatesDifferentlyAreReadWithGit(string condition, bool headOnBranch)
        {
            string gitPath = GetGitPathOrIgnore();
            using (TestRepository repo = new TestRepository(string.Empty))
            {
                if (!headOnBranch)
                {
                    File.WriteAllText(Path.Combine(repo.WorkingDirectory, ".git", "HEAD"), new string('1', 40) + "\n");
                }

                string gitDir = Path.Combine(repo.WorkingDirectory, ".git").Replace('\\', '/') + "/";
                string includedPath = Path.Combine(repo.WorkingDirectory, "included.gitconfig");
                File.WriteAllText(includedPath, "[gvfs]\n\tmax-retries = 13\n");
                File.AppendAllText(
                    Path.Combine(repo.WorkingDirectory, ".git", "config"),
                    "[includeIf \"" + condition.Replace("<git dir without drive>", gitDir.Substring(2)) + "\"]\n\tpath = " +
                    includedPath.Replace('\\', '/') + "\n");

                MockTracer tracer = new MockTracer();
                RetryConfig.ConfigStringReader readWithLibGit2 = RetryConfig.CreateLibGit2Reader(tracer, repo.WorkingDirectory);
                GitProcess git = new GitProcess(gitPath, repo.WorkingDirectory);
                RetryConfig.ConfigStringReader readWithGit = (string name, out string value, out string error) =>
                    git.GetFromConfig(name).TryParseAsString(out value, out error);

                readWithLibGit2("gvfs.max-retries", out string _, out string libGit2Error).ShouldBeFalse();
                libGit2Error.ShouldContain("libgit2 does not evaluate like git");

                readWithGit("gvfs.max-retries", out string gitValue, out string gitError).ShouldBeTrue(gitError);
                RetryConfig config = RetryConfig.LoadFromGitConfig(tracer, RetryConfig.WithGitFallback(tracer, readWithLibGit2, readWithGit));
                config.MaxRetries.ShouldEqual(gitValue == null ? RetryConfig.DefaultMaxRetries : int.Parse(gitValue));
                config.MaxRetries.ShouldEqual(headOnBranch && condition.StartsWith("onbranch:") ? 13 : RetryConfig.DefaultMaxRetries);
                tracer.RelatedWarningEvents.Count.ShouldEqual(0);
            }
        }

        // git.exe expands %(prefix) to its installation directory. libgit2 skips the include.
        [TestCase("[include]\n\tpath = %(prefix)/etc/gvfs.gitconfig\n")]
        [TestCase("[includeIf \"gitdir:**/\"]\n\tpath = %(prefix)/etc/gvfs.gitconfig\n")]
        public void IncludePathsWithAGitPrefixAreReadWithGit(string includeConfig)
        {
            using (TestRepository repo = new TestRepository(includeConfig))
            {
                RetryConfig.CreateLibGit2Reader(new MockTracer(), repo.WorkingDirectory)("gvfs.max-retries", out string _, out string error)
                    .ShouldBeFalse();
                error.ShouldContain("libgit2 does not evaluate like git", "%(prefix)/etc/gvfs.gitconfig");
            }
        }

        [TestCase("gitdir:**/")]
        [TestCase("gitdir/i:**/")]
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
            string gitPath = GitTestHelper.FindGitOnPath();
            if (gitPath == null)
            {
                Assert.Ignore("Git was not found on PATH.");
            }

            return gitPath;
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
