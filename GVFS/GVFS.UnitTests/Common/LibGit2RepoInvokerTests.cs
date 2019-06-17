using GVFS.Common.Git;
using GVFS.Tests.Should;
using GVFS.UnitTests.Mock.Common;
using NUnit.Framework;
using System.Collections.Concurrent;
using System.Threading;

namespace GVFS.UnitTests.Common
{
    [TestFixture]
    public class LibGit2RepoInvokerTests
    {
        private MockTracer tracer;
        private LibGit2RepoInvoker invoker;
        private int numConstructors;
        private int numDisposals;

        public BlockingCollection<object> DisposalTriggers { get; set; }

        [SetUp]
        public void Setup()
        {
            this.invoker?.Dispose();

            this.tracer = new MockTracer();
            this.numConstructors = 0;
            this.numDisposals = 0;
            this.DisposalTriggers = new BlockingCollection<object>();

            this.invoker = new LibGit2RepoInvoker(this.tracer, this.CreateRepo);
        }

        [TestCase]
        public void DoesCreateRepoOnConstruction()
        {
            this.numConstructors.ShouldEqual(1);
        }

        [TestCase]
        public void CreatedByInitializeAfterClosed()
        {
            this.numDisposals.ShouldEqual(0);
            this.numConstructors.ShouldEqual(1);

            this.invoker.DisposeSharedRepo();

            this.numDisposals.ShouldEqual(1);
            this.numConstructors.ShouldEqual(1);

            this.invoker.InitializeSharedRepo();

            this.numDisposals.ShouldEqual(1);
            this.numConstructors.ShouldEqual(2);

            // This should not create another repo
            this.invoker.TryInvoke(repo => { return true; }, out bool result);

            this.numDisposals.ShouldEqual(1);
            this.numConstructors.ShouldEqual(2);
        }

        [TestCase]
        public void CreatesOnInvokeAfterClosed()
        {
            this.numConstructors.ShouldEqual(1);

            this.invoker.DisposeSharedRepo();

            this.numDisposals.ShouldEqual(1);
            this.numConstructors.ShouldEqual(1);

            this.invoker.TryInvoke(repo => { return true; }, out bool result);

            this.numDisposals.ShouldEqual(1);
            this.numConstructors.ShouldEqual(2);

            // This should not create another repo
            this.invoker.InitializeSharedRepo();

            this.numDisposals.ShouldEqual(1);
            this.numConstructors.ShouldEqual(2);
        }

        [TestCase]
        public void DoesNotCreateMultipleRepos()
        {
            this.numConstructors.ShouldEqual(1);

            this.invoker.TryInvoke(repo => { return true; }, out bool result);
            result.ShouldEqual(true);
            this.numConstructors.ShouldEqual(1);

            this.invoker.TryInvoke(repo => { return true; }, out result);
            result.ShouldEqual(true);
            this.numConstructors.ShouldEqual(1);

            this.invoker.InitializeSharedRepo();
            this.numConstructors.ShouldEqual(1);
        }

        [TestCase]
        public void DoesNotCreateRepoAfterDisposal()
        {
            this.numConstructors.ShouldEqual(1);
            this.invoker.Dispose();
            this.invoker.TryInvoke(repo => { return true; }, out bool result);
            result.ShouldEqual(false);
            this.numConstructors.ShouldEqual(1);
        }

        [TestCase]
        public void DisposesSharedRepo()
        {
            this.numConstructors.ShouldEqual(1);
            this.numDisposals.ShouldEqual(0);

            this.invoker.TryInvoke(repo => { return true; }, out bool result);
            result.ShouldEqual(true);
            this.numConstructors.ShouldEqual(1);

            this.invoker.Dispose();
            this.numConstructors.ShouldEqual(1);
            this.numDisposals.ShouldEqual(1);
        }

        [TestCase]
        public void UsesOnlyOneRepoMultipleThreads()
        {
            this.numConstructors.ShouldEqual(1);

            Thread[] threads = new Thread[10];
            BlockingCollection<object> threadStarted = new BlockingCollection<object>();
            BlockingCollection<object> allowNextThreadToContinue = new BlockingCollection<object>();

            for (int i = 0; i < threads.Length; i++)
            {
                threads[i] = new Thread(() =>
                {
                    this.invoker.TryInvoke(
                        repo =>
                        {
                            threadStarted.Add(new object());
                            allowNextThreadToContinue.Take();

                            // Give the timer an opportunity to fire
                            Thread.Sleep(2);

                            allowNextThreadToContinue.Add(new object());
                            return true;
                        },
                        out bool result);
                    result.ShouldEqual(true);
                    this.numConstructors.ShouldEqual(1);
                });
            }

            // Ensure all threads are started before letting them continue
            for (int i = 0; i < threads.Length; i++)
            {
                threads[i].Start();
                threadStarted.Take();
            }

            allowNextThreadToContinue.Add(new object());

            for (int i = 0; i < threads.Length; i++)
            {
                threads[i].Join();
            }

            this.numConstructors.ShouldEqual(1);
        }

        private LibGit2Repo CreateRepo()
        {
            Interlocked.Increment(ref this.numConstructors);
            return new MockLibGit2Repo(this);
        }

        private class MockLibGit2Repo : LibGit2Repo
        {
            private readonly LibGit2RepoInvokerTests parent;

            public MockLibGit2Repo(LibGit2RepoInvokerTests parent)
            {
                this.parent = parent;
            }

            public override bool ObjectExists(string sha)
            {
                return false;
            }

            protected override void Dispose(bool disposing)
            {
                if (disposing)
                {
                    Interlocked.Increment(ref this.parent.numDisposals);
                    this.parent.DisposalTriggers.Add(new object());
                }
            }
        }
    }
}
