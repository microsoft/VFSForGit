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
                BlockedCreateDirectoryFileSystem fileSystem = new BlockedCreateDirectoryFileSystem(setup.FileSystem.RootDirectory);
                using (BackgroundPrefetcher prefetcher = new BackgroundPrefetcher(
                    setup.Context.Tracer,
                    setup.Context.Enlistment,
                    fileSystem,
                    setup.GitObjects))
                {
                    prefetcher.LaunchPrefetchJobIfIdle().ShouldBeTrue();
                    prefetcher.LaunchPrefetchJobIfIdle().ShouldBeFalse();
                    fileSystem.UnblockCreateDirectory();
                    prefetcher.WaitForPrefetchToFinish();
                }
            }
        }

        private class BlockedCreateDirectoryFileSystem : MockFileSystem
        {
            private ManualResetEvent unblockCreateDirectory;

            public BlockedCreateDirectoryFileSystem(MockDirectory rootDirectory)
                : base(rootDirectory)
            {
                this.unblockCreateDirectory = new ManualResetEvent(initialState: false);
            }

            public void UnblockCreateDirectory()
            {
                this.unblockCreateDirectory.Set();
            }

            public override void CreateDirectory(string path)
            {
                this.unblockCreateDirectory.WaitOne();
                base.CreateDirectory(path);
            }
        }
    }
}
