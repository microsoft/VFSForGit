using GVFS.Common;
using GVFS.Tests.Should;
using NUnit.Framework;
using System.Diagnostics;
using System.IO;

namespace GVFS.UnitTests.Common
{
    [TestFixture]
    public class ProcessHelperTests
    {
        [TestCase]
        public void GetCommandLineTest()
        {
            Process internalProcess = null;
            StreamWriter stdin = null;

            try
            {
                ProcessStartInfo processInfo = new ProcessStartInfo("git.exe");
                processInfo.UseShellExecute = false;
                processInfo.RedirectStandardOutput = false;
                processInfo.RedirectStandardError = false;
                processInfo.RedirectStandardInput = true;
                processInfo.Arguments = "hash-object --stdin";

                internalProcess = Process.Start(processInfo);
                stdin = internalProcess.StandardInput;

                // Get the process as an external process
                string commandLine = ProcessHelper.GetCommandLine(Process.GetProcessById(internalProcess.Id));

                commandLine.EndsWith("\"git.exe\" hash-object --stdin").ShouldEqual(true);
            }
            finally
            {
                // End internal process.
                if (stdin != null)
                {
                    stdin.WriteLine("dummy");
                    stdin.Close();
                }

                if (internalProcess != null)
                {
                    if (!internalProcess.HasExited)
                    {
                        internalProcess.Kill();
                    }

                    internalProcess.Dispose();
                }
            }
        }
    }
}
