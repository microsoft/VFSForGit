using GVFS.Platform.Windows;
using GVFS.Tests.Should;
using GVFS.UnitTests.Category;
using GVFS.UnitTests.Mock.Common;
using GVFS.UnitTests.Mock.Git;
using GVFS.UnitTests.Virtual;
using GVFS.UnitTests.Windows.Mock;
using GVFS.Virtualization.FileSystem;
using Microsoft.Windows.ProjFS;
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
            using (WindowsFileSystemVirtualizerTester tester = new WindowsFileSystemVirtualizerTester(this.Repo))
            {
                Guid enumerationGuid = Guid.NewGuid();
                tester.GitIndexProjection.EnumerationInMemory = false;
                tester.MockVirtualization.requiredCallbacks.StartDirectoryEnumerationCallback(1, enumerationGuid, "test", TriggeringProcessId, TriggeringProcessImageFileName).ShouldEqual(HResult.Pending);
                tester.MockVirtualization.WaitForCompletionStatus().ShouldEqual(HResult.Ok);
                tester.MockVirtualization.requiredCallbacks.EndDirectoryEnumerationCallback(enumerationGuid).ShouldEqual(HResult.Ok);
            }
        }

        [TestCase]
        public void OnStartDirectoryEnumerationReturnsSuccessWhenResultsInMemory()
        {
            using (WindowsFileSystemVirtualizerTester tester = new WindowsFileSystemVirtualizerTester(this.Repo, new[] { "test" }))
            {
                Guid enumerationGuid = Guid.NewGuid();
                tester.GitIndexProjection.EnumerationInMemory = true;
                tester.MockVirtualization.requiredCallbacks.StartDirectoryEnumerationCallback(1, enumerationGuid, "test", TriggeringProcessId, TriggeringProcessImageFileName).ShouldEqual(HResult.Ok);
                tester.MockVirtualization.requiredCallbacks.EndDirectoryEnumerationCallback(enumerationGuid).ShouldEqual(HResult.Ok);
            }
        }

        [TestCase]
        public void GetPlaceholderInformationHandlerPathNotProjected()
        {
            using (WindowsFileSystemVirtualizerTester tester = new WindowsFileSystemVirtualizerTester(this.Repo))
            {
                tester.MockVirtualization.requiredCallbacks.GetPlaceholderInfoCallback(1, "doesNotExist", TriggeringProcessId, TriggeringProcessImageFileName).ShouldEqual(HResult.FileNotFound);
            }
        }

        [TestCase]
        public void GetPlaceholderInformationHandlerPathProjected()
        {
            using (WindowsFileSystemVirtualizerTester tester = new WindowsFileSystemVirtualizerTester(this.Repo))
            {
                tester.MockVirtualization.requiredCallbacks.GetPlaceholderInfoCallback(1, "test.txt", TriggeringProcessId, TriggeringProcessImageFileName).ShouldEqual(HResult.Pending);
                tester.MockVirtualization.WaitForCompletionStatus().ShouldEqual(HResult.Ok);
                tester.MockVirtualization.CreatedPlaceholders.ShouldContain(entry => entry == "test.txt");
                tester.GitIndexProjection.PlaceholdersCreated.ShouldContain(entry => entry == "test.txt");
            }
        }

        [TestCase]
        public void GetPlaceholderInformationHandlerCancelledBeforeSchedulingAsync()
        {
            using (WindowsFileSystemVirtualizerTester tester = new WindowsFileSystemVirtualizerTester(this.Repo))
            {
                tester.GitIndexProjection.BlockIsPathProjected(willWaitForRequest: true);

                Task.Run(() =>
                {
                    // Wait for OnGetPlaceholderInformation to call IsPathProjected and then while it's blocked there
                    // call OnCancelCommand
                    tester.GitIndexProjection.WaitForIsPathProjected();
                    tester.MockVirtualization.OnCancelCommand(1);
                    tester.GitIndexProjection.UnblockIsPathProjected();
                });

                tester.MockVirtualization.requiredCallbacks.GetPlaceholderInfoCallback(1, "test.txt", TriggeringProcessId, TriggeringProcessImageFileName).ShouldEqual(HResult.Pending);

                // Cancelling before GetPlaceholderInformation has registered the command results in placeholders being created
                tester.MockVirtualization.WaitForPlaceholderCreate();
                tester.GitIndexProjection.WaitForPlaceholderCreate();
                tester.MockVirtualization.CreatedPlaceholders.ShouldContain(entry => entry == "test.txt");
                tester.GitIndexProjection.PlaceholdersCreated.ShouldContain(entry => entry == "test.txt");
            }
        }

        [TestCase]
        public void GetPlaceholderInformationHandlerCancelledDuringAsyncCallback()
        {
            using (WindowsFileSystemVirtualizerTester tester = new WindowsFileSystemVirtualizerTester(this.Repo))
            {
                tester.GitIndexProjection.BlockGetProjectedFileInfo(willWaitForRequest: true);
                tester.MockVirtualization.requiredCallbacks.GetPlaceholderInfoCallback(1, "test.txt", TriggeringProcessId, TriggeringProcessImageFileName).ShouldEqual(HResult.Pending);
                tester.GitIndexProjection.WaitForGetProjectedFileInfo();
                tester.MockVirtualization.OnCancelCommand(1);
                tester.GitIndexProjection.UnblockGetProjectedFileInfo();

                // Cancelling in the middle of GetPlaceholderInformation still allows it to create placeholders when the cancellation does not
                // interrupt network requests
                tester.MockVirtualization.WaitForPlaceholderCreate();
                tester.GitIndexProjection.WaitForPlaceholderCreate();
                tester.MockVirtualization.CreatedPlaceholders.ShouldContain(entry => entry == "test.txt");
                tester.GitIndexProjection.PlaceholdersCreated.ShouldContain(entry => entry == "test.txt");
            }
        }

        [TestCase]
        [Category(CategoryConstants.ExceptionExpected)]
        public void GetPlaceholderInformationHandlerCancelledDuringNetworkRequest()
        {
            using (WindowsFileSystemVirtualizerTester tester = new WindowsFileSystemVirtualizerTester(this.Repo))
            {
                MockTracer mockTracker = this.Repo.Context.Tracer as MockTracer;
                mockTracker.WaitRelatedEventName = "GetPlaceholderInformationAsyncHandler_GetProjectedFileInfo_Cancelled";
                tester.GitIndexProjection.ThrowOperationCanceledExceptionOnProjectionRequest = true;
                tester.MockVirtualization.requiredCallbacks.GetPlaceholderInfoCallback(1, "test.txt", TriggeringProcessId, TriggeringProcessImageFileName).ShouldEqual(HResult.Pending);

                // Cancelling in the middle of GetPlaceholderInformation in the middle of a network request should not result in placeholder
                // getting created
                mockTracker.WaitForRelatedEvent();
                tester.MockVirtualization.CreatedPlaceholders.ShouldNotContain(entry => entry == "test.txt");
                tester.GitIndexProjection.PlaceholdersCreated.ShouldNotContain(entry => entry == "test.txt");
            }
        }

        [TestCase]
        public void OnGetFileStreamReturnsInternalErrorWhenOffsetNonZero()
        {
            using (WindowsFileSystemVirtualizerTester tester = new WindowsFileSystemVirtualizerTester(this.Repo))
            {
                tester.InvokeGetFileDataCallback(expectedResult: HResult.InternalError, byteOffset: 10);
            }
        }

        [TestCase]
        public void OnGetFileStreamReturnsInternalErrorWhenPlaceholderVersionDoesNotMatchExpected()
        {
            using (WindowsFileSystemVirtualizerTester tester = new WindowsFileSystemVirtualizerTester(this.Repo))
            {
                byte[] epochId = new byte[] { FileSystemVirtualizer.PlaceholderVersion + 1 };
                tester.InvokeGetFileDataCallback(expectedResult: HResult.InternalError, providerId: epochId);
            }
        }

        [TestCase]
        public void MoveFileIntoDotGitDirectory()
        {
            using (WindowsFileSystemVirtualizerTester tester = new WindowsFileSystemVirtualizerTester(this.Repo))
            {
                NotificationType notificationType = NotificationType.UseExistingMask;
                tester.MockVirtualization.OnNotifyFileRenamed(
                        "test.txt",
                        Path.Combine(".git", "test.txt"),
                        isDirectory: false,
                        triggeringProcessId: TriggeringProcessId,
                        triggeringProcessImageFileName: TriggeringProcessImageFileName,
                        notificationMask: out notificationType);
                notificationType.ShouldEqual(NotificationType.UseExistingMask);
                tester.FileSystemCallbacks.OnIndexFileChangeCallCount.ShouldEqual(0);
                tester.FileSystemCallbacks.OnLogsHeadChangeCallCount.ShouldEqual(0);
                tester.FileSystemCallbacks.OnFileRenamedCallCount.ShouldEqual(1);
                tester.FileSystemCallbacks.OnFolderRenamedCallCount.ShouldEqual(0);
                tester.FileSystemCallbacks.ResetCalls();

                // We don't expect something to rename something from outside the .gitdir to the .git\index, but this
                // verifies that we behave as expected in case that happens
                tester.MockVirtualization.OnNotifyFileRenamed(
                        "test.txt",
                        Path.Combine(".git", "index"),
                        isDirectory: false,
                        triggeringProcessId: TriggeringProcessId,
                        triggeringProcessImageFileName: TriggeringProcessImageFileName,
                        notificationMask: out notificationType);
                notificationType.ShouldEqual(NotificationType.UseExistingMask);
                tester.FileSystemCallbacks.OnIndexFileChangeCallCount.ShouldEqual(1);
                tester.FileSystemCallbacks.OnLogsHeadChangeCallCount.ShouldEqual(0);
                tester.FileSystemCallbacks.OnFileRenamedCallCount.ShouldEqual(1);
                tester.FileSystemCallbacks.OnFolderRenamedCallCount.ShouldEqual(0);
                tester.FileSystemCallbacks.ResetCalls();

                // We don't expect something to rename something from outside the .gitdir to the .git\logs\HEAD, but this
                // verifies that we behave as expected in case that happens
                tester.MockVirtualization.OnNotifyFileRenamed(
                        "test.txt",
                        Path.Combine(".git", "logs\\HEAD"),
                        isDirectory: false,
                        triggeringProcessId: TriggeringProcessId,
                        triggeringProcessImageFileName: TriggeringProcessImageFileName,
                        notificationMask: out notificationType);
                notificationType.ShouldEqual(NotificationType.UseExistingMask);
                tester.FileSystemCallbacks.OnIndexFileChangeCallCount.ShouldEqual(0);
                tester.FileSystemCallbacks.OnLogsHeadChangeCallCount.ShouldEqual(1);
                tester.FileSystemCallbacks.OnFileRenamedCallCount.ShouldEqual(1);
                tester.FileSystemCallbacks.OnFolderRenamedCallCount.ShouldEqual(0);
                tester.FileSystemCallbacks.ResetCalls();
            }
        }

        [TestCase]
        public void MoveFileFromDotGitToSrc()
        {
            using (WindowsFileSystemVirtualizerTester tester = new WindowsFileSystemVirtualizerTester(this.Repo))
            {
                NotificationType notificationType = NotificationType.UseExistingMask;
                tester.MockVirtualization.OnNotifyFileRenamed(
                        Path.Combine(".git", "test.txt"),
                        "test2.txt",
                        isDirectory: false,
                        triggeringProcessId: TriggeringProcessId,
                        triggeringProcessImageFileName: TriggeringProcessImageFileName,
                        notificationMask: out notificationType);
                notificationType.ShouldEqual(NotificationType.UseExistingMask);
                tester.FileSystemCallbacks.OnIndexFileChangeCallCount.ShouldEqual(0);
                tester.FileSystemCallbacks.OnLogsHeadChangeCallCount.ShouldEqual(0);
                tester.FileSystemCallbacks.OnFileRenamedCallCount.ShouldEqual(1);
                tester.FileSystemCallbacks.OnFolderRenamedCallCount.ShouldEqual(0);
            }
        }

        [TestCase]
        public void MoveFile()
        {
            using (WindowsFileSystemVirtualizerTester tester = new WindowsFileSystemVirtualizerTester(this.Repo))
            {
                NotificationType notificationType = NotificationType.UseExistingMask;
                tester.MockVirtualization.OnNotifyFileRenamed(
                    "test.txt",
                    "test2.txt",
                    isDirectory: false,
                    triggeringProcessId: TriggeringProcessId,
                    triggeringProcessImageFileName: TriggeringProcessImageFileName,
                    notificationMask: out notificationType);
                notificationType.ShouldEqual(NotificationType.UseExistingMask);
                tester.FileSystemCallbacks.OnIndexFileChangeCallCount.ShouldEqual(0);
                tester.FileSystemCallbacks.OnLogsHeadChangeCallCount.ShouldEqual(0);
                tester.FileSystemCallbacks.OnFileRenamedCallCount.ShouldEqual(1);
                tester.FileSystemCallbacks.OnFolderRenamedCallCount.ShouldEqual(0);
                tester.FileSystemCallbacks.ResetCalls();

                tester.MockVirtualization.OnNotifyFileRenamed(
                    "test_folder_src",
                    "test_folder_dst",
                    isDirectory: true,
                    triggeringProcessId: TriggeringProcessId,
                    triggeringProcessImageFileName: TriggeringProcessImageFileName,
                    notificationMask: out notificationType);
                notificationType.ShouldEqual(NotificationType.UseExistingMask);
                tester.FileSystemCallbacks.OnIndexFileChangeCallCount.ShouldEqual(0);
                tester.FileSystemCallbacks.OnLogsHeadChangeCallCount.ShouldEqual(0);
                tester.FileSystemCallbacks.OnFileRenamedCallCount.ShouldEqual(0);
                tester.FileSystemCallbacks.OnFolderRenamedCallCount.ShouldEqual(1);
            }
        }

        [TestCase]
        public void OnGetFileStreamReturnsPendingAndCompletesWithSuccessWhenNoFailures()
        {
            using (WindowsFileSystemVirtualizerTester tester = new WindowsFileSystemVirtualizerTester(this.Repo))
            {
                tester.MockVirtualization.WriteFileReturnResult = HResult.Ok;

                tester.InvokeGetFileDataCallback(expectedResult: HResult.Pending);

                tester.MockVirtualization.WaitForCompletionStatus().ShouldEqual(HResult.Ok);
            }
        }

        [TestCase]
        [Category(CategoryConstants.ExceptionExpected)]
        public void OnGetFileStreamHandlesTryCopyBlobContentStreamThrowingOperationCanceled()
        {
            using (WindowsFileSystemVirtualizerTester tester = new WindowsFileSystemVirtualizerTester(this.Repo))
            {
                MockTracer mockTracker = this.Repo.Context.Tracer as MockTracer;
                mockTracker.WaitRelatedEventName = "GetFileStreamHandlerAsyncHandler_OperationCancelled";
                MockGVFSGitObjects mockGVFSGitObjects = this.Repo.GitObjects as MockGVFSGitObjects;
                mockGVFSGitObjects.CancelTryCopyBlobContentStream = true;

                tester.InvokeGetFileDataCallback(expectedResult: HResult.Pending);

                mockTracker.WaitForRelatedEvent();
            }
        }

        [TestCase]
        [Category(CategoryConstants.ExceptionExpected)]
        public void OnGetFileStreamHandlesCancellationDuringWriteAction()
        {
            using (WindowsFileSystemVirtualizerTester tester = new WindowsFileSystemVirtualizerTester(this.Repo))
            {
                MockTracer mockTracker = this.Repo.Context.Tracer as MockTracer;
                mockTracker.WaitRelatedEventName = "GetFileStreamHandlerAsyncHandler_OperationCancelled";

                tester.MockVirtualization.BlockCreateWriteBuffer(willWaitForRequest: true);
                tester.InvokeGetFileDataCallback(expectedResult: HResult.Pending);

                tester.MockVirtualization.WaitForCreateWriteBuffer();
                tester.MockVirtualization.OnCancelCommand(1);
                tester.MockVirtualization.UnblockCreateWriteBuffer();
                mockTracker.WaitForRelatedEvent();
            }
        }

        [TestCase]
        public void OnGetFileStreamHandlesWriteFailure()
        {
            using (WindowsFileSystemVirtualizerTester tester = new WindowsFileSystemVirtualizerTester(this.Repo))
            {
                tester.MockVirtualization.WriteFileReturnResult = HResult.InternalError;
                tester.InvokeGetFileDataCallback(expectedResult: HResult.Pending);

                HResult result = tester.MockVirtualization.WaitForCompletionStatus();
                result.ShouldEqual(tester.MockVirtualization.WriteFileReturnResult);
            }
        }

        [TestCase]
        [Category(CategoryConstants.ExceptionExpected)]
        public void OnGetFileStreamHandlesHResultHandleResult()
        {
            using (WindowsFileSystemVirtualizerTester tester = new WindowsFileSystemVirtualizerTester(this.Repo))
            {
                tester.MockVirtualization.WriteFileReturnResult = HResult.Handle;
                tester.InvokeGetFileDataCallback(expectedResult: HResult.Pending);

                HResult result = tester.MockVirtualization.WaitForCompletionStatus();
                result.ShouldEqual(tester.MockVirtualization.WriteFileReturnResult);
                MockTracer mockTracker = this.Repo.Context.Tracer as MockTracer;
                mockTracker.RelatedErrorEvents.ShouldBeEmpty();
            }
        }
    }
}
