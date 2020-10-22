using GVFS.Common;
using GVFS.Common.Git;
using GVFS.Common.Http;
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
using System.Net;
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
                    (stream, length) => Assert.Fail("Should not be able to call copy stream callback"))
                    .ShouldEqual(false);
            }
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

        private GVFSGitObjects CreateTestableGVFSGitObjects(MockHttpGitObjects httpObjects, MockFileSystemWithCallbacks fileSystem)
        {
            MockTracer tracer = new MockTracer();
            GVFSEnlistment enlistment = new GVFSEnlistment(TestEnlistmentRoot, "https://fakeRepoUrl", "fakeGitBinPath", authentication: null);
            enlistment.InitializeCachePathsFromKey(TestLocalCacheRoot, TestObjectRoot);
            GitRepo repo = new GitRepo(tracer, enlistment, fileSystem, () => new MockLibGit2Repo(tracer));

            GVFSContext context = new GVFSContext(tracer, fileSystem, repo, enlistment);
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
                using (GitEndPointResponseData response = new GitEndPointResponseData(
                    HttpStatusCode.OK,
                    this.MediaType,
                    this.InputStream,
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
    }
}