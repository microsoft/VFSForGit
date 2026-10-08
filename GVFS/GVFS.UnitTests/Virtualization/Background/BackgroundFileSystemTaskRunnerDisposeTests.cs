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

                    // FileSystemCallbacks.Dispose() disposes this runner on the failed-mount
                    // path, without calling Shutdown first, so the background thread is still
                    // running. Calling Task.Dispose on a task that has not completed throws
                    // InvalidOperationException, which stopped GVFS.Mount from reporting why
                    // the mount failed. This guards against reintroducing that call.
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
