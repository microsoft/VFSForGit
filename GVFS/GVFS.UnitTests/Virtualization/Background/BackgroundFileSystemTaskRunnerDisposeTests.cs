using GVFS.Virtualization.Background;
using NUnit.Framework;
using System.Threading;
using System.Threading.Tasks;

namespace GVFS.UnitTests.Virtualization.Background
{
    [TestFixture]
    public class BackgroundFileSystemTaskRunnerDisposeTests
    {
        [TestCase]
        public void DisposeDoesNotThrowWhenBackgroundThreadIsStillRunning()
        {
            using (ManualResetEventSlim releaseBackgroundThread = new ManualResetEventSlim(false))
            {
                Task backgroundThread = Task.Factory.StartNew(
                    () => releaseBackgroundThread.Wait(),
                    TaskCreationOptions.LongRunning);

                try
                {
                    TestableBackgroundFileSystemTaskRunner runner = new TestableBackgroundFileSystemTaskRunner();
                    runner.SetBackgroundThreadForTests(backgroundThread);

                    // Dispose without Shutdown leaves the task running. Task.Dispose throws
                    // InvalidOperationException for a task that has not completed.
                    Assert.DoesNotThrow(() => runner.Dispose());
                }
                finally
                {
                    releaseBackgroundThread.Set();
                    backgroundThread.Wait();
                    backgroundThread.Dispose();
                }
            }
        }

        private class TestableBackgroundFileSystemTaskRunner : BackgroundFileSystemTaskRunner
        {
        }
    }
}
