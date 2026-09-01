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

                    // A failed mount disposes the projection without calling Shutdown, so
                    // the parsing thread is still running here. Disposing a Task that has
                    // not completed throws InvalidOperationException, and that secondary
                    // exception used to kill GVFS.Mount before it could report why the
                    // mount failed -- leaving the client with a broken pipe.
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

        [TestCase]
        public void DisposeDisposesIndexParsingThreadOnceItHasCompleted()
        {
            // Never use Task.CompletedTask here: it is a runtime-wide singleton, and
            // disposing it breaks every later await in the process.
            Task parsingThread = Task.Factory.StartNew(() => { }, TaskCreationOptions.LongRunning);
            parsingThread.Wait();

            TestableGitIndexProjection projection = new TestableGitIndexProjection();
            projection.SetIndexParsingThreadForTests(parsingThread);

            Assert.DoesNotThrow(() => projection.Dispose());
        }

        private class TestableGitIndexProjection : GitIndexProjection
        {
        }
    }
}
