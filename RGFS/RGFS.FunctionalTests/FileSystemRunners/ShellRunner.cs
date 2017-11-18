using RGFS.FunctionalTests.Tools;
using System.Diagnostics;

namespace RGFS.FunctionalTests.FileSystemRunners
{
    public abstract class ShellRunner : FileSystemRunner
    {
        protected const string SuccessOutput = "True";
        protected const string FailureOutput = "False";

        protected abstract string FileName { get; }

        protected virtual string RunProcess(string arguments, string errorMsgDelimeter = "")
        {
            ProcessStartInfo startInfo = new ProcessStartInfo();
            startInfo.UseShellExecute = false;
            startInfo.RedirectStandardOutput = true;
            startInfo.RedirectStandardError = true;
            startInfo.CreateNoWindow = true;
            startInfo.FileName = this.FileName;
            startInfo.Arguments = arguments;

            ProcessResult result = ProcessHelper.Run(startInfo, errorMsgDelimeter: errorMsgDelimeter);
            return !string.IsNullOrEmpty(result.Output) ? result.Output : result.Errors;
        }
    }
}