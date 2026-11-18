using GVFS.Common.Git;
using GVFS.Tests.Should;
using GVFS.UnitTests.Category;
using GVFS.UnitTests.Mock.Common;
using NUnit.Framework;
using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
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

            this.invoker = new LibGit2RepoInvoker(this.tracer, this.CreateRepo, ReadBoolWithGitMustNotRun, new Hashtable());
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

        [TestCase]
        public void GetConfigBoolOrDefaultReadsWithGitWhenTheEnvironmentChangesGitConfig()
        {
            List<string> gitKeys = new List<string>();
            using (LibGit2RepoInvoker invoker = new LibGit2RepoInvoker(
                this.tracer,
                this.CreateRepo,
                (string key, out bool? value, out string error) =>
                {
                    gitKeys.Add(key);
                    value = false;
                    error = null;
                    return true;
                },
                new Hashtable { { "GIT_CONFIG_GLOBAL", "C:\\test.gitconfig" } }))
            {
                // The mock libgit2 repo returns true.
                invoker.GetConfigBoolOrDefault("gvfs.test", true).ShouldEqual(false);
            }

            gitKeys.ShouldMatchInOrder("gvfs.test");
        }

        [TestCase]
        public void GetConfigBoolOrDefaultReadsWithLibGit2WhenTheEnvironmentDoesNotChangeGitConfig()
        {
            using (LibGit2RepoInvoker invoker = new LibGit2RepoInvoker(
                this.tracer,
                this.CreateRepo,
                ReadBoolWithGitMustNotRun,
                new Hashtable { { "PATH", "C:\\bin" } }))
            {
                invoker.GetConfigBoolOrDefault("gvfs.test", false).ShouldEqual(true);
            }
        }

        [TestCase]
        public void GetConfigBoolOrDefaultReturnsTheDefaultWhenGitReportsAnUnsetKey()
        {
            using (LibGit2RepoInvoker invoker = new LibGit2RepoInvoker(
                this.tracer,
                this.CreateRepo,
                (string key, out bool? value, out string error) =>
                {
                    value = null;
                    error = null;
                    return true;
                },
                new Hashtable { { "GIT_CONFIG_COUNT", "1" } }))
            {
                // The mock libgit2 repo returns true.
                invoker.GetConfigBoolOrDefault("gvfs.test", false).ShouldEqual(false);
            }
        }

        [TestCase]
        [Category(CategoryConstants.ExceptionExpected)]
        public void GetConfigBoolOrDefaultThrowsWhenGitFailsToReadTheKey()
        {
            using (LibGit2RepoInvoker invoker = new LibGit2RepoInvoker(
                this.tracer,
                this.CreateRepo,
                (string key, out bool? value, out string error) =>
                {
                    value = null;
                    error = "fatal: bad boolean config value 'of' for 'gvfs.test'";
                    return false;
                },
                new Hashtable { { "GIT_CONFIG_PARAMETERS", "'gvfs.test=of'" } }))
            {
                InvalidDataException exception = Assert.Throws<InvalidDataException>(() => invoker.GetConfigBoolOrDefault("gvfs.test", true));
                exception.Message.ShouldContain("bad boolean config value");
            }
        }

        [TestCase]
        [Category(CategoryConstants.ExceptionExpected)]
        public void ConstructorRequiresAGitReader()
        {
            Assert.Throws<ArgumentNullException>(() => new LibGit2RepoInvoker(this.tracer, this.CreateRepo, readBoolWithGit: null));
        }

        private static bool ReadBoolWithGitMustNotRun(string key, out bool? value, out string error)
        {
            Assert.Fail("Read " + key + " with git.exe");
            value = null;
            error = null;
            return false;
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

            public override bool? GetConfigBool(string name)
            {
                return true;
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
