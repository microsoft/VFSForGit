using RGFS.Common;
using RGFS.Common.FileSystem;
using RGFS.Common.Git;
using RGFS.Common.Http;
using RGFS.Tests.Should;
using RGFS.UnitTests.Category;
using RGFS.UnitTests.Mock;
using RGFS.UnitTests.Mock.Common;
using RGFS.UnitTests.Mock.FileSystem;
using RGFS.UnitTests.Mock.Git;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Reflection;
using System.Threading;

namespace RGFS.UnitTests.Git
{
    [TestFixture]
    public class RGFSGitObjectsTests
    {
        private const string ValidTestObjectFileContents = "421dc4df5e1de427e363b8acd9ddb2d41385dbdf";
        private const string TestEnlistmentRoot = "mock:\\src";

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
                httpObjects.MediaType = RGFSConstants.MediaTypes.LooseObjectMediaType;
                RGFSGitObjects dut = this.CreateTestableRGFSGitObjects(httpObjects, fileSystem);

                dut.TryCopyBlobContentStream(ValidTestObjectFileContents, new CancellationToken(), (stream, length) => Assert.Fail("Should not be able to call copy stream callback"))
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
                httpObjects.MediaType = RGFSConstants.MediaTypes.LooseObjectMediaType;
                RGFSGitObjects dut = this.CreateTestableRGFSGitObjects(httpObjects, fileSystem);

                dut.TryDownloadAndSaveObject(ValidTestObjectFileContents)
                    .ShouldEqual(GitObjects.DownloadAndSaveObjectResult.Success);
            }
        }

        [TestCase]
        [Category(CategoryConstants.ExceptionExpected)]
        public void FailsZeroByteLooseObjectsDownloads()
        {
            this.AssertRetryableExceptionOnDownload(
                new MemoryStream(),
                RGFSConstants.MediaTypes.LooseObjectMediaType,
                gitObjects => gitObjects.TryDownloadAndSaveObject("aabbcc"));
        }

        [TestCase]
        [Category(CategoryConstants.ExceptionExpected)]
        public void FailsNullByteLooseObjectsDownloads()
        {
            this.AssertRetryableExceptionOnDownload(
                new MemoryStream(new byte[256]),
                RGFSConstants.MediaTypes.LooseObjectMediaType,
                gitObjects => gitObjects.TryDownloadAndSaveObject("aabbcc"));
        }

        [TestCase]
        [Category(CategoryConstants.ExceptionExpected)]
        public void FailsZeroBytePackDownloads()
        {
            this.AssertRetryableExceptionOnDownload(
                new MemoryStream(),
                RGFSConstants.MediaTypes.PackFileMediaType,
                gitObjects => gitObjects.TryDownloadCommit("object0"));
        }

        [TestCase]
        [Category(CategoryConstants.ExceptionExpected)]
        public void FailsNullBytePackDownloads()
        {
            this.AssertRetryableExceptionOnDownload(
                new MemoryStream(new byte[256]),
                RGFSConstants.MediaTypes.PackFileMediaType,
                gitObjects => gitObjects.TryDownloadCommit("object0"));
        }

        private void AssertRetryableExceptionOnDownload(
            MemoryStream inputStream,
            string mediaType,
            Action<RGFSGitObjects> download)
        {
            MockHttpGitObjects httpObjects = new MockHttpGitObjects();
            httpObjects.InputStream = inputStream;
            httpObjects.MediaType = mediaType;
            MockFileSystemWithCallbacks fileSystem = new MockFileSystemWithCallbacks();

            using (ReusableMemoryStream downloadDestination = new ReusableMemoryStream(string.Empty))
            {
                fileSystem.OnFileExists = () => false;
                fileSystem.OnOpenFileStream = (path, mode, access) => downloadDestination;

                RGFSGitObjects gitObjects = this.CreateTestableRGFSGitObjects(httpObjects, fileSystem);

                Assert.Throws<RetryableException>(() => download(gitObjects));
                inputStream.Dispose();
            }
        }

        private RGFSGitObjects CreateTestableRGFSGitObjects(MockHttpGitObjects httpObjects, MockFileSystemWithCallbacks fileSystem)
        {
            MockTracer tracer = new MockTracer();
            RGFSEnlistment enlistment = new RGFSEnlistment(TestEnlistmentRoot, "https://fakeRepoUrl", "fakeGitBinPath", rgfsHooksRoot: null);
            GitRepo repo = new GitRepo(tracer, enlistment, fileSystem, () => new MockLibGit2Repo(tracer));

            RGFSContext context = new RGFSContext(tracer, fileSystem, repo, enlistment);
            RGFSGitObjects dut = new RGFSGitObjects(context, httpObjects);
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
                : this(new MockEnlistment())
            {
            }

            private MockHttpGitObjects(MockEnlistment enlistment)
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
                onSuccess(0, new GitEndPointResponseData(HttpStatusCode.OK, this.MediaType, this.InputStream));

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