using GVFS.Common;
using GVFS.Common.Database;
using GVFS.Common.NamedPipes;
using GVFS.UnitTests.Category;
using GVFS.UnitTests.Mock.Common;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace GVFS.UnitTests.Common
{
    [TestFixture]
    public class EnlistmentPathDiagnosticsTests
    {
        [TestCase("S", "Success")]
        [TestCase("NotReady", "MountNotReady")]
        [TestCase("InvalidVersion", "InvalidVersion")]
        [TestCase("unknown", "UnexpectedResponse")]
        public void RealLoaderRecordsPipeOutcomeAndDiskFallback(string header, string expected)
        {
            if (header == "NotReady")
            {
                header = NamedPipeMessages.MountNotReadyResult;
            }
            else if (header == "InvalidVersion")
            {
                header = NamedPipeMessages.ModifiedPaths.InvalidVersion;
            }

            SourcePathData paths = new SourcePathData();
            paths.Pipe.Response = new NamedPipeMessages.Message(header, "file\0folder/\0");
            HydrationSummaryDiagnostics diagnostics = new HydrationSummaryDiagnostics();
            using (MockTracer tracer = new MockTracer())
            {
                paths.LoadModifiedPaths(CreateEnlistment(), tracer, diagnostics);
            }

            Assert.AreEqual(expected, diagnostics.ModifiedPathsPipeOutcome.ToString());
            Assert.AreEqual(HydrationDataState.Complete, diagnostics.ModifiedPathsState);
            Assert.AreEqual(expected == "Success" ? HydrationPathSourceOutcome.NotAttempted : HydrationPathSourceOutcome.Success, diagnostics.ModifiedPathsDiskOutcome);
            Assert.AreEqual(expected == "Success" ? "file" : "disk-file", paths.ModifiedFilePaths.Single());
            Assert.AreEqual(3000, paths.Pipe.ConnectTimeoutMs);
            Assert.IsTrue(paths.Pipe.Disposed);
        }

        [TestCase(true, "ConnectTimeout")]
        [TestCase(false, "ConnectFailed")]
        [Category(CategoryConstants.ExceptionExpected)]
        public void ConnectionFailuresRetainCauseAndUseDisk(bool timeout, string expected)
        {
            SourcePathData paths = new SourcePathData();
            paths.Pipe.ConnectError = timeout ? new TimeoutException("private") : new IOException("private");
            HydrationSummaryDiagnostics diagnostics = new HydrationSummaryDiagnostics();
            using (MockTracer tracer = new MockTracer())
            {
                paths.LoadModifiedPaths(CreateEnlistment(), tracer, diagnostics);
            }

            Assert.AreEqual(expected, diagnostics.ModifiedPathsPipeOutcome.ToString());
            Assert.AreEqual(paths.Pipe.ConnectError.GetType().FullName, diagnostics.ModifiedPathsPipeExceptionType);
            Assert.AreEqual(paths.Pipe.ConnectError.HResult, diagnostics.ModifiedPathsPipeExceptionHResult);
            Assert.AreEqual(HydrationPathSourceOutcome.Success, diagnostics.ModifiedPathsDiskOutcome);
            Assert.AreEqual(HydrationDataState.Complete, diagnostics.ModifiedPathsState);
        }

        [TestCase(false, "Failed", 0)]
        [TestCase(true, "Partial", 1)]
        [Category(CategoryConstants.ExceptionExpected)]
        public void DiskFailuresRetainCompletenessAndPartialPaths(bool partial, string state, int count)
        {
            SourcePathData paths = new SourcePathData { FailDisk = true, PartialDisk = partial };
            HydrationSummaryDiagnostics diagnostics = new HydrationSummaryDiagnostics();
            using (MockTracer tracer = new MockTracer())
            {
                paths.LoadModifiedPaths(CreateEnlistment(), tracer, diagnostics);
                Assert.That(tracer.RelatedWarningEvents.Single(), Does.Contain("System.IO.IOException"));
            }

            Assert.AreEqual(state, diagnostics.ModifiedPathsState.ToString());
            Assert.AreEqual(HydrationPathSourceOutcome.Failed, diagnostics.ModifiedPathsDiskOutcome);
            Assert.AreEqual(typeof(IOException).FullName, diagnostics.ModifiedPathsExceptionType);
            Assert.IsNotNull(diagnostics.ModifiedPathsExceptionHResult);
            Assert.AreEqual(count, paths.ModifiedFilePaths.Count);
        }

        [TestCase(true)]
        [TestCase(false)]
        [Category(CategoryConstants.ExceptionExpected)]
        public void PipeSendAndReadExceptionsRetainCause(bool send)
        {
            SourcePathData paths = new SourcePathData();
            IOException error = new IOException("private");
            paths.Pipe.SendError = send ? error : null;
            paths.Pipe.ReadError = send ? null : error;
            HydrationSummaryDiagnostics diagnostics = new HydrationSummaryDiagnostics();
            using (MockTracer tracer = new MockTracer())
            {
                paths.LoadModifiedPaths(CreateEnlistment(), tracer, diagnostics);
            }

            Assert.AreEqual(HydrationPathSourceOutcome.Failed, diagnostics.ModifiedPathsPipeOutcome);
            Assert.AreEqual(typeof(IOException).FullName, diagnostics.ModifiedPathsPipeExceptionType);
            Assert.AreEqual(error.HResult, diagnostics.ModifiedPathsPipeExceptionHResult);
            Assert.AreEqual(HydrationPathSourceOutcome.Success, diagnostics.ModifiedPathsDiskOutcome);
        }

        [TestCase]
        public void RealPlaceholderLoaderClassifiesEachFolderKind()
        {
            SourcePathData paths = new SourcePathData();
            HydrationSummaryDiagnostics diagnostics = new HydrationSummaryDiagnostics();
            paths.LoadPlaceholdersFromDatabase(CreateEnlistment(), diagnostics);
            Assert.AreEqual(1, paths.PlaceholderFilePaths.Count);
            Assert.AreEqual(3, paths.PlaceholderFolderPaths.Count);
            Assert.AreEqual(1, diagnostics.PartialFolderCount);
            Assert.AreEqual(1, diagnostics.ExpandedFolderCount);
            Assert.AreEqual(1, diagnostics.PossibleTombstoneFolderCount);
        }

        private static GVFSEnlistment CreateEnlistment()
        {
            return new MockGVFSEnlistment("mock:\\repo", "mock://repo", "mock:\\git", gitProcess: null);
        }

        private sealed class SourcePathData : EnlistmentPathData
        {
            public MockNamedPipeClient Pipe { get; } = new MockNamedPipeClient
            {
                Response = new NamedPipeMessages.Message("unknown", null),
            };
            public bool FailDisk { get; set; }
            public bool PartialDisk { get; set; }

            protected override NamedPipeClient CreateModifiedPathsPipeClient(GVFSEnlistment enlistment)
            {
                return this.Pipe;
            }

            protected override IEnumerable<string> ReadModifiedPathsFile(string filePath, HydrationSummaryDiagnostics diagnostics)
            {
                if (!this.FailDisk || this.PartialDisk)
                {
                    yield return "disk-file";
                }

                if (this.FailDisk)
                {
                    throw new IOException("mock:\\private\\modified-paths");
                }
            }

            protected override void ReadPlaceholderEntries(GVFSEnlistment enlistment, out List<IPlaceholderData> files, out List<IPlaceholderData> folders)
            {
                files = new List<IPlaceholderData> { new PlaceholderTable.PlaceholderData { Path = "file", PathType = PlaceholderTable.PlaceholderData.PlaceholderType.File } };
                folders = new List<IPlaceholderData>
                {
                    new PlaceholderTable.PlaceholderData { Path = "partial", PathType = PlaceholderTable.PlaceholderData.PlaceholderType.PartialFolder },
                    new PlaceholderTable.PlaceholderData { Path = "expanded", PathType = PlaceholderTable.PlaceholderData.PlaceholderType.ExpandedFolder },
                    new PlaceholderTable.PlaceholderData { Path = "tombstone", PathType = PlaceholderTable.PlaceholderData.PlaceholderType.PossibleTombstoneFolder },
                };
            }
        }
    }
}
