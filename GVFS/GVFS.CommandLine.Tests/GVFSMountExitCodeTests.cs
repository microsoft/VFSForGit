using System;
using System.CommandLine;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using GVFS.Common;
using GVFS.PlatformLoader;
using NUnit.Framework;

namespace GVFS.CommandLine.Tests
{
    [TestFixture]
    public class GVFSMountExitCodeTests
    {
        [OneTimeSetUp]
        public void SetUp()
        {
            GVFSPlatformLoader.Initialize();
        }

        [Test]
        public void InvalidEnlistment_ReturnsGenericErrorExitCode()
        {
            RootCommand rootCommand = GVFS.Mount.Program.BuildRootCommand();
            string nonExistentPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());

            int exitCode = rootCommand.Parse(new[] { nonExistentPath }).Invoke();

            Assert.That(exitCode, Is.EqualTo((int)ReturnCode.GenericError));
        }

        [Test]
        public void InvalidEnlistmentProcess_ReturnsGenericErrorExitCode()
        {
            string nonExistentPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
            string mountOutputDirectory = AppContext.BaseDirectory.Replace(
                "GVFS.CommandLine.Tests",
                "GVFS.Mount",
                StringComparison.OrdinalIgnoreCase);
            ProcessStartInfo processInfo = new ProcessStartInfo
            {
                FileName = Path.Combine(mountOutputDirectory, "GVFS.Mount.exe"),
                CreateNoWindow = true,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                UseShellExecute = false,
            };
            processInfo.ArgumentList.Add(nonExistentPath);

            using (Process process = Process.Start(processInfo))
            {
                Task<string> output = process.StandardOutput.ReadToEndAsync();
                Task<string> errors = process.StandardError.ReadToEndAsync();

                process.WaitForExit();
                Task.WaitAll(output, errors);

                Assert.That(
                    process.ExitCode,
                    Is.EqualTo((int)ReturnCode.GenericError),
                    $"stdout: {output.Result}{Environment.NewLine}stderr: {errors.Result}");
            }
        }
    }
}
