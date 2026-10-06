using System;
using System.CommandLine;
using System.IO;
using GVFS.Common;
using GVFS.PlatformLoader;
using NUnit.Framework;

namespace GVFS.CommandLine.Tests
{
    /// <summary>
    /// Regression coverage for the GVFS.Mount exit code: the background mount
    /// process must exit with the real ReturnCode when a mount attempt fails,
    /// not with 0. System.CommandLine's Invoke() never rethrows an exception
    /// that escapes the root command's action, so the action itself must catch
    /// MountAbortedException and return its ReturnCode for Invoke() to report.
    /// </summary>
    [SetUpFixture]
    public class GVFSMountExitCodeTestsSetup
    {
        [OneTimeSetUp]
        public void SetUp()
        {
            GVFSPlatformLoader.Initialize();
        }
    }

    [TestFixture]
    public class GVFSMountExitCodeTests
    {
        [Test]
        public void InvalidEnlistment_ReturnsGenericErrorExitCode()
        {
            RootCommand rootCommand = GVFS.Mount.Program.BuildRootCommand();
            string nonExistentPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());

            int exitCode = rootCommand.Parse(new[] { nonExistentPath }).Invoke();

            Assert.That(exitCode, Is.EqualTo((int)ReturnCode.GenericError));
        }
    }
}
