using RGFS.FunctionalTests.FileSystemRunners;
using RGFS.FunctionalTests.Tools;
using RGFS.Tests.Should;
using NUnit.Framework;
using System.Diagnostics;
using System.IO;
using System.Threading;

namespace RGFS.FunctionalTests.Tests.EnlistmentPerFixture
{
    [TestFixture]
    public class UnmountTests : TestsWithEnlistmentPerFixture
    {
        private FileSystemRunner fileSystem;

        public UnmountTests()
        {
            this.fileSystem = new SystemIORunner();
        }

        [SetUp]
        public void SetupTest()
        {
            RGFSProcess rgfsProcess = new RGFSProcess(
                Path.Combine(TestContext.CurrentContext.TestDirectory, Properties.Settings.Default.PathToRGFS),
                this.Enlistment.EnlistmentRoot);

            if (!rgfsProcess.IsEnlistmentMounted())
            {
                rgfsProcess.Mount();
            }
        }

        [TestCase]
        public void UnmountWaitsForLock()
        {
            ManualResetEventSlim lockHolder = GitHelpers.AcquireRGFSLock(this.Enlistment);

            using (Process unmountingProcess = this.StartUnmount())
            {
                unmountingProcess.WaitForExit(3000).ShouldEqual(false, "Unmount completed while lock was acquired.");

                // Release the lock.
                lockHolder.Set();

                unmountingProcess.WaitForExit(10000).ShouldEqual(true, "Unmount didn't complete as expected.");
            }
        }

        [TestCase]
        public void UnmountSkipLock()
        {
            ManualResetEventSlim lockHolder = GitHelpers.AcquireRGFSLock(this.Enlistment);

            using (Process unmountingProcess = this.StartUnmount("--skip-wait-for-lock"))
            {
                unmountingProcess.WaitForExit(10000).ShouldEqual(true, "Unmount didn't complete as expected.");
            }

            // Signal process holding lock to terminate and release lock.
            lockHolder.Set();
        }

        private Process StartUnmount(string extraParams = "")
        {
            string pathToRGFS = Path.Combine(TestContext.CurrentContext.TestDirectory, Properties.Settings.Default.PathToRGFS);
            string enlistmentRoot = this.Enlistment.EnlistmentRoot;

            // TODO: 865304 Use app.config instead of --internal* arguments
            ProcessStartInfo processInfo = new ProcessStartInfo(pathToRGFS);
            processInfo.Arguments = "unmount " + extraParams + " --internal_use_only_service_name " + RGFSServiceProcess.TestServiceName;
            processInfo.WindowStyle = ProcessWindowStyle.Hidden;
            processInfo.WorkingDirectory = enlistmentRoot;
            processInfo.UseShellExecute = false;

            Process executingProcess = new Process();
            executingProcess.StartInfo = processInfo;
            executingProcess.Start();

            return executingProcess;
        }
    }
}
