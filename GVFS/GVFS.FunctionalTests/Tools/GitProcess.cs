using System.Diagnostics;

namespace GVFS.FunctionalTests.Tools
{
    public class GitProcess
    {
        public static string Invoke(string executionWorkingDirectory, string command)
        {
            return InvokeProcess(executionWorkingDirectory, command).Output;
        }

        public static ProcessResult InvokeProcess(string executionWorkingDirectory, string command)
        {
            ProcessStartInfo processInfo = new ProcessStartInfo(Properties.Settings.Default.PathToGit);
            processInfo.WorkingDirectory = executionWorkingDirectory;
            processInfo.UseShellExecute = false;
            processInfo.RedirectStandardOutput = true;
            processInfo.RedirectStandardError = true;
            processInfo.Arguments = command;

            processInfo.EnvironmentVariables["GIT_TERMINAL_PROMPT"] = "0";

            return ProcessHelper.Run(processInfo);
        }
    }
}
