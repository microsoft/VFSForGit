using GVFS.Common;
using GVFS.GVFlt;
using GVFS.Tests.Should;
using GVFS.UnitTests.Category;
using GVFS.UnitTests.Mock.Common;
using GVFS.UnitTests.Mock.Git;
using GVFS.UnitTests.Mock.GvFlt;
using GVFS.UnitTests.Mock.GvFlt.BlobSize;
using GVFS.UnitTests.Mock.GVFS.GvFlt;
using GVFS.UnitTests.Mock.GVFS.GvFlt.DotGit;
using GVFS.UnitTests.Virtual;
using NUnit.Framework;
using ProjFS;
using System;
using System.Threading.Tasks;

namespace GVFS.UnitTests.GVFlt.DotGit
{
    public class GVFltCallbacksTests : TestsWithCommonRepo
    {
        [TestCase]
        public void OnStartDirectoryEnumerationReturnsPendingWhenResultsNotInMemory()
        {
            using (MockVirtualizationInstance mockGvFlt = new MockVirtualizationInstance())
            using (MockGitIndexProjection gitIndexProjection = new MockGitIndexProjection(new[] { "test.txt" }))
            {
                GVFltCallbacks callbacks = new GVFltCallbacks(
                    this.Repo.Context,
                    this.Repo.GitObjects,
                    RepoMetadata.Instance,
                    new MockBlobSizes(),
                    gvflt: mockGvFlt,
                    gitIndexProjection: gitIndexProjection,
                    reliableBackgroundOperations: new MockReliableBackgroundOperations());

                string error;
                callbacks.TryStart(out error).ShouldEqual(true);

                Guid enumerationGuid = Guid.NewGuid();
                gitIndexProjection.EnumerationInMemory = false;
                mockGvFlt.OnStartDirectoryEnumeration(1, enumerationGuid, "test").ShouldEqual(HResult.Pending);
                mockGvFlt.WaitForCompletionStatus().ShouldEqual(HResult.Ok);
                mockGvFlt.OnEndDirectoryEnumeration(enumerationGuid).ShouldEqual(HResult.Ok);
                callbacks.Stop();
            }
        }

        [TestCase]
        public void OnStartDirectoryEnumerationReturnsSuccessWhenResultsInMemory()
        {
            using (MockVirtualizationInstance mockGvFlt = new MockVirtualizationInstance())
            using (MockGitIndexProjection gitIndexProjection = new MockGitIndexProjection(new[] { "test" }))
            {
                GVFltCallbacks callbacks = new GVFltCallbacks(
                    this.Repo.Context,
                    this.Repo.GitObjects,
                    RepoMetadata.Instance,
                    new MockBlobSizes(),
                    gvflt: mockGvFlt,
                    gitIndexProjection: gitIndexProjection,
                    reliableBackgroundOperations: new MockReliableBackgroundOperations());

                string error;
                callbacks.TryStart(out error).ShouldEqual(true);

                Guid enumerationGuid = Guid.NewGuid();
                gitIndexProjection.EnumerationInMemory = true;
                mockGvFlt.OnStartDirectoryEnumeration(1, enumerationGuid, "test").ShouldEqual(HResult.Ok);
                mockGvFlt.OnEndDirectoryEnumeration(enumerationGuid).ShouldEqual(HResult.Ok);
                callbacks.Stop();
            }
        }

        [TestCase]
        public void GetPlaceholderInformationHandlerPathNotProjected()
        {
            using (MockVirtualizationInstance mockGvFlt = new MockVirtualizationInstance())
            using (MockGitIndexProjection gitIndexProjection = new MockGitIndexProjection(new[] { "test.txt" }))
            {
                GVFltCallbacks callbacks = new GVFltCallbacks(
                    this.Repo.Context,
                    this.Repo.GitObjects,
                    RepoMetadata.Instance,
                    new MockBlobSizes(),
                    gvflt: mockGvFlt,
                    gitIndexProjection: gitIndexProjection,
                    reliableBackgroundOperations: new MockReliableBackgroundOperations());

                string error;
                callbacks.TryStart(out error).ShouldEqual(true);

                mockGvFlt.OnGetPlaceholderInformation(1, "doesNotExist", 0, 0, 0, 0, 1, "UnitTests").ShouldEqual(HResult.FileNotFound);

                callbacks.Stop();
            }
        }

        [TestCase]
        public void GetPlaceholderInformationHandlerPathProjected()
        {
            using (MockVirtualizationInstance mockGvFlt = new MockVirtualizationInstance())
            using (MockGitIndexProjection gitIndexProjection = new MockGitIndexProjection(new[] { "test.txt" }))
            {
                GVFltCallbacks callbacks = new GVFltCallbacks(
                    this.Repo.Context,
                    this.Repo.GitObjects,
                    RepoMetadata.Instance,
                    new MockBlobSizes(),
                    gvflt: mockGvFlt,
                    gitIndexProjection: gitIndexProjection,
                    reliableBackgroundOperations: new MockReliableBackgroundOperations());

                string error;
                callbacks.TryStart(out error).ShouldEqual(true);

                mockGvFlt.OnGetPlaceholderInformation(1, "test.txt", 0, 0, 0, 0, 1, "UnitTests").ShouldEqual(HResult.Pending);
                mockGvFlt.WaitForCompletionStatus().ShouldEqual(HResult.Ok);
                mockGvFlt.CreatedPlaceholders.ShouldContain(entry => entry == "test.txt");
                gitIndexProjection.PlaceholdersCreated.ShouldContain(entry => entry == "test.txt");

                callbacks.Stop();
            }
        }

        [TestCase]
        public void GetPlaceholderInformationHandlerCancelledBeforeSchedulingAsync()
        {
            using (MockVirtualizationInstance mockGvFlt = new MockVirtualizationInstance())
            using (MockGitIndexProjection gitIndexProjection = new MockGitIndexProjection(new[] { "test.txt" }))
            {
                GVFltCallbacks callbacks = new GVFltCallbacks(
                    this.Repo.Context,
                    this.Repo.GitObjects,
                    RepoMetadata.Instance,
                    new MockBlobSizes(),
                    gvflt: mockGvFlt,
                    gitIndexProjection: gitIndexProjection,
                    reliableBackgroundOperations: new MockReliableBackgroundOperations());

                string error;
                callbacks.TryStart(out error).ShouldEqual(true);

                gitIndexProjection.BlockIsPathProjected(willWaitForRequest: true);

                Task.Run(() =>
                {
                    // Wait for OnGetPlaceholderInformation to call IsPathProjected and then while it's blocked there
                    // call OnCancelCommand
                    gitIndexProjection.WaitForIsPathProjected();
                    mockGvFlt.OnCancelCommand(1);
                    gitIndexProjection.UnblockIsPathProjected();
                });

                mockGvFlt.OnGetPlaceholderInformation(1, "test.txt", 0, 0, 0, 0, 1, "UnitTests").ShouldEqual(HResult.Pending);

                // Cancelling before GetPlaceholderInformation has registered the command results in placeholders being created
                mockGvFlt.WaitForPlaceholderCreate();
                gitIndexProjection.WaitForPlaceholderCreate();
                mockGvFlt.CreatedPlaceholders.ShouldContain(entry => entry == "test.txt");
                gitIndexProjection.PlaceholdersCreated.ShouldContain(entry => entry == "test.txt");

                callbacks.Stop();
            }
        }

        [TestCase]
        public void GetPlaceholderInformationHandlerCancelledDuringAsyncCallback()
        {
            using (MockVirtualizationInstance mockGvFlt = new MockVirtualizationInstance())
            using (MockGitIndexProjection gitIndexProjection = new MockGitIndexProjection(new[] { "test.txt" }))
            {
                GVFltCallbacks callbacks = new GVFltCallbacks(
                    this.Repo.Context,
                    this.Repo.GitObjects,
                    RepoMetadata.Instance,
                    new MockBlobSizes(),
                    gvflt: mockGvFlt,
                    gitIndexProjection: gitIndexProjection,
                    reliableBackgroundOperations: new MockReliableBackgroundOperations());

                string error;
                callbacks.TryStart(out error).ShouldEqual(true);

                gitIndexProjection.BlockGetProjectedFileInfo(willWaitForRequest: true);
                mockGvFlt.OnGetPlaceholderInformation(1, "test.txt", 0, 0, 0, 0, 1, "UnitTests").ShouldEqual(HResult.Pending);
                gitIndexProjection.WaitForGetProjectedFileInfo();
                mockGvFlt.OnCancelCommand(1);
                gitIndexProjection.UnblockGetProjectedFileInfo();

                // Cancelling in the middle of GetPlaceholderInformation still allows it to create placeholders when the cancellation does not
                // interrupt network requests                
                mockGvFlt.WaitForPlaceholderCreate();
                gitIndexProjection.WaitForPlaceholderCreate();
                mockGvFlt.CreatedPlaceholders.ShouldContain(entry => entry == "test.txt");
                gitIndexProjection.PlaceholdersCreated.ShouldContain(entry => entry == "test.txt");

                callbacks.Stop();
            }
        }

        [TestCase]
        [Category(CategoryConstants.ExceptionExpected)]
        public void GetPlaceholderInformationHandlerCancelledDuringNetworkRequest()
        {
            using (MockVirtualizationInstance mockGvFlt = new MockVirtualizationInstance())
            using (MockGitIndexProjection gitIndexProjection = new MockGitIndexProjection(new[] { "test.txt" }))
            {
                GVFltCallbacks callbacks = new GVFltCallbacks(
                    this.Repo.Context,
                    this.Repo.GitObjects,
                    RepoMetadata.Instance,
                    new MockBlobSizes(),
                    gvflt: mockGvFlt,
                    gitIndexProjection: gitIndexProjection,
                    reliableBackgroundOperations: new MockReliableBackgroundOperations());

                string error;
                callbacks.TryStart(out error).ShouldEqual(true);

                MockTracer mockTracker = this.Repo.Context.Tracer as MockTracer;
                mockTracker.WaitRelatedEventName = "GVFltGetPlaceholderInformationAsyncHandler_GetProjectedGVFltFileInfoAndShaCancelled";
                gitIndexProjection.ThrowOperationCanceledExceptionOnProjectionRequest = true;
                mockGvFlt.OnGetPlaceholderInformation(1, "test.txt", 0, 0, 0, 0, 1, "UnitTests").ShouldEqual(HResult.Pending);

                // Cancelling in the middle of GetPlaceholderInformation in the middle of a network request should not result in placeholder
                // getting created
                mockTracker.WaitForRelatedEvent();
                mockGvFlt.CreatedPlaceholders.ShouldNotContain(entry => entry == "test.txt");
                gitIndexProjection.PlaceholdersCreated.ShouldNotContain(entry => entry == "test.txt");

                callbacks.Stop();
            }
        }

        [TestCase]
        public void OnGetFileStreamReturnsInternalErrorWhenOffsetNonZero()
        {
            using (MockVirtualizationInstance mockGvFlt = new MockVirtualizationInstance())
            using (MockGitIndexProjection gitIndexProjection = new MockGitIndexProjection(new[] { "test.txt" }))
            {
                GVFltCallbacks callbacks = new GVFltCallbacks(
                    this.Repo.Context,
                    this.Repo.GitObjects,
                    RepoMetadata.Instance,
                    new MockBlobSizes(),
                    gvflt: mockGvFlt,
                    gitIndexProjection: gitIndexProjection,
                    reliableBackgroundOperations: new MockReliableBackgroundOperations());

                string error;
                callbacks.TryStart(out error).ShouldEqual(true);

                Guid enumerationGuid = Guid.NewGuid();

                byte[] contentId = GVFltCallbacks.ConvertShaToContentId("0123456789012345678901234567890123456789");
                byte[] placeholderVersion = GVFltCallbacks.GetPlaceholderVersionId();

                mockGvFlt.OnGetFileStream(
                    commandId: 1,
                    relativePath: "test.txt",
                    byteOffset: 10,
                    length: 100,
                    streamGuid: Guid.NewGuid(),
                    contentId: contentId,
                    providerId: placeholderVersion,
                    triggeringProcessId: 2,
                    triggeringProcessImageFileName: "UnitTest").ShouldEqual(HResult.InternalError);

                callbacks.Stop();
            }
        }

        [TestCase]
        public void OnGetFileStreamReturnsInternalErrorWhenPlaceholderVersionDoesNotMatchExpected()
        {
            using (MockVirtualizationInstance mockGvFlt = new MockVirtualizationInstance())
            using (MockGitIndexProjection gitIndexProjection = new MockGitIndexProjection(new[] { "test.txt" }))
            {
                GVFltCallbacks callbacks = new GVFltCallbacks(
                    this.Repo.Context,
                    this.Repo.GitObjects,
                    RepoMetadata.Instance,
                    new MockBlobSizes(),
                    gvflt: mockGvFlt,
                    gitIndexProjection: gitIndexProjection,
                    reliableBackgroundOperations: new MockReliableBackgroundOperations());

                string error;
                callbacks.TryStart(out error).ShouldEqual(true);

                Guid enumerationGuid = Guid.NewGuid();

                byte[] contentId = GVFltCallbacks.ConvertShaToContentId("0123456789012345678901234567890123456789");
                byte[] epochId = new byte[] { GVFltCallbacks.PlaceholderVersion + 1 };

                mockGvFlt.OnGetFileStream(
                    commandId: 1,
                    relativePath: "test.txt",
                    byteOffset: 0,
                    length: 100,
                    streamGuid: Guid.NewGuid(),
                    contentId: contentId,
                    providerId: epochId,
                    triggeringProcessId: 2,
                    triggeringProcessImageFileName: "UnitTest").ShouldEqual(HResult.InternalError);

                callbacks.Stop();
            }
        }

        [TestCase]
        public void OnGetFileStreamReturnsPendingAndCompletesWithSuccessWhenNoFailures()
        {
            using (MockVirtualizationInstance mockGvFlt = new MockVirtualizationInstance())
            using (MockGitIndexProjection gitIndexProjection = new MockGitIndexProjection(new[] { "test.txt" }))
            {
                GVFltCallbacks callbacks = new GVFltCallbacks(
                    this.Repo.Context,
                    this.Repo.GitObjects,
                    RepoMetadata.Instance,
                    new MockBlobSizes(),
                    gvflt: mockGvFlt,
                    gitIndexProjection: gitIndexProjection,
                    reliableBackgroundOperations: new MockReliableBackgroundOperations());

                string error;
                callbacks.TryStart(out error).ShouldEqual(true);

                Guid enumerationGuid = Guid.NewGuid();

                byte[] contentId = GVFltCallbacks.ConvertShaToContentId("0123456789012345678901234567890123456789");
                byte[] placeholderVersion = GVFltCallbacks.GetPlaceholderVersionId();

                uint fileLength = 100;
                MockGVFSGitObjects mockGVFSGitObjects = this.Repo.GitObjects as MockGVFSGitObjects;
                mockGVFSGitObjects.FileLength = fileLength;
                mockGvFlt.WriteFileReturnResult = HResult.Ok;

                mockGvFlt.OnGetFileStream(
                    commandId: 1,
                    relativePath: "test.txt",
                    byteOffset: 0,
                    length: fileLength,
                    streamGuid: Guid.NewGuid(),
                    contentId: contentId,
                    providerId: placeholderVersion,
                    triggeringProcessId: 2,
                    triggeringProcessImageFileName: "UnitTest").ShouldEqual(HResult.Pending);

                mockGvFlt.WaitForCompletionStatus().ShouldEqual(HResult.Ok);

                callbacks.Stop();
            }
        }

        [TestCase]
        [Category(CategoryConstants.ExceptionExpected)]
        public void OnGetFileStreamHandlesTryCopyBlobContentStreamThrowingOperationCanceled()
        {
            using (MockVirtualizationInstance mockGvFlt = new MockVirtualizationInstance())
            using (MockGitIndexProjection gitIndexProjection = new MockGitIndexProjection(new[] { "test.txt" }))
            {
                GVFltCallbacks callbacks = new GVFltCallbacks(
                    this.Repo.Context,
                    this.Repo.GitObjects,
                    RepoMetadata.Instance,
                    new MockBlobSizes(),
                    gvflt: mockGvFlt,
                    gitIndexProjection: gitIndexProjection,
                    reliableBackgroundOperations: new MockReliableBackgroundOperations());

                string error;
                callbacks.TryStart(out error).ShouldEqual(true);

                Guid enumerationGuid = Guid.NewGuid();

                byte[] contentId = GVFltCallbacks.ConvertShaToContentId("0123456789012345678901234567890123456789");
                byte[] placeholderVersion = GVFltCallbacks.GetPlaceholderVersionId();

                MockGVFSGitObjects mockGVFSGitObjects = this.Repo.GitObjects as MockGVFSGitObjects;

                MockTracer mockTracker = this.Repo.Context.Tracer as MockTracer;
                mockTracker.WaitRelatedEventName = "GVFltGetFileStreamHandlerAsyncHandler_OperationCancelled";
                mockGVFSGitObjects.CancelTryCopyBlobContentStream = true;

                mockGvFlt.OnGetFileStream(
                    commandId: 1,
                    relativePath: "test.txt",
                    byteOffset: 0,
                    length: 100,
                    streamGuid: Guid.NewGuid(),
                    contentId: contentId,
                    providerId: placeholderVersion,
                    triggeringProcessId: 2,
                    triggeringProcessImageFileName: "UnitTest").ShouldEqual(HResult.Pending);

                mockTracker.WaitForRelatedEvent();

                callbacks.Stop();
            }
        }

        [TestCase]
        [Category(CategoryConstants.ExceptionExpected)]
        public void OnGetFileStreamHandlesCancellationDuringWriteAction()
        {
            using (MockVirtualizationInstance mockGvFlt = new MockVirtualizationInstance())
            using (MockGitIndexProjection gitIndexProjection = new MockGitIndexProjection(new[] { "test.txt" }))
            {
                GVFltCallbacks callbacks = new GVFltCallbacks(
                    this.Repo.Context,
                    this.Repo.GitObjects,
                    RepoMetadata.Instance,
                    new MockBlobSizes(),
                    gvflt: mockGvFlt,
                    gitIndexProjection: gitIndexProjection,
                    reliableBackgroundOperations: new MockReliableBackgroundOperations());

                string error;
                callbacks.TryStart(out error).ShouldEqual(true);

                Guid enumerationGuid = Guid.NewGuid();

                byte[] contentId = GVFltCallbacks.ConvertShaToContentId("0123456789012345678901234567890123456789");
                byte[] placeholderVersion = GVFltCallbacks.GetPlaceholderVersionId();

                uint fileLength = 100;
                MockGVFSGitObjects mockGVFSGitObjects = this.Repo.GitObjects as MockGVFSGitObjects;
                mockGVFSGitObjects.FileLength = fileLength;

                MockTracer mockTracker = this.Repo.Context.Tracer as MockTracer;
                mockTracker.WaitRelatedEventName = "GVFltGetFileStreamHandlerAsyncHandler_OperationCancelled";

                mockGvFlt.BlockCreateWriteBuffer(willWaitForRequest: true);
                mockGvFlt.OnGetFileStream(
                    commandId: 1,
                    relativePath: "test.txt",
                    byteOffset: 0,
                    length: fileLength,
                    streamGuid: Guid.NewGuid(),
                    contentId: contentId,
                    providerId: placeholderVersion,
                    triggeringProcessId: 2,
                    triggeringProcessImageFileName: "UnitTest").ShouldEqual(HResult.Pending);

                mockGvFlt.WaitForCreateWriteBuffer();
                mockGvFlt.OnCancelCommand(1);
                mockGvFlt.UnblockCreateWriteBuffer();
                mockTracker.WaitForRelatedEvent();

                callbacks.Stop();
            }
        }

        [TestCase]
        [Category(CategoryConstants.ExceptionExpected)]
        public void OnGetFileStreamHandlesGvWriteFailure()
        {
            using (MockVirtualizationInstance mockGvFlt = new MockVirtualizationInstance())
            using (MockGitIndexProjection gitIndexProjection = new MockGitIndexProjection(new[] { "test.txt" }))
            {
                GVFltCallbacks callbacks = new GVFltCallbacks(
                    this.Repo.Context,
                    this.Repo.GitObjects,
                    RepoMetadata.Instance,
                    new MockBlobSizes(),
                    gvflt: mockGvFlt,
                    gitIndexProjection: gitIndexProjection,
                    reliableBackgroundOperations: new MockReliableBackgroundOperations());

                string error;
                callbacks.TryStart(out error).ShouldEqual(true);

                Guid enumerationGuid = Guid.NewGuid();

                byte[] contentId = GVFltCallbacks.ConvertShaToContentId("0123456789012345678901234567890123456789");
                byte[] placeholderVersion = GVFltCallbacks.GetPlaceholderVersionId();

                uint fileLength = 100;
                MockGVFSGitObjects mockGVFSGitObjects = this.Repo.GitObjects as MockGVFSGitObjects;
                mockGVFSGitObjects.FileLength = fileLength;

                MockTracer mockTracker = this.Repo.Context.Tracer as MockTracer;
                mockTracker.WaitRelatedEventName = "GVFltGetFileStreamHandlerAsyncHandler_OperationCancelled";

                mockGvFlt.WriteFileReturnResult = HResult.InternalError;
                mockGvFlt.OnGetFileStream(
                    commandId: 1,
                    relativePath: "test.txt",
                    byteOffset: 0,
                    length: fileLength,
                    streamGuid: Guid.NewGuid(),
                    contentId: contentId,
                    providerId: placeholderVersion,
                    triggeringProcessId: 2,
                    triggeringProcessImageFileName: "UnitTest").ShouldEqual(HResult.Pending);

                mockGvFlt.WaitForCompletionStatus().ShouldEqual(mockGvFlt.WriteFileReturnResult);

                callbacks.Stop();
            }
        }
    }
}