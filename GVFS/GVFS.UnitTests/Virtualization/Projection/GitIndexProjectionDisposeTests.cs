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
                    // the parsing thread is still running here. Calling Task.Dispose on a
                    // task that has not completed throws InvalidOperationException, and
                    // that secondary exception used to kill GVFS.Mount before it could
                    // report why the mount failed -- leaving the client with a broken pipe.
                    // This guards against reintroducing that call.
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
