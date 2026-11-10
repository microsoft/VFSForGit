using GVFS.Common.Git;
using GVFS.Common.Tracing;
using GVFS.Tests.Should;
using GVFS.FunctionalTests.Properties;
using GVFS.UnitTests.Mock.Common;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;

namespace GVFS.FunctionalTests.Tests.LocalComponents
{
    [TestFixture]
    [NonParallelizable]
    public class GitProcessCredentialTests
    {
        private const string SecretValue = "S3cretSentinelValueThatMustNotBeTraced";
        private const int GitFatalExitCode = 128;

        [TestCase(true, 30000)]
        [TestCase(false, 30000)]
        [TestCase(true, Timeout.Infinite)]
        [TestCase(false, Timeout.Infinite)]
        public void CredentialFillHandlesARealLockedGitConfig(bool releaseAfterFirstAttempt, int timeoutMs)
        {
            string gitPath = Settings.Default.PathToGit;

            using IsolatedGitEnvironment environment = new IsolatedGitEnvironment();
            string repoPath = Path.Combine(environment.RootDirectory, "repo");
            string dotGit = Path.Combine(repoPath, ".git");
            Directory.CreateDirectory(Path.Combine(dotGit, "objects"));
            Directory.CreateDirectory(Path.Combine(dotGit, "refs", "heads"));
            File.WriteAllText(Path.Combine(dotGit, "HEAD"), "ref: refs/heads/main\n");
            File.WriteAllText(Path.Combine(dotGit, "config"), "[core]\n\trepositoryformatversion = 0\n\tbare = false\n");
            LockedConfigGitProcess gitProcess = new LockedConfigGitProcess(gitPath, repoPath, releaseAfterFirstAttempt);
            using MockTracer tracer = new MockTracer();

            bool success = gitProcess.TryGetCredential(tracer, "mock://repoUrl", out string username, out string password, out string error, timeoutMs: timeoutMs);

            success.ShouldEqual(releaseAfterFirstAttempt, error);
            gitProcess.Results.Count.ShouldEqual(releaseAfterFirstAttempt ? 2 : 5);
            gitProcess.Results[0].ExitCode.ShouldEqual(GitFatalExitCode);
            gitProcess.Results[0].Errors.ShouldContain("Permission denied");
            gitProcess.Results[0].Errors.ShouldContain("unknown error occurred while reading the configuration files");
            if (releaseAfterFirstAttempt)
            {
                username.ShouldEqual("someone");
                password.ShouldEqual(SecretValue);
            }
            else
            {
                error.Contains(SecretValue).ShouldBeFalse("The returned error must not contain stderr: " + error);
                error.ShouldEqual("Git credential fill failed with exit code " + GitFatalExitCode + "; see the log for its error output");
            }

            AssertNoSecret(tracer);
        }

        private static void AssertNoSecret(MockTracer tracer)
        {
            foreach (MockTracer source in new[] { tracer, tracer.StartActivityTracer })
            {
                foreach (string traced in source.RelatedInfoEvents
                    .Concat(source.RelatedWarningEvents)
                    .Concat(source.RelatedErrorEvents))
                {
                    traced.Contains(SecretValue).ShouldBeFalse("Credential output must not be traced: " + traced);
                }

                foreach (EventMetadata metadata in source.TelemetryWarnings
                    .Concat(source.RelatedEventMetadata)
                    .Concat(source.StopMetadata)
                    .Where(item => item != null))
                {
                    foreach (object value in metadata.Values)
                    {
                        string text = value?.ToString() ?? string.Empty;
                        text.Contains(SecretValue).ShouldBeFalse("Credential output must not be traced: " + text);
                    }
                }
            }
        }

        private sealed class LockedConfigGitProcess : GitProcess
        {
            private readonly string configPath;
            private readonly bool releaseAfterFirstAttempt;

            public LockedConfigGitProcess(string gitPath, string repoPath, bool releaseAfterFirstAttempt)
                : base(gitPath, repoPath)
            {
                this.configPath = Path.Combine(repoPath, ".git", "config");
                this.releaseAfterFirstAttempt = releaseAfterFirstAttempt;
            }

            public List<Result> Results { get; } = new List<Result>();

            protected override Result InvokeGitImpl(
                string command,
                string workingDirectory,
                string dotGitDirectory,
                bool useReadObjectHook,
                Action<StreamWriter> writeStdIn,
                Action<string> parseStdOutLine,
                int timeoutMs,
                string gitObjectsDirectory = null,
                bool usePreCommandHook = true,
                bool mayRequireAuth = false,
                bool? hasVisibleConsoleWindow = null)
            {
                // Replace all credential helpers with a deterministic local helper. No credentials leave the test.
                string isolatedCommand = "-c credential.helper= -c credential.helper=\"!f() { echo username=someone; echo password=" +
                    SecretValue + "; }; f\" " + command;
                using (FileStream configLock = this.Results.Count == 0 || !this.releaseAfterFirstAttempt
                    ? File.Open(this.configPath, FileMode.Open, FileAccess.Read, FileShare.None)
                    : null)
                {
                    Result result = base.InvokeGitImpl(
                        isolatedCommand,
                        workingDirectory,
                        dotGitDirectory,
                        useReadObjectHook,
                        writeStdIn,
                        parseStdOutLine,
                        timeoutMs,
                        gitObjectsDirectory,
                        usePreCommandHook,
                        mayRequireAuth,
                        hasVisibleConsoleWindow);
                    this.Results.Add(result);
                    return result;
                }
            }
        }
    }
}
