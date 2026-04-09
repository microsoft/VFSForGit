using GVFS.FunctionalTests.Tools;
using System.Diagnostics;

namespace GVFS.FunctionalTests.FileSystemRunners
{
    public abstract class ShellRunner : FileSystemRunner
    {
        protected const string SuccessOutput = "True";
        protected const string FailureOutput = "False";

        protected abstract string FileName { get; }

        protected virtual string RunProcess(string arguments, string workingDirectory = "", string errorMsgDelimeter = "")
        {
            ProcessStartInfo startInfo = new ProcessStartInfo();
            startInfo.UseShellExecute = false;
            startInfo.RedirectStandardOutput = true;
            startInfo.RedirectStandardError = true;
            startInfo.CreateNoWindow = true;
            startInfo.FileName = this.FileName;
            startInfo.Arguments = arguments;
            startInfo.WorkingDirectory = workingDirectory;

            ProcessResult result = ProcessHelper.Run(startInfo, errorMsgDelimeter: errorMsgDelimeter);
            return !string.IsNullOrEmpty(result.Output) ? result.Output : result.Errors;
        }
    }
}