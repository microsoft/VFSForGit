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
        private const string ValidTestObjectFileContents = "421dc4df5e1de427e363b8acd9ddb2d41385dbdf";
        private const string TestEnlistmentRoot = "mock:\\src";
        private const string TestLocalCacheRoot = "mock:\\.gvfs";
        private const string TestObjecRoot = "mock:\\.gvfs\\gitObjectCache";

        [TestCase]
        [Category(CategoryConstants.ExceptionExpected)]
        public void CatchesFileNotFoundAfterFileDeleted()
        {
            MockFileSystemWithCallbacks fileSystem = new MockFileSystemWithCallbacks();
            fileSystem.OnFileExists = () => true;
            fileSystem.OnOpenFileStream = (path, fileMode, fileAccess) =>
            {
                if (fileAccess == FileAccess.Write)
                {
                    return new MemoryStream();
                }

                throw new FileNotFoundException();
            };

            MockHttpGitObjects httpObjects = new MockHttpGitObjects();
            using (httpObjects.InputStream = new MemoryStream(System.Text.Encoding.ASCII.GetBytes(ValidTestObjectFileContents)))
            {
                httpObjects.MediaType = GVFSConstants.MediaTypes.LooseObjectMediaType;
                GVFSGitObjects dut = this.CreateTestableGVFSGitObjects(httpObjects, fileSystem);

                dut.TryCopyBlobContentStream(
                    ValidTestObjectFileContents,
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
            fileSystem.OnFileExists = () => true;
            fileSystem.OnOpenFileStream = (path, mode, access) => new MemoryStream();
            MockHttpGitObjects httpObjects = new MockHttpGitObjects();
            using (httpObjects.InputStream = new MemoryStream(System.Text.Encoding.ASCII.GetBytes(ValidTestObjectFileContents)))
            {
                httpObjects.MediaType = GVFSConstants.MediaTypes.LooseObjectMediaType;
                GVFSGitObjects dut = this.CreateTestableGVFSGitObjects(httpObjects, fileSystem);

                dut.TryDownloadAndSaveObject(ValidTestObjectFileContents, GVFSGitObjects.RequestSource.FileStreamCallback)
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
                gitObjects => gitObjects.TryDownloadAndSaveObject("aabbcc", GVFSGitObjects.RequestSource.FileStreamCallback));
        }

        [TestCase]
        [Category(CategoryConstants.ExceptionExpected)]
        public void FailsNullByteLooseObjectsDownloads()
        {
            this.AssertRetryableExceptionOnDownload(
                new MemoryStream(new byte[256]),
                GVFSConstants.MediaTypes.LooseObjectMediaType,
                gitObjects => gitObjects.TryDownloadAndSaveObject("aabbcc", GVFSGitObjects.RequestSource.FileStreamCallback));
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
                fileSystem.OnFileExists = () => false;
                fileSystem.OnOpenFileStream = (path, mode, access) => downloadDestination;

                GVFSGitObjects gitObjects = this.CreateTestableGVFSGitObjects(httpObjects, fileSystem);

                Assert.Throws<RetryableException>(() => download(gitObjects));
                inputStream.Dispose();
            }
        }

        private GVFSGitObjects CreateTestableGVFSGitObjects(MockHttpGitObjects httpObjects, MockFileSystemWithCallbacks fileSystem)
        {
            MockTracer tracer = new MockTracer();
            GVFSEnlistment enlistment = new GVFSEnlistment(TestEnlistmentRoot, "https://fakeRepoUrl", "fakeGitBinPath", gvfsHooksRoot: null, authentication: null);
            enlistment.InitializeCachePathsFromKey(TestLocalCacheRoot, TestObjecRoot);
            GitRepo repo = new GitRepo(tracer, enlistment, fileSystem, () => new MockLibGit2Repo(tracer));

            GVFSContext context = new GVFSContext(tracer, fileSystem, repo, enlistment);
            GVFSGitObjects dut = new GVFSGitObjects(context, httpObjects);
            return dut;
        }

        private string GetDataPath(string fileName)
        {
            string workingDirectory = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            return Path.Combine(workingDirectory, "Data", fileName);
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
    }
}