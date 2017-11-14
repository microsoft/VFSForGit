using GVFS.Common;
using GVFS.GVFlt;
using GVFS.Tests.Should;
using GVFS.UnitTests.Category;
using GVFS.UnitTests.Mock.Common;
using GVFS.UnitTests.Mock.Git;
using GVFS.UnitTests.Mock.GvFlt;
using GVFS.UnitTests.Mock.GVFS.GvFlt;
using GVFS.UnitTests.Mock.GVFS.GvFlt.DotGit;
using GVFS.UnitTests.Virtual;
using GvLib;
using NUnit.Framework;
using System;
using System.Threading.Tasks;

namespace GVFS.UnitTests.GVFlt.DotGit
{
    public class GVFltCallbacksTests : TestsWithCommonRepo
    {
        [TestCase]
        public void CannotDeleteIndexOrPacks()
        {
            GVFltCallbacks.DoesPathAllowDelete(string.Empty).ShouldEqual(true);

            GVFltCallbacks.DoesPathAllowDelete(@".git\index").ShouldEqual(false);
            GVFltCallbacks.DoesPathAllowDelete(@".git\INDEX").ShouldEqual(false);

            GVFltCallbacks.DoesPathAllowDelete(@".git\index.lock").ShouldEqual(true);
            GVFltCallbacks.DoesPathAllowDelete(@".git\INDEX.lock").ShouldEqual(true);
            GVFltCallbacks.DoesPathAllowDelete(@".git\objects\pack").ShouldEqual(true);
            GVFltCallbacks.DoesPathAllowDelete(@".git\objects\pack-temp").ShouldEqual(true);
            GVFltCallbacks.DoesPathAllowDelete(@".git\objects\pack\pack-1e88df2a4e234c82858cfe182070645fb96d6131.pack").ShouldEqual(true);
            GVFltCallbacks.DoesPathAllowDelete(@".git\objects\pack\pack-1e88df2a4e234c82858cfe182070645fb96d6131.idx").ShouldEqual(true);
        }

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
                    blobSizes: null,
                    gvflt: mockGvFlt,
                    gitIndexProjection: gitIndexProjection,
                    reliableBackgroundOperations: new MockReliableBackgroundOperations());

                string error;
                callbacks.TryStart(out error).ShouldEqual(true);

                Guid enumerationGuid = Guid.NewGuid();
                gitIndexProjection.EnumerationInMemory = false;
                mockGvFlt.OnStartDirectoryEnumeration(1, enumerationGuid, "test").ShouldEqual(NtStatus.Pending);
                mockGvFlt.WaitForCompletionStatus().ShouldEqual(NtStatus.Success);
                mockGvFlt.OnEndDirectoryEnumeration(enumerationGuid).ShouldEqual(NtStatus.Success);
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
                    blobSizes: null,
                    gvflt: mockGvFlt,
                    gitIndexProjection: gitIndexProjection,
                    reliableBackgroundOperations: new MockReliableBackgroundOperations());

                string error;
                callbacks.TryStart(out error).ShouldEqual(true);

                Guid enumerationGuid = Guid.NewGuid();
                gitIndexProjection.EnumerationInMemory = true;
                mockGvFlt.OnStartDirectoryEnumeration(1, enumerationGuid, "test").ShouldEqual(NtStatus.Success);
                mockGvFlt.OnEndDirectoryEnumeration(enumerationGuid).ShouldEqual(NtStatus.Success);
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
                    blobSizes: null,
                    gvflt: mockGvFlt,
                    gitIndexProjection: gitIndexProjection,
                    reliableBackgroundOperations: new MockReliableBackgroundOperations());

                string error;
                callbacks.TryStart(out error).ShouldEqual(true);

                mockGvFlt.OnGetPlaceholderInformation(1, "doesNotExist", 0, 0, 0, 0, 1, "UnitTests").ShouldEqual(NtStatus.ObjectNameNotFound);
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
                    blobSizes: null,
                    gvflt: mockGvFlt,
                    gitIndexProjection: gitIndexProjection,
                    reliableBackgroundOperations: new MockReliableBackgroundOperations());

                string error;
                callbacks.TryStart(out error).ShouldEqual(true);

                mockGvFlt.OnGetPlaceholderInformation(1, "test.txt", 0, 0, 0, 0, 1, "UnitTests").ShouldEqual(NtStatus.Pending);
                mockGvFlt.WaitForCompletionStatus().ShouldEqual(NtStatus.Success);
                mockGvFlt.CreatedPlaceholders.ShouldContain(entry => entry == "test.txt");
                gitIndexProjection.PlaceholdersCreated.ShouldContain(entry => entry == "test.txt");
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
                    blobSizes: null,
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

                mockGvFlt.OnGetPlaceholderInformation(1, "test.txt", 0, 0, 0, 0, 1, "UnitTests").ShouldEqual(NtStatus.Pending);

                // Cancelling before GetPlaceholderInformation has registered the command results in placeholders being created
                mockGvFlt.WaitForPlaceholderCreate();
                gitIndexProjection.WaitForPlaceholderCreate();
                mockGvFlt.CreatedPlaceholders.ShouldContain(entry => entry == "test.txt");
                gitIndexProjection.PlaceholdersCreated.ShouldContain(entry => entry == "test.txt");
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
                    blobSizes: null,
                    gvflt: mockGvFlt,
                    gitIndexProjection: gitIndexProjection,
                    reliableBackgroundOperations: new MockReliableBackgroundOperations());

                string error;
                callbacks.TryStart(out error).ShouldEqual(true);

                gitIndexProjection.BlockGetProjectedFileInfo(willWaitForRequest: true);
                mockGvFlt.OnGetPlaceholderInformation(1, "test.txt", 0, 0, 0, 0, 1, "UnitTests").ShouldEqual(NtStatus.Pending);
                gitIndexProjection.WaitForGetProjectedFileInfo();
                mockGvFlt.OnCancelCommand(1);
                gitIndexProjection.UnblockGetProjectedFileInfo();

                // Cancelling in the middle of GetPlaceholderInformation still allows it to create placeholders when the cancellation does not
                // interrupt network requests                
                mockGvFlt.WaitForPlaceholderCreate();
                gitIndexProjection.WaitForPlaceholderCreate();
                mockGvFlt.CreatedPlaceholders.ShouldContain(entry => entry == "test.txt");
                gitIndexProjection.PlaceholdersCreated.ShouldContain(entry => entry == "test.txt");
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
                    blobSizes: null,
                    gvflt: mockGvFlt,
                    gitIndexProjection: gitIndexProjection,
                    reliableBackgroundOperations: new MockReliableBackgroundOperations());

                string error;
                callbacks.TryStart(out error).ShouldEqual(true);

                MockTracer mockTracker = this.Repo.Context.Tracer as MockTracer;
                mockTracker.WaitRelatedEventName = "GVFltGetPlaceholderInformationAsyncHandler_GetProjectedGVFltFileInfoAndShaCancelled";
                gitIndexProjection.ThrowOperationCanceledExceptionOnProjectionRequest = true;
                mockGvFlt.OnGetPlaceholderInformation(1, "test.txt", 0, 0, 0, 0, 1, "UnitTests").ShouldEqual(NtStatus.Pending);

                // Cancelling in the middle of GetPlaceholderInformation in the middle of a network request should not result in placeholder
                // getting created
                mockTracker.WaitForRelatedEvent();
                mockGvFlt.CreatedPlaceholders.ShouldNotContain(entry => entry == "test.txt");
                gitIndexProjection.PlaceholdersCreated.ShouldNotContain(entry => entry == "test.txt");
            }
        }

        [TestCase]
        public void OnGetFileStreamReturnsInvalidParameterWhenOffsetNonZero()
        {
            using (MockVirtualizationInstance mockGvFlt = new MockVirtualizationInstance())
            using (MockGitIndexProjection gitIndexProjection = new MockGitIndexProjection(new[] { "test.txt" }))
            {
                GVFltCallbacks callbacks = new GVFltCallbacks(
                    this.Repo.Context,
                    this.Repo.GitObjects,
                    RepoMetadata.Instance,
                    blobSizes: null,
                    gvflt: mockGvFlt,
                    gitIndexProjection: gitIndexProjection,
                    reliableBackgroundOperations: new MockReliableBackgroundOperations());

                string error;
                callbacks.TryStart(out error).ShouldEqual(true);

                Guid enumerationGuid = Guid.NewGuid();

                byte[] contentId = GVFltCallbacks.ConvertShaToContentId("0123456789012345678901234567890123456789");
                byte[] epochId = GVFltCallbacks.GetEpochId();

                mockGvFlt.OnGetFileStream(
                    commandId: 1,
                    relativePath: "test.txt",
                    byteOffset: 10,
                    length: 100,
                    streamGuid: Guid.NewGuid(),
                    contentId: contentId,
                    epochId: epochId,
                    triggeringProcessId: 2,
                    triggeringProcessImageFileName: "UnitTest").ShouldEqual(NtStatus.InvalidParameter);
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
                    blobSizes: null,
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
                    epochId: epochId,
                    triggeringProcessId: 2,
                    triggeringProcessImageFileName: "UnitTest").ShouldEqual(NtStatus.InternalError);
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
                    blobSizes: null,
                    gvflt: mockGvFlt,
                    gitIndexProjection: gitIndexProjection,
                    reliableBackgroundOperations: new MockReliableBackgroundOperations());

                string error;
                callbacks.TryStart(out error).ShouldEqual(true);

                Guid enumerationGuid = Guid.NewGuid();

                byte[] contentId = GVFltCallbacks.ConvertShaToContentId("0123456789012345678901234567890123456789");
                byte[] epochId = GVFltCallbacks.GetEpochId();

                uint fileLength = 100;
                MockGVFSGitObjects mockGVFSGitObjects = this.Repo.GitObjects as MockGVFSGitObjects;
                mockGVFSGitObjects.FileLength = fileLength;
                mockGvFlt.WriteFileReturnStatus = NtStatus.Success;

                mockGvFlt.OnGetFileStream(
                    commandId: 1,
                    relativePath: "test.txt",
                    byteOffset: 0,
                    length: fileLength,
                    streamGuid: Guid.NewGuid(),
                    contentId: contentId,
                    epochId: epochId,
                    triggeringProcessId: 2,
                    triggeringProcessImageFileName: "UnitTest").ShouldEqual(NtStatus.Pending);

                mockGvFlt.WaitForCompletionStatus().ShouldEqual(NtStatus.Success);
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
                    blobSizes: null,
                    gvflt: mockGvFlt,
                    gitIndexProjection: gitIndexProjection,
                    reliableBackgroundOperations: new MockReliableBackgroundOperations());

                string error;
                callbacks.TryStart(out error).ShouldEqual(true);

                Guid enumerationGuid = Guid.NewGuid();

                byte[] contentId = GVFltCallbacks.ConvertShaToContentId("0123456789012345678901234567890123456789");
                byte[] epochId = GVFltCallbacks.GetEpochId();

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
                    epochId: epochId,
                    triggeringProcessId: 2,
                    triggeringProcessImageFileName: "UnitTest").ShouldEqual(NtStatus.Pending);

                mockTracker.WaitForRelatedEvent();
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
                    blobSizes: null,
                    gvflt: mockGvFlt,
                    gitIndexProjection: gitIndexProjection,
                    reliableBackgroundOperations: new MockReliableBackgroundOperations());

                string error;
                callbacks.TryStart(out error).ShouldEqual(true);

                Guid enumerationGuid = Guid.NewGuid();

                byte[] contentId = GVFltCallbacks.ConvertShaToContentId("0123456789012345678901234567890123456789");
                byte[] epochId = GVFltCallbacks.GetEpochId();

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
                    epochId: epochId,
                    triggeringProcessId: 2,
                    triggeringProcessImageFileName: "UnitTest").ShouldEqual(NtStatus.Pending);

                mockGvFlt.WaitForCreateWriteBuffer();
                mockGvFlt.OnCancelCommand(1);
                mockGvFlt.UnblockCreateWriteBuffer();
                mockTracker.WaitForRelatedEvent();
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
                    blobSizes: null,
                    gvflt: mockGvFlt,
                    gitIndexProjection: gitIndexProjection,
                    reliableBackgroundOperations: new MockReliableBackgroundOperations());

                string error;
                callbacks.TryStart(out error).ShouldEqual(true);

                Guid enumerationGuid = Guid.NewGuid();

                byte[] contentId = GVFltCallbacks.ConvertShaToContentId("0123456789012345678901234567890123456789");
                byte[] epochId = GVFltCallbacks.GetEpochId();

                uint fileLength = 100;
                MockGVFSGitObjects mockGVFSGitObjects = this.Repo.GitObjects as MockGVFSGitObjects;
                mockGVFSGitObjects.FileLength = fileLength;

                MockTracer mockTracker = this.Repo.Context.Tracer as MockTracer;
                mockTracker.WaitRelatedEventName = "GVFltGetFileStreamHandlerAsyncHandler_OperationCancelled";

                mockGvFlt.WriteFileReturnStatus = NtStatus.InternalError;
                mockGvFlt.OnGetFileStream(
                    commandId: 1,
                    relativePath: "test.txt",
                    byteOffset: 0,
                    length: fileLength,
                    streamGuid: Guid.NewGuid(),
                    contentId: contentId,
                    epochId: epochId,
                    triggeringProcessId: 2,
                    triggeringProcessImageFileName: "UnitTest").ShouldEqual(NtStatus.Pending);

                mockGvFlt.WaitForCompletionStatus().ShouldEqual(mockGvFlt.WriteFileReturnStatus);
            }
        }
    }
}