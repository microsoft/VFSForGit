using GVFS.Common;
using GVFS.Common.Git;
using GVFS.Common.Tracing;
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
            ITracer tracer = new MockTracer();
            int size = 3;
            TimeSpan dueTime = TimeSpan.FromMilliseconds(1);
            TimeSpan period = TimeSpan.FromMilliseconds(1);

            BlockingCollection<object> disposalTriggers = new BlockingCollection<object>();

            LibGit2RepoPool pool = new LibGit2RepoPool(tracer, () => new MockLibGit2Repo(disposalTriggers), dueTime, period);

            for (int i = 0; i < size; i++)
            {
                new Thread(() => pool.TryInvoke(repo => { Thread.Sleep(1); return true; }, out bool result)).Start();
            }

            for (int i = 0; i < size; i++)
            {
                disposalTriggers.TryTake(out object obj, millisecondsTimeout: 500).ShouldBeTrue();
            }

            disposalTriggers.TryTake(out object _, millisecondsTimeout: 0).ShouldBeFalse();
        }

        [TestCase]
        public void PoolReallocatesRepos()
        {
            ITracer tracer = new MockTracer();
            int size = 3;
            TimeSpan dueTime = TimeSpan.FromMilliseconds(1);
            TimeSpan period = TimeSpan.FromMilliseconds(1);

            BlockingCollection<object> disposalTriggers = new BlockingCollection<object>();

            LibGit2RepoPool pool = new LibGit2RepoPool(tracer, () => new MockLibGit2Repo(disposalTriggers), dueTime, period);

            for (int i = 0; i < size; i++)
            {
                new Thread(() => pool.TryInvoke(repo => { Thread.Sleep(1); return true; }, out bool result)).Start();
            }

            for (int i = 0; i < size; i++)
            {
                disposalTriggers.TryTake(out object _, millisecondsTimeout: 50).ShouldBeTrue();
            }

            pool.TryInvoke(repo => true, out bool invoked);

            invoked.ShouldBeTrue();
            disposalTriggers.TryTake(out object _, millisecondsTimeout: 50).ShouldBeTrue();
            disposalTriggers.TryTake(out object _, millisecondsTimeout: 0).ShouldBeFalse();
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
