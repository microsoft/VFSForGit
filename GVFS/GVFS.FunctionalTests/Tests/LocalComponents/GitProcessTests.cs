using GVFS.Common.Git;
using GVFS.Common;
using IsolatedTestProcess = GVFS.FunctionalTests.Tools.IsolatedTestProcess;
using GVFS.Tests.Should;
using GVFS.FunctionalTests.Properties;
using NUnit.Framework;
using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace GVFS.FunctionalTests.Tests.LocalComponents
{
    [TestFixture]
    public class GitProcessTests
    {
        [TestCase(30000)]
        [TestCase(Timeout.Infinite)]
        public void GitCommandWaitsForOutputCallbacks(int timeoutMs)
        {
            string gitPath = Settings.Default.PathToGit;

            using IsolatedGitEnvironment environment = new IsolatedGitEnvironment();
            OutputCallbackGitProcess gitProcess = new OutputCallbackGitProcess(gitPath, environment);
            using (ManualResetEventSlim callbackStarted = new ManualResetEventSlim())
            using (ManualResetEventSlim releaseCallback = new ManualResetEventSlim())
            {
                bool callbackCompleted = false;
                Task<GitProcess.Result> command = Task.Run(() => gitProcess.GetVersion(
                    line =>
                    {
                        callbackStarted.Set();
                        callbackCompleted = releaseCallback.Wait(TimeSpan.FromSeconds(10));
                    },
                    timeoutMs));

                try
                {
                    callbackStarted.Wait(TimeSpan.FromSeconds(10)).ShouldBeTrue("Git did not produce output");
                    command.Wait(TimeSpan.FromSeconds(1)).ShouldBeFalse("The command returned before its output callback finished");
                }
                finally
                {
                    releaseCallback.Set();
                    command.Wait(TimeSpan.FromSeconds(10)).ShouldBeTrue("The command did not finish after its output callback was released");
                }

                command.Result.ExitCode.ShouldEqual(0);
                callbackCompleted.ShouldBeTrue();
            }
        }

        [TestCase]
        public void QuoteGitPath_RoundTripThroughProcess()
        {
            string[] testPaths = new[]
            {
                "simple/path.txt",
                "path with spaces/file name.txt",
                "path\\with\\backslashes\\file.txt",
            };

            string gitPath = Settings.Default.PathToGit;

            using IsolatedGitEnvironment environment = new IsolatedGitEnvironment();
            foreach (string testPath in testPaths)
            {
                string quoted = GitProcess.QuoteGitPath(testPath);
                ProcessStartInfo processInfo = new ProcessStartInfo(gitPath)
                {
                    Arguments = "rev-parse --sq-quote " + quoted,
                    WorkingDirectory = environment.RootDirectory,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                };
                environment.Apply(processInfo);

                using (Process process = Process.Start(processInfo))
                {
                    process.ShouldNotBeNull("Git did not start.");
                    Task<string> outputTask = process.StandardOutput.ReadToEndAsync();
                    Task<string> errorsTask = process.StandardError.ReadToEndAsync();
                    try
                    {
                        process.WaitForExit(30000).ShouldBeTrue("Git did not exit within 30 seconds.");
                        Task.WaitAll(new Task[] { outputTask, errorsTask }, 10000).ShouldBeTrue("Git output did not close.");
                        process.ExitCode.ShouldEqual(0, errorsTask.Result);
                        string output = outputTask.Result.Trim();

                        // Git wraps --sq-quote output in single quotes.
                        if (output.StartsWith("'") && output.EndsWith("'"))
                        {
                            output = output.Substring(1, output.Length - 2);
                        }

                        output.ShouldEqual(
                            testPath,
                            $"Path round-trip failed for: {testPath} (quoted as: {quoted})");
                    }
                    finally
                    {
                        if (!process.HasExited)
                        {
                            process.Kill(entireProcessTree: true);
                            process.WaitForExit(10000).ShouldBeTrue("Git did not exit after cleanup.");
                        }
                    }
                }
            }
        }

        [TestCase(null)]
        [TestCase("fr_FR.UTF-8")]
        public void GitChildUsesCLocaleWithoutChangingParentEnvironment(string inheritedLocale)
        {
            IsolatedTestProcess.Run(() =>
            {
                string gitPath = Settings.Default.PathToGit;

                string originalLocale = Environment.GetEnvironmentVariable("LC_ALL");
                string originalLanguage = Environment.GetEnvironmentVariable("LANGUAGE");
                try
                {
                    Environment.SetEnvironmentVariable("LC_ALL", inheritedLocale);
                    Environment.SetEnvironmentVariable("LANGUAGE", "de");
                    GitProcess git = new GitProcess(gitPath, workingDirectoryRoot: null);
                    using (Process child = git.GetGitProcess(
                        "-c alias.gvfs-locale-test=\"!echo $LC_ALL/$LANGUAGE\" gvfs-locale-test",
                        Environment.SystemDirectory,
                        dotGitDirectory: null,
                        useReadObjectHook: false,
                        gitObjectsDirectory: null,
                        usePreCommandHook: false))
                    {
                        child.StartInfo.Environment["LC_ALL"].ShouldEqual("C");
                        child.StartInfo.Environment["LANGUAGE"].ShouldEqual("de");
                        child.Start();
                        child.StandardInput.Close();
                        string output = child.StandardOutput.ReadToEnd();
                        string errors = child.StandardError.ReadToEnd();
                        child.WaitForExit();

                        child.ExitCode.ShouldEqual(0, errors);
                        output.Trim().ShouldEqual("C/de");
                    }

                    Environment.GetEnvironmentVariable("LC_ALL").ShouldEqual(inheritedLocale);
                    Environment.GetEnvironmentVariable("LANGUAGE").ShouldEqual("de");
                }
                finally
                {
                    Environment.SetEnvironmentVariable("LC_ALL", originalLocale);
                    Environment.SetEnvironmentVariable("LANGUAGE", originalLanguage);
                }
            });
        }

        [TestCase(true, true)]
        [TestCase(true, false)]
        [TestCase(false, true)]
        [TestCase(false, false)]
        public void ProcessHelperPreservesLocalePolicyAndOutputRedirection(bool gitSpecific, bool redirectOutput)
        {
            IsolatedTestProcess.Run(() =>
            {
                string gitPath = Settings.Default.PathToGit;

                const string InheritedLocale = "fr_FR.UTF-8";
                string originalLocale = Environment.GetEnvironmentVariable("LC_ALL");
                string originalLanguage = Environment.GetEnvironmentVariable("LANGUAGE");
                try
                {
                    Environment.SetEnvironmentVariable("LC_ALL", InheritedLocale);
                    Environment.SetEnvironmentVariable("LANGUAGE", "de");
                    string expectedLocale = gitSpecific ? "C" : InheritedLocale;
                    string command = $"-c alias.gvfs-locale-test=\"!test $LC_ALL/$LANGUAGE = {expectedLocale}/de && echo locale-output && echo locale-error >&2\" gvfs-locale-test";
                    ProcessResult result = gitSpecific ?
                        ProcessHelper.RunGit(gitPath, command, redirectOutput) :
                        ProcessHelper.Run(gitPath, command, redirectOutput);

                    result.ExitCode.ShouldEqual(0, result.Errors);
                    result.Output.Trim().ShouldEqual(redirectOutput ? "locale-output" : string.Empty);
                    result.Errors.Trim().ShouldEqual(redirectOutput ? "locale-error" : string.Empty);
                    Environment.GetEnvironmentVariable("LC_ALL").ShouldEqual(InheritedLocale);
                    Environment.GetEnvironmentVariable("LANGUAGE").ShouldEqual("de");
                }
                finally
                {
                    Environment.SetEnvironmentVariable("LC_ALL", originalLocale);
                    Environment.SetEnvironmentVariable("LANGUAGE", originalLanguage);
                }
            });
        }

        private sealed class OutputCallbackGitProcess : IsolatedGitProcess
        {
            public OutputCallbackGitProcess(string gitPath, IsolatedGitEnvironment environment)
                : base(gitPath, workingDirectory: null, environment)
            {
            }

            public Result GetVersion(Action<string> onOutput, int timeoutMs)
            {
                return this.InvokeGitImpl(
                    "--version",
                    workingDirectory: null,
                    dotGitDirectory: null,
                    useReadObjectHook: false,
                    writeStdIn: null,
                    parseStdOutLine: onOutput,
                    timeoutMs: timeoutMs,
                    usePreCommandHook: false);
            }
        }
    }
}
