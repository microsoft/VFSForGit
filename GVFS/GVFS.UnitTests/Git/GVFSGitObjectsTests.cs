using GVFS.Common;
using GVFS.Common.Git;
using GVFS.Common.Http;
using GVFS.Common.Tracing;
using GVFS.Tests.Should;
using GVFS.UnitTests.Category;
using GVFS.UnitTests.Mock;
using GVFS.UnitTests.Mock.Common;
using GVFS.UnitTests.Mock.FileSystem;
using GVFS.UnitTests.Mock.Git;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Threading;

namespace GVFS.UnitTests.Git
{
    [TestFixture]
    public class GVFSGitObjectsTests
    {
        private const string ValidTestObjectFileSha1 = "c1f2535cd983afa20de0d64fcaaba06ce535aa30";
        private const string TestEnlistmentRoot = "mock:\\src";
        private const string TestLocalCacheRoot = "mock:\\.gvfs";
        private const string TestObjectRoot = "mock:\\.gvfs\\gitObjectCache";
        private readonly byte[] validTestObjectFileContents = new byte[]
        {
            0x78, 0x01, 0x4B, 0xCA, 0xC9, 0x4F, 0x52, 0x30, 0x62,
            0x48, 0xE4, 0x02, 0x00, 0x0E, 0x64, 0x02, 0x5D
        };

        [SetUp]
        public void SetUp()
        {
            // These tests deliberately drive the retrier to failure, which records failures on the
            // process-global RetryCircuitBreaker. Reset it before each test so an accumulation of
            // failures from earlier tests cannot open the circuit and fast-fail a later test with a
            // circuit-open RetryableException (which would otherwise be mis-read as its cause).
            RetryCircuitBreaker.Reset();
        }

        [TearDown]
        public void TearDown()
        {
            // Reset on exit too, so this fixture cannot leave the process-global circuit dirty for a
            // later breaker-sensitive fixture (NUnit does not guarantee cross-fixture ordering).
            RetryCircuitBreaker.Reset();
        }

        [TestCase]
        [Category(CategoryConstants.ExceptionExpected)]
        public void CatchesFileNotFoundAfterFileDeleted()
        {
            MockFileSystemWithCallbacks fileSystem = new MockFileSystemWithCallbacks();
            fileSystem.OnFileExists = (path) => true;
            fileSystem.OnOpenFileStream = (path, fileMode, fileAccess) =>
            {
                if (fileAccess == FileAccess.Write)
                {
                    return new MemoryStream();
                }

                throw new FileNotFoundException();
            };

            MockHttpGitObjects httpObjects = new MockHttpGitObjects();
            using (httpObjects.InputStream = new MemoryStream(this.validTestObjectFileContents))
            {
                httpObjects.MediaType = GVFSConstants.MediaTypes.LooseObjectMediaType;
                GVFSGitObjects dut = this.CreateTestableGVFSGitObjects(httpObjects, fileSystem);

                dut.TryCopyBlobContentStream(
                    ValidTestObjectFileSha1,
                    new CancellationToken(),
                    GVFSGitObjects.RequestSource.FileStreamCallback,
                    (stream, length) => Assert.Fail("Should not be able to call copy stream callback"),
                    out GVFSGitObjects.BlobHydrationFailureCategory _)
                    .ShouldEqual(false);
            }
        }

        [TestCase]
        [Category(CategoryConstants.ExceptionExpected)]
        public void TerminalBlobHydrationFailureIsTaggedWithCategory()
        {
            MockFileSystemWithCallbacks fileSystem = new MockFileSystemWithCallbacks();
            fileSystem.OnFileExists = (path) => true;
            fileSystem.OnOpenFileStream = (path, fileMode, fileAccess) =>
            {
                if (fileAccess == FileAccess.Write)
                {
                    return new MemoryStream();
                }

                throw new FileNotFoundException();
            };

            MockHttpGitObjects httpObjects = new MockHttpGitObjects();
            using (httpObjects.InputStream = new MemoryStream(this.validTestObjectFileContents))
            {
                httpObjects.MediaType = GVFSConstants.MediaTypes.LooseObjectMediaType;
                GVFSGitObjects dut = this.CreateTestableGVFSGitObjects(httpObjects, fileSystem, out MockTracer tracer);

                bool copied = dut.TryCopyBlobContentStream(
                    ValidTestObjectFileSha1,
                    new CancellationToken(),
                    GVFSGitObjects.RequestSource.FileStreamCallback,
                    (stream, length) => Assert.Fail("Should not be able to call copy stream callback"),
                    out GVFSGitObjects.BlobHydrationFailureCategory failureCategory);
                copied.ShouldEqual(false);

                // The terminal failure carries a specific BlobHydrationFailureCategory so telemetry
                // can tell failures outside gvfs.exe's control apart from actionable ones. Here the
                // local copy misses and the download then fails, so the cause is NetworkUnavailable —
                // surfaced both on the telemetry event and via the out parameter.
                failureCategory.ShouldEqual(GVFSGitObjects.BlobHydrationFailureCategory.NetworkUnavailable);
                string terminalError = tracer.RelatedErrorEvents.First(e => e.Contains("Failed to provide blob contents"));
                terminalError.ShouldContain("\"BlobHydrationFailureCategory\":\"NetworkUnavailable\"");
            }
        }

        [TestCase]
        [Category(CategoryConstants.ExceptionExpected)]
        public void TerminalBlobHydrationFailureTagsObjectNotOnServer()
        {
            MockFileSystemWithCallbacks fileSystem = new MockFileSystemWithCallbacks();
            fileSystem.OnFileExists = (path) => true;
            fileSystem.OnOpenFileStream = (path, mode, access) =>
            {
                if (access == FileAccess.Write)
                {
                    return new MemoryStream();
                }

                throw new FileNotFoundException();
            };

            MockHttpGitObjects httpObjects = new MockHttpGitObjects();
            httpObjects.StatusCodeToReturn = HttpStatusCode.NotFound;
            GVFSGitObjects dut = this.CreateTestableGVFSGitObjects(httpObjects, fileSystem, out MockTracer tracer);

            bool copied = dut.TryCopyBlobContentStream(
                ValidTestObjectFileSha1,
                new CancellationToken(),
                GVFSGitObjects.RequestSource.FileStreamCallback,
                (stream, length) => Assert.Fail("Should not be able to call copy stream callback"),
                out GVFSGitObjects.BlobHydrationFailureCategory failureCategory);
            copied.ShouldEqual(false);

            // The server returned 404, so the blob is genuinely missing on the server (actionable).
            failureCategory.ShouldEqual(GVFSGitObjects.BlobHydrationFailureCategory.ObjectNotOnServer);
            string terminalError = tracer.RelatedErrorEvents.First(e => e.Contains("Failed to provide blob contents"));
            terminalError.ShouldContain("\"BlobHydrationFailureCategory\":\"ObjectNotOnServer\"");
        }

        [TestCase]
        [Category(CategoryConstants.ExceptionExpected)]
        public void TerminalBlobHydrationFailureTagsLocalCopyFailed()
        {
            MockFileSystemWithCallbacks fileSystem = new MockFileSystemWithCallbacks();
            fileSystem.OnFileExists = (path) => true;
            fileSystem.OnOpenFileStream = (path, mode, access) =>
            {
                if (access == FileAccess.Write)
                {
                    return new MemoryStream();
                }

                throw new FileNotFoundException();
            };
            fileSystem.OnMoveFile = (source, target) => { };

            MockHttpGitObjects httpObjects = new MockHttpGitObjects();

            // Serve fresh content on every attempt so the download succeeds; the failure must then be
            // attributed to the local copy that keeps failing afterward, not to the download.
            httpObjects.ContentBytesToServe = this.validTestObjectFileContents;
            httpObjects.MediaType = GVFSConstants.MediaTypes.LooseObjectMediaType;
            GVFSGitObjects dut = this.CreateTestableGVFSGitObjects(httpObjects, fileSystem, out MockTracer tracer);

            bool copied = dut.TryCopyBlobContentStream(
                ValidTestObjectFileSha1,
                new CancellationToken(),
                GVFSGitObjects.RequestSource.FileStreamCallback,
                (stream, length) => Assert.Fail("Should not be able to call copy stream callback"),
                out GVFSGitObjects.BlobHydrationFailureCategory failureCategory);
            copied.ShouldEqual(false);

            failureCategory.ShouldEqual(GVFSGitObjects.BlobHydrationFailureCategory.LocalCopyFailed);
            string terminalError = tracer.RelatedErrorEvents.First(e => e.Contains("Failed to provide blob contents"));
            terminalError.ShouldContain("\"BlobHydrationFailureCategory\":\"LocalCopyFailed\"");
        }

        [TestCase]
        [Category(CategoryConstants.ExceptionExpected)]
        public void TerminalBlobHydrationFailureUnwrapsRetryableLocalIOException()
        {
            // A RetryableException reaches this branch from Context.Repository.TryCopyBlobContentStream -
            // e.g. StreamUtil wrapping an IOException while reading a corrupt/truncated local loose
            // object. Its inner cause must be attributed to LocalIO, not NetworkUnavailable. Regression
            // guard for the RetryableException.InnerException unwrap in the failure categorization.
            this.AssertRetryableCauseMapsToCategory(
                new RetryableException("wrapped local IO failure", new IOException("The device is not ready.")),
                GVFSGitObjects.BlobHydrationFailureCategory.LocalIO);
        }

        [TestCase]
        [Category(CategoryConstants.ExceptionExpected)]
        public void TerminalBlobHydrationFailureUnwrapsRetryableUnauthorizedAccessAsLocalIO()
        {
            // UnauthorizedAccessException belongs to the local disk/IO family and must map to LocalIO
            // after the InnerException unwrap.
            this.AssertRetryableCauseMapsToCategory(
                new RetryableException("wrapped local access failure", new UnauthorizedAccessException("Access to the path is denied.")),
                GVFSGitObjects.BlobHydrationFailureCategory.LocalIO);
        }

        [TestCase]
        [Category(CategoryConstants.ExceptionExpected)]
        public void TerminalBlobHydrationFailureUnwrapsRetryableWin32AsLocalIO()
        {
            // Win32Exception (here a disk-full native error) belongs to the local disk/IO family and
            // must map to LocalIO after the InnerException unwrap.
            this.AssertRetryableCauseMapsToCategory(
                new RetryableException("wrapped local Win32 failure", new System.ComponentModel.Win32Exception(112)),
                GVFSGitObjects.BlobHydrationFailureCategory.LocalIO);
        }

        [TestCase]
        [Category(CategoryConstants.ExceptionExpected)]
        public void TerminalBlobHydrationFailureKeepsRetryableNonLocalInnerAsNetworkUnavailable()
        {
            // A RetryableException whose inner cause is NOT a local disk/IO type (here an
            // HttpRequestException) must stay NetworkUnavailable - this proves the unwrap does not
            // over-attribute to LocalIO.
            this.AssertRetryableCauseMapsToCategory(
                new RetryableException("wrapped network failure", new HttpRequestException("Connection refused.")),
                GVFSGitObjects.BlobHydrationFailureCategory.NetworkUnavailable);
        }

        [TestCase]
        [Category(CategoryConstants.ExceptionExpected)]
        public void TerminalBlobHydrationFailureTagsRetryableNetworkAsNetworkUnavailable()
        {
            // A RetryableException with no inner exception (a bare network-flakiness signal, e.g. a
            // null download stream) is genuinely outside gvfs.exe's control, so it stays
            // NetworkUnavailable after the InnerException unwrap.
            this.AssertRetryableCauseMapsToCategory(
                new RetryableException("Stream is null (this could be a result of network flakiness), retrying."),
                GVFSGitObjects.BlobHydrationFailureCategory.NetworkUnavailable);
        }

        private void AssertRetryableCauseMapsToCategory(RetryableException thrownException, GVFSGitObjects.BlobHydrationFailureCategory expected)
        {
            GitRepo throwingRepo = new ThrowingGitRepo(new MockTracer(), thrownException);
            MockHttpGitObjects httpObjects = new MockHttpGitObjects();
            GVFSGitObjects dut = this.CreateTestableGVFSGitObjects(httpObjects, throwingRepo, out MockTracer tracer);

            bool copied = dut.TryCopyBlobContentStream(
                ValidTestObjectFileSha1,
                new CancellationToken(),
                GVFSGitObjects.RequestSource.FileStreamCallback,
                (stream, length) => Assert.Fail("Should not be able to call copy stream callback"),
                out GVFSGitObjects.BlobHydrationFailureCategory failureCategory);

            copied.ShouldEqual(false);
            failureCategory.ShouldEqual(expected);
            string terminalError = tracer.RelatedErrorEvents.First(e => e.Contains("Failed to provide blob contents"));
            terminalError.ShouldContain("\"BlobHydrationFailureCategory\":\"" + expected + "\"");
        }

        [TestCase]
        public void SucceedsForNormalLookingLooseObjectDownloads()
        {
            MockFileSystemWithCallbacks fileSystem = new Mock.FileSystem.MockFileSystemWithCallbacks();
            fileSystem.OnFileExists = (path) => true;
            fileSystem.OnOpenFileStream = (path, mode, access) => new MemoryStream();
            MockHttpGitObjects httpObjects = new MockHttpGitObjects();
            using (httpObjects.InputStream = new MemoryStream(this.validTestObjectFileContents))
            {
                httpObjects.MediaType = GVFSConstants.MediaTypes.LooseObjectMediaType;
                GVFSGitObjects dut = this.CreateTestableGVFSGitObjects(httpObjects, fileSystem);

                dut.TryDownloadAndSaveObject(ValidTestObjectFileSha1, GVFSGitObjects.RequestSource.FileStreamCallback)
                    .ShouldEqual(GitObjects.DownloadAndSaveObjectResult.Success);
            }
        }

        [TestCase]
        [Category(CategoryConstants.ExceptionExpected)]
        public void FailsZeroByteLooseObjectsDownloads()
        {
            this.AssertRetryableExceptionOnDownload(
                new MemoryStream(),
                GVFSConstants.MediaTypes.LooseObjectMediaType,
                gitObjects => gitObjects.TryDownloadAndSaveObject(ValidTestObjectFileSha1, GVFSGitObjects.RequestSource.FileStreamCallback));
        }

        [TestCase]
        [Category(CategoryConstants.ExceptionExpected)]
        public void FailsNullByteLooseObjectsDownloads()
        {
            this.AssertRetryableExceptionOnDownload(
                new MemoryStream(new byte[256]),
                GVFSConstants.MediaTypes.LooseObjectMediaType,
                gitObjects => gitObjects.TryDownloadAndSaveObject("b376885ac8452b6cbf9ced81b1080bfd570d9b91", GVFSGitObjects.RequestSource.FileStreamCallback));
        }

        [TestCase]
        [Category(CategoryConstants.ExceptionExpected)]
        public void FailsZeroBytePackDownloads()
        {
            this.AssertRetryableExceptionOnDownload(
                new MemoryStream(),
                GVFSConstants.MediaTypes.PackFileMediaType,
                gitObjects => gitObjects.TryDownloadCommit("object0"));
        }

        [TestCase]
        [Category(CategoryConstants.ExceptionExpected)]
        public void FailsNullBytePackDownloads()
        {
            this.AssertRetryableExceptionOnDownload(
                new MemoryStream(new byte[256]),
                GVFSConstants.MediaTypes.PackFileMediaType,
                gitObjects => gitObjects.TryDownloadCommit("object0"));
        }

        [TestCase]
        public void TryCopyBlobContentStreamFailsCleanlyForCorruptAllNullByteSha()
        {
            // A corrupt placeholder can present a content-id of 40 NUL bytes instead of a
            // hex SHA. Those characters are illegal in a file path, so building a loose
            // object path from them used to throw an unhandled ArgumentException that
            // bypassed retry and download fallback and permanently failed (and retry-stormed)
            // the hydration. The read must now fail cleanly with no exception.
            this.AssertMalformedShaHydrationFailsCleanly(new string('\0', 40));
        }

        [TestCase]
        public void TryCopyBlobContentStreamFailsCleanlyForOtherMalformedShas()
        {
            this.AssertMalformedShaHydrationFailsCleanly(string.Empty);
            this.AssertMalformedShaHydrationFailsCleanly("0123456789");
            this.AssertMalformedShaHydrationFailsCleanly(new string('0', 39));
            this.AssertMalformedShaHydrationFailsCleanly("000000000000000000000000000000000000000g");

            // 40 chars long but with a NUL embedded among hex digits — the realistic
            // corrupt-content-id shape that actually reproduces the original "Illegal
            // characters in path" ArgumentException (length passes, hex check fails).
            this.AssertMalformedShaHydrationFailsCleanly(new string('0', 20) + "\0" + new string('0', 19));

            // 40 chars long with an embedded backslash. Unlike NUL this would NOT have thrown
            // pre-fix (backslash is a legal path separator), but it is still non-hex, so the
            // guard must reject it as a clean miss rather than probe a bogus path.
            this.AssertMalformedShaHydrationFailsCleanly(new string('a', 20) + "\\" + new string('a', 19));
        }

        [TestCase]
        public void CoalescesMultipleConcurrentRequestsForSameObject()
        {
            ManualResetEventSlim downloadStarted = new ManualResetEventSlim(false);
            ManualResetEventSlim downloadGate = new ManualResetEventSlim(false);
            int downloadCount = 0;

            CoalescingTestHttpGitObjects httpObjects = new CoalescingTestHttpGitObjects(
                this.validTestObjectFileContents,
                onDownloadStarting: () =>
                {
                    Interlocked.Increment(ref downloadCount);
                    downloadStarted.Set();
                    downloadGate.Wait();
                });

            MockFileSystemWithCallbacks fileSystem = new MockFileSystemWithCallbacks();
            fileSystem.OnFileExists = (path) => false;
            fileSystem.OnMoveFile = (source, target) => { };
            fileSystem.OnOpenFileStream = (path, mode, access) =>
            {
                if (access == FileAccess.Read)
                {
                    return new MemoryStream(this.validTestObjectFileContents);
                }

                return new MemoryStream();
            };

            GVFSGitObjects dut = this.CreateTestableGVFSGitObjects(httpObjects, fileSystem);

            const int threadCount = 10;
            GitObjects.DownloadAndSaveObjectResult[] results = new GitObjects.DownloadAndSaveObjectResult[threadCount];
            Thread[] threads = new Thread[threadCount];
            CountdownEvent allReady = new CountdownEvent(threadCount);
            ManualResetEventSlim go = new ManualResetEventSlim(false);

            for (int i = 0; i < threadCount; i++)
            {
                int idx = i;
                threads[i] = new Thread(() =>
                {
                    allReady.Signal();
                    go.Wait();
                    results[idx] = dut.TryDownloadAndSaveObject(
                        ValidTestObjectFileSha1,
                        GVFSGitObjects.RequestSource.NamedPipeMessage);
                });
                threads[i].Start();
            }

            // Release all threads simultaneously
            allReady.Wait();
            go.Set();

            // Wait for the first download to start (proves one thread entered the factory)
            downloadStarted.Wait(TimeSpan.FromSeconds(5)).ShouldBeTrue("Download should have started");

            // Give other threads time to pile up on the Lazy<T>
            Thread.Sleep(200);

            // Release the download
            downloadGate.Set();

            // Wait for all threads
            foreach (Thread t in threads)
            {
                t.Join(TimeSpan.FromSeconds(10)).ShouldBeTrue("Thread should complete");
            }

            // Only one download should have occurred
            downloadCount.ShouldEqual(1);

            // All threads should have gotten Success
            foreach (GitObjects.DownloadAndSaveObjectResult result in results)
            {
                result.ShouldEqual(GitObjects.DownloadAndSaveObjectResult.Success);
            }
        }

        [TestCase]
        public void DifferentObjectsAreNotCoalesced()
        {
            string secondSha = "b376885ac8452b6cbf9ced81b1080bfd570d9b91";
            int downloadCount = 0;

            CoalescingTestHttpGitObjects httpObjects = new CoalescingTestHttpGitObjects(
                this.validTestObjectFileContents,
                onDownloadStarting: () => Interlocked.Increment(ref downloadCount));

            MockFileSystemWithCallbacks fileSystem = new MockFileSystemWithCallbacks();
            fileSystem.OnFileExists = (path) => false;
            fileSystem.OnMoveFile = (source, target) => { };
            fileSystem.OnOpenFileStream = (path, mode, access) =>
            {
                if (access == FileAccess.Read)
                {
                    return new MemoryStream(this.validTestObjectFileContents);
                }

                return new MemoryStream();
            };

            GVFSGitObjects dut = this.CreateTestableGVFSGitObjects(httpObjects, fileSystem);

            dut.TryDownloadAndSaveObject(ValidTestObjectFileSha1, GVFSGitObjects.RequestSource.NamedPipeMessage)
                .ShouldEqual(GitObjects.DownloadAndSaveObjectResult.Success);

            dut.TryDownloadAndSaveObject(secondSha, GVFSGitObjects.RequestSource.NamedPipeMessage)
                .ShouldEqual(GitObjects.DownloadAndSaveObjectResult.Success);

            downloadCount.ShouldEqual(2);
        }

        [TestCase]
        public void FailedDownloadAllowsSubsequentRetry()
        {
            int downloadCount = 0;

            CoalescingTestHttpGitObjects httpObjects = new CoalescingTestHttpGitObjects(
                this.validTestObjectFileContents,
                onDownloadStarting: () => Interlocked.Increment(ref downloadCount),
                failUntilAttempt: 2);

            MockFileSystemWithCallbacks fileSystem = new MockFileSystemWithCallbacks();
            fileSystem.OnFileExists = (path) => false;
            fileSystem.OnMoveFile = (source, target) => { };
            fileSystem.OnOpenFileStream = (path, mode, access) =>
            {
                if (access == FileAccess.Read)
                {
                    return new MemoryStream(this.validTestObjectFileContents);
                }

                return new MemoryStream();
            };

            GVFSGitObjects dut = this.CreateTestableGVFSGitObjects(httpObjects, fileSystem);

            // First attempt fails
            dut.TryDownloadAndSaveObject(ValidTestObjectFileSha1, GVFSGitObjects.RequestSource.NamedPipeMessage)
                .ShouldEqual(GitObjects.DownloadAndSaveObjectResult.Error);

            // Second attempt should start a new download (not reuse cached failure)
            dut.TryDownloadAndSaveObject(ValidTestObjectFileSha1, GVFSGitObjects.RequestSource.NamedPipeMessage)
                .ShouldEqual(GitObjects.DownloadAndSaveObjectResult.Success);

            // Two separate downloads should have occurred
            downloadCount.ShouldEqual(2);
        }

        [TestCase]
        public void ConcurrentFailedDownloadAllowsSubsequentRetry()
        {
            ManualResetEventSlim downloadStarted = new ManualResetEventSlim(false);
            ManualResetEventSlim downloadGate = new ManualResetEventSlim(false);
            int downloadCount = 0;

            CoalescingTestHttpGitObjects httpObjects = new CoalescingTestHttpGitObjects(
                this.validTestObjectFileContents,
                onDownloadStarting: () =>
                {
                    int count = Interlocked.Increment(ref downloadCount);
                    if (count == 1)
                    {
                        downloadStarted.Set();
                        downloadGate.Wait();
                    }
                },
                failUntilAttempt: 2);

            MockFileSystemWithCallbacks fileSystem = new MockFileSystemWithCallbacks();
            fileSystem.OnFileExists = (path) => false;
            fileSystem.OnMoveFile = (source, target) => { };
            fileSystem.OnOpenFileStream = (path, mode, access) =>
            {
                if (access == FileAccess.Read)
                {
                    return new MemoryStream(this.validTestObjectFileContents);
                }

                return new MemoryStream();
            };

            GVFSGitObjects dut = this.CreateTestableGVFSGitObjects(httpObjects, fileSystem);

            const int threadCount = 5;
            GitObjects.DownloadAndSaveObjectResult[] results = new GitObjects.DownloadAndSaveObjectResult[threadCount];
            Thread[] threads = new Thread[threadCount];
            CountdownEvent allReady = new CountdownEvent(threadCount);
            ManualResetEventSlim go = new ManualResetEventSlim(false);

            for (int i = 0; i < threadCount; i++)
            {
                int idx = i;
                threads[i] = new Thread(() =>
                {
                    allReady.Signal();
                    go.Wait();
                    results[idx] = dut.TryDownloadAndSaveObject(
                        ValidTestObjectFileSha1,
                        GVFSGitObjects.RequestSource.NamedPipeMessage);
                });
                threads[i].Start();
            }

            allReady.Wait();
            go.Set();

            downloadStarted.Wait(TimeSpan.FromSeconds(5)).ShouldBeTrue("Download should have started");
            Thread.Sleep(200);
            downloadGate.Set();

            foreach (Thread t in threads)
            {
                t.Join(TimeSpan.FromSeconds(10)).ShouldBeTrue("Thread should complete");
            }

            // All coalesced threads should have gotten Error
            foreach (GitObjects.DownloadAndSaveObjectResult result in results)
            {
                result.ShouldEqual(GitObjects.DownloadAndSaveObjectResult.Error);
            }

            // Subsequent request should succeed (new download, not cached failure)
            dut.TryDownloadAndSaveObject(ValidTestObjectFileSha1, GVFSGitObjects.RequestSource.NamedPipeMessage)
                .ShouldEqual(GitObjects.DownloadAndSaveObjectResult.Success);

            downloadCount.ShouldEqual(2);
        }

        [TestCase]
        [Category(CategoryConstants.ExceptionExpected)]
        public void ExceptionInDownloadFactoryAllowsRetry()
        {
            ManualResetEventSlim downloadStarted = new ManualResetEventSlim(false);
            ManualResetEventSlim downloadGate = new ManualResetEventSlim(false);
            int downloadCount = 0;

            CoalescingTestHttpGitObjects httpObjects = new CoalescingTestHttpGitObjects(
                this.validTestObjectFileContents,
                onDownloadStarting: () =>
                {
                    int count = Interlocked.Increment(ref downloadCount);
                    if (count == 1)
                    {
                        downloadStarted.Set();
                        downloadGate.Wait();
                    }
                },
                throwUntilAttempt: 2);

            MockFileSystemWithCallbacks fileSystem = new MockFileSystemWithCallbacks();
            fileSystem.OnFileExists = (path) => false;
            fileSystem.OnMoveFile = (source, target) => { };
            fileSystem.OnOpenFileStream = (path, mode, access) =>
            {
                if (access == FileAccess.Read)
                {
                    return new MemoryStream(this.validTestObjectFileContents);
                }

                return new MemoryStream();
            };

            GVFSGitObjects dut = this.CreateTestableGVFSGitObjects(httpObjects, fileSystem);

            const int threadCount = 5;
            Exception[] exceptions = new Exception[threadCount];
            Thread[] threads = new Thread[threadCount];
            CountdownEvent allReady = new CountdownEvent(threadCount);
            ManualResetEventSlim go = new ManualResetEventSlim(false);

            for (int i = 0; i < threadCount; i++)
            {
                int idx = i;
                threads[i] = new Thread(() =>
                {
                    allReady.Signal();
                    go.Wait();
                    try
                    {
                        dut.TryDownloadAndSaveObject(
                            ValidTestObjectFileSha1,
                            GVFSGitObjects.RequestSource.NamedPipeMessage);
                    }
                    catch (Exception ex)
                    {
                        exceptions[idx] = ex;
                    }
                });
                threads[i].Start();
            }

            allReady.Wait();
            go.Set();

            downloadStarted.Wait(TimeSpan.FromSeconds(5)).ShouldBeTrue("Download should have started");
            Thread.Sleep(200);
            downloadGate.Set();

            foreach (Thread t in threads)
            {
                t.Join(TimeSpan.FromSeconds(10)).ShouldBeTrue("Thread should complete");
            }

            // All coalesced threads should have caught the exception
            foreach (Exception ex in exceptions)
            {
                Assert.IsNotNull(ex, "Each coalesced caller should receive the exception");
                Assert.IsInstanceOf<IOException>(ex);
            }

            // Subsequent retry should succeed (inflight entry was cleaned up)
            dut.TryDownloadAndSaveObject(ValidTestObjectFileSha1, GVFSGitObjects.RequestSource.NamedPipeMessage)
                .ShouldEqual(GitObjects.DownloadAndSaveObjectResult.Success);

            downloadCount.ShouldEqual(2);
        }

        [TestCase]
        public void StragglingFinallyDoesNotRemoveNewInflightDownload()
        {
            // Deterministically reproduce the ABA race against the real inflightDownloads
            // dictionary: a straggling wave-1 thread's TryRemoveInflightDownload must not
            // remove a wave-2 Lazy that was added for the same key.
            ManualResetEventSlim wave2Started = new ManualResetEventSlim(false);
            ManualResetEventSlim wave2Gate = new ManualResetEventSlim(false);
            int downloadCount = 0;

            CoalescingTestHttpGitObjects httpObjects = new CoalescingTestHttpGitObjects(
                this.validTestObjectFileContents,
                onDownloadStarting: () =>
                {
                    int count = Interlocked.Increment(ref downloadCount);
                    if (count == 2)
                    {
                        // Wave 2's download: signal that it's in-flight, then block
                        wave2Started.Set();
                        wave2Gate.Wait();
                    }
                });

            MockFileSystemWithCallbacks fileSystem = new MockFileSystemWithCallbacks();
            fileSystem.OnFileExists = (path) => false;
            fileSystem.OnMoveFile = (source, target) => { };
            fileSystem.OnOpenFileStream = (path, mode, access) =>
            {
                if (access == FileAccess.Read)
                {
                    return new MemoryStream(this.validTestObjectFileContents);
                }

                return new MemoryStream();
            };

            GVFSGitObjects dut = this.CreateTestableGVFSGitObjects(httpObjects, fileSystem);

            // Wave 1: single download completes immediately (downloadCount becomes 1)
            dut.TryDownloadAndSaveObject(ValidTestObjectFileSha1, GVFSGitObjects.RequestSource.NamedPipeMessage)
                .ShouldEqual(GitObjects.DownloadAndSaveObjectResult.Success);

            // After wave 1, the inflight entry should be cleaned up
            dut.inflightDownloads.ContainsKey(ValidTestObjectFileSha1).ShouldBeFalse("Wave 1 should have cleaned up");

            // Wave 2: start a new download that blocks inside its factory (downloadCount becomes 2)
            Thread wave2Thread = new Thread(() =>
            {
                dut.TryDownloadAndSaveObject(ValidTestObjectFileSha1, GVFSGitObjects.RequestSource.NamedPipeMessage)
                    .ShouldEqual(GitObjects.DownloadAndSaveObjectResult.Success);
            });
            wave2Thread.Start();

            // Wait until wave 2's download factory is executing (Lazy is in the dictionary)
            wave2Started.Wait(TimeSpan.FromSeconds(5)).ShouldBeTrue("Wave 2 download should have started");

            // Capture wave 2's Lazy from the dictionary
            Lazy<GitObjects.DownloadAndSaveObjectResult> wave2Lazy;
            dut.inflightDownloads.TryGetValue(ValidTestObjectFileSha1, out wave2Lazy).ShouldBeTrue("Wave 2 Lazy should be in dictionary");

            // Simulate a straggling wave-1 thread: create a different Lazy and try to remove it.
            // With value-aware removal, this must NOT remove wave 2's Lazy.
            Lazy<GitObjects.DownloadAndSaveObjectResult> staleLazy =
                new Lazy<GitObjects.DownloadAndSaveObjectResult>(() => GitObjects.DownloadAndSaveObjectResult.Success);
            bool staleRemoved = ((ICollection<KeyValuePair<string, Lazy<GitObjects.DownloadAndSaveObjectResult>>>)dut.inflightDownloads)
                .Remove(new KeyValuePair<string, Lazy<GitObjects.DownloadAndSaveObjectResult>>(ValidTestObjectFileSha1, staleLazy));

            staleRemoved.ShouldBeFalse("Straggling finally must not remove wave 2's Lazy");
            dut.inflightDownloads.ContainsKey(ValidTestObjectFileSha1).ShouldBeTrue("Wave 2 Lazy must survive");
            ReferenceEquals(dut.inflightDownloads[ValidTestObjectFileSha1], wave2Lazy).ShouldBeTrue("The entry should still be wave 2's Lazy");

            // Release wave 2 and verify it completes
            wave2Gate.Set();
            wave2Thread.Join(TimeSpan.FromSeconds(10)).ShouldBeTrue("Wave 2 thread should complete");

            // Both waves should have triggered separate downloads
            downloadCount.ShouldEqual(2);
        }

        private void AssertRetryableExceptionOnDownload(
            MemoryStream inputStream,
            string mediaType,
            Action<GVFSGitObjects> download)
        {
            MockHttpGitObjects httpObjects = new MockHttpGitObjects();
            httpObjects.InputStream = inputStream;
            httpObjects.MediaType = mediaType;
            MockFileSystemWithCallbacks fileSystem = new MockFileSystemWithCallbacks();

            using (ReusableMemoryStream downloadDestination = new ReusableMemoryStream(string.Empty))
            {
                fileSystem.OnFileExists = (path) => false;
                fileSystem.OnOpenFileStream = (path, mode, access) => downloadDestination;

                GVFSGitObjects gitObjects = this.CreateTestableGVFSGitObjects(httpObjects, fileSystem);

                Assert.Throws<RetryableException>(() => download(gitObjects));
                inputStream.Dispose();
            }
        }

        private void AssertMalformedShaHydrationFailsCleanly(string malformedSha)
        {
            MockFileSystemWithCallbacks fileSystem = new MockFileSystemWithCallbacks();
            fileSystem.OnFileExists = (path) =>
            {
                Assert.Fail("A malformed SHA must never be turned into a filesystem path");
                return false;
            };

            MockTracer tracer = new MockTracer();
            GVFSEnlistment enlistment = new GVFSEnlistment(TestEnlistmentRoot, "https://fakeRepoUrl", "fakeGitBinPath", authentication: null);
            enlistment.InitializeCachePathsFromKey(TestLocalCacheRoot, TestObjectRoot);
            GitRepo repo = new GitRepo(tracer, enlistment, fileSystem, () => new MockLibGit2Repo(tracer));
            GVFSContext context = new GVFSContext(tracer, fileSystem, repo, enlistment);
            GVFSGitObjects gitObjects = new UnsafeGVFSGitObjects(context, new MockHttpGitObjects());

            // GitRepo layer: must not throw ArgumentException ("Illegal characters in path"),
            // and must report a clean miss.
            bool repoResult = true;
            Assert.DoesNotThrow(
                () => repoResult = repo.TryCopyBlobContentStream(
                    malformedSha,
                    (stream, length) => Assert.Fail("Should not copy any content for a malformed SHA")),
                "GitRepo.TryCopyBlobContentStream must not throw for a malformed SHA");
            repoResult.ShouldEqual(false);

            // GVFSGitObjects layer: must fail fast with no throw. The out category stays
            // Unexpected (a malformed SHA gets no dedicated telemetry category).
            bool copied = true;
            GVFSGitObjects.BlobHydrationFailureCategory failureCategory = GVFSGitObjects.BlobHydrationFailureCategory.None;
            Assert.DoesNotThrow(
                () => copied = gitObjects.TryCopyBlobContentStream(
                    malformedSha,
                    new CancellationToken(),
                    GVFSGitObjects.RequestSource.FileStreamCallback,
                    (stream, length) => Assert.Fail("Should not copy any content for a malformed SHA"),
                    out failureCategory),
                "GVFSGitObjects.TryCopyBlobContentStream must not throw for a malformed SHA");
            copied.ShouldEqual(false);
            failureCategory.ShouldEqual(GVFSGitObjects.BlobHydrationFailureCategory.Unexpected);

            // The short-circuit must happen BEFORE the retry/download loop: a malformed SHA
            // must never reach a server download. The retrier logs "Failed to provide blob
            // contents" on every attempt, so its absence proves no download/retry ran.
            bool anyDownloadAttemptLogged = tracer.RelatedErrorEvents
                .Concat(tracer.RelatedWarningEvents)
                .Any(e => e.Contains("Failed to provide blob contents"));
            anyDownloadAttemptLogged.ShouldEqual(false);

            // The corrupt-placeholder read must stay diagnosable: both guard layers emit their
            // distinct greppable *_MalformedBlobSha event. Assert the emission so a regression
            // that silently dropped the warning would fail here.
            tracer.RelatedEventNames.ShouldContain(e => e == "GetLooseBlobState_MalformedBlobSha");
            tracer.RelatedEventNames.ShouldContain(e => e == "TryCopyBlobContentStream_MalformedBlobSha");
        }

        private GVFSGitObjects CreateTestableGVFSGitObjects(GitObjectsHttpRequestor httpObjects, MockFileSystemWithCallbacks fileSystem)
        {
            return this.CreateTestableGVFSGitObjects(httpObjects, fileSystem, out _);
        }

        private GVFSGitObjects CreateTestableGVFSGitObjects(GitObjectsHttpRequestor httpObjects, GitRepo repo, out MockTracer tracer)
        {
            MockTracer localTracer = new MockTracer();
            tracer = localTracer;
            GVFSEnlistment enlistment = new GVFSEnlistment(TestEnlistmentRoot, "https://fakeRepoUrl", "fakeGitBinPath", authentication: null);
            enlistment.InitializeCachePathsFromKey(TestLocalCacheRoot, TestObjectRoot);

            GVFSContext context = new GVFSContext(localTracer, new MockFileSystemWithCallbacks(), repo, enlistment);
            GVFSGitObjects dut = new UnsafeGVFSGitObjects(context, httpObjects);
            return dut;
        }

        private GVFSGitObjects CreateTestableGVFSGitObjects(GitObjectsHttpRequestor httpObjects, MockFileSystemWithCallbacks fileSystem, out MockTracer tracer)
        {
            MockTracer localTracer = new MockTracer();
            tracer = localTracer;
            GVFSEnlistment enlistment = new GVFSEnlistment(TestEnlistmentRoot, "https://fakeRepoUrl", "fakeGitBinPath", authentication: null);
            enlistment.InitializeCachePathsFromKey(TestLocalCacheRoot, TestObjectRoot);
            GitRepo repo = new GitRepo(localTracer, enlistment, fileSystem, () => new MockLibGit2Repo(localTracer));

            GVFSContext context = new GVFSContext(localTracer, fileSystem, repo, enlistment);
            GVFSGitObjects dut = new UnsafeGVFSGitObjects(context, httpObjects);
            return dut;
        }

        private class MockHttpGitObjects : GitObjectsHttpRequestor
        {
            public MockHttpGitObjects()
                : this(new MockGVFSEnlistment())
            {
            }

            private MockHttpGitObjects(MockGVFSEnlistment enlistment)
                : base(new MockTracer(), enlistment, new MockCacheServerInfo(), new RetryConfig(maxRetries: 1))
            {
            }

            public Stream InputStream { get; set; }
            public string MediaType { get; set; }
            public HttpStatusCode? StatusCodeToReturn { get; set; }
            public byte[] ContentBytesToServe { get; set; }

            public static MemoryStream GetRandomStream(int size)
            {
                Random randy = new Random(0);
                MemoryStream stream = new MemoryStream();
                byte[] buffer = new byte[size];

                randy.NextBytes(buffer);
                stream.Write(buffer, 0, buffer.Length);

                stream.Position = 0;
                return stream;
            }

            public override RetryWrapper<GitObjectTaskResult>.InvocationResult TryDownloadLooseObject(
                string objectId,
                bool retryOnFailure,
                CancellationToken cancellationToken,
                string requestSource,
                Func<int, GitEndPointResponseData, RetryWrapper<GitObjectTaskResult>.CallbackResult> onSuccess)
            {
                return this.TryDownloadObjects(new[] { objectId }, onSuccess, null, false);
            }

            public override RetryWrapper<GitObjectTaskResult>.InvocationResult TryDownloadObjects(
                IEnumerable<string> objectIds,
                Func<int, GitEndPointResponseData, RetryWrapper<GitObjectTaskResult>.CallbackResult> onSuccess,
                Action<RetryWrapper<GitObjectTaskResult>.ErrorEventArgs> onFailure,
                bool preferBatchedLooseObjects)
            {
                if (this.StatusCodeToReturn.HasValue)
                {
                    // Simulate the server returning a non-OK status (e.g. 404) so callers can exercise
                    // the ObjectNotOnServer path.
                    return new RetryWrapper<GitObjectTaskResult>.InvocationResult(
                        0,
                        error: null,
                        result: new GitObjectTaskResult(this.StatusCodeToReturn.Value));
                }

                // Serve a fresh stream per call when ContentBytesToServe is set so the download
                // succeeds even across retries (InputStream would be consumed after the first read).
                Stream contentStream = this.ContentBytesToServe != null
                    ? new MemoryStream(this.ContentBytesToServe)
                    : this.InputStream;

                using (GitEndPointResponseData response = new GitEndPointResponseData(
                    HttpStatusCode.OK,
                    this.MediaType,
                    contentStream,
                    message: null,
                    onResponseDisposed: null))
                {
                    onSuccess(0, response);
                }

                GitObjectTaskResult result = new GitObjectTaskResult(true);
                return new RetryWrapper<GitObjectTaskResult>.InvocationResult(0, true, result);
            }

            public override List<GitObjectSize> QueryForFileSizes(IEnumerable<string> objectIds, CancellationToken cancellationToken)
            {
                throw new NotImplementedException();
            }
        }

        private class UnsafeGVFSGitObjects : GVFSGitObjects
        {
            public UnsafeGVFSGitObjects(GVFSContext context, GitObjectsHttpRequestor objectRequestor)
                : base(context, objectRequestor)
            {
                this.checkData = false;
            }
        }

        private sealed class ThrowingGitRepo : GitRepo
        {
            private readonly Exception toThrow;

            public ThrowingGitRepo(ITracer tracer, Exception toThrow)
                : base(tracer)
            {
                this.toThrow = toThrow;
            }

            public override bool TryCopyBlobContentStream(string blobSha, Action<Stream, long> writeAction)
            {
                throw this.toThrow;
            }
        }

        private class CoalescingTestHttpGitObjects : GitObjectsHttpRequestor
        {
            private readonly byte[] objectContents;
            private readonly Action onDownloadStarting;
            private readonly int failUntilAttempt;
            private readonly int throwUntilAttempt;
            private int attemptCount;

            public CoalescingTestHttpGitObjects(byte[] objectContents, Action onDownloadStarting, int failUntilAttempt = 0, int throwUntilAttempt = 0)
                : this(new MockGVFSEnlistment(), objectContents, onDownloadStarting, failUntilAttempt, throwUntilAttempt)
            {
            }

            private CoalescingTestHttpGitObjects(MockGVFSEnlistment enlistment, byte[] objectContents, Action onDownloadStarting, int failUntilAttempt, int throwUntilAttempt)
                : base(new MockTracer(), enlistment, new MockCacheServerInfo(), new RetryConfig(maxRetries: 1))
            {
                this.objectContents = objectContents;
                this.onDownloadStarting = onDownloadStarting;
                this.failUntilAttempt = failUntilAttempt;
                this.throwUntilAttempt = throwUntilAttempt;
            }

            public override RetryWrapper<GitObjectTaskResult>.InvocationResult TryDownloadLooseObject(
                string objectId,
                bool retryOnFailure,
                CancellationToken cancellationToken,
                string requestSource,
                Func<int, GitEndPointResponseData, RetryWrapper<GitObjectTaskResult>.CallbackResult> onSuccess)
            {
                this.onDownloadStarting?.Invoke();

                int attempt = Interlocked.Increment(ref this.attemptCount);
                if (attempt < this.throwUntilAttempt)
                {
                    throw new IOException("Simulated download exception");
                }

                if (attempt < this.failUntilAttempt)
                {
                    GitObjectTaskResult failResult = new GitObjectTaskResult(false);
                    return new RetryWrapper<GitObjectTaskResult>.InvocationResult(0, false, failResult);
                }

                using (MemoryStream stream = new MemoryStream(this.objectContents))
                using (GitEndPointResponseData response = new GitEndPointResponseData(
                    HttpStatusCode.OK,
                    GVFSConstants.MediaTypes.LooseObjectMediaType,
                    stream,
                    message: null,
                    onResponseDisposed: null))
                {
                    onSuccess(0, response);
                }

                GitObjectTaskResult result = new GitObjectTaskResult(true);
                return new RetryWrapper<GitObjectTaskResult>.InvocationResult(0, true, result);
            }

            public override RetryWrapper<GitObjectTaskResult>.InvocationResult TryDownloadObjects(
                IEnumerable<string> objectIds,
                Func<int, GitEndPointResponseData, RetryWrapper<GitObjectTaskResult>.CallbackResult> onSuccess,
                Action<RetryWrapper<GitObjectTaskResult>.ErrorEventArgs> onFailure,
                bool preferBatchedLooseObjects)
            {
                throw new NotImplementedException();
            }

            public override List<GitObjectSize> QueryForFileSizes(IEnumerable<string> objectIds, CancellationToken cancellationToken)
            {
                throw new NotImplementedException();
            }
        }
    }
}