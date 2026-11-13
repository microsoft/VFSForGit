using System;
using System.CommandLine;
using System.IO;
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
    }
}
