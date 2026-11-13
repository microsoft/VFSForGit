using GVFS.Common;
using GVFS.FunctionalTests.Properties;
using GVFS.Tests.Should;
using NUnit.Framework;
using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;

namespace GVFS.FunctionalTests.Tests.LocalComponents
{
    [TestFixture]
    public class GVFSMountExitCodeTests
    {
        [TestCase]
        public void InvalidEnlistmentProcess_ReturnsGenericErrorExitCode()
        {
            Settings.Default.PathToGVFS.ShouldNotBeNull("The functional runner must select a GVFS payload.");
            string mountPath = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(Settings.Default.PathToGVFS)), "GVFS.Mount.exe");
            File.Exists(mountPath).ShouldBeTrue("The selected GVFS payload must contain " + mountPath);

            string nonExistentPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
            ProcessStartInfo processInfo = new ProcessStartInfo
            {
                FileName = mountPath,
                CreateNoWindow = true,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                UseShellExecute = false,
            };
            processInfo.ArgumentList.Add(nonExistentPath);

            using (Process process = Process.Start(processInfo))
            {
                process.ShouldNotBeNull("GVFS.Mount did not start.");
                Task<string> output = process.StandardOutput.ReadToEndAsync();
                Task<string> errors = process.StandardError.ReadToEndAsync();
                try
                {
                    process.WaitForExit(30000).ShouldBeTrue("GVFS.Mount did not exit within 30 seconds.");
                    Task.WaitAll(new Task[] { output, errors }, 10000).ShouldBeTrue("GVFS.Mount output did not close.");

                    Assert.That(
                        process.ExitCode,
                        Is.EqualTo((int)ReturnCode.GenericError),
                        $"stdout: {output.Result}{Environment.NewLine}stderr: {errors.Result}");
                }
                finally
                {
                    if (!process.HasExited)
                    {
                        process.Kill(entireProcessTree: true);
                        process.WaitForExit(10000).ShouldBeTrue("GVFS.Mount did not exit after cleanup.");
                    }
                }
            }
        }
    }
}
