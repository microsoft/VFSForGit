using FastFetch.Jobs;
using FastFetch.Jobs.Data;
using GVFS.Common.Tracing;
using GVFS.Tests.Should;
using GVFS.UnitTests.Mock.Common;
using GVFS.UnitTests.Mock.Physical.Git;
using NUnit.Framework;
using System.Collections.Concurrent;

namespace GVFS.UnitTests.FastFetch
{
    [TestFixture]
    public class FastFetchTracingTests
    {
        private const string FakeSha = "fakesha";
        private const string FakeShaContents = "fakeshacontents";

        [TestCase]
        public void ErrorsForBatchObjectDownloadJob()
        {
            using (JsonEtwTracer tracer = CreateTracer())
            {
                MockEnlistment enlistment = new MockEnlistment();
                MockHttpGitObjects httpGitObjects = new MockHttpGitObjects(tracer, enlistment);
                MockPhysicalGitObjects gitObjects = new MockPhysicalGitObjects(tracer, enlistment, httpGitObjects);

                BlockingCollection<string> input = new BlockingCollection<string>();
                input.Add(FakeSha);
                input.CompleteAdding();

                BatchObjectDownloadJob dut = new BatchObjectDownloadJob(1, 1, input, new BlockingCollection<string>(), tracer, enlistment, httpGitObjects, gitObjects);
                dut.Start();
                dut.WaitForCompletion();

                string sha;
                input.TryTake(out sha).ShouldEqual(false);

                IndexPackRequest request;
                dut.AvailablePacks.TryTake(out request).ShouldEqual(false);
            }
        }

        [TestCase]
        public void SuccessForBatchObjectDownloadJob()
        {
            using (JsonEtwTracer tracer = CreateTracer())
            {
                MockEnlistment enlistment = new MockEnlistment();
                MockHttpGitObjects httpGitObjects = new MockHttpGitObjects(tracer, enlistment);
                httpGitObjects.AddBlobContent(FakeSha, FakeShaContents);
                MockPhysicalGitObjects gitObjects = new MockPhysicalGitObjects(tracer, enlistment, httpGitObjects);

                BlockingCollection<string> input = new BlockingCollection<string>();
                input.Add(FakeSha);
                input.CompleteAdding();

                BatchObjectDownloadJob dut = new BatchObjectDownloadJob(1, 1, input, new BlockingCollection<string>(), tracer, enlistment, httpGitObjects, gitObjects);
                dut.Start();
                dut.WaitForCompletion();

                string sha;
                input.TryTake(out sha).ShouldEqual(false);
                dut.AvailablePacks.Count.ShouldEqual(0);

                dut.AvailableObjects.Count.ShouldEqual(1);
                string output = dut.AvailableObjects.Take();
                output.ShouldEqual(FakeSha);
            }
        }

        [TestCase]
        public void ErrorsForIndexPackFile()
        {
            using (JsonEtwTracer tracer = CreateTracer())
            {
                MockEnlistment enlistment = new MockEnlistment();
                MockPhysicalGitObjects gitObjects = new MockPhysicalGitObjects(tracer, enlistment, null);

                BlockingCollection<IndexPackRequest> input = new BlockingCollection<IndexPackRequest>();
                BlobDownloadRequest downloadRequest = new BlobDownloadRequest(new string[] { FakeSha });
                input.Add(new IndexPackRequest("mock:\\path\\packFileName", downloadRequest));
                input.CompleteAdding();

                IndexPackJob dut = new IndexPackJob(1, input, new BlockingCollection<string>(), tracer, gitObjects);
                dut.Start();
                dut.WaitForCompletion();
            }
        }

        private static JsonEtwTracer CreateTracer()
        {
            return new JsonEtwTracer("Microsoft-GVFS-Test", "FastFetchTest");
        }
    }
}
