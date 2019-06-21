using GVFS.Common;
using GVFS.Common.Database;
using GVFS.Platform.Windows;
using GVFS.Tests.Should;
using GVFS.UnitTests.Category;
using GVFS.UnitTests.Mock.Common;
using GVFS.UnitTests.Mock.FileSystem;
using GVFS.UnitTests.Mock.Git;
using GVFS.UnitTests.Mock.Virtualization.Background;
using GVFS.UnitTests.Mock.Virtualization.BlobSize;
using GVFS.UnitTests.Mock.Virtualization.Projection;
using GVFS.UnitTests.Virtual;
using GVFS.UnitTests.Windows.Mock;
using GVFS.Virtualization;
using GVFS.Virtualization.FileSystem;
using Microsoft.Windows.ProjFS;
using Moq;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace GVFS.UnitTests.Windows.Virtualization
{
    [TestFixture]
    public class WindowsFileSystemVirtualizerTests : TestsWithCommonRepo
    {
        private const uint TriggeringProcessId = 1;
        private const string TriggeringProcessImageFileName = "UnitTests";

        private static readonly Dictionary<HResult, FSResult> MappedHResults = new Dictionary<HResult, FSResult>()
        {
            { HResult.Ok, FSResult.Ok },
            { HResult.DirNotEmpty, FSResult.DirectoryNotEmpty },
            { HResult.FileNotFound, FSResult.FileOrPathNotFound },
            { HResult.PathNotFound, FSResult.FileOrPathNotFound },
            { (HResult)HResultExtensions.HResultFromNtStatus.IoReparseTagNotHandled, FSResult.IoReparseTagNotHandled },
            { HResult.VirtualizationInvalidOp, FSResult.VirtualizationInvalidOperation },
        };

        private static int numWorkThreads = 1;

        [TestCase]
        public void HResultToFSResultMapsHResults()
        {
            foreach (HResult result in Enum.GetValues(typeof(HResult)))
            {
                if (MappedHResults.ContainsKey(result))
                {
                    WindowsFileSystemVirtualizer.HResultToFSResult(result).ShouldEqual(MappedHResults[result]);
                }
                else
                {
                    WindowsFileSystemVirtualizer.HResultToFSResult(result).ShouldEqual(FSResult.IOError);
                }
            }
        }

        [TestCase]
        public void ClearNegativePathCache()
        {
            const uint InitialNegativePathCacheCount = 7;
            using (MockVirtualizationInstance mockVirtualization = new MockVirtualizationInstance())
            using (WindowsFileSystemVirtualizer virtualizer = new WindowsFileSystemVirtualizer(this.Repo.Context, this.Repo.GitObjects, mockVirtualization, numWorkThreads))
            {
                mockVirtualization.NegativePathCacheCount = InitialNegativePathCacheCount;

                uint totalEntryCount;
                virtualizer.ClearNegativePathCache(out totalEntryCount).ShouldEqual(new FileSystemResult(FSResult.Ok, (int)HResult.Ok));
                totalEntryCount.ShouldEqual(InitialNegativePathCacheCount);
            }
        }

        [TestCase]
        public void DeleteFile()
        {
            using (MockVirtualizationInstance mockVirtualization = new MockVirtualizationInstance())
            using (WindowsFileSystemVirtualizer virtualizer = new WindowsFileSystemVirtualizer(this.Repo.Context, this.Repo.GitObjects, mockVirtualization, numWorkThreads))
            {
                UpdateFailureReason failureReason = UpdateFailureReason.NoFailure;

                mockVirtualization.DeleteFileResult = HResult.Ok;
                mockVirtualization.DeleteFileUpdateFailureCause = UpdateFailureCause.NoFailure;
                virtualizer
                    .DeleteFile("test.txt", UpdatePlaceholderType.AllowReadOnly, out failureReason)
                    .ShouldEqual(new FileSystemResult(FSResult.Ok, (int)mockVirtualization.DeleteFileResult));
                failureReason.ShouldEqual((UpdateFailureReason)mockVirtualization.DeleteFileUpdateFailureCause);

                mockVirtualization.DeleteFileResult = HResult.FileNotFound;
                mockVirtualization.DeleteFileUpdateFailureCause = UpdateFailureCause.NoFailure;
                virtualizer
                    .DeleteFile("test.txt", UpdatePlaceholderType.AllowReadOnly, out failureReason)
                    .ShouldEqual(new FileSystemResult(FSResult.FileOrPathNotFound, (int)mockVirtualization.DeleteFileResult));
                failureReason.ShouldEqual((UpdateFailureReason)mockVirtualization.DeleteFileUpdateFailureCause);

                mockVirtualization.DeleteFileResult = HResult.VirtualizationInvalidOp;
                mockVirtualization.DeleteFileUpdateFailureCause = UpdateFailureCause.DirtyData;
                virtualizer
                    .DeleteFile("test.txt", UpdatePlaceholderType.AllowReadOnly, out failureReason)
                    .ShouldEqual(new FileSystemResult(FSResult.VirtualizationInvalidOperation, (int)mockVirtualization.DeleteFileResult));
                failureReason.ShouldEqual((UpdateFailureReason)mockVirtualization.DeleteFileUpdateFailureCause);
            }
        }

        [TestCase]
        public void UpdatePlaceholderIfNeeded()
        {
            using (MockVirtualizationInstance mockVirtualization = new MockVirtualizationInstance())
            using (WindowsFileSystemVirtualizer virtualizer = new WindowsFileSystemVirtualizer(this.Repo.Context, this.Repo.GitObjects, mockVirtualization, numWorkThreads))
            {
                UpdateFailureReason failureReason = UpdateFailureReason.NoFailure;

                mockVirtualization.UpdateFileIfNeededResult = HResult.Ok;
                mockVirtualization.UpdateFileIfNeededFailureCase = UpdateFailureCause.NoFailure;
                virtualizer
                    .UpdatePlaceholderIfNeeded(
                        "test.txt",
                        DateTime.Now,
                        DateTime.Now,
                        DateTime.Now,
                        DateTime.Now,
                        0,
                        15,
                        string.Empty,
                        UpdatePlaceholderType.AllowReadOnly,
                        out failureReason)
                    .ShouldEqual(new FileSystemResult(FSResult.Ok, (int)mockVirtualization.UpdateFileIfNeededResult));
                failureReason.ShouldEqual((UpdateFailureReason)mockVirtualization.UpdateFileIfNeededFailureCase);

                mockVirtualization.UpdateFileIfNeededResult = HResult.FileNotFound;
                mockVirtualization.UpdateFileIfNeededFailureCase = UpdateFailureCause.NoFailure;
                virtualizer
                    .UpdatePlaceholderIfNeeded(
                        "test.txt",
                        DateTime.Now,
                        DateTime.Now,
                        DateTime.Now,
                        DateTime.Now,
                        0,
                        15,
                        string.Empty,
                        UpdatePlaceholderType.AllowReadOnly,
                        out failureReason)
                    .ShouldEqual(new FileSystemResult(FSResult.FileOrPathNotFound, (int)mockVirtualization.UpdateFileIfNeededResult));
                failureReason.ShouldEqual((UpdateFailureReason)mockVirtualization.UpdateFileIfNeededFailureCase);

                mockVirtualization.UpdateFileIfNeededResult = HResult.VirtualizationInvalidOp;
                mockVirtualization.UpdateFileIfNeededFailureCase = UpdateFailureCause.DirtyData;
                virtualizer
                    .UpdatePlaceholderIfNeeded(
                        "test.txt",
                        DateTime.Now,
                        DateTime.Now,
                        DateTime.Now,
                        DateTime.Now,
                        0,
                        15,
                        string.Empty,
                        UpdatePlaceholderType.AllowReadOnly,
                        out failureReason)
                    .ShouldEqual(new FileSystemResult(FSResult.VirtualizationInvalidOperation, (int)mockVirtualization.UpdateFileIfNeededResult));
                failureReason.ShouldEqual((UpdateFailureReason)mockVirtualization.UpdateFileIfNeededFailureCase);
            }
        }

        [TestCase]
        public void OnStartDirectoryEnumerationReturnsPendingWhenResultsNotInMemory()
        {
            Mock<IPlaceholderCollection> mockPlaceholderDb = new Mock<IPlaceholderCollection>(MockBehavior.Strict);
            mockPlaceholderDb.Setup(x => x.GetCount()).Returns(1);
            Mock<IIncludedFolderCollection> mockIncludeDb = new Mock<IIncludedFolderCollection>(MockBehavior.Strict);
            using (MockBackgroundFileSystemTaskRunner backgroundTaskRunner = new MockBackgroundFileSystemTaskRunner())
            using (MockVirtualizationInstance mockVirtualization = new MockVirtualizationInstance())
            using (MockGitIndexProjection gitIndexProjection = new MockGitIndexProjection(new[] { "test.txt" }))
            using (WindowsFileSystemVirtualizer virtualizer = new WindowsFileSystemVirtualizer(this.Repo.Context, this.Repo.GitObjects, mockVirtualization, numWorkThreads))
            using (FileSystemCallbacks fileSystemCallbacks = new FileSystemCallbacks(
                this.Repo.Context,
                this.Repo.GitObjects,
                RepoMetadata.Instance,
                new MockBlobSizes(),
                gitIndexProjection,
                backgroundTaskRunner,
                virtualizer,
                mockPlaceholderDb.Object,
                mockIncludeDb.Object))
            {
                try
                {
                    string error;
                    fileSystemCallbacks.TryStart(out error).ShouldEqual(true);

                    Guid enumerationGuid = Guid.NewGuid();
                    gitIndexProjection.EnumerationInMemory = false;
                    mockVirtualization.requiredCallbacks.StartDirectoryEnumerationCallback(1, enumerationGuid, "test", TriggeringProcessId, TriggeringProcessImageFileName).ShouldEqual(HResult.Pending);
                    mockVirtualization.WaitForCompletionStatus().ShouldEqual(HResult.Ok);
                    mockVirtualization.requiredCallbacks.EndDirectoryEnumerationCallback(enumerationGuid).ShouldEqual(HResult.Ok);
                }
                finally
                {
                    fileSystemCallbacks.Stop();
                }
            }

            mockPlaceholderDb.VerifyAll();
            mockIncludeDb.VerifyAll();
        }

        [TestCase]
        public void OnStartDirectoryEnumerationReturnsSuccessWhenResultsInMemory()
        {
            Mock<IPlaceholderCollection> mockPlaceholderDb = new Mock<IPlaceholderCollection>(MockBehavior.Strict);
            mockPlaceholderDb.Setup(x => x.GetCount()).Returns(1);
            Mock<IIncludedFolderCollection> mockIncludeDb = new Mock<IIncludedFolderCollection>(MockBehavior.Strict);
            using (MockBackgroundFileSystemTaskRunner backgroundTaskRunner = new MockBackgroundFileSystemTaskRunner())
            using (MockVirtualizationInstance mockVirtualization = new MockVirtualizationInstance())
            using (MockGitIndexProjection gitIndexProjection = new MockGitIndexProjection(new[] { "test" }))
            using (WindowsFileSystemVirtualizer virtualizer = new WindowsFileSystemVirtualizer(this.Repo.Context, this.Repo.GitObjects, mockVirtualization, numWorkThreads))
            using (FileSystemCallbacks fileSystemCallbacks = new FileSystemCallbacks(
                this.Repo.Context,
                this.Repo.GitObjects,
                RepoMetadata.Instance,
                new MockBlobSizes(),
                gitIndexProjection,
                backgroundTaskRunner,
                virtualizer,
                mockPlaceholderDb.Object,
                mockIncludeDb.Object))
            {
                try
                {
                    string error;
                    fileSystemCallbacks.TryStart(out error).ShouldEqual(true);

                    Guid enumerationGuid = Guid.NewGuid();
                    gitIndexProjection.EnumerationInMemory = true;
                    mockVirtualization.requiredCallbacks.StartDirectoryEnumerationCallback(1, enumerationGuid, "test", TriggeringProcessId, TriggeringProcessImageFileName).ShouldEqual(HResult.Ok);
                    mockVirtualization.requiredCallbacks.EndDirectoryEnumerationCallback(enumerationGuid).ShouldEqual(HResult.Ok);
                }
                finally
                {
                    fileSystemCallbacks.Stop();
                }
            }

            mockPlaceholderDb.VerifyAll();
            mockIncludeDb.VerifyAll();
        }

        [TestCase]
        public void GetPlaceholderInformationHandlerPathNotProjected()
        {
            Mock<IPlaceholderCollection> mockPlaceholderDb = new Mock<IPlaceholderCollection>(MockBehavior.Strict);
            mockPlaceholderDb.Setup(x => x.GetCount()).Returns(1);
            Mock<IIncludedFolderCollection> mockIncludeDb = new Mock<IIncludedFolderCollection>(MockBehavior.Strict);
            using (MockBackgroundFileSystemTaskRunner backgroundTaskRunner = new MockBackgroundFileSystemTaskRunner())
            using (MockVirtualizationInstance mockVirtualization = new MockVirtualizationInstance())
            using (MockGitIndexProjection gitIndexProjection = new MockGitIndexProjection(new[] { "test.txt" }))
            using (WindowsFileSystemVirtualizer virtualizer = new WindowsFileSystemVirtualizer(this.Repo.Context, this.Repo.GitObjects, mockVirtualization, numWorkThreads))
            using (FileSystemCallbacks fileSystemCallbacks = new FileSystemCallbacks(
                this.Repo.Context,
                this.Repo.GitObjects,
                RepoMetadata.Instance,
                new MockBlobSizes(),
                gitIndexProjection,
                backgroundTaskRunner,
                virtualizer,
                mockPlaceholderDb.Object,
                mockIncludeDb.Object))
            {
                try
                {
                    string error;
                    fileSystemCallbacks.TryStart(out error).ShouldEqual(true);

                    mockVirtualization.requiredCallbacks.GetPlaceholderInfoCallback(1, "doesNotExist", TriggeringProcessId, TriggeringProcessImageFileName).ShouldEqual(HResult.FileNotFound);
                }
                finally
                {
                    fileSystemCallbacks.Stop();
                }
            }

            mockPlaceholderDb.VerifyAll();
            mockIncludeDb.VerifyAll();
        }

        [TestCase]
        public void GetPlaceholderInformationHandlerPathProjected()
        {
            Mock<IPlaceholderCollection> mockPlaceholderDb = new Mock<IPlaceholderCollection>(MockBehavior.Strict);
            mockPlaceholderDb.Setup(x => x.GetCount()).Returns(1);
            Mock<IIncludedFolderCollection> mockIncludeDb = new Mock<IIncludedFolderCollection>(MockBehavior.Strict);
            using (MockBackgroundFileSystemTaskRunner backgroundTaskRunner = new MockBackgroundFileSystemTaskRunner())
            using (MockVirtualizationInstance mockVirtualization = new MockVirtualizationInstance())
            using (MockGitIndexProjection gitIndexProjection = new MockGitIndexProjection(new[] { "test.txt" }))
            using (WindowsFileSystemVirtualizer virtualizer = new WindowsFileSystemVirtualizer(this.Repo.Context, this.Repo.GitObjects, mockVirtualization, numWorkThreads))
            using (FileSystemCallbacks fileSystemCallbacks = new FileSystemCallbacks(
                this.Repo.Context,
                this.Repo.GitObjects,
                RepoMetadata.Instance,
                new MockBlobSizes(),
                gitIndexProjection,
                backgroundTaskRunner,
                virtualizer,
                mockPlaceholderDb.Object,
                mockIncludeDb.Object))
            {
                try
                {
                    string error;
                    fileSystemCallbacks.TryStart(out error).ShouldEqual(true);

                    mockVirtualization.requiredCallbacks.GetPlaceholderInfoCallback(1, "test.txt", TriggeringProcessId, TriggeringProcessImageFileName).ShouldEqual(HResult.Pending);
                    mockVirtualization.WaitForCompletionStatus().ShouldEqual(HResult.Ok);
                    mockVirtualization.CreatedPlaceholders.ShouldContain(entry => entry == "test.txt");
                    gitIndexProjection.PlaceholdersCreated.ShouldContain(entry => entry == "test.txt");
                }
                finally
                {
                    fileSystemCallbacks.Stop();
                }
            }

            mockPlaceholderDb.VerifyAll();
            mockIncludeDb.VerifyAll();
        }

        [TestCase]
        public void GetPlaceholderInformationHandlerCancelledBeforeSchedulingAsync()
        {
            Mock<IPlaceholderCollection> mockPlaceholderDb = new Mock<IPlaceholderCollection>(MockBehavior.Strict);
            mockPlaceholderDb.Setup(x => x.GetCount()).Returns(1);
            Mock<IIncludedFolderCollection> mockIncludeDb = new Mock<IIncludedFolderCollection>(MockBehavior.Strict);
            using (MockBackgroundFileSystemTaskRunner backgroundTaskRunner = new MockBackgroundFileSystemTaskRunner())
            using (MockVirtualizationInstance mockVirtualization = new MockVirtualizationInstance())
            using (MockGitIndexProjection gitIndexProjection = new MockGitIndexProjection(new[] { "test.txt" }))
            using (WindowsFileSystemVirtualizer virtualizer = new WindowsFileSystemVirtualizer(this.Repo.Context, this.Repo.GitObjects, mockVirtualization, numWorkThreads))
            using (FileSystemCallbacks fileSystemCallbacks = new FileSystemCallbacks(
                this.Repo.Context,
                this.Repo.GitObjects,
                RepoMetadata.Instance,
                new MockBlobSizes(),
                gitIndexProjection,
                backgroundTaskRunner,
                virtualizer,
                mockPlaceholderDb.Object,
                mockIncludeDb.Object))
            {
                try
                {
                    string error;
                    fileSystemCallbacks.TryStart(out error).ShouldEqual(true);

                    gitIndexProjection.BlockIsPathProjected(willWaitForRequest: true);

                    Task.Run(() =>
                    {
                        // Wait for OnGetPlaceholderInformation to call IsPathProjected and then while it's blocked there
                        // call OnCancelCommand
                        gitIndexProjection.WaitForIsPathProjected();
                        mockVirtualization.OnCancelCommand(1);
                        gitIndexProjection.UnblockIsPathProjected();
                    });

                    mockVirtualization.requiredCallbacks.GetPlaceholderInfoCallback(1, "test.txt", TriggeringProcessId, TriggeringProcessImageFileName).ShouldEqual(HResult.Pending);

                    // Cancelling before GetPlaceholderInformation has registered the command results in placeholders being created
                    mockVirtualization.WaitForPlaceholderCreate();
                    gitIndexProjection.WaitForPlaceholderCreate();
                    mockVirtualization.CreatedPlaceholders.ShouldContain(entry => entry == "test.txt");
                    gitIndexProjection.PlaceholdersCreated.ShouldContain(entry => entry == "test.txt");
                }
                finally
                {
                    fileSystemCallbacks.Stop();
                }
            }

            mockPlaceholderDb.VerifyAll();
            mockIncludeDb.VerifyAll();
        }

        [TestCase]
        public void GetPlaceholderInformationHandlerCancelledDuringAsyncCallback()
        {
            Mock<IPlaceholderCollection> mockPlaceholderDb = new Mock<IPlaceholderCollection>(MockBehavior.Strict);
            mockPlaceholderDb.Setup(x => x.GetCount()).Returns(1);
            Mock<IIncludedFolderCollection> mockIncludeDb = new Mock<IIncludedFolderCollection>(MockBehavior.Strict);
            using (MockBackgroundFileSystemTaskRunner backgroundTaskRunner = new MockBackgroundFileSystemTaskRunner())
            using (MockVirtualizationInstance mockVirtualization = new MockVirtualizationInstance())
            using (MockGitIndexProjection gitIndexProjection = new MockGitIndexProjection(new[] { "test.txt" }))
            using (WindowsFileSystemVirtualizer virtualizer = new WindowsFileSystemVirtualizer(this.Repo.Context, this.Repo.GitObjects, mockVirtualization, numWorkThreads))
            using (FileSystemCallbacks fileSystemCallbacks = new FileSystemCallbacks(
                this.Repo.Context,
                this.Repo.GitObjects,
                RepoMetadata.Instance,
                new MockBlobSizes(),
                gitIndexProjection,
                backgroundTaskRunner,
                virtualizer,
                mockPlaceholderDb.Object,
                mockIncludeDb.Object))
            {
                try
                {
                    string error;
                    fileSystemCallbacks.TryStart(out error).ShouldEqual(true);

                    gitIndexProjection.BlockGetProjectedFileInfo(willWaitForRequest: true);
                    mockVirtualization.requiredCallbacks.GetPlaceholderInfoCallback(1, "test.txt", TriggeringProcessId, TriggeringProcessImageFileName).ShouldEqual(HResult.Pending);
                    gitIndexProjection.WaitForGetProjectedFileInfo();
                    mockVirtualization.OnCancelCommand(1);
                    gitIndexProjection.UnblockGetProjectedFileInfo();

                    // Cancelling in the middle of GetPlaceholderInformation still allows it to create placeholders when the cancellation does not
                    // interrupt network requests
                    mockVirtualization.WaitForPlaceholderCreate();
                    gitIndexProjection.WaitForPlaceholderCreate();
                    mockVirtualization.CreatedPlaceholders.ShouldContain(entry => entry == "test.txt");
                    gitIndexProjection.PlaceholdersCreated.ShouldContain(entry => entry == "test.txt");
                }
                finally
                {
                    fileSystemCallbacks.Stop();
                }
            }

            mockPlaceholderDb.VerifyAll();
            mockIncludeDb.VerifyAll();
        }

        [TestCase]
        [Category(CategoryConstants.ExceptionExpected)]
        public void GetPlaceholderInformationHandlerCancelledDuringNetworkRequest()
        {
            Mock<IPlaceholderCollection> mockPlaceholderDb = new Mock<IPlaceholderCollection>(MockBehavior.Strict);
            mockPlaceholderDb.Setup(x => x.GetCount()).Returns(1);
            Mock<IIncludedFolderCollection> mockIncludeDb = new Mock<IIncludedFolderCollection>(MockBehavior.Strict);
            using (MockBackgroundFileSystemTaskRunner backgroundTaskRunner = new MockBackgroundFileSystemTaskRunner())
            using (MockVirtualizationInstance mockVirtualization = new MockVirtualizationInstance())
            using (MockGitIndexProjection gitIndexProjection = new MockGitIndexProjection(new[] { "test.txt" }))
            using (WindowsFileSystemVirtualizer virtualizer = new WindowsFileSystemVirtualizer(this.Repo.Context, this.Repo.GitObjects, mockVirtualization, numWorkThreads))
            using (FileSystemCallbacks fileSystemCallbacks = new FileSystemCallbacks(
                this.Repo.Context,
                this.Repo.GitObjects,
                RepoMetadata.Instance,
                new MockBlobSizes(),
                gitIndexProjection,
                backgroundTaskRunner,
                virtualizer,
                mockPlaceholderDb.Object,
                mockIncludeDb.Object))
            {
                try
                {
                    string error;
                    fileSystemCallbacks.TryStart(out error).ShouldEqual(true);

                    MockTracer mockTracker = this.Repo.Context.Tracer as MockTracer;
                    mockTracker.WaitRelatedEventName = "GetPlaceholderInformationAsyncHandler_GetProjectedFileInfo_Cancelled";
                    gitIndexProjection.ThrowOperationCanceledExceptionOnProjectionRequest = true;
                    mockVirtualization.requiredCallbacks.GetPlaceholderInfoCallback(1, "test.txt", TriggeringProcessId, TriggeringProcessImageFileName).ShouldEqual(HResult.Pending);

                    // Cancelling in the middle of GetPlaceholderInformation in the middle of a network request should not result in placeholder
                    // getting created
                    mockTracker.WaitForRelatedEvent();
                    mockVirtualization.CreatedPlaceholders.ShouldNotContain(entry => entry == "test.txt");
                    gitIndexProjection.PlaceholdersCreated.ShouldNotContain(entry => entry == "test.txt");
                }
                finally
                {
                    fileSystemCallbacks.Stop();
                }
            }

            mockPlaceholderDb.VerifyAll();
            mockIncludeDb.VerifyAll();
        }

        [TestCase]
        public void OnGetFileStreamReturnsInternalErrorWhenOffsetNonZero()
        {
            Mock<IPlaceholderCollection> mockPlaceholderDb = new Mock<IPlaceholderCollection>(MockBehavior.Strict);
            mockPlaceholderDb.Setup(x => x.GetCount()).Returns(1);
            Mock<IIncludedFolderCollection> mockIncludeDb = new Mock<IIncludedFolderCollection>(MockBehavior.Strict);
            using (MockBackgroundFileSystemTaskRunner backgroundTaskRunner = new MockBackgroundFileSystemTaskRunner())
            using (MockVirtualizationInstance mockVirtualization = new MockVirtualizationInstance())
            using (MockGitIndexProjection gitIndexProjection = new MockGitIndexProjection(new[] { "test.txt" }))
            using (WindowsFileSystemVirtualizer virtualizer = new WindowsFileSystemVirtualizer(this.Repo.Context, this.Repo.GitObjects, mockVirtualization, numWorkThreads))
            using (FileSystemCallbacks fileSystemCallbacks = new FileSystemCallbacks(
                this.Repo.Context,
                this.Repo.GitObjects,
                RepoMetadata.Instance,
                new MockBlobSizes(),
                gitIndexProjection,
                backgroundTaskRunner,
                virtualizer,
                mockPlaceholderDb.Object,
                mockIncludeDb.Object))
            {
                try
                {
                    string error;
                    fileSystemCallbacks.TryStart(out error).ShouldEqual(true);

                    Guid enumerationGuid = Guid.NewGuid();

                    byte[] contentId = FileSystemVirtualizer.ConvertShaToContentId("0123456789012345678901234567890123456789");
                    byte[] placeholderVersion = WindowsFileSystemVirtualizer.PlaceholderVersionId;

                    mockVirtualization.requiredCallbacks.GetFileDataCallback(
                        commandId: 1,
                        relativePath: "test.txt",
                        byteOffset: 10,
                        length: 100,
                        dataStreamId: Guid.NewGuid(),
                        contentId: contentId,
                        providerId: placeholderVersion,
                        triggeringProcessId: 2,
                        triggeringProcessImageFileName: "UnitTest").ShouldEqual(HResult.InternalError);
                }
                finally
                {
                    fileSystemCallbacks.Stop();
                }
            }

            mockPlaceholderDb.VerifyAll();
            mockIncludeDb.VerifyAll();
        }

        [TestCase]
        public void OnGetFileStreamReturnsInternalErrorWhenPlaceholderVersionDoesNotMatchExpected()
        {
            Mock<IPlaceholderCollection> mockPlaceholderDb = new Mock<IPlaceholderCollection>(MockBehavior.Strict);
            mockPlaceholderDb.Setup(x => x.GetCount()).Returns(1);
            Mock<IIncludedFolderCollection> mockIncludeDb = new Mock<IIncludedFolderCollection>(MockBehavior.Strict);
            using (MockBackgroundFileSystemTaskRunner backgroundTaskRunner = new MockBackgroundFileSystemTaskRunner())
            using (MockVirtualizationInstance mockVirtualization = new MockVirtualizationInstance())
            using (MockGitIndexProjection gitIndexProjection = new MockGitIndexProjection(new[] { "test.txt" }))
            using (WindowsFileSystemVirtualizer virtualizer = new WindowsFileSystemVirtualizer(this.Repo.Context, this.Repo.GitObjects, mockVirtualization, numWorkThreads))
            using (FileSystemCallbacks fileSystemCallbacks = new FileSystemCallbacks(
                this.Repo.Context,
                this.Repo.GitObjects,
                RepoMetadata.Instance,
                new MockBlobSizes(),
                gitIndexProjection,
                backgroundTaskRunner,
                virtualizer,
                mockPlaceholderDb.Object,
                mockIncludeDb.Object))
            {
                try
                {
                    string error;
                    fileSystemCallbacks.TryStart(out error).ShouldEqual(true);

                    Guid enumerationGuid = Guid.NewGuid();

                    byte[] contentId = FileSystemVirtualizer.ConvertShaToContentId("0123456789012345678901234567890123456789");
                    byte[] epochId = new byte[] { FileSystemVirtualizer.PlaceholderVersion + 1 };

                    mockVirtualization.requiredCallbacks.GetFileDataCallback(
                        commandId: 1,
                        relativePath: "test.txt",
                        byteOffset: 0,
                        length: 100,
                        dataStreamId: Guid.NewGuid(),
                        contentId: contentId,
                        providerId: epochId,
                        triggeringProcessId: 2,
                        triggeringProcessImageFileName: "UnitTest").ShouldEqual(HResult.InternalError);
                }
                finally
                {
                    fileSystemCallbacks.Stop();
                }
            }

            mockPlaceholderDb.VerifyAll();
            mockIncludeDb.VerifyAll();
        }

        [TestCase]
        public void MoveFileIntoDotGitDirectory()
        {
            Mock<IPlaceholderCollection> mockPlaceholderDb = new Mock<IPlaceholderCollection>(MockBehavior.Strict);
            mockPlaceholderDb.Setup(x => x.GetCount()).Returns(1);
            Mock<IIncludedFolderCollection> mockIncludeDb = new Mock<IIncludedFolderCollection>(MockBehavior.Strict);
            using (MockBackgroundFileSystemTaskRunner backgroundTaskRunner = new MockBackgroundFileSystemTaskRunner())
            using (MockVirtualizationInstance mockVirtualization = new MockVirtualizationInstance())
            using (MockGitIndexProjection gitIndexProjection = new MockGitIndexProjection(new[] { "test.txt" }))
            using (WindowsFileSystemVirtualizer virtualizer = new WindowsFileSystemVirtualizer(this.Repo.Context, this.Repo.GitObjects, mockVirtualization, numWorkThreads))
            using (MockFileSystemCallbacks fileSystemCallbacks = new MockFileSystemCallbacks(
                this.Repo.Context,
                this.Repo.GitObjects,
                RepoMetadata.Instance,
                new MockBlobSizes(),
                gitIndexProjection,
                backgroundTaskRunner,
                virtualizer,
                mockPlaceholderDb.Object,
                mockIncludeDb.Object))
            {
                try
                {
                    fileSystemCallbacks.TryStart(out string error).ShouldEqual(true);

                    NotificationType notificationType = NotificationType.UseExistingMask;
                    mockVirtualization.OnNotifyFileRenamed(
                        "test.txt",
                        Path.Combine(".git", "test.txt"),
                        isDirectory: false,
                        triggeringProcessId: TriggeringProcessId,
                        triggeringProcessImageFileName: TriggeringProcessImageFileName,
                        notificationMask: out notificationType);
                    notificationType.ShouldEqual(NotificationType.UseExistingMask);
                    fileSystemCallbacks.OnIndexFileChangeCallCount.ShouldEqual(0);
                    fileSystemCallbacks.OnLogsHeadChangeCallCount.ShouldEqual(0);
                    fileSystemCallbacks.OnFileRenamedCallCount.ShouldEqual(1);
                    fileSystemCallbacks.OnFolderRenamedCallCount.ShouldEqual(0);
                    fileSystemCallbacks.ResetCalls();

                    // We don't expect something to rename something from outside the .gitdir to the .git\index, but this
                    // verifies that we behave as expected in case that happens
                    mockVirtualization.OnNotifyFileRenamed(
                        "test.txt",
                        Path.Combine(".git", "index"),
                        isDirectory: false,
                        triggeringProcessId: TriggeringProcessId,
                        triggeringProcessImageFileName: TriggeringProcessImageFileName,
                        notificationMask: out notificationType);
                    notificationType.ShouldEqual(NotificationType.UseExistingMask);
                    fileSystemCallbacks.OnIndexFileChangeCallCount.ShouldEqual(1);
                    fileSystemCallbacks.OnLogsHeadChangeCallCount.ShouldEqual(0);
                    fileSystemCallbacks.OnFileRenamedCallCount.ShouldEqual(1);
                    fileSystemCallbacks.OnFolderRenamedCallCount.ShouldEqual(0);
                    fileSystemCallbacks.ResetCalls();

                    // We don't expect something to rename something from outside the .gitdir to the .git\logs\HEAD, but this
                    // verifies that we behave as expected in case that happens
                    mockVirtualization.OnNotifyFileRenamed(
                        "test.txt",
                        Path.Combine(".git", "logs\\HEAD"),
                        isDirectory: false,
                        triggeringProcessId: TriggeringProcessId,
                        triggeringProcessImageFileName: TriggeringProcessImageFileName,
                        notificationMask: out notificationType);
                    notificationType.ShouldEqual(NotificationType.UseExistingMask);
                    fileSystemCallbacks.OnIndexFileChangeCallCount.ShouldEqual(0);
                    fileSystemCallbacks.OnLogsHeadChangeCallCount.ShouldEqual(1);
                    fileSystemCallbacks.OnFileRenamedCallCount.ShouldEqual(1);
                    fileSystemCallbacks.OnFolderRenamedCallCount.ShouldEqual(0);
                    fileSystemCallbacks.ResetCalls();
                }
                finally
                {
                    fileSystemCallbacks.Stop();
                }
            }

            mockPlaceholderDb.VerifyAll();
            mockIncludeDb.VerifyAll();
        }

        [TestCase]
        public void MoveFileFromDotGitToSrc()
        {
            Mock<IPlaceholderCollection> mockPlaceholderDb = new Mock<IPlaceholderCollection>(MockBehavior.Strict);
            mockPlaceholderDb.Setup(x => x.GetCount()).Returns(1);
            Mock<IIncludedFolderCollection> mockIncludeDb = new Mock<IIncludedFolderCollection>(MockBehavior.Strict);
            using (MockBackgroundFileSystemTaskRunner backgroundTaskRunner = new MockBackgroundFileSystemTaskRunner())
            using (MockVirtualizationInstance mockVirtualization = new MockVirtualizationInstance())
            using (MockGitIndexProjection gitIndexProjection = new MockGitIndexProjection(new[] { "test.txt" }))
            using (WindowsFileSystemVirtualizer virtualizer = new WindowsFileSystemVirtualizer(this.Repo.Context, this.Repo.GitObjects, mockVirtualization, numWorkThreads))
            using (MockFileSystemCallbacks fileSystemCallbacks = new MockFileSystemCallbacks(
                this.Repo.Context,
                this.Repo.GitObjects,
                RepoMetadata.Instance,
                new MockBlobSizes(),
                gitIndexProjection,
                backgroundTaskRunner,
                virtualizer,
                mockPlaceholderDb.Object,
                mockIncludeDb.Object))
            {
                try
                {
                    fileSystemCallbacks.TryStart(out string error).ShouldEqual(true);

                    NotificationType notificationType = NotificationType.UseExistingMask;
                    mockVirtualization.OnNotifyFileRenamed(
                        Path.Combine(".git", "test.txt"),
                        "test2.txt",
                        isDirectory: false,
                        triggeringProcessId: TriggeringProcessId,
                        triggeringProcessImageFileName: TriggeringProcessImageFileName,
                        notificationMask: out notificationType);
                    notificationType.ShouldEqual(NotificationType.UseExistingMask);
                    fileSystemCallbacks.OnIndexFileChangeCallCount.ShouldEqual(0);
                    fileSystemCallbacks.OnLogsHeadChangeCallCount.ShouldEqual(0);
                    fileSystemCallbacks.OnFileRenamedCallCount.ShouldEqual(1);
                    fileSystemCallbacks.OnFolderRenamedCallCount.ShouldEqual(0);
                }
                finally
                {
                    fileSystemCallbacks.Stop();
                }
            }

            mockPlaceholderDb.VerifyAll();
            mockIncludeDb.VerifyAll();
        }

        [TestCase]
        public void MoveFile()
        {
            Mock<IPlaceholderCollection> mockPlaceholderDb = new Mock<IPlaceholderCollection>(MockBehavior.Strict);
            mockPlaceholderDb.Setup(x => x.GetCount()).Returns(1);
            Mock<IIncludedFolderCollection> mockIncludeDb = new Mock<IIncludedFolderCollection>(MockBehavior.Strict);
            using (MockBackgroundFileSystemTaskRunner backgroundTaskRunner = new MockBackgroundFileSystemTaskRunner())
            using (MockVirtualizationInstance mockVirtualization = new MockVirtualizationInstance())
            using (MockGitIndexProjection gitIndexProjection = new MockGitIndexProjection(new[] { "test.txt" }))
            using (WindowsFileSystemVirtualizer virtualizer = new WindowsFileSystemVirtualizer(this.Repo.Context, this.Repo.GitObjects, mockVirtualization, numWorkThreads))
            using (MockFileSystemCallbacks fileSystemCallbacks = new MockFileSystemCallbacks(
                this.Repo.Context,
                this.Repo.GitObjects,
                RepoMetadata.Instance,
                new MockBlobSizes(),
                gitIndexProjection,
                backgroundTaskRunner,
                virtualizer,
                mockPlaceholderDb.Object,
                mockIncludeDb.Object))
            {
                try
                {
                    fileSystemCallbacks.TryStart(out string error).ShouldEqual(true);

                    NotificationType notificationType = NotificationType.UseExistingMask;
                    mockVirtualization.OnNotifyFileRenamed(
                        "test.txt",
                        "test2.txt",
                        isDirectory: false,
                        triggeringProcessId: TriggeringProcessId,
                        triggeringProcessImageFileName: TriggeringProcessImageFileName,
                        notificationMask: out notificationType);
                    notificationType.ShouldEqual(NotificationType.UseExistingMask);
                    fileSystemCallbacks.OnIndexFileChangeCallCount.ShouldEqual(0);
                    fileSystemCallbacks.OnLogsHeadChangeCallCount.ShouldEqual(0);
                    fileSystemCallbacks.OnFileRenamedCallCount.ShouldEqual(1);
                    fileSystemCallbacks.OnFolderRenamedCallCount.ShouldEqual(0);
                    fileSystemCallbacks.ResetCalls();

                    mockVirtualization.OnNotifyFileRenamed(
                        "test_folder_src",
                        "test_folder_dst",
                        isDirectory: true,
                        triggeringProcessId: TriggeringProcessId,
                        triggeringProcessImageFileName: TriggeringProcessImageFileName,
                        notificationMask: out notificationType);
                    notificationType.ShouldEqual(NotificationType.UseExistingMask);
                    fileSystemCallbacks.OnIndexFileChangeCallCount.ShouldEqual(0);
                    fileSystemCallbacks.OnLogsHeadChangeCallCount.ShouldEqual(0);
                    fileSystemCallbacks.OnFileRenamedCallCount.ShouldEqual(0);
                    fileSystemCallbacks.OnFolderRenamedCallCount.ShouldEqual(1);
                }
                finally
                {
                    fileSystemCallbacks.Stop();
                }
            }

            mockPlaceholderDb.VerifyAll();
            mockIncludeDb.VerifyAll();
        }

        [TestCase]
        public void OnGetFileStreamReturnsPendingAndCompletesWithSuccessWhenNoFailures()
        {
            Mock<IPlaceholderCollection> mockPlaceholderDb = new Mock<IPlaceholderCollection>(MockBehavior.Strict);
            mockPlaceholderDb.Setup(x => x.GetCount()).Returns(1);
            Mock<IIncludedFolderCollection> mockIncludeDb = new Mock<IIncludedFolderCollection>(MockBehavior.Strict);
            using (MockBackgroundFileSystemTaskRunner backgroundTaskRunner = new MockBackgroundFileSystemTaskRunner())
            using (MockVirtualizationInstance mockVirtualization = new MockVirtualizationInstance())
            using (MockGitIndexProjection gitIndexProjection = new MockGitIndexProjection(new[] { "test.txt" }))
            using (WindowsFileSystemVirtualizer virtualizer = new WindowsFileSystemVirtualizer(this.Repo.Context, this.Repo.GitObjects, mockVirtualization, numWorkThreads))
            using (FileSystemCallbacks fileSystemCallbacks = new FileSystemCallbacks(
                this.Repo.Context,
                this.Repo.GitObjects,
                RepoMetadata.Instance,
                new MockBlobSizes(),
                gitIndexProjection,
                backgroundTaskRunner,
                virtualizer,
                mockPlaceholderDb.Object,
                mockIncludeDb.Object))
            {
                try
                {
                    string error;
                    fileSystemCallbacks.TryStart(out error).ShouldEqual(true);

                    Guid enumerationGuid = Guid.NewGuid();

                    byte[] contentId = FileSystemVirtualizer.ConvertShaToContentId("0123456789012345678901234567890123456789");
                    byte[] placeholderVersion = WindowsFileSystemVirtualizer.PlaceholderVersionId;

                    uint fileLength = 100;
                    MockGVFSGitObjects mockGVFSGitObjects = this.Repo.GitObjects as MockGVFSGitObjects;
                    mockGVFSGitObjects.FileLength = fileLength;
                    mockVirtualization.WriteFileReturnResult = HResult.Ok;

                    mockVirtualization.requiredCallbacks.GetFileDataCallback(
                        commandId: 1,
                        relativePath: "test.txt",
                        byteOffset: 0,
                        length: fileLength,
                        dataStreamId: Guid.NewGuid(),
                        contentId: contentId,
                        providerId: placeholderVersion,
                        triggeringProcessId: 2,
                        triggeringProcessImageFileName: "UnitTest").ShouldEqual(HResult.Pending);

                    mockVirtualization.WaitForCompletionStatus().ShouldEqual(HResult.Ok);
                }
                finally
                {
                    fileSystemCallbacks.Stop();
                }
            }

            mockPlaceholderDb.VerifyAll();
            mockIncludeDb.VerifyAll();
        }

        [TestCase]
        [Category(CategoryConstants.ExceptionExpected)]
        public void OnGetFileStreamHandlesTryCopyBlobContentStreamThrowingOperationCanceled()
        {
            Mock<IPlaceholderCollection> mockPlaceholderDb = new Mock<IPlaceholderCollection>(MockBehavior.Strict);
            mockPlaceholderDb.Setup(x => x.GetCount()).Returns(1);
            Mock<IIncludedFolderCollection> mockIncludeDb = new Mock<IIncludedFolderCollection>(MockBehavior.Strict);
            using (MockBackgroundFileSystemTaskRunner backgroundTaskRunner = new MockBackgroundFileSystemTaskRunner())
            using (MockVirtualizationInstance mockVirtualization = new MockVirtualizationInstance())
            using (MockGitIndexProjection gitIndexProjection = new MockGitIndexProjection(new[] { "test.txt" }))
            using (WindowsFileSystemVirtualizer virtualizer = new WindowsFileSystemVirtualizer(this.Repo.Context, this.Repo.GitObjects, mockVirtualization, numWorkThreads))
            using (FileSystemCallbacks fileSystemCallbacks = new FileSystemCallbacks(
                this.Repo.Context,
                this.Repo.GitObjects,
                RepoMetadata.Instance,
                new MockBlobSizes(),
                gitIndexProjection,
                backgroundTaskRunner,
                virtualizer,
                mockPlaceholderDb.Object,
                mockIncludeDb.Object))
            {
                try
                {
                    string error;
                    fileSystemCallbacks.TryStart(out error).ShouldEqual(true);

                    Guid enumerationGuid = Guid.NewGuid();

                    byte[] contentId = FileSystemVirtualizer.ConvertShaToContentId("0123456789012345678901234567890123456789");
                    byte[] placeholderVersion = WindowsFileSystemVirtualizer.PlaceholderVersionId;

                    MockGVFSGitObjects mockGVFSGitObjects = this.Repo.GitObjects as MockGVFSGitObjects;

                    MockTracer mockTracker = this.Repo.Context.Tracer as MockTracer;
                    mockTracker.WaitRelatedEventName = "GetFileStreamHandlerAsyncHandler_OperationCancelled";
                    mockGVFSGitObjects.CancelTryCopyBlobContentStream = true;

                    mockVirtualization.requiredCallbacks.GetFileDataCallback(
                        commandId: 1,
                        relativePath: "test.txt",
                        byteOffset: 0,
                        length: 100,
                        dataStreamId: Guid.NewGuid(),
                        contentId: contentId,
                        providerId: placeholderVersion,
                        triggeringProcessId: 2,
                        triggeringProcessImageFileName: "UnitTest").ShouldEqual(HResult.Pending);

                    mockTracker.WaitForRelatedEvent();
                }
                finally
                {
                    fileSystemCallbacks.Stop();
                }
            }

            mockPlaceholderDb.VerifyAll();
            mockIncludeDb.VerifyAll();
        }

        [TestCase]
        [Category(CategoryConstants.ExceptionExpected)]
        public void OnGetFileStreamHandlesCancellationDuringWriteAction()
        {
            Mock<IPlaceholderCollection> mockPlaceholderDb = new Mock<IPlaceholderCollection>(MockBehavior.Strict);
            mockPlaceholderDb.Setup(x => x.GetCount()).Returns(1);
            Mock<IIncludedFolderCollection> mockIncludeDb = new Mock<IIncludedFolderCollection>(MockBehavior.Strict);
            using (MockBackgroundFileSystemTaskRunner backgroundTaskRunner = new MockBackgroundFileSystemTaskRunner())
            using (MockVirtualizationInstance mockVirtualization = new MockVirtualizationInstance())
            using (MockGitIndexProjection gitIndexProjection = new MockGitIndexProjection(new[] { "test.txt" }))
            using (WindowsFileSystemVirtualizer virtualizer = new WindowsFileSystemVirtualizer(this.Repo.Context, this.Repo.GitObjects, mockVirtualization, numWorkThreads))
            using (FileSystemCallbacks fileSystemCallbacks = new FileSystemCallbacks(
                this.Repo.Context,
                this.Repo.GitObjects,
                RepoMetadata.Instance,
                new MockBlobSizes(),
                gitIndexProjection,
                backgroundTaskRunner,
                virtualizer,
                mockPlaceholderDb.Object,
                mockIncludeDb.Object))
            {
                string error;
                fileSystemCallbacks.TryStart(out error).ShouldEqual(true);

                Guid enumerationGuid = Guid.NewGuid();

                byte[] contentId = FileSystemVirtualizer.ConvertShaToContentId("0123456789012345678901234567890123456789");
                byte[] placeholderVersion = WindowsFileSystemVirtualizer.PlaceholderVersionId;

                uint fileLength = 100;
                MockGVFSGitObjects mockGVFSGitObjects = this.Repo.GitObjects as MockGVFSGitObjects;
                mockGVFSGitObjects.FileLength = fileLength;

                MockTracer mockTracker = this.Repo.Context.Tracer as MockTracer;
                mockTracker.WaitRelatedEventName = "GetFileStreamHandlerAsyncHandler_OperationCancelled";

                mockVirtualization.BlockCreateWriteBuffer(willWaitForRequest: true);
                mockVirtualization.requiredCallbacks.GetFileDataCallback(
                    commandId: 1,
                    relativePath: "test.txt",
                    byteOffset: 0,
                    length: fileLength,
                    dataStreamId: Guid.NewGuid(),
                    contentId: contentId,
                    providerId: placeholderVersion,
                    triggeringProcessId: 2,
                    triggeringProcessImageFileName: "UnitTest").ShouldEqual(HResult.Pending);

                mockVirtualization.WaitForCreateWriteBuffer();
                mockVirtualization.OnCancelCommand(1);
                mockVirtualization.UnblockCreateWriteBuffer();
                mockTracker.WaitForRelatedEvent();

                fileSystemCallbacks.Stop();
            }

            mockPlaceholderDb.VerifyAll();
            mockIncludeDb.VerifyAll();
        }

        [TestCase]
        [Category(CategoryConstants.ExceptionExpected)]
        public void OnGetFileStreamHandlesWriteFailure()
        {
            Mock<IPlaceholderCollection> mockPlaceholderDb = new Mock<IPlaceholderCollection>(MockBehavior.Strict);
            mockPlaceholderDb.Setup(x => x.GetCount()).Returns(1);
            Mock<IIncludedFolderCollection> mockIncludeDb = new Mock<IIncludedFolderCollection>(MockBehavior.Strict);
            using (MockBackgroundFileSystemTaskRunner backgroundTaskRunner = new MockBackgroundFileSystemTaskRunner())
            using (MockVirtualizationInstance mockVirtualization = new MockVirtualizationInstance())
            using (MockGitIndexProjection gitIndexProjection = new MockGitIndexProjection(new[] { "test.txt" }))
            using (WindowsFileSystemVirtualizer virtualizer = new WindowsFileSystemVirtualizer(this.Repo.Context, this.Repo.GitObjects, mockVirtualization, numWorkThreads))
            using (FileSystemCallbacks fileSystemCallbacks = new FileSystemCallbacks(
                this.Repo.Context,
                this.Repo.GitObjects,
                RepoMetadata.Instance,
                new MockBlobSizes(),
                gitIndexProjection,
                backgroundTaskRunner,
                virtualizer,
                mockPlaceholderDb.Object,
                mockIncludeDb.Object))
            {
                try
                {
                    string error;
                    fileSystemCallbacks.TryStart(out error).ShouldEqual(true);

                    Guid enumerationGuid = Guid.NewGuid();

                    byte[] contentId = FileSystemVirtualizer.ConvertShaToContentId("0123456789012345678901234567890123456789");
                    byte[] placeholderVersion = WindowsFileSystemVirtualizer.PlaceholderVersionId;

                    uint fileLength = 100;
                    MockGVFSGitObjects mockGVFSGitObjects = this.Repo.GitObjects as MockGVFSGitObjects;
                    mockGVFSGitObjects.FileLength = fileLength;

                    MockTracer mockTracker = this.Repo.Context.Tracer as MockTracer;

                    mockVirtualization.WriteFileReturnResult = HResult.InternalError;
                    mockVirtualization.requiredCallbacks.GetFileDataCallback(
                        commandId: 1,
                        relativePath: "test.txt",
                        byteOffset: 0,
                        length: fileLength,
                        dataStreamId: Guid.NewGuid(),
                        contentId: contentId,
                        providerId: placeholderVersion,
                        triggeringProcessId: 2,
                        triggeringProcessImageFileName: "UnitTest").ShouldEqual(HResult.Pending);

                    mockVirtualization.WaitForCompletionStatus().ShouldEqual(mockVirtualization.WriteFileReturnResult);
                }
                finally
                {
                    fileSystemCallbacks.Stop();
                }
            }

            mockPlaceholderDb.VerifyAll();
            mockIncludeDb.VerifyAll();
        }

        [TestCase]
        [Category(CategoryConstants.ExceptionExpected)]
        public void OnGetFileStreamHandlesHResultHandleResult()
        {
            Mock<IPlaceholderCollection> mockPlaceholderDb = new Mock<IPlaceholderCollection>(MockBehavior.Strict);
            mockPlaceholderDb.Setup(x => x.GetCount()).Returns(1);
            Mock<IIncludedFolderCollection> mockIncludeDb = new Mock<IIncludedFolderCollection>(MockBehavior.Strict);
            using (MockBackgroundFileSystemTaskRunner backgroundTaskRunner = new MockBackgroundFileSystemTaskRunner())
            using (MockVirtualizationInstance mockVirtualization = new MockVirtualizationInstance())
            using (MockGitIndexProjection gitIndexProjection = new MockGitIndexProjection(new[] { "test.txt" }))
            using (WindowsFileSystemVirtualizer virtualizer = new WindowsFileSystemVirtualizer(this.Repo.Context, this.Repo.GitObjects, mockVirtualization, numWorkThreads))
            using (FileSystemCallbacks fileSystemCallbacks = new FileSystemCallbacks(
                this.Repo.Context,
                this.Repo.GitObjects,
                RepoMetadata.Instance,
                new MockBlobSizes(),
                gitIndexProjection,
                backgroundTaskRunner,
                virtualizer,
                mockPlaceholderDb.Object,
                mockIncludeDb.Object))
            {
                try
                {
                    string error;
                    fileSystemCallbacks.TryStart(out error).ShouldEqual(true);

                    Guid enumerationGuid = Guid.NewGuid();

                    byte[] contentId = FileSystemVirtualizer.ConvertShaToContentId("0123456789012345678901234567890123456789");
                    byte[] placeholderVersion = WindowsFileSystemVirtualizer.PlaceholderVersionId;

                    uint fileLength = 100;
                    MockGVFSGitObjects mockGVFSGitObjects = this.Repo.GitObjects as MockGVFSGitObjects;
                    mockGVFSGitObjects.FileLength = fileLength;

                    MockTracer mockTracker = this.Repo.Context.Tracer as MockTracer;

                    mockVirtualization.WriteFileReturnResult = HResult.Handle;
                    mockVirtualization.requiredCallbacks.GetFileDataCallback(
                        commandId: 1,
                        relativePath: "test.txt",
                        byteOffset: 0,
                        length: fileLength,
                        dataStreamId: Guid.NewGuid(),
                        contentId: contentId,
                        providerId: placeholderVersion,
                        triggeringProcessId: 2,
                        triggeringProcessImageFileName: "UnitTest").ShouldEqual(HResult.Pending);

                    HResult result = mockVirtualization.WaitForCompletionStatus();
                    result.ShouldEqual(mockVirtualization.WriteFileReturnResult);
                    mockTracker.RelatedErrorEvents.ShouldBeEmpty();
                }
                finally
                {
                    fileSystemCallbacks.Stop();
                }
            }

            mockPlaceholderDb.VerifyAll();
            mockIncludeDb.VerifyAll();
        }
    }
}
