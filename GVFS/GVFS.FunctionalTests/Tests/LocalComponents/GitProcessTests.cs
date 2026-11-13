using GVFS.Common.Git;
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
    [NonParallelizable]
    public class GitProcessTests
    {
        [TestCase(30000)]
        [TestCase(Timeout.Infinite)]
        public void GitCommandWaitsForOutputCallbacks(int timeoutMs)
        {
            string gitPath = Settings.Default.PathToGit;

            using IsolatedGitEnvironment environment = new IsolatedGitEnvironment();
            OutputCallbackGitProcess gitProcess = new OutputCallbackGitProcess(gitPath);
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

        private sealed class OutputCallbackGitProcess : GitProcess
        {
            public OutputCallbackGitProcess(string gitPath)
                : base(gitPath, workingDirectoryRoot: null)
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
