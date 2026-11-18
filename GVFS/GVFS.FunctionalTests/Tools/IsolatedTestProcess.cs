using GVFS.FunctionalTests.Tests.LocalComponents;
using NUnit.Framework;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace GVFS.FunctionalTests.Tools
{
    internal static class IsolatedTestProcess
    {
        internal const string WorkerArgument = "--isolated-test-process";

        private const int TimeoutSeconds = 110;
        private const int CleanupTimeoutSeconds = 5;

        public static void Run(Action test)
        {
            if (GVFSTestConfig.IsIsolatedTestProcess)
            {
                test();
                return;
            }

            RunInChildAsync().GetAwaiter().GetResult();
        }

        public static Task RunAsync(Func<Task> test)
        {
            return GVFSTestConfig.IsIsolatedTestProcess ? test() : RunInChildAsync();
        }

        private static async Task RunInChildAsync()
        {
            string testName = TestContext.CurrentContext.Test.FullName;
            StringBuilder output = new StringBuilder();
            StringBuilder errors = new StringBuilder();
            StringBuilder failures = new StringBuilder();
            IsolatedGitEnvironment environment = null;
            using Process process = new Process();
            using CancellationTokenSource deadline = new CancellationTokenSource(TimeSpan.FromSeconds(TimeoutSeconds));
            using CancellationTokenSource readCancellation = new CancellationTokenSource();
            Task outputTask = Task.CompletedTask;
            Task errorsTask = Task.CompletedTask;
            bool started = false;
            try
            {
                environment = new IsolatedGitEnvironment();
                string resultPath = Path.Combine(environment.RootDirectory, "TestResult.xml");
                string testListPath = Path.Combine(environment.RootDirectory, "SelectedTest.txt");
                File.WriteAllLines(testListPath, new[] { testName });
                string workDirectory = Path.Combine(environment.RootDirectory, "work");
                string temporaryDirectory = Path.Combine(environment.RootDirectory, "temp");
                Directory.CreateDirectory(workDirectory);
                Directory.CreateDirectory(temporaryDirectory);
                ProcessStartInfo startInfo = new ProcessStartInfo
                {
                    FileName = Path.Combine(AppContext.BaseDirectory, "GVFS.FunctionalTests.exe"),
                    WorkingDirectory = workDirectory,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                };
                environment.Apply(startInfo);
                startInfo.Environment["TEMP"] = temporaryDirectory;
                startInfo.Environment["TMP"] = temporaryDirectory;
                startInfo.ArgumentList.Add(WorkerArgument);
                startInfo.ArgumentList.Add("--testlist=" + testListPath);
                startInfo.ArgumentList.Add("--workers=0");
                startInfo.ArgumentList.Add("--result=" + resultPath + ";format=nunit3");
                startInfo.ArgumentList.Add("--work=" + workDirectory);
                process.StartInfo = startInfo;
                started = process.Start();
                if (!started)
                {
                    throw new InvalidOperationException("The isolated test process did not start.");
                }

                outputTask = ReadOutputAsync(process.StandardOutput, output, readCancellation.Token);
                errorsTask = ReadOutputAsync(process.StandardError, errors, readCancellation.Token);
                await Task.WhenAll(process.WaitForExitAsync(deadline.Token), outputTask, errorsTask)
                    .WaitAsync(deadline.Token).ConfigureAwait(false);
                if (process.ExitCode != 0)
                {
                    throw new InvalidOperationException("The isolated test process returned exit code " + process.ExitCode + ".");
                }

                ValidateResult(resultPath, testName);
            }
            catch (OperationCanceledException) when (deadline.IsCancellationRequested)
            {
                failures.AppendLine("The isolated test process exceeded the " + TimeoutSeconds + "-second deadline.");
            }
            catch (Exception exception)
            {
                failures.AppendLine(exception.ToString());
            }
            finally
            {
                if (started)
                {
                    try
                    {
                        if (!process.HasExited)
                        {
                            try
                            {
                                process.Kill(entireProcessTree: true);
                            }
                            catch (InvalidOperationException) when (process.HasExited)
                            {
                            }
                        }

                        using CancellationTokenSource cleanupDeadline = new CancellationTokenSource(TimeSpan.FromSeconds(CleanupTimeoutSeconds));
                        await process.WaitForExitAsync(cleanupDeadline.Token).ConfigureAwait(false);
                    }
                    catch (Exception exception)
                    {
                        failures.AppendLine("Could not stop the isolated test process: " + exception);
                    }

                    readCancellation.Cancel();
                    CloseOutput(process.StandardOutput, failures);
                    CloseOutput(process.StandardError, failures);
                    try
                    {
                        await Task.WhenAll(outputTask, errorsTask)
                            .WaitAsync(TimeSpan.FromSeconds(CleanupTimeoutSeconds)).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (readCancellation.IsCancellationRequested)
                    {
                    }
                    catch (ObjectDisposedException) when (readCancellation.IsCancellationRequested)
                    {
                    }
                    catch (Exception exception)
                    {
                        failures.AppendLine("Could not drain the isolated test output: " + exception);
                    }
                }

                try
                {
                    environment?.Dispose();
                }
                catch (Exception exception)
                {
                    failures.AppendLine("Could not remove the isolated test directory: " + exception);
                }
            }

            if (failures.Length > 0)
            {
                Assert.Fail(
                    "Isolated test failed: " + testName + Environment.NewLine + failures +
                    Environment.NewLine + "stdout:" + Environment.NewLine + output +
                    Environment.NewLine + "stderr:" + Environment.NewLine + errors);
            }
        }

        private static async Task ReadOutputAsync(StreamReader reader, StringBuilder output, CancellationToken cancellationToken)
        {
            char[] buffer = new char[4096];
            int count;
            while ((count = await reader.ReadAsync(buffer.AsMemory(), cancellationToken).ConfigureAwait(false)) > 0)
            {
                output.Append(buffer, 0, count);
            }
        }

        private static void CloseOutput(StreamReader reader, StringBuilder failures)
        {
            try
            {
                reader.Dispose();
            }
            catch (Exception exception)
            {
                failures.AppendLine("Could not close the isolated test output: " + exception);
            }
        }

        private static void ValidateResult(string resultPath, string testName)
        {
            if (!File.Exists(resultPath))
            {
                throw new InvalidOperationException("The isolated test process did not write its NUnit result XML.");
            }

            XDocument result = XDocument.Load(resultPath);
            XElement run = result.Root;
            XElement[] cases = result.Descendants("test-case").ToArray();
            if (run?.Name != "test-run" ||
                (string)run.Attribute("result") != "Passed" ||
                (string)run.Attribute("total") != "1" ||
                (string)run.Attribute("passed") != "1" ||
                (string)run.Attribute("failed") != "0" ||
                (string)run.Attribute("inconclusive") != "0" ||
                (string)run.Attribute("skipped") != "0" ||
                cases.Length != 1 ||
                (string)cases[0].Attribute("fullname") != testName ||
                (string)cases[0].Attribute("result") != "Passed")
            {
                throw new InvalidOperationException(
                    "Expected exactly one Passed NUnit case named '" + testName + "'." +
                    Environment.NewLine + result);
            }
        }
    }
}
