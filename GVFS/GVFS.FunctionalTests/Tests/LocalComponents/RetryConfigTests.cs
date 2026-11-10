using GVFS.Common;
using GVFS.Common.Git;
using GVFS.Tests.Should;
using GVFS.UnitTests;
using GVFS.UnitTests.Mock.Common;
using GVFS.UnitTests.Mock.Git;
using NUnit.Framework;
using System;
using System.IO;

namespace GVFS.FunctionalTests.Tests.LocalComponents
{
    [TestFixture]
    [NonParallelizable]
    public class RetryConfigTests
    {
        private IsolatedGitEnvironment environment;
        private string gitPath;

        [SetUp]
        public void SetUp()
        {
            this.gitPath = GitTestHelper.FindGitOnPath();
            this.gitPath.ShouldNotBeNull("Git must be available on PATH.");
            this.environment = new IsolatedGitEnvironment();
        }

        [TearDown]
        public void TearDown()
        {
            this.environment?.Dispose();
            this.environment = null;
        }

        [TestCase]
        public void UserConfigThatGitDoesNotReadIsReadWithGit()
        {
            string[] variables = { "HOMEDRIVE", "HOMEPATH", "USERPROFILE" };
            string[] originalValues = Array.ConvertAll(variables, Environment.GetEnvironmentVariable);
            using (IsolatedLibGit2Config machineConfig = new IsolatedLibGit2Config())
            {
                // Git reads only HOME\.gitconfig. libgit2 also checks USERPROFILE\.gitconfig.
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

                    string missingWorkingDirectory = Path.Combine(machineConfig.Directory, "missing");
                    using MockTracer tracer = new MockTracer();
                    RetryConfig.ConfigStringReader readWithLibGit2 = RetryConfig.CreateLibGit2Reader(tracer, missingWorkingDirectory);
                    GitProcess git = new GitProcess(this.gitPath, workingDirectoryRoot: null);
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
        public void LibGit2AndGitReadTheSameRepositoryValues()
        {
            using (RetryConfigTestRepository repo = new RetryConfigTestRepository("[gvfs]\n\tmax-retries = 7\n\tmax-retries = 8\n\ttimeout-seconds = 31\n\tread-timeout-seconds = 32\n"))
            {
                using MockTracer tracer = new MockTracer();
                RetryConfig libGit2Config = RetryConfig.LoadFromGitConfig(tracer, RetryConfig.CreateLibGit2Reader(tracer, repo.WorkingDirectory));
                RetryConfig gitConfig = RetryConfig.LoadFromGitConfig(tracer, new GitProcess(this.gitPath, repo.WorkingDirectory));

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
            using (RetryConfigTestRepository repo = new RetryConfigTestRepository(string.Empty))
            {
                string includedPath = Path.Combine(repo.WorkingDirectory, "included.gitconfig");
                File.WriteAllText(includedPath, "[gvfs]\n\tmax-retries = 12\n");
                File.AppendAllText(
                    Path.Combine(repo.WorkingDirectory, ".git", "config"),
                    "[remote \"origin\"]\n\turl = https://example.com/repo\n" +
                    "[includeIf \"hasconfig:remote.*.url:https://example.com/**\"]\n\tpath = " + includedPath.Replace('\\', '/') + "\n");

                using MockTracer tracer = new MockTracer();
                RetryConfig.ConfigStringReader readWithLibGit2 = RetryConfig.CreateLibGit2Reader(tracer, repo.WorkingDirectory);
                GitProcess git = new GitProcess(this.gitPath, repo.WorkingDirectory);
                RetryConfig.ConfigStringReader readWithGit = (string name, out string value, out string error) =>
                    git.GetFromConfig(name).TryParseAsString(out value, out error);

                readWithLibGit2("gvfs.max-retries", out string _, out string libGit2Error).ShouldBeFalse();
                libGit2Error.ShouldContain("libgit2 does not evaluate like git: includeif.hasconfig:remote.*.url:https://example.com/**.path");

                RetryConfig config = RetryConfig.LoadFromGitConfig(tracer, RetryConfig.WithGitFallback(tracer, readWithLibGit2, readWithGit));
                config.MaxRetries.ShouldEqual(12);
                tracer.RelatedWarningEvents.Count.ShouldEqual(0);
            }
        }

        // Each layout makes libgit2 evaluate the include differently from Git.
        [TestCase("onbranch:main", false)]
        [TestCase("onbranch:main", true)]
        [TestCase("gitdir:<git dir without drive>", false)]
        [TestCase("gitdir/i:<git dir without drive>", false)]
        public void IncludeLayoutsLibGit2EvaluatesDifferentlyAreReadWithGit(string condition, bool headOnBranch)
        {
            using (RetryConfigTestRepository repo = new RetryConfigTestRepository(string.Empty))
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

                using MockTracer tracer = new MockTracer();
                RetryConfig.ConfigStringReader readWithLibGit2 = RetryConfig.CreateLibGit2Reader(tracer, repo.WorkingDirectory);
                GitProcess git = new GitProcess(this.gitPath, repo.WorkingDirectory);
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
    }
}
