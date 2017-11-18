using FastFetch.Jobs;
using RGFS.Tests.Should;
using RGFS.UnitTests.Category;
using RGFS.UnitTests.Mock.Common;
using RGFS.UnitTests.Mock.Git;
using NUnit.Framework;
using System;
using System.Collections.Concurrent;

namespace RGFS.UnitTests.FastFetch
{
    [TestFixture]
    public class BatchObjectDownloadJobTests
    {
        private const int MaxParallel = 1;
        private const int ChunkSize = 2;

        // This test confirms that if two objects are downloaded at the same time and the second
        // object's download fails, the first object should not be downloaded again
        [TestCase]
        [Category(CategoryConstants.ExceptionExpected)]
        public void OnlyRequestsObjectsNotDownloaded()
        {
            string obj1Sha = new string('1', 40);
            string obj2Sha = new string('2', 40);

            BlockingCollection<string> input = new BlockingCollection<string>();
            input.Add(obj1Sha);
            input.Add(obj2Sha);
            input.CompleteAdding();

            int obj1Count = 0;
            int obj2Count = 0;

            Func<string, string> objectResolver = (oid) =>
            {
                if (oid.Equals(obj1Sha))
                {
                    obj1Count++;
                    return "Object1Contents";
                }

                if (oid.Equals(obj2Sha) && obj2Count++ == 1)
                {
                    return "Object2Contents";
                }

                return null;
            };

            BlockingCollection<string> output = new BlockingCollection<string>();
            MockTracer tracer = new MockTracer();
            MockEnlistment enlistment = new MockEnlistment();
            MockBatchHttpGitObjects httpObjects = new MockBatchHttpGitObjects(tracer, enlistment, objectResolver);

            BatchObjectDownloadJob dut = new BatchObjectDownloadJob(
                MaxParallel,
                ChunkSize,
                input,
                output,
                tracer,
                enlistment,
                httpObjects,
                new MockPhysicalGitObjects(tracer, null, enlistment, httpObjects));

            dut.Start();
            dut.WaitForCompletion();

            input.Count.ShouldEqual(0);
            output.Count.ShouldEqual(2);
            output.Take().ShouldEqual(obj1Sha);
            output.Take().ShouldEqual(obj2Sha);
            obj1Count.ShouldEqual(1);
            obj2Count.ShouldEqual(2);
        }
    }
}