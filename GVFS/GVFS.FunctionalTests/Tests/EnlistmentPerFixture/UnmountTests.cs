using GVFS.FunctionalTests.FileSystemRunners;
using GVFS.FunctionalTests.Tools;
using GVFS.Tests.Should;
using NUnit.Framework;
using System.Diagnostics;
using System.IO;
using System.Threading;

namespace GVFS.FunctionalTests.Tests.EnlistmentPerFixture
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
            GVFSProcess gvfsProcess = new GVFSProcess(
                Path.Combine(TestContext.CurrentContext.TestDirectory, Properties.Settings.Default.PathToGVFS),
                this.Enlistment.EnlistmentRoot);

            if (!gvfsProcess.IsEnlistmentMounted())
            {
                gvfsProcess.Mount();
            }
        }

        [TestCase]
        public void UnmountWaitsForLock()
        {
            ManualResetEventSlim lockHolder = GitHelpers.AcquireGVFSLock(this.Enlistment);

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
            ManualResetEventSlim lockHolder = GitHelpers.AcquireGVFSLock(this.Enlistment);

            using (Process unmountingProcess = this.StartUnmount("--skip-wait-for-lock"))
            {
                unmountingProcess.WaitForExit(10000).ShouldEqual(true, "Unmount didn't complete as expected.");
            }

            // Signal process holding lock to terminate and release lock.
            lockHolder.Set();
        }

        private Process StartUnmount(string extraParams = "")
        {
            string pathToGVFS = Path.Combine(TestContext.CurrentContext.TestDirectory, Properties.Settings.Default.PathToGVFS);
            string enlistmentRoot = this.Enlistment.EnlistmentRoot;

            // TODO: 865304 Use app.config instead of --internal* arguments
            ProcessStartInfo processInfo = new ProcessStartInfo(pathToGVFS);
            processInfo.Arguments = "unmount " + extraParams + " --internal_use_only_service_name " + GVFSServiceProcess.TestServiceName;
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
