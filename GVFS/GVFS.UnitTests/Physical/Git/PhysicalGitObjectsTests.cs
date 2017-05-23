using GVFS.Common;
using GVFS.Common.Http;
using GVFS.Common.Physical.Git;
using GVFS.Tests.Should;
using GVFS.UnitTests.Category;
using GVFS.UnitTests.Mock.Common;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Reflection;

namespace GVFS.UnitTests.Physical.Git
{
    [TestFixture]
    public class PhysicalGitObjectsTests
    {
        private const string ValidTestObjectFileContents = "421dc4df5e1de427e363b8acd9ddb2d41385dbdf";
        private string tempFolder;

        [OneTimeSetUp]
        public void Setup()
        {
            this.tempFolder = Path.Combine(Environment.CurrentDirectory, Path.GetRandomFileName());
            string objectsFolder = Path.Combine(
                this.tempFolder,
                GVFSConstants.WorkingDirectoryRootName,
                GVFSConstants.DotGit.Objects.Pack.Root);
            Directory.CreateDirectory(objectsFolder);
        }

        [OneTimeTearDown]
        public void Teardown()
        {
            Directory.Delete(this.tempFolder, true);
        }

        [TestCase]
        public void SucceedsForNormalLookingLooseObjectDownloads()
        {
            MockHttpGitObjects httpObjects = new MockHttpGitObjects();
            using (httpObjects.InputStream = new MemoryStream(System.Text.Encoding.ASCII.GetBytes(ValidTestObjectFileContents)))
            {
                httpObjects.MediaType = GVFSConstants.MediaTypes.LooseObjectMediaType;
                GVFSGitObjects dut = this.CreateTestableGVFSGitObjects(httpObjects);

                dut.TryDownloadAndSaveObject(ValidTestObjectFileContents.Substring(0, 2), ValidTestObjectFileContents.Substring(2))
                    .ShouldEqual(true);
            }
        }

        [TestCase]
        [Category(CategoryConstants.ExceptionExpected)]
        public void FailsZeroByteLooseObjectsDownloads()
        {
            MockHttpGitObjects httpObjects = new MockHttpGitObjects();
            using (httpObjects.InputStream = new MemoryStream())
            {
                httpObjects.MediaType = GVFSConstants.MediaTypes.LooseObjectMediaType;
                GVFSGitObjects dut = this.CreateTestableGVFSGitObjects(httpObjects);

                Assert.Throws<RetryableException>(() => dut.TryDownloadAndSaveObject("aa", "bbcc"));
            }
        }

        [TestCase]
        [Category(CategoryConstants.ExceptionExpected)]
        public void FailsNullByteLooseObjectsDownloads()
        {
            MockHttpGitObjects httpObjects = new MockHttpGitObjects();
            using (httpObjects.InputStream = new MemoryStream(new byte[256]))
            {
                httpObjects.MediaType = GVFSConstants.MediaTypes.LooseObjectMediaType;
                GVFSGitObjects dut = this.CreateTestableGVFSGitObjects(httpObjects);

                Assert.Throws<RetryableException>(() => dut.TryDownloadAndSaveObject("aa", "bbcc"));
            }
        }

        [TestCase]
        [Category(CategoryConstants.ExceptionExpected)]
        public void FailsZeroBytePackDownloads()
        {
            MockHttpGitObjects httpObjects = new MockHttpGitObjects();
            using (httpObjects.InputStream = new MemoryStream())
            {
                httpObjects.MediaType = GVFSConstants.MediaTypes.PackFileMediaType;
                GVFSGitObjects dut = this.CreateTestableGVFSGitObjects(httpObjects);

                Assert.Throws<RetryableException>(() => dut.TryDownloadAndSaveCommits(new[] { "object0", "object1" }, 0));
            }
        }

        [TestCase]
        [Category(CategoryConstants.ExceptionExpected)]
        public void FailsNullBytePackDownloads()
        {
            MockHttpGitObjects httpObjects = new MockHttpGitObjects();
            using (httpObjects.InputStream = new MemoryStream(new byte[256]))
            {
                httpObjects.MediaType = GVFSConstants.MediaTypes.PackFileMediaType;
                GVFSGitObjects dut = this.CreateTestableGVFSGitObjects(httpObjects);

                Assert.Throws<RetryableException>(() => dut.TryDownloadAndSaveCommits(new[] { "object0", "object1" }, 0));
            }
        }

        private GVFSGitObjects CreateTestableGVFSGitObjects(MockHttpGitObjects httpObjects)
        {
            MockTracer tracer = new MockTracer();
            GVFSEnlistment enlistment = new GVFSEnlistment(this.tempFolder, "notused", "notused", "notused", null);

            GVFSContext context = new GVFSContext(tracer, null, null, enlistment);
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
            public MockHttpGitObjects() : base(null, new MockEnlistment(), 1)
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
                Func<int, GitEndPointResponseData, RetryWrapper<GitObjectTaskResult>.CallbackResult> onSuccess,
                Action<RetryWrapper<GitObjectTaskResult>.ErrorEventArgs> onFailure)
            {
                return this.TryDownloadObjects(new[] { objectId }, 0, onSuccess, onFailure, false);
            }

            public override RetryWrapper<GitObjectTaskResult>.InvocationResult TryDownloadObjects(
                IEnumerable<string> objectIds,
                int commitDepth,
                Func<int, GitEndPointResponseData, RetryWrapper<GitObjectTaskResult>.CallbackResult> onSuccess,
                Action<RetryWrapper<GitObjectTaskResult>.ErrorEventArgs> onFailure,
                bool preferBatchedLooseObjects)
            {
                onSuccess(0, new GitEndPointResponseData(HttpStatusCode.OK, this.MediaType, this.InputStream));

                GitObjectTaskResult result = new GitObjectTaskResult(true);
                return new RetryWrapper<GitObjectTaskResult>.InvocationResult(0, true, result);
            }

            public override List<GitObjectSize> QueryForFileSizes(IEnumerable<string> objectIds)
            {
                throw new NotImplementedException();
            }
        }
    }
}