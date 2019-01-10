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
        private readonly TimeSpan disposalPeriod = TimeSpan.FromMilliseconds(1);
        private MockTracer tracer;
        private LibGit2RepoPool pool;
        public int NumConstructors { get; set; }
        public int NumDisposals { get; set; }
        public BlockingCollection<object> DisposalTriggers { get; set; }

        [SetUp]
        public void Setup()
        {
            this.tracer = new MockTracer();
            this.pool = new LibGit2RepoPool(this.tracer, this.CreateRepo, disposalPeriod: this.disposalPeriod);
            this.NumConstructors = 0;
            this.NumDisposals = 0;
            this.DisposalTriggers = new BlockingCollection<object>();
        }

        [TestCase]
        public void DoesNotCreateRepoOnConstruction()
        {
            this.NumConstructors.ShouldEqual(0);
        }

        [TestCase]
        public void CreatesRepoOnTryInvoke()
        {
            this.NumConstructors.ShouldEqual(0);

            this.pool.TryInvoke(repo => { return true; }, out bool result);
            result.ShouldEqual(true);
            this.NumConstructors.ShouldEqual(1);
        }

        [TestCase]
        public void DoesNotCreateMultipleRepos()
        {
            this.NumConstructors.ShouldEqual(0);

            this.pool.TryInvoke(repo => { return true; }, out bool result);
            result.ShouldEqual(true);
            this.NumConstructors.ShouldEqual(1);

            this.pool.TryInvoke(repo => { return true; }, out result);
            result.ShouldEqual(true);
            this.NumConstructors.ShouldEqual(1);

            this.pool.TryInvoke(repo => { return true; }, out result);
            result.ShouldEqual(true);
            this.NumConstructors.ShouldEqual(1);
        }

        [TestCase]
        public void DoesNotCreateRepoAfterDisposal()
        {
            this.NumConstructors.ShouldEqual(0);
            this.pool.Dispose();
            this.pool.TryInvoke(repo => { return true; }, out bool result);
            result.ShouldEqual(false);
            this.NumConstructors.ShouldEqual(0);
        }

        [TestCase]
        public void DisposesSharedRepo()
        {
            this.pool.TryInvoke(repo => { return true; }, out bool result);
            result.ShouldEqual(true);
            this.NumConstructors.ShouldEqual(1);

            this.pool.Dispose();
            this.NumDisposals.ShouldEqual(1);
        }

        [TestCase]
        public void UsesOnlyOneRepoMultipleThreads()
        {
            this.NumConstructors.ShouldEqual(0);

            Thread[] threads = new Thread[10];
            BlockingCollection<object> allowNextThreadToContinue = new BlockingCollection<object>();

            for (int i = 0; i < threads.Length; i++)
            {
                threads[i] = new Thread(() =>
                {
                    this.pool.TryInvoke(
                        repo =>
                        {
                            allowNextThreadToContinue.Take();

                            // Give the timer an opportunity to fire
                            Thread.Sleep(2);

                            allowNextThreadToContinue.Add(new object());
                            return true;
                        },
                        out bool result);
                    result.ShouldEqual(true);
                    this.NumConstructors.ShouldEqual(1);
                });
            }

            for (int i = 0; i < threads.Length; i++)
            {
                threads[i].Start();
            }

            allowNextThreadToContinue.Add(new object());

            for (int i = 0; i < threads.Length; i++)
            {
                threads[i].Join();
            }

            this.NumConstructors.ShouldEqual(1);
        }

        [TestCase]
        public void AutomaticallyDisposesAfterNoUse()
        {
            this.NumConstructors.ShouldEqual(0);

            this.pool.TryInvoke(repo => { return true; }, out bool result);
            result.ShouldEqual(true);
            this.NumConstructors.ShouldEqual(1);

            this.DisposalTriggers.TryTake(out object _, (int)this.disposalPeriod.TotalMilliseconds * 100).ShouldBeTrue();
            this.NumDisposals.ShouldEqual(1);
            this.NumConstructors.ShouldEqual(1);

            this.pool.TryInvoke(repo => { return true; }, out result);
            result.ShouldEqual(true);
            this.NumConstructors.ShouldEqual(2);
        }

        private LibGit2Repo CreateRepo()
        {
            this.NumConstructors++;
            return new MockLibGit2Repo(this);
        }

        private class MockLibGit2Repo : LibGit2Repo
        {
            private readonly LibGit2RepoPoolTests parent;

            public MockLibGit2Repo(LibGit2RepoPoolTests parent)
            {
                this.parent = parent;
            }

            protected override void Dispose(bool disposing)
            {
                if (disposing)
                {
                    this.parent.NumDisposals++;
                    this.parent.DisposalTriggers.Add(new object());
                }
            }
        }
    }
}
