using GVFS.FunctionalTests.Properties;
using GVFS.Tests.Should;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace GVFS.FunctionalTests.Tools
{
    public static class GitHelpers
    {
        /// <summary>
        /// This string must match the command name provided in the
        /// GVFS.FunctionalTests.LockHolder program.
        /// </summary>
        private const string LockHolderCommandName = @"GVFS.FunctionalTests.LockHolder";
        private const string LockHolderCommand = @"GVFS.FunctionalTests.LockHolder.exe";

        private const string WindowsPathSeparator = "\\";
        private const string GitPathSeparator = "/";

        private static string LockHolderCommandPath
        {
            get
            {
                // LockHolder is a .NET Framework application and can be found inside
                // GVFS.FunctionalTest Output directory.
                return Path.Combine(Settings.Default.CurrentDirectory, LockHolderCommand);
            }
        }

        public static string ConvertPathToGitFormat(string relativePath)
        {
            return relativePath.Replace(WindowsPathSeparator, GitPathSeparator);
        }

        public static void CheckGitCommand(string virtualRepoRoot, string command, params string[] expectedLinesInResult)
        {
            ProcessResult result = GitProcess.InvokeProcess(virtualRepoRoot, command);
            result.Errors.ShouldBeEmpty();
            foreach (string line in expectedLinesInResult)
            {
                result.Output.ShouldContain(line);
            }
        }

        public static void CheckGitCommandAgainstGVFSRepo(string virtualRepoRoot, string command, params string[] expectedLinesInResult)
        {
            ProcessResult result = InvokeGitAgainstGVFSRepo(virtualRepoRoot, command);
            result.Errors.ShouldBeEmpty();
            foreach (string line in expectedLinesInResult)
            {
                result.Output.ShouldContain(line);
            }
        }

        public static ProcessResult InvokeGitAgainstGVFSRepo(
            string gvfsRepoRoot,
            string command,
            Dictionary<string, string> environmentVariables = null,
            bool removeWaitingMessages = true,
            bool removeUpgradeMessages = true,
            bool removePartialHydrationMessages = true,
            bool removeFSMonitorMessages = true)
        {
            ProcessResult result = GitProcess.InvokeProcess(gvfsRepoRoot, command, environmentVariables);
            string output = FilterMessages(result.Output, false, false, false, removePartialHydrationMessages, removeFSMonitorMessages);
            string errors = FilterMessages(result.Errors, true, removeWaitingMessages, removeUpgradeMessages, removePartialHydrationMessages, removeFSMonitorMessages);

            return new ProcessResult(
                output,
                errors,
                result.ExitCode);
        }

        private static IEnumerable<string> SplitLinesKeepingNewlines(string input)
        {
            for (int start = 0;  start < input.Length; )
            {
                int nextLine = input.IndexOf('\n', start) + 1;

                if (nextLine == 0)
                {
                    // No more newlines, yield the rest
                    nextLine = input.Length;
                }

                yield return input.Substring(start, nextLine - start);
                start = nextLine;
            }
        }

        private static string FilterMessages(
            string input,
            bool removeEmptyLines,
            bool removeWaitingMessages,
            bool removeUpgradeMessages,
            bool removePartialHydrationMessages,
            bool removeFSMonitorMessages)
        {
            if (!string.IsNullOrEmpty(input) && (removeWaitingMessages || removeUpgradeMessages || removePartialHydrationMessages || removeFSMonitorMessages))
            {
                IEnumerable<string> lines = SplitLinesKeepingNewlines(input);
                IEnumerable<string> filteredLines = lines.Where(line =>
                {
                    if ((removeEmptyLines && string.IsNullOrWhiteSpace(line)) ||
                        (removeUpgradeMessages && line.StartsWith("A new version of VFS for Git is available.")) ||
                        (removeWaitingMessages && line.StartsWith("Waiting for ")) ||
                        (removePartialHydrationMessages && line.StartsWith("You are in a partially-hydrated checkout with ")) ||
                        (removeFSMonitorMessages && line.TrimEnd().EndsWith(" is incompatible with fsmonitor")))
                    {
                        return false;
                    }
                    else
                    {
                        return true;
                    }
                });

                return filteredLines.Any() ? string.Join("", filteredLines) : string.Empty;
            }

            return input;
        }

        public static void ValidateGitCommand(
            GVFSFunctionalTestEnlistment enlistment,
            ControlGitRepo controlGitRepo,
            string command,
            params object[] args)
        {
            command = string.Format(command, args);
            string controlRepoRoot = controlGitRepo.RootPath;
            string gvfsRepoRoot = enlistment.RepoRoot;

            Dictionary<string, string> environmentVariables = new Dictionary<string, string>();
            environmentVariables["GIT_QUIET"] = "true";
            environmentVariables["GIT_COMMITTER_DATE"] = "Thu Feb 16 10:07:35 2017 -0700";

            ProcessResult expectedResult = GitProcess.InvokeProcess(controlRepoRoot, command, environmentVariables);
            ProcessResult actualResult = GitHelpers.InvokeGitAgainstGVFSRepo(gvfsRepoRoot, command, environmentVariables);

            ErrorsShouldMatch(command, expectedResult, actualResult);
            actualResult.Output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .ShouldMatchInOrder(expectedResult.Output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries), LinesAreEqual, command + " Output Lines");

            if (command != "status")
            {
                ValidateGitCommand(enlistment, controlGitRepo, "status");
            }
        }

        /// <summary>
        /// Acquire the GVFSLock. This method will return once the GVFSLock has been acquired.
        /// </summary>
        /// <param name="processId">The ID of the process that acquired the lock.</param>
        /// <returns><see cref="ManualResetEvent"/> that can be signaled to exit the lock acquisition program.</returns>
        public static ManualResetEventSlim AcquireGVFSLock(
            GVFSFunctionalTestEnlistment enlistment,
            out int processId,
            int resetTimeout = Timeout.Infinite,
            bool skipReleaseLock = false)
        {
            string args = null;
            if (skipReleaseLock)
            {
                args = "--skip-release-lock";
            }

            return RunCommandWithWaitAndStdIn(
                enlistment,
                resetTimeout,
                LockHolderCommandPath,
                args,
                GitHelpers.LockHolderCommandName,
                "done",
                out processId);
        }

        /// <summary>
        /// Run the specified Git command. This method will return once the GVFSLock has been acquired.
        /// </summary>
        /// <param name="processId">The ID of the process that acquired the lock.</param>
        /// <returns><see cref="ManualResetEvent"/> that can be signaled to exit the lock acquisition program.</returns>
        public static ManualResetEventSlim RunGitCommandWithWaitAndStdIn(
            GVFSFunctionalTestEnlistment enlistment,
            int resetTimeout,
            string command,
            string stdinToQuit,
            out int processId)
        {
            return
                RunCommandWithWaitAndStdIn(
                    enlistment,
                    resetTimeout,
                    Properties.Settings.Default.PathToGit,
                    command,
                    "git " + command,
                    stdinToQuit,
                    out processId);
        }

        public static void ErrorsShouldMatch(string command, ProcessResult expectedResult, ProcessResult actualResult)
        {
            actualResult.Errors.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .ShouldMatchInOrder(expectedResult.Errors.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries), LinesAreEqual, command + " Errors Lines");
        }

        /// <summary>
        /// Run the specified command as an external program. This method will return once the GVFSLock has been acquired.
        /// </summary>
        /// <param name="processId">The ID of the process that acquired the lock.</param>
        /// <returns><see cref="ManualResetEvent"/> that can be signaled to exit the lock acquisition program.</returns>
        private static ManualResetEventSlim RunCommandWithWaitAndStdIn(
            GVFSFunctionalTestEnlistment enlistment,
            int resetTimeout,
            string pathToCommand,
            string args,
            string lockingProcessCommandName,
            string stdinToQuit,
            out int processId)
        {
            ManualResetEventSlim resetEvent = new ManualResetEventSlim(initialState: false);

            ProcessStartInfo processInfo = new ProcessStartInfo(pathToCommand);
            processInfo.WorkingDirectory = enlistment.RepoRoot;
            processInfo.UseShellExecute = false;
            processInfo.RedirectStandardOutput = true;
            processInfo.RedirectStandardError = true;
            processInfo.RedirectStandardInput = true;
            processInfo.Arguments = args;

            Process holdingProcess = Process.Start(processInfo);
            StreamWriter stdin = holdingProcess.StandardInput;
            processId = holdingProcess.Id;

            enlistment.WaitForLock(lockingProcessCommandName);

            Task.Run(
                () =>
                {
                    resetEvent.Wait(resetTimeout);

                    try
                    {
                        // Make sure to let the holding process end.
                        if (stdin != null)
                        {
                            stdin.WriteLine(stdinToQuit);
                            stdin.Close();
                        }

                        if (holdingProcess != null)
                        {
                            bool holdingProcessHasExited = holdingProcess.WaitForExit(10000);

                            if (!holdingProcess.HasExited)
                            {
                                holdingProcess.Kill();
                            }

                            holdingProcess.Dispose();

                            holdingProcessHasExited.ShouldBeTrue("Locking process did not exit in time.");
                        }
                    }
                    catch (Exception ex)
                    {
                        Assert.Fail($"{nameof(RunCommandWithWaitAndStdIn)} exception closing stdin {ex.ToString()}");
                    }
                    finally
                    {
                        resetEvent.Set();
                    }
                });

            return resetEvent;
        }

        private static bool LinesAreEqual(string actualLine, string expectedLine)
        {
            return actualLine.Equals(expectedLine);
        }
    }
}
