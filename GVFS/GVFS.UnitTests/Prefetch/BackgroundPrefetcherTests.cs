using GVFS.Common.Prefetch;
using GVFS.Tests.Should;
using GVFS.UnitTests.Mock.FileSystem;
using GVFS.UnitTests.Virtual;
using NUnit.Framework;
using System.Threading;

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

        [TestCase]
        public void LaunchPrefetchJobIfIdleDoesNotLaunchSecondThreadIfFirstInProgress()
        {
            using (CommonRepoSetup setup = new CommonRepoSetup())
            {
                BlockedDirectoryExistsFileSystem fileSystem = new BlockedDirectoryExistsFileSystem(setup.FileSystem.RootDirectory);
                using (BackgroundPrefetcher prefetcher = new BackgroundPrefetcher(
                    setup.Context.Tracer,
                    setup.Context.Enlistment,
                    fileSystem,
                    setup.GitObjects))
                {
                    prefetcher.LaunchPrefetchJobIfIdle().ShouldBeTrue();
                    prefetcher.LaunchPrefetchJobIfIdle().ShouldBeFalse();
                    fileSystem.UnblockDirectoryExists();
                    prefetcher.WaitForPrefetchToFinish();
                }
            }
        }

        private class BlockedDirectoryExistsFileSystem : MockFileSystem
        {
            private ManualResetEvent unblockDirectoryExists;

            public BlockedDirectoryExistsFileSystem(MockDirectory rootDirectory)
                : base(rootDirectory)
            {
                this.unblockDirectoryExists = new ManualResetEvent(initialState: false);
            }

            public void UnblockDirectoryExists()
            {
                this.unblockDirectoryExists.Set();
            }

            public override bool DirectoryExists(string path)
            {
                this.unblockDirectoryExists.WaitOne();
                return base.DirectoryExists(path);
            }
        }
    }
}
