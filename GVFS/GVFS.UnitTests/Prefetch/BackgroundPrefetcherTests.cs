using GVFS.Common.Prefetch;
using GVFS.Tests.Should;
using GVFS.UnitTests.Virtual;
using NUnit.Framework;

namespace GVFS.UnitTests.Prefetch
{
    [TestFixture]
    public class BackgroundPrefetcherTests
    {
        [TestCase]
        public void LaunchBackgroundJobSucceeds()
        {
            using (CommonRepoSetup setup = new CommonRepoSetup())
            using (BackgroundPrefetcher prefetcher = new BackgroundPrefetcher(setup.Context.Tracer, setup.Context.Enlistment, setup.Context.FileSystem, setup.GitObjects))
            {
                prefetcher.LaunchPrefetchJobIfIdle().ShouldBeTrue();
                prefetcher.WaitForPrefetchToFinish();
            }
        }

        [TestCase]
        public void RestartBackgroundJobSucceeds()
        {
            using (CommonRepoSetup setup = new CommonRepoSetup())
            using (BackgroundPrefetcher prefetcher = new BackgroundPrefetcher(setup.Context.Tracer, setup.Context.Enlistment, setup.Context.FileSystem, setup.GitObjects))
            {
                prefetcher.LaunchPrefetchJobIfIdle().ShouldBeTrue();
                prefetcher.WaitForPrefetchToFinish();

                prefetcher.LaunchPrefetchJobIfIdle().ShouldBeTrue();
                prefetcher.WaitForPrefetchToFinish();
            }
        }
    }
}
