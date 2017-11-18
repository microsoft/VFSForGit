using RGFS.Tests.Should;
using NUnit.Framework;
using System.Diagnostics;
using System.IO;

namespace RGFS.FunctionalTests.Tests
{
    [TestFixture]
    public class RGFSVerbTests
    {
        private string pathToRGFS;

        public RGFSVerbTests()
        {
            this.pathToRGFS = Path.Combine(TestContext.CurrentContext.TestDirectory, Properties.Settings.Default.PathToRGFS);
        }

        private enum ExpectedReturnCode
        {
            Success = 0,
            ParsingError = 1,
        }

        [TestCase]
        public void UnknownVerb()
        {
            this.CallRGFS("help", ExpectedReturnCode.Success);
            this.CallRGFS("unknownverb", ExpectedReturnCode.ParsingError);
        }

        [TestCase]
        public void UnknownArgs()
        {
            this.CallRGFS("log --help", ExpectedReturnCode.Success);
            this.CallRGFS("log --unknown-arg", ExpectedReturnCode.ParsingError);
        }

        private void CallRGFS(string args, ExpectedReturnCode expectedErrorCode)
        {
            ProcessStartInfo processInfo = new ProcessStartInfo(this.pathToRGFS);
            processInfo.Arguments = args;
            processInfo.WindowStyle = ProcessWindowStyle.Hidden;
            processInfo.UseShellExecute = false;
            processInfo.RedirectStandardOutput = true;
            processInfo.RedirectStandardError = true;

            using (Process process = Process.Start(processInfo))
            {
                string result = process.StandardOutput.ReadToEnd();
                process.WaitForExit();

                process.ExitCode.ShouldEqual((int)expectedErrorCode, result);
            }
        }
    }
}
