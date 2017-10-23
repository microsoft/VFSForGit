using GVFS.Tests.Should;
using NUnit.Framework;
using System.Diagnostics;
using System.IO;

namespace GVFS.FunctionalTests.Tests
{
    [TestFixture]
    public class GVFSVerbTests
    {
        private string pathToGVFS;

        public GVFSVerbTests()
        {
            this.pathToGVFS = Path.Combine(TestContext.CurrentContext.TestDirectory, Properties.Settings.Default.PathToGVFS);
        }

        private enum ExpectedReturnCode
        {
            Success = 0,
            ParsingError = 1,
        }

        [TestCase]
        public void UnknownVerb()
        {
            this.CallGVFS("help", ExpectedReturnCode.Success);
            this.CallGVFS("unknownverb", ExpectedReturnCode.ParsingError);
        }

        [TestCase]
        public void UnknownArgs()
        {
            this.CallGVFS("log --help", ExpectedReturnCode.Success);
            this.CallGVFS("log --unknown-arg", ExpectedReturnCode.ParsingError);
        }

        private void CallGVFS(string args, ExpectedReturnCode expectedErrorCode)
        {
            ProcessStartInfo processInfo = new ProcessStartInfo(this.pathToGVFS);
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
