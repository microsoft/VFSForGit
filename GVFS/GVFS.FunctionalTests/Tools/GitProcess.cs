using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;

namespace GVFS.FunctionalTests.Tools
{
    public static class GitProcess
    {
        // Default: 5 minutes per git operation. Override with GVFS_FT_GIT_TIMEOUT_SECONDS.
        public static int DefaultGitTimeoutMs { get; set; } = ReadGitTimeoutFromEnvironment();

        public static string Invoke(string executionWorkingDirectory, string command)
        {
            return InvokeProcess(executionWorkingDirectory, command).Output;
        }

        public static ProcessResult InvokeProcess(
            string executionWorkingDirectory,
            string command,
            Dictionary<string, string> environmentVariables = null,
            Stream inputStream = null,
            int timeoutMs = -1)
        {
            ProcessStartInfo processInfo = new ProcessStartInfo(Properties.Settings.Default.PathToGit);
            processInfo.WorkingDirectory = executionWorkingDirectory;
            processInfo.UseShellExecute = false;
            processInfo.RedirectStandardOutput = true;
            processInfo.RedirectStandardError = true;
            processInfo.Arguments = command;

            if (inputStream != null)
            {
                processInfo.RedirectStandardInput = true;
            }

            processInfo.EnvironmentVariables["GIT_TERMINAL_PROMPT"] = "0";

            if (environmentVariables != null)
            {
                foreach (string key in environmentVariables.Keys)
                {
                    processInfo.EnvironmentVariables[key] = environmentVariables[key];
                }
            }

            int effectiveTimeout = timeoutMs > 0 ? timeoutMs : DefaultGitTimeoutMs;
            return ProcessHelper.Run(processInfo, inputStream: inputStream, timeoutMs: effectiveTimeout);
        }

        private static int ReadGitTimeoutFromEnvironment()
        {
            string envValue = Environment.GetEnvironmentVariable("GVFS_FT_GIT_TIMEOUT_SECONDS");
            if (!string.IsNullOrEmpty(envValue) && int.TryParse(envValue, out int seconds) && seconds > 0)
            {
                return seconds * 1000;
            }

            // Default: 5 minutes per git operation
            return 300_000;
        }
    }
}
