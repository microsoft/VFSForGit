using GVFS.Common;
using GVFS.Common.Git;
using GVFS.Tests.Should;
using GVFS.UnitTests.Mock.Common;
using NUnit.Framework;
using System;
using System.Collections.Concurrent;
using System.Threading;

namespace GVFS.UnitTests.Common
{
    [TestFixture]
    public class LibGit2RepoPoolTests
    {
        [TestCase]
        public void PoolConstructsThenDisposesRepos()
        {
            MockTracer tracer = new MockTracer();
            int size = 3;
            TimeSpan dueTime = TimeSpan.FromMilliseconds(1);
            TimeSpan period = TimeSpan.FromMilliseconds(1);

            BlockingCollection<object> threadReady = new BlockingCollection<object>();
            BlockingCollection<object> threadTriggers = new BlockingCollection<object>();
            BlockingCollection<object> disposalTriggers = new BlockingCollection<object>();

            using (LibGit2RepoPool pool = new LibGit2RepoPool(tracer, () => new MockLibGit2Repo(disposalTriggers), size, dueTime, period))
            {
                for (int i = 0; i < size; i++)
                {
                    new Thread(() => pool.TryInvoke(
                        repo =>
                        {
                            threadReady.TryAdd(new object(), 0);
                            return threadTriggers.TryTake(out object _, 5000);
                        },
                        out bool result)).Start();
                    threadReady.TryTake(out object _, 5000);
                }

                for (int i = 0; i < size; i++)
                {
                    threadTriggers.TryAdd(new object(), 0);
                    disposalTriggers.TryTake(out object obj, millisecondsTimeout: 5000).ShouldBeTrue();
                }
            }

            disposalTriggers.TryTake(out object _, millisecondsTimeout: 0).ShouldBeFalse();
            tracer.RelatedWarningEvents.Count.ShouldEqual(0);
        }

        [TestCase]
        public void PoolReallocatesRepos()
        {
            MockTracer tracer = new MockTracer();
            int size = 3;
            TimeSpan dueTime = TimeSpan.FromMilliseconds(1);
            TimeSpan period = TimeSpan.FromMilliseconds(1);

            BlockingCollection<object> threadReady = new BlockingCollection<object>();
            BlockingCollection<object> threadTriggers = new BlockingCollection<object>();
            BlockingCollection<object> disposalTriggers = new BlockingCollection<object>();

            using (LibGit2RepoPool pool = new LibGit2RepoPool(tracer, () => new MockLibGit2Repo(disposalTriggers), size, dueTime, period))
            {
                for (int i = 0; i < size; i++)
                {
                    new Thread(() => pool.TryInvoke(
                        repo =>
                        {
                            threadReady.TryAdd(new object(), 0);
                            return threadTriggers.TryTake(out object _, 5000);
                        },
                        out bool result)).Start();
                    threadReady.TryTake(out object _, 5000);
                }

                for (int i = 0; i < size; i++)
                {
                    threadReady.TryTake(out object _, 5000);
                    threadTriggers.TryAdd(new object(), 0);
                    disposalTriggers.TryTake(out object _, millisecondsTimeout: 5000).ShouldBeTrue();
                }

                pool.TryInvoke(repo => true, out bool invoked);

                invoked.ShouldBeTrue();
                disposalTriggers.TryTake(out object _, millisecondsTimeout: 5000).ShouldBeTrue();
            }

            disposalTriggers.TryTake(out object _, millisecondsTimeout: 0).ShouldBeFalse();
            tracer.RelatedWarningEvents.Count.ShouldEqual(0);
        }

        private class MockLibGit2Repo : LibGit2Repo
        {
            private readonly BlockingCollection<object> disposalTriggers;

            /// <summary>
            /// Specifically call the protected empty constructor
            /// </summary>
            public MockLibGit2Repo(BlockingCollection<object> disposalTriggers)
                : base()
            {
                this.disposalTriggers = disposalTriggers;
            }

            protected override void Dispose(bool disposing)
            {
                this.disposalTriggers.Add(new object());
            }
        }
    }
}
