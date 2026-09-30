using GVFS.Common;
using GVFS.Common.Prefetch.Git;
using GVFS.Common.Prefetch.Pipeline;
using GVFS.Common.Tracing;
using GVFS.Tests.Should;
using GVFS.UnitTests.Mock.Common;
using GVFS.UnitTests.Mock.FileSystem;
using NUnit.Framework;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;

namespace GVFS.UnitTests.Prefetch
{
    [TestFixture]
    public class HydrateFilesStageTests
    {
        private const string WorkingDirectoryRoot = "mock:\\repo";

        private static readonly int SharingViolationHResult = unchecked((int)0x80070000) | 32;
        private static readonly int AccessDeniedHResult = unchecked((int)0x80070000) | 5;

        private MockPlatformFileSystem mockFileSystem;

        [SetUp]
        public void SetUp()
        {
            // GVFSPlatform.Instance.FileSystem is a process-wide singleton (registered once by
            // GVFS.UnitTests.Setup), so scripting it per-test and resetting afterward avoids
            // leaking one test's hydration behavior into another.
            this.mockFileSystem = GVFSPlatform.Instance.FileSystem as MockPlatformFileSystem;
            this.mockFileSystem.ShouldNotBeNull();
        }

        [TearDown]
        public void TearDown()
        {
            this.mockFileSystem.HydrateFileImplementation = null;
        }

        [TestCase]
        public void AllFilesHydrateSuccessfully()
        {
            this.mockFileSystem.HydrateFileImplementation = (path, buffer) => (true, null);

            MockTracer tracer = new MockTracer();
            HydrateFilesStage dut = this.CreateStage(
                tracer,
                out ConcurrentDictionary<string, HashSet<PathWithMode>> blobIdToPaths,
                out BlockingCollection<string> availableBlobs);

            blobIdToPaths["sha1"] = new HashSet<PathWithMode> { new PathWithMode("file1.txt", 0) };
            blobIdToPaths["sha2"] = new HashSet<PathWithMode> { new PathWithMode("file2.txt", 0) };
            availableBlobs.Add("sha1");
            availableBlobs.Add("sha2");
            availableBlobs.CompleteAdding();

            dut.Start();
            dut.WaitForCompletion();

            dut.ReadFileCount.ShouldEqual(2);
            dut.HasFailures.ShouldBeFalse();

            MockTracer activity = tracer.StartActivityTracer;
            activity.ShouldNotBeNull();
            activity.RelatedErrorEvents.Count.ShouldEqual(0);

            EventMetadataShouldNotContainKey(activity.StopMetadata[0], "FailureSignatureCounts");
        }

        [TestCase]
        public void FailedHydrationsAreReportedPerFileAndAggregatedInStopSummary()
        {
            this.mockFileSystem.HydrateFileImplementation = (path, buffer) =>
            {
                if (path.Contains("unauthorized"))
                {
                    UnauthorizedAccessException ex = new UnauthorizedAccessException("denied");
                    SetHResult(ex, AccessDeniedHResult);
                    return (false, ex);
                }

                return (false, new IOException("sharing violation", SharingViolationHResult));
            };

            MockTracer tracer = new MockTracer();
            HydrateFilesStage dut = this.CreateStage(
                tracer,
                out ConcurrentDictionary<string, HashSet<PathWithMode>> blobIdToPaths,
                out BlockingCollection<string> availableBlobs);

            blobIdToPaths["sha1"] = new HashSet<PathWithMode> { new PathWithMode("locked1.txt", 0) };
            blobIdToPaths["sha2"] = new HashSet<PathWithMode> { new PathWithMode("locked2.txt", 0) };
            blobIdToPaths["sha3"] = new HashSet<PathWithMode> { new PathWithMode("unauthorized.txt", 0) };
            availableBlobs.Add("sha1");
            availableBlobs.Add("sha2");
            availableBlobs.Add("sha3");
            availableBlobs.CompleteAdding();

            dut.Start();
            dut.WaitForCompletion();

            dut.ReadFileCount.ShouldEqual(0);
            dut.HasFailures.ShouldBeTrue();

            MockTracer activity = tracer.StartActivityTracer;
            activity.ShouldNotBeNull();

            // One RelatedError per failed file — the original incident's core ask (per-file
            // diagnostic identity) regardless of how the aggregate summary below is formatted.
            activity.RelatedErrorEvents.Count.ShouldEqual(3);

            EventMetadata stopMetadata = activity.StopMetadata[0];
            stopMetadata["FilesRead"].ShouldEqual(0);
            stopMetadata["Failures"].ShouldEqual(3);

            string signatureCounts = stopMetadata["FailureSignatureCounts"] as string;
            signatureCounts.ShouldNotBeNull();
            signatureCounts.ShouldEqual("IOException(Win32=32)=2;UnauthorizedAccessException(Win32=5)=1");
        }

        private HydrateFilesStage CreateStage(
            MockTracer tracer,
            out ConcurrentDictionary<string, HashSet<PathWithMode>> blobIdToPaths,
            out BlockingCollection<string> availableBlobs)
        {
            blobIdToPaths = new ConcurrentDictionary<string, HashSet<PathWithMode>>();
            availableBlobs = new BlockingCollection<string>();

            return new HydrateFilesStage(
                maxThreads: 1,
                workingDirectoryRoot: WorkingDirectoryRoot,
                blobIdToPaths: blobIdToPaths,
                availableBlobs: availableBlobs,
                tracer: tracer);
        }

        private static void SetHResult(Exception exception, int hResult)
        {
            typeof(Exception)
                .GetProperty(nameof(Exception.HResult))
                .SetValue(exception, hResult);
        }

        private static void EventMetadataShouldNotContainKey(EventMetadata metadata, string key)
        {
            metadata.ContainsKey(key).ShouldBeFalse();
        }
    }
}
