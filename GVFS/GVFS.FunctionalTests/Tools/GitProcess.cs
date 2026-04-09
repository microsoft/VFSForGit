using System.Collections.Generic;
using System.Diagnostics;
using System.IO;

namespace GVFS.FunctionalTests.Tools
{
    public static class GitProcess
    {
        public static string Invoke(string executionWorkingDirectory, string command)
        {
            return InvokeProcess(executionWorkingDirectory, command).Output;
        }

        public static ProcessResult InvokeProcess(string executionWorkingDirectory, string command, Dictionary<string, string> environmentVariables = null, Stream inputStream = null)
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

            return ProcessHelper.Run(processInfo, inputStream: inputStream);
        }
    }
}
