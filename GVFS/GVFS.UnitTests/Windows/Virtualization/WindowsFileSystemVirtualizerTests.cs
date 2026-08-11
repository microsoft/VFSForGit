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
using System.Threading;
using System.Threading.Tasks;
using System.Linq;
using GVFS.Common.Tracing;

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
        public void DeleteFileReturnsIOErrorWhenVirtualizationInstanceThrows()
        {
            using (MockVirtualizationInstance mockVirtualization = new MockVirtualizationInstance())
            using (WindowsFileSystemVirtualizer virtualizer = new WindowsFileSystemVirtualizer(this.Repo.Context, this.Repo.GitObjects, mockVirtualization, numWorkThreads))
            {
                // A native ProjFS failure (for example a NullReferenceException surfaced from
                // projectedfslib.dll under memory pressure) must fail this single delete rather than
                // propagate out and crash the mount.
                mockVirtualization.DeleteFileException = new NullReferenceException("simulated native ProjFS failure");

                UpdateFailureReason failureReason = UpdateFailureReason.DirtyData;
                virtualizer
                    .DeleteFile("test.txt", UpdatePlaceholderType.AllowReadOnly, out failureReason)
                    .ShouldEqual(new FileSystemResult(FSResult.IOError, (int)HResult.InternalError));
                failureReason.ShouldEqual(UpdateFailureReason.NoFailure);
            }
        }

        [TestCase]
        public void OutboundProjFSOperationsReturnFailureWhenVirtualizationInstanceThrows()
        {
            using (MockVirtualizationInstance mockVirtualization = new MockVirtualizationInstance())
            using (WindowsFileSystemVirtualizer virtualizer = new WindowsFileSystemVirtualizer(this.Repo.Context, this.Repo.GitObjects, mockVirtualization, numWorkThreads))
            {
                // Every GVFS-initiated native ProjFS operation must fail the single call rather than
                // propagate a native exception (e.g. a NullReferenceException from projectedfslib.dll
                // under memory pressure) and crash the mount.
                mockVirtualization.NativeCallException = new NullReferenceException("simulated native ProjFS failure");

                virtualizer
                    .ClearNegativePathCache(out uint _)
                    .ShouldEqual(new FileSystemResult(FSResult.IOError, (int)HResult.InternalError));

                UpdateFailureReason failureReason = UpdateFailureReason.DirtyData;
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
                    .ShouldEqual(new FileSystemResult(FSResult.IOError, (int)HResult.InternalError));
                failureReason.ShouldEqual(UpdateFailureReason.NoFailure);
            }
        }

        [TestCase]
        public void WritePlaceholderOperationsReturnFailureWhenVirtualizationInstanceThrows()
        {
            using (WindowsFileSystemVirtualizerTester tester = new WindowsFileSystemVirtualizerTester(this.Repo))
            {
                tester.MockVirtualization.NativeCallException = new NullReferenceException("simulated native ProjFS failure");

                tester.WindowsVirtualizer
                    .WritePlaceholderFile("test.txt", endOfFile: 15, sha: new string('0', 40))
                    .ShouldEqual(new FileSystemResult(FSResult.IOError, (int)HResult.InternalError));

                tester.WindowsVirtualizer
                    .WritePlaceholderDirectory("testDir")
                    .ShouldEqual(new FileSystemResult(FSResult.IOError, (int)HResult.InternalError));
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
                tester.MockVirtualization.RequiredCallbacks.StartDirectoryEnumerationCallback(1, enumerationGuid, "test", TriggeringProcessId, TriggeringProcessImageFileName).ShouldEqual(HResult.Pending);
                tester.MockVirtualization.WaitForCompletionStatus().ShouldEqual(HResult.Ok);
                tester.MockVirtualization.RequiredCallbacks.EndDirectoryEnumerationCallback(enumerationGuid).ShouldEqual(HResult.Ok);
            }
        }

        [TestCase]
        public void OnStartDirectoryEnumerationReturnsSuccessWhenResultsInMemory()
        {
            using (WindowsFileSystemVirtualizerTester tester = new WindowsFileSystemVirtualizerTester(this.Repo, new[] { "test" }))
            {
                Guid enumerationGuid = Guid.NewGuid();
                tester.GitIndexProjection.EnumerationInMemory = true;
                tester.MockVirtualization.RequiredCallbacks.StartDirectoryEnumerationCallback(1, enumerationGuid, "test", TriggeringProcessId, TriggeringProcessImageFileName).ShouldEqual(HResult.Ok);
                tester.MockVirtualization.RequiredCallbacks.EndDirectoryEnumerationCallback(enumerationGuid).ShouldEqual(HResult.Ok);
            }
        }

        [TestCase]
        public void HeartbeatMetadataReportsActiveEnumerationCount()
        {
            using (WindowsFileSystemVirtualizerTester tester = new WindowsFileSystemVirtualizerTester(this.Repo, new[] { "test" }))
            {
                tester.GitIndexProjection.EnumerationInMemory = true;

                tester.MockVirtualization.RequiredCallbacks.StartDirectoryEnumerationCallback(1, Guid.NewGuid(), "test", TriggeringProcessId, TriggeringProcessImageFileName).ShouldEqual(HResult.Ok);
                tester.MockVirtualization.RequiredCallbacks.StartDirectoryEnumerationCallback(2, Guid.NewGuid(), "test", TriggeringProcessId, TriggeringProcessImageFileName).ShouldEqual(HResult.Ok);

                EventMetadata metadata = new EventMetadata();
                tester.WindowsVirtualizer.AddHeartbeatMetadata(metadata);

                metadata.ContainsKey("ActiveEnumerationCount").ShouldBeTrue();
                ((int)metadata["ActiveEnumerationCount"]).ShouldEqual(2);
                metadata.ContainsKey("ActiveCommandCount").ShouldBeTrue();
            }
        }

        [TestCase]
        public void StaleEnumerationsAreNotEvictedWhenDisabled()
        {
            using (WindowsFileSystemVirtualizerTester tester = new WindowsFileSystemVirtualizerTester(this.Repo, new[] { "test" }))
            {
                tester.GitIndexProjection.EnumerationInMemory = true;

                // Eviction is disabled by default (gvfs.max-active-enumerations unset => 0). Even with
                // a zero stale timeout - which would make every enumeration eligible - nothing is evicted.
                tester.WindowsVirtualizer.MaxActiveEnumerationsForTest = 0;
                tester.WindowsVirtualizer.ActiveEnumerationStaleTimeoutForTest = TimeSpan.Zero;

                Guid firstId = Guid.NewGuid();
                Guid secondId = Guid.NewGuid();
                tester.MockVirtualization.RequiredCallbacks.StartDirectoryEnumerationCallback(1, firstId, "test", TriggeringProcessId, TriggeringProcessImageFileName).ShouldEqual(HResult.Ok);
                Thread.Sleep(20);
                tester.MockVirtualization.RequiredCallbacks.StartDirectoryEnumerationCallback(2, secondId, "test", TriggeringProcessId, TriggeringProcessImageFileName).ShouldEqual(HResult.Ok);

                tester.WindowsVirtualizer.ForceEnumerationEvictionSweepForTest();

                // Both enumerations are still present (a missing id would return InternalError).
                tester.MockVirtualization.RequiredCallbacks.EndDirectoryEnumerationCallback(firstId).ShouldEqual(HResult.Ok);
                tester.MockVirtualization.RequiredCallbacks.EndDirectoryEnumerationCallback(secondId).ShouldEqual(HResult.Ok);
            }
        }

        [TestCase]
        public void StaleEnumerationsAreEvictedWhenEnabledButLiveOnesAreKept()
        {
            using (WindowsFileSystemVirtualizerTester tester = new WindowsFileSystemVirtualizerTester(this.Repo, new[] { "test" }))
            {
                tester.GitIndexProjection.EnumerationInMemory = true;

                // Enable eviction above 1 live enumeration, treating anything idle longer than 20ms as stale.
                tester.WindowsVirtualizer.MaxActiveEnumerationsForTest = 1;
                tester.WindowsVirtualizer.ActiveEnumerationStaleTimeoutForTest = TimeSpan.FromMilliseconds(20);

                Guid staleId = Guid.NewGuid();
                tester.MockVirtualization.RequiredCallbacks.StartDirectoryEnumerationCallback(1, staleId, "test", TriggeringProcessId, TriggeringProcessImageFileName).ShouldEqual(HResult.Ok);

                // Let the first enumeration age well past the stale timeout before adding a fresh one.
                Thread.Sleep(200);

                Guid freshId = Guid.NewGuid();
                tester.MockVirtualization.RequiredCallbacks.StartDirectoryEnumerationCallback(2, freshId, "test", TriggeringProcessId, TriggeringProcessImageFileName).ShouldEqual(HResult.Ok);

                tester.WindowsVirtualizer.ForceEnumerationEvictionSweepForTest();

                // The stale enumeration was evicted: its End now fails to find it (fails loudly, does
                // not silently return partial results). The fresh enumeration is retained.
                tester.MockVirtualization.RequiredCallbacks.EndDirectoryEnumerationCallback(staleId).ShouldEqual(HResult.InternalError);
                tester.MockVirtualization.RequiredCallbacks.EndDirectoryEnumerationCallback(freshId).ShouldEqual(HResult.Ok);
            }
        }

        [TestCase]
        public void GetDirectoryEnumerationTagsEvictedVersusUnknownId()
        {
            using (WindowsFileSystemVirtualizerTester tester = new WindowsFileSystemVirtualizerTester(this.Repo, new[] { "test" }))
            {
                tester.GitIndexProjection.EnumerationInMemory = true;

                tester.WindowsVirtualizer.MaxActiveEnumerationsForTest = 1;
                tester.WindowsVirtualizer.ActiveEnumerationStaleTimeoutForTest = TimeSpan.FromMilliseconds(20);

                Guid staleId = Guid.NewGuid();
                tester.MockVirtualization.RequiredCallbacks.StartDirectoryEnumerationCallback(1, staleId, "test", TriggeringProcessId, TriggeringProcessImageFileName).ShouldEqual(HResult.Ok);

                Thread.Sleep(200);

                Guid freshId = Guid.NewGuid();
                tester.MockVirtualization.RequiredCallbacks.StartDirectoryEnumerationCallback(2, freshId, "test", TriggeringProcessId, TriggeringProcessImageFileName).ShouldEqual(HResult.Ok);

                tester.WindowsVirtualizer.ForceEnumerationEvictionSweepForTest();

                MockTracer mockTracker = this.Repo.Context.Tracer as MockTracer;

                // A Get for the evicted enumeration is attributed to GVFS eviction (self-inflicted).
                // results is unused on the failure path, so null is safe.
                tester.MockVirtualization.RequiredCallbacks.GetDirectoryEnumerationCallback(3, staleId, string.Empty, false, null).ShouldEqual(HResult.InternalError);
                mockTracker.RelatedErrorEvents.Any(
                    e => e.Contains("Failed to find active enumeration ID") && e.Contains("\"EnumerationFailureReason\":\"Evicted\"")).ShouldBeTrue();

                // A Get for an ID GVFS never held is attributed to a ProjFS unknown-ID delivery.
                Guid neverSeenId = Guid.NewGuid();
                tester.MockVirtualization.RequiredCallbacks.GetDirectoryEnumerationCallback(4, neverSeenId, string.Empty, false, null).ShouldEqual(HResult.InternalError);
                mockTracker.RelatedErrorEvents.Any(
                    e => e.Contains("Failed to find active enumeration ID") && e.Contains("\"EnumerationFailureReason\":\"Unknown\"")).ShouldBeTrue();
            }
        }

        [TestCase]
        public void GetPlaceholderInformationHandlerPathNotProjected()
        {
            using (WindowsFileSystemVirtualizerTester tester = new WindowsFileSystemVirtualizerTester(this.Repo))
            {
                tester.MockVirtualization.RequiredCallbacks.GetPlaceholderInfoCallback(1, "doesNotExist", TriggeringProcessId, TriggeringProcessImageFileName).ShouldEqual(HResult.FileNotFound);
            }
        }

        [TestCase]
        public void GetPlaceholderInformationHandlerPathProjected()
        {
            using (WindowsFileSystemVirtualizerTester tester = new WindowsFileSystemVirtualizerTester(this.Repo))
            {
                tester.MockVirtualization.RequiredCallbacks.GetPlaceholderInfoCallback(1, "test.txt", TriggeringProcessId, TriggeringProcessImageFileName).ShouldEqual(HResult.Pending);
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

                tester.MockVirtualization.RequiredCallbacks.GetPlaceholderInfoCallback(1, "test.txt", TriggeringProcessId, TriggeringProcessImageFileName).ShouldEqual(HResult.Pending);

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
                tester.MockVirtualization.RequiredCallbacks.GetPlaceholderInfoCallback(1, "test.txt", TriggeringProcessId, TriggeringProcessImageFileName).ShouldEqual(HResult.Pending);
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
                tester.MockVirtualization.RequiredCallbacks.GetPlaceholderInfoCallback(1, "test.txt", TriggeringProcessId, TriggeringProcessImageFileName).ShouldEqual(HResult.Pending);

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

                // The failure is tagged as a ProjFS write failure (a cause outside gvfs.exe's
                // control) so telemetry can bucket it apart from actionable hydration failures.
                MockTracer mockTracker = this.Repo.Context.Tracer as MockTracer;
                mockTracker.RelatedErrorEvents.Any(
                    e => e.Contains("\"BlobHydrationFailureCategory\":\"ProjFSWriteFailed\"")).ShouldBeTrue();
            }
        }

        [TestCase]
        [Category(CategoryConstants.ExceptionExpected)]
        public void OnGetFileStreamTagsSizeMismatch()
        {
            using (WindowsFileSystemVirtualizerTester tester = new WindowsFileSystemVirtualizerTester(this.Repo))
            {
                // The blob length served (FileLength) differs from the length ProjFS requested
                // (DefaultFileLength), so hydration fails with a size mismatch (actionable cause).
                MockGVFSGitObjects mockGVFSGitObjects = this.Repo.GitObjects as MockGVFSGitObjects;
                mockGVFSGitObjects.FileLength = MockGVFSGitObjects.DefaultFileLength - 1;

                tester.InvokeGetFileDataCallback(expectedResult: HResult.Pending);
                tester.MockVirtualization.WaitForCompletionStatus();

                MockTracer mockTracker = this.Repo.Context.Tracer as MockTracer;
                mockTracker.RelatedErrorEvents.Any(
                    e => e.Contains("\"BlobHydrationFailureCategory\":\"SizeMismatch\"")).ShouldBeTrue();
            }
        }

        [TestCase]
        [Category(CategoryConstants.ExceptionExpected)]
        public void OnGetFileStreamTagsUnexpectedException()
        {
            using (WindowsFileSystemVirtualizerTester tester = new WindowsFileSystemVirtualizerTester(this.Repo))
            {
                // A non-cancellation, non-GetFileStreamException failure hits the generic catch and
                // is tagged Unexpected so it can be triaged separately from known causes.
                MockGVFSGitObjects mockGVFSGitObjects = this.Repo.GitObjects as MockGVFSGitObjects;
                mockGVFSGitObjects.ThrowOnTryCopyBlobContentStream = true;

                tester.InvokeGetFileDataCallback(expectedResult: HResult.Pending);
                tester.MockVirtualization.WaitForCompletionStatus();

                MockTracer mockTracker = this.Repo.Context.Tracer as MockTracer;
                mockTracker.RelatedErrorEvents.Any(
                    e => e.Contains("\"BlobHydrationFailureCategory\":\"Unexpected\"")).ShouldBeTrue();
            }
        }

        [TestCase]
        [Category(CategoryConstants.ExceptionExpected)]
        public void OnGetFileStreamTagsLocalIO()
        {
            using (WindowsFileSystemVirtualizerTester tester = new WindowsFileSystemVirtualizerTester(this.Repo))
            {
                // Reading the blob content throws IOException while copying to the ProjFS buffer,
                // so hydration fails with the LocalIO cause (outside gvfs.exe's control).
                MockGVFSGitObjects mockGVFSGitObjects = this.Repo.GitObjects as MockGVFSGitObjects;
                mockGVFSGitObjects.ThrowIOExceptionDuringCopy = true;

                tester.InvokeGetFileDataCallback(expectedResult: HResult.Pending);
                tester.MockVirtualization.WaitForCompletionStatus();

                MockTracer mockTracker = this.Repo.Context.Tracer as MockTracer;
                mockTracker.RelatedErrorEvents.Any(
                    e => e.Contains("\"BlobHydrationFailureCategory\":\"LocalIO\"")).ShouldBeTrue();
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

        [TestCase]
        public void OnStartDirectoryEnumerationCleansUpEnumerationWhenNativeCompletionFails()
        {
            using (WindowsFileSystemVirtualizerTester tester = new WindowsFileSystemVirtualizerTester(this.Repo))
            {
                MockTracer mockTracker = this.Repo.Context.Tracer as MockTracer;
                mockTracker.WaitRelatedEventName = "StartDirectoryEnumerationAsyncHandler_CommandNotCompleted";

                // Simulate the native ProjFS completion faulting under memory pressure. TryInvokeProjFS
                // must swallow the exception so the mount survives, and because ProjFS never accepted the
                // completion no EndDirectoryEnumeration callback will arrive. The enumeration registered by
                // the async handler must therefore be removed here rather than leaked.
                tester.MockVirtualization.CompleteCommandException = new NullReferenceException("simulated native ProjFS failure");

                Guid enumerationGuid = Guid.NewGuid();
                tester.GitIndexProjection.EnumerationInMemory = false;
                tester.MockVirtualization.RequiredCallbacks.StartDirectoryEnumerationCallback(1, enumerationGuid, "test", TriggeringProcessId, TriggeringProcessImageFileName).ShouldEqual(HResult.Pending);

                // Deterministically wait for the async handler to reach its cleanup path (the enumeration is
                // removed immediately before this event is emitted). If the native failure were reported as a
                // successful completion, this event would never fire.
                mockTracker.WaitForRelatedEvent();

                // The enumeration must already have been removed, so End cannot find it.
                tester.MockVirtualization.RequiredCallbacks.EndDirectoryEnumerationCallback(enumerationGuid).ShouldEqual(HResult.InternalError);
            }
        }
    }
}
