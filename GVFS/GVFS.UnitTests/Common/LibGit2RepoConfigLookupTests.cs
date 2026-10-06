using GVFS.Common.Git;
using GVFS.Tests.Should;
using GVFS.UnitTests.Mock.Common;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.IO;

namespace GVFS.UnitTests.Common
{
    [TestFixture]
    public class LibGit2RepoConfigLookupTests
    {
        private const string LockedConfigError =
            "Failed to get config handle: could not open 'C:\\repo\\.git\\config': The process cannot access the file because it is being used by another process.";

        [TestCase]
        public void GetConfigBoolOrDefaultOnRepoReturnsConfiguredValue()
        {
            MockTracer tracer = new MockTracer();

            using (MockConfigRepo repo = new MockConfigRepo(tracer, true))
            {
                bool value = repo.GetConfigBoolOrDefault("gvfs.test", false);

                value.ShouldEqual(true);
                tracer.RelatedWarningEvents.Count.ShouldEqual(0);
            }
        }

        [TestCase]
        public void GetConfigBoolOrDefaultOnRepoReturnsDefaultWhenKeyIsUnset()
        {
            MockTracer tracer = new MockTracer();

            using (MockConfigRepo repo = new MockConfigRepo(tracer, (bool?)null))
            {
                bool value = repo.GetConfigBoolOrDefault("gvfs.test", true);

                value.ShouldEqual(true);
                tracer.RelatedWarningEvents.Count.ShouldEqual(0);
            }
        }

        [TestCase]
        public void GetConfigBoolOrDefaultOnRepoReturnsDefaultOnLibGit2ExceptionAndLogsOnce()
        {
            MockTracer tracer = new MockTracer();

            using (MockConfigRepo repo = new MockConfigRepo(tracer, new LibGit2Exception("boom")))
            {
                bool value = repo.GetConfigBoolOrDefault("gvfs.test", false);

                value.ShouldEqual(false);
                tracer.RelatedWarningEvents.Count.ShouldEqual(1);
                tracer.RelatedWarningEvents[0].ShouldContain("Failed to read gvfs.test config, using default: boom");
            }
        }

        [TestCase]
        public void GetConfigBoolOrDefaultOnRepoReturnsDefaultOnInvalidDataExceptionAndLogsOnce()
        {
            MockTracer tracer = new MockTracer();

            using (MockConfigRepo repo = new MockConfigRepo(tracer, new InvalidDataException("corrupt config")))
            {
                bool value = repo.GetConfigBoolOrDefault("gvfs.test", false);

                value.ShouldEqual(false);
                tracer.RelatedWarningEvents.Count.ShouldEqual(1);
                tracer.RelatedWarningEvents[0].ShouldContain("Failed to read gvfs.test config, using default: corrupt config");
            }
        }

        [TestCase]
        public void GetConfigBoolRetriesTransientLibGit2Failure()
        {
            MockTracer tracer = new MockTracer();
            int delayCount = 0;
            Queue<object> results = new Queue<object>();
            results.Enqueue(new LibGit2Exception(LockedConfigError));
            results.Enqueue(true);

            using (MockConfigRepo repo = new MockConfigRepo(tracer, results, delay => ++delayCount))
            {
                repo.GetConfigBool("gvfs.test").ShouldEqual(true);
            }

            delayCount.ShouldEqual(1);
            tracer.RelatedWarningEvents.Count.ShouldEqual(1);
            tracer.RelatedWarningEvents[0].ShouldContain("Transient failure while reading git config");
        }

        [TestCase]
        public void GetConfigStringRetriesTransientLibGit2Failure()
        {
            int delayCount = 0;
            Queue<object> results = new Queue<object>();
            results.Enqueue(new LibGit2Exception(LockedConfigError));
            results.Enqueue("configured-value");

            using (MockConfigRepo repo = new MockConfigRepo(new MockTracer(), results, delay => ++delayCount))
            {
                repo.GetConfigString("gvfs.test").ShouldEqual("configured-value");
            }

            delayCount.ShouldEqual(1);
        }

        [TestCase]
        public void GetConfigBoolRethrowsAfterRetryBudget()
        {
            MockTracer tracer = new MockTracer();
            int delayCount = 0;
            Queue<object> results = new Queue<object>();
            for (int i = 0; i < GitConfigReadRetry.MaxAttempts; ++i)
            {
                results.Enqueue(new LibGit2Exception(LockedConfigError));
            }

            using (MockConfigRepo repo = new MockConfigRepo(tracer, results, delay => ++delayCount))
            {
                Assert.Throws<LibGit2Exception>(() => repo.GetConfigBool("gvfs.test"));
            }

            delayCount.ShouldEqual(GitConfigReadRetry.MaxAttempts - 1);
            results.Count.ShouldEqual(0);
            tracer.RelatedWarningEvents.Count.ShouldEqual(GitConfigReadRetry.MaxAttempts);
            tracer.RelatedWarningEvents[GitConfigReadRetry.MaxAttempts - 1].ShouldContain("persisted after all attempts");
        }

        [TestCase]
        public void GetConfigBoolDoesNotRetryNonTransientLibGit2Failure()
        {
            int delayCount = 0;
            Queue<object> results = new Queue<object>();
            results.Enqueue(new LibGit2Exception("Failed to get config handle: could not open 'C:\\repo\\.git\\config': The system cannot find the file specified."));
            results.Enqueue(true);

            using (MockConfigRepo repo = new MockConfigRepo(new MockTracer(), results, delay => ++delayCount))
            {
                Assert.Throws<LibGit2Exception>(() => repo.GetConfigBool("gvfs.test"));
            }

            delayCount.ShouldEqual(0);
            results.Count.ShouldEqual(1);
        }

        [TestCase]
        public void ForEachMultiVarConfigInvokesCallbackOncePerValueAcrossRetries()
        {
            int delayCount = 0;
            Queue<object> results = new Queue<object>();
            results.Enqueue(new LibGit2Exception(LockedConfigError));
            results.Enqueue(new List<string> { "first", "second" });
            List<string> callbackValues = new List<string>();

            using (MockConfigRepo repo = new MockConfigRepo(new MockTracer(), results, delay => ++delayCount))
            {
                repo.ForEachMultiVarConfig("safe.directory", callbackValues.Add);
            }

            delayCount.ShouldEqual(1);
            callbackValues.ShouldMatchInOrder("first", "second");
        }

        [TestCase]
        public void RepositoryOpenRetriesTransientFailure()
        {
            MockTracer tracer = new MockTracer();
            int delayCount = 0;
            Queue<string> openErrors = new Queue<string>();
            openErrors.Enqueue("could not open 'C:\\repo\\.git\\config': The process cannot access the file because it is being used by another process.");
            openErrors.Enqueue("could not open 'C:\\repo\\.git\\config': Permission denied");

            using (MockOpenRepo.Create(tracer, openErrors, delay => ++delayCount, GitConfigReadRetry.MaxAttempts))
            {
            }

            delayCount.ShouldEqual(2);
            openErrors.Count.ShouldEqual(0);
        }

        [TestCase]
        public void RepositoryOpenStopsAfterConfiguredAttempts()
        {
            int delayCount = 0;
            Queue<string> openErrors = new Queue<string>();
            for (int i = 0; i < 5; ++i)
            {
                openErrors.Enqueue("could not open 'C:\\repo\\.git\\config': Permission denied");
            }

            Assert.Throws<InvalidDataException>(
                () => MockOpenRepo.Create(new MockTracer(), openErrors, delay => ++delayCount, maxAttempts: 2));

            delayCount.ShouldEqual(1);
            openErrors.Count.ShouldEqual(3);
        }

        [TestCase]
        public void RepositoryOpenDoesNotRetryNonTransientFailure()
        {
            int delayCount = 0;
            Queue<string> openErrors = new Queue<string>();
            openErrors.Enqueue("could not find repository at 'C:\\repo'");

            Assert.Throws<InvalidDataException>(
                () => MockOpenRepo.Create(new MockTracer(), openErrors, delay => ++delayCount, GitConfigReadRetry.MaxAttempts));

            delayCount.ShouldEqual(0);
        }

        [TestCase]
        public void GetConfigBoolOrDefaultOnPathReturnsDefaultForMissingRepoAndLogsExactlyOnce()
        {
            MockTracer tracer = new MockTracer();

            // A GUID-suffixed path under the OS temp directory is guaranteed not to exist and
            // does not depend on any particular drive letter being unmapped (unlike a
            // hardcoded "Z:\..." path, which could resolve on a host with that drive mapped).
            string missingRepoPath = Path.Combine(
                Path.GetTempPath(),
                "LibGit2RepoConfigLookupTests_" + Guid.NewGuid().ToString("N"));

            bool value = LibGit2Repo.GetConfigBoolOrDefault(
                tracer,
                missingRepoPath,
                "gvfs.test",
                false);

            value.ShouldEqual(false);

            // The LibGit2Repo constructor logs a RelatedWarning with the native open-failure
            // reason before throwing InvalidDataException; the static helper's catch does not
            // log a second time for that case (see LibGit2Repo.GetConfigBoolOrDefault), so
            // exactly one warning is expected here.
            tracer.RelatedWarningEvents.Count.ShouldEqual(1);
            tracer.RelatedWarningEvents[0].ShouldContain("Couldn't open repo at");
        }

        private class MockConfigRepo : LibGit2Repo
        {
            private readonly bool? value;
            private readonly Exception exceptionToThrow;
            private readonly Queue<object> results;

            public MockConfigRepo(MockTracer tracer, bool? value)
                : base(tracer)
            {
                this.value = value;
            }

            public MockConfigRepo(MockTracer tracer, Exception exceptionToThrow)
                : base(tracer)
            {
                this.exceptionToThrow = exceptionToThrow;
            }

            public MockConfigRepo(MockTracer tracer, Queue<object> results, Action<TimeSpan> configReadDelay)
                : base(tracer, configReadDelay)
            {
                this.results = results;
            }

            protected override bool? GetConfigBoolOnce(string name)
            {
                if (this.results != null)
                {
                    object result = this.results.Dequeue();
                    if (result is Exception exception)
                    {
                        throw exception;
                    }

                    return (bool?)result;
                }

                if (this.exceptionToThrow != null)
                {
                    throw this.exceptionToThrow;
                }

                return this.value;
            }

            protected override string GetConfigStringOnce(string name)
            {
                object result = this.results.Dequeue();
                if (result is Exception exception)
                {
                    throw exception;
                }

                return (string)result;
            }

            protected override List<string> GetMultiVarConfigValues(string key)
            {
                object result = this.results.Dequeue();
                if (result is Exception exception)
                {
                    throw exception;
                }

                return (List<string>)result;
            }

            // This mock never calls the base LibGit2Repo(tracer, repoPath) constructor, so it
            // never initializes native libgit2 state or a real RepoHandle. Override Dispose(bool)
            // to skip the base implementation's native Free/Shutdown calls, which would otherwise
            // run on an uninitialized handle without a matching Init (same pattern as
            // LibGit2RepoInvokerTests.MockLibGit2Repo and LibGit2RepoSafeDirectoryTests.MockSafeDirectoryRepo).
            protected override void Dispose(bool disposing)
            {
            }
        }

        // Exercises the real LibGit2Repo(tracer, repoPath, ...) constructor with the native open
        // replaced. State is passed through thread-static fields because the base constructor
        // calls the virtual members before derived constructors run.
        private class MockOpenRepo : LibGit2Repo
        {
            [ThreadStatic]
            private static Queue<string> pendingOpenErrors;

            private MockOpenRepo(MockTracer tracer, Action<TimeSpan> configReadDelay, int maxAttempts)
                : base(tracer, "C:\\repo", configReadDelay, maxAttempts)
            {
            }

            private string LastOpenError { get; set; }

            public static MockOpenRepo Create(MockTracer tracer, Queue<string> openErrors, Action<TimeSpan> configReadDelay, int maxAttempts)
            {
                pendingOpenErrors = openErrors;
                try
                {
                    return new MockOpenRepo(tracer, configReadDelay, maxAttempts);
                }
                finally
                {
                    pendingOpenErrors = null;
                }
            }

            protected override void InitNative()
            {
            }

            protected override void ShutdownNative()
            {
            }

            protected override Native.ResultCode TryOpenRepo(string path, out IntPtr repoHandle)
            {
                repoHandle = IntPtr.Zero;
                this.LastOpenError = pendingOpenErrors.Count > 0 ? pendingOpenErrors.Dequeue() : null;
                return this.LastOpenError == null ? Native.ResultCode.Success : Native.ResultCode.Failure;
            }

            protected override string GetLastNativeError()
            {
                return this.LastOpenError;
            }

            protected override void Dispose(bool disposing)
            {
            }
        }
    }
}
