using GVFS.Virtualization.Projection;
using NUnit.Framework;
using System.Threading;
using System.Threading.Tasks;

namespace GVFS.UnitTests.Virtualization.Projection
{
    [TestFixture]
    public class GitIndexProjectionDisposeTests
    {
        [TestCase]
        public void DisposeDoesNotThrowWhenIndexParsingThreadIsStillRunning()
        {
            using (ManualResetEventSlim releaseParsingThread = new ManualResetEventSlim(false))
            {
                Task parsingThread = Task.Factory.StartNew(
                    () => releaseParsingThread.Wait(),
                    TaskCreationOptions.LongRunning);

                try
                {
                    TestableGitIndexProjection projection = new TestableGitIndexProjection();
                    projection.SetIndexParsingThreadForTests(parsingThread);

                    // Dispose without Shutdown leaves the task running. Task.Dispose throws
                    // InvalidOperationException for a task that has not completed.
                    Assert.DoesNotThrow(() => projection.Dispose());
                }
                finally
                {
                    releaseParsingThread.Set();
                    parsingThread.Wait();
                    parsingThread.Dispose();
                }
            }
        }

        private class TestableGitIndexProjection : GitIndexProjection
        {
        }
    }
}
