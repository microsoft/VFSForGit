using GVFS.Common;
using GVFS.Platform.Mac;
using GVFS.Tests.Should;
using GVFS.UnitTests.Category;
using GVFS.UnitTests.Mock.Git;
using GVFS.UnitTests.Mock.Mac;
using GVFS.UnitTests.Mock.Virtualization.Background;
using GVFS.UnitTests.Mock.Virtualization.BlobSize;
using GVFS.UnitTests.Mock.Virtualization.Projection;
using GVFS.UnitTests.Virtual;
using GVFS.Virtualization;
using GVFS.Virtualization.FileSystem;
using NUnit.Framework;
using PrjFSLib.Mac;
using System;
using System.Collections.Generic;
using System.IO;

namespace GVFS.UnitTests.Platform.Mac
{
    [TestFixture]
    public class MacFileSystemVirtualizerTests : TestsWithCommonRepo
    {
        private static readonly ushort FileMode644 = Convert.ToUInt16("644", 8);
        private static readonly ushort FileMode664 = Convert.ToUInt16("664", 8);
        private static readonly ushort FileMode755 = Convert.ToUInt16("755", 8);

        private static readonly Dictionary<Result, FSResult> MappedResults = new Dictionary<Result, FSResult>()
        {
            { Result.Success, FSResult.Ok },
            { Result.EFileNotFound, FSResult.FileOrPathNotFound },
            { Result.EPathNotFound, FSResult.FileOrPathNotFound },
        };

        [TestCase]
        public void ResultToFSResultMapsHResults()
        {
            foreach (Result result in Enum.GetValues(typeof(Result)))
            {
                if (MappedResults.ContainsKey(result))
                {
                    MacFileSystemVirtualizer.ResultToFSResult(result).ShouldEqual(MappedResults[result]);
                }
                else
                {
                    MacFileSystemVirtualizer.ResultToFSResult(result).ShouldEqual(FSResult.IOError);
                }
            }
        }

        [TestCase]
        public void DeleteFile()
        {
            using (MockVirtualizationInstance mockVirtualization = new MockVirtualizationInstance())
            using (MacFileSystemVirtualizer virtualizer = new MacFileSystemVirtualizer(this.Repo.Context, this.Repo.GitObjects, mockVirtualization))
            {
                UpdateFailureReason failureReason = UpdateFailureReason.NoFailure;

                mockVirtualization.DeleteFileResult = Result.Success;
                mockVirtualization.DeleteFileUpdateFailureCause = UpdateFailureCause.NoFailure;
                virtualizer
                    .DeleteFile("test.txt", UpdatePlaceholderType.AllowReadOnly, out failureReason)
                    .ShouldEqual(new FileSystemResult(FSResult.Ok, (int)mockVirtualization.DeleteFileResult));
                failureReason.ShouldEqual((UpdateFailureReason)mockVirtualization.DeleteFileUpdateFailureCause);

                mockVirtualization.DeleteFileResult = Result.EFileNotFound;
                mockVirtualization.DeleteFileUpdateFailureCause = UpdateFailureCause.NoFailure;
                virtualizer
                    .DeleteFile("test.txt", UpdatePlaceholderType.AllowReadOnly, out failureReason)
                    .ShouldEqual(new FileSystemResult(FSResult.FileOrPathNotFound, (int)mockVirtualization.DeleteFileResult));
                failureReason.ShouldEqual((UpdateFailureReason)mockVirtualization.DeleteFileUpdateFailureCause);

                // TODO: What will the result be when the UpdateFailureCause is DirtyData
                mockVirtualization.DeleteFileResult = Result.EInvalidOperation;

                // TODO: The result should probably be VirtualizationInvalidOperation but for now it's IOError
                mockVirtualization.DeleteFileUpdateFailureCause = UpdateFailureCause.DirtyData;
                virtualizer
                    .DeleteFile("test.txt", UpdatePlaceholderType.AllowReadOnly, out failureReason)
                    .ShouldEqual(new FileSystemResult(FSResult.IOError, (int)mockVirtualization.DeleteFileResult));
                failureReason.ShouldEqual((UpdateFailureReason)mockVirtualization.DeleteFileUpdateFailureCause);
            }
        }

        [TestCase]
        public void UpdatePlaceholderIfNeeded()
        {
            using (MockBackgroundFileSystemTaskRunner backgroundTaskRunner = new MockBackgroundFileSystemTaskRunner())
            using (MockVirtualizationInstance mockVirtualization = new MockVirtualizationInstance())
            using (MockGitIndexProjection gitIndexProjection = new MockGitIndexProjection(new[] { "test.txt" }))
            using (MacFileSystemVirtualizer virtualizer = new MacFileSystemVirtualizer(this.Repo.Context, this.Repo.GitObjects, mockVirtualization))
            using (FileSystemCallbacks fileSystemCallbacks = new FileSystemCallbacks(
                this.Repo.Context,
                this.Repo.GitObjects,
                RepoMetadata.Instance,
                new MockBlobSizes(),
                gitIndexProjection,
                backgroundTaskRunner,
                virtualizer))
            {
                gitIndexProjection.MockFileModes.TryAdd("test" + Path.DirectorySeparatorChar + "test.txt", FileMode644);
                string error;
                fileSystemCallbacks.TryStart(out error).ShouldEqual(true);

                UpdateFailureReason failureReason = UpdateFailureReason.NoFailure;

                mockVirtualization.UpdatePlaceholderIfNeededResult = Result.Success;
                mockVirtualization.UpdatePlaceholderIfNeededFailureCause = UpdateFailureCause.NoFailure;
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
                    .ShouldEqual(new FileSystemResult(FSResult.Ok, (int)mockVirtualization.UpdatePlaceholderIfNeededResult));
                failureReason.ShouldEqual((UpdateFailureReason)mockVirtualization.UpdatePlaceholderIfNeededFailureCause);

                mockVirtualization.UpdatePlaceholderIfNeededResult = Result.EFileNotFound;
                mockVirtualization.UpdatePlaceholderIfNeededFailureCause = UpdateFailureCause.NoFailure;
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
                    .ShouldEqual(new FileSystemResult(FSResult.FileOrPathNotFound, (int)mockVirtualization.UpdatePlaceholderIfNeededResult));
                failureReason.ShouldEqual((UpdateFailureReason)mockVirtualization.UpdatePlaceholderIfNeededFailureCause);

                // TODO: What will the result be when the UpdateFailureCause is DirtyData
                mockVirtualization.UpdatePlaceholderIfNeededResult = Result.EInvalidOperation;
                mockVirtualization.UpdatePlaceholderIfNeededFailureCause = UpdateFailureCause.DirtyData;

                // TODO: The result should probably be VirtualizationInvalidOperation but for now it's IOError
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
                    .ShouldEqual(new FileSystemResult(FSResult.IOError, (int)mockVirtualization.UpdatePlaceholderIfNeededResult));
                failureReason.ShouldEqual((UpdateFailureReason)mockVirtualization.UpdatePlaceholderIfNeededFailureCause);
                fileSystemCallbacks.Stop();
            }
        }

        [TestCase]
        public void ClearNegativePathCacheIsNoOp()
        {
            using (MockVirtualizationInstance mockVirtualization = new MockVirtualizationInstance())
            using (MacFileSystemVirtualizer virtualizer = new MacFileSystemVirtualizer(this.Repo.Context, this.Repo.GitObjects, mockVirtualization))
            {
                uint totalEntryCount = 0;
                virtualizer.ClearNegativePathCache(out totalEntryCount).ShouldEqual(new FileSystemResult(FSResult.Ok, (int)Result.Success));
                totalEntryCount.ShouldEqual(0U);
            }
        }

        [TestCase]
        public void OnEnumerateDirectoryReturnsSuccessWhenResultsNotInMemory()
        {
            using (MockBackgroundFileSystemTaskRunner backgroundTaskRunner = new MockBackgroundFileSystemTaskRunner())
            using (MockVirtualizationInstance mockVirtualization = new MockVirtualizationInstance())
            using (MockGitIndexProjection gitIndexProjection = new MockGitIndexProjection(new[] { "test.txt" }))
            using (MacFileSystemVirtualizer virtualizer = new MacFileSystemVirtualizer(this.Repo.Context, this.Repo.GitObjects, mockVirtualization))
            using (FileSystemCallbacks fileSystemCallbacks = new FileSystemCallbacks(
                this.Repo.Context,
                this.Repo.GitObjects,
                RepoMetadata.Instance,
                new MockBlobSizes(),
                gitIndexProjection,
                backgroundFileSystemTaskRunner: backgroundTaskRunner,
                fileSystemVirtualizer: virtualizer))
            {
                gitIndexProjection.MockFileModes.TryAdd("test" + Path.DirectorySeparatorChar + "test.txt", FileMode644);

                string error;
                fileSystemCallbacks.TryStart(out error).ShouldEqual(true);

                Guid enumerationGuid = Guid.NewGuid();
                gitIndexProjection.EnumerationInMemory = false;
                mockVirtualization.OnEnumerateDirectory(1, "test", triggeringProcessId: 1, triggeringProcessName: "UnitTests").ShouldEqual(Result.Success);
                mockVirtualization.CreatedPlaceholders.ShouldContain(
                    kvp => kvp.Key.Equals(Path.Combine("test", "test.txt"), StringComparison.OrdinalIgnoreCase) && kvp.Value == FileMode644);
                fileSystemCallbacks.Stop();
            }
        }

        [TestCase]
        public void OnEnumerateDirectoryReturnsSuccessWhenResultsInMemory()
        {
            using (MockBackgroundFileSystemTaskRunner backgroundTaskRunner = new MockBackgroundFileSystemTaskRunner())
            using (MockVirtualizationInstance mockVirtualization = new MockVirtualizationInstance())
            using (MockGitIndexProjection gitIndexProjection = new MockGitIndexProjection(new[] { "test.txt" }))
            using (MacFileSystemVirtualizer virtualizer = new MacFileSystemVirtualizer(this.Repo.Context, this.Repo.GitObjects, mockVirtualization))
            using (FileSystemCallbacks fileSystemCallbacks = new FileSystemCallbacks(
                this.Repo.Context,
                this.Repo.GitObjects,
                RepoMetadata.Instance,
                new MockBlobSizes(),
                gitIndexProjection,
                backgroundFileSystemTaskRunner: backgroundTaskRunner,
                fileSystemVirtualizer: virtualizer))
            {
                gitIndexProjection.MockFileModes.TryAdd("test" + Path.DirectorySeparatorChar + "test.txt", FileMode644);

                string error;
                fileSystemCallbacks.TryStart(out error).ShouldEqual(true);

                Guid enumerationGuid = Guid.NewGuid();
                gitIndexProjection.EnumerationInMemory = true;
                mockVirtualization.OnEnumerateDirectory(1, "test", triggeringProcessId: 1, triggeringProcessName: "UnitTests").ShouldEqual(Result.Success);
                mockVirtualization.CreatedPlaceholders.ShouldContain(
                    kvp => kvp.Key.Equals(Path.Combine("test", "test.txt"), StringComparison.OrdinalIgnoreCase) && kvp.Value == FileMode644);
                gitIndexProjection.ExpandedFolders.ShouldMatchInOrder("test");
                fileSystemCallbacks.Stop();
            }
        }

        [TestCase]
        public void OnEnumerateDirectorySetsFileModes()
        {
            using (MockBackgroundFileSystemTaskRunner backgroundTaskRunner = new MockBackgroundFileSystemTaskRunner())
            using (MockVirtualizationInstance mockVirtualization = new MockVirtualizationInstance())
            using (MockGitIndexProjection gitIndexProjection = new MockGitIndexProjection(new[] { "test644.txt", "test664.txt", "test755.txt" }))
            using (MacFileSystemVirtualizer virtualizer = new MacFileSystemVirtualizer(this.Repo.Context, this.Repo.GitObjects, mockVirtualization))
            using (FileSystemCallbacks fileSystemCallbacks = new FileSystemCallbacks(
                this.Repo.Context,
                this.Repo.GitObjects,
                RepoMetadata.Instance,
                new MockBlobSizes(),
                gitIndexProjection,
                backgroundFileSystemTaskRunner: backgroundTaskRunner,
                fileSystemVirtualizer: virtualizer))
            {
                gitIndexProjection.MockFileModes.TryAdd("test" + Path.DirectorySeparatorChar + "test644.txt", FileMode644);
                gitIndexProjection.MockFileModes.TryAdd("test" + Path.DirectorySeparatorChar + "test664.txt", FileMode664);
                gitIndexProjection.MockFileModes.TryAdd("test" + Path.DirectorySeparatorChar + "test755.txt", FileMode755);

                string error;
                fileSystemCallbacks.TryStart(out error).ShouldEqual(true);

                Guid enumerationGuid = Guid.NewGuid();
                gitIndexProjection.EnumerationInMemory = true;
                mockVirtualization.OnEnumerateDirectory(1, "test", triggeringProcessId: 1, triggeringProcessName: "UnitTests").ShouldEqual(Result.Success);
                mockVirtualization.CreatedPlaceholders.ShouldContain(
                    kvp => kvp.Key.Equals(Path.Combine("test", "test644.txt"), StringComparison.OrdinalIgnoreCase) && kvp.Value == FileMode644);
                mockVirtualization.CreatedPlaceholders.ShouldContain(
                    kvp => kvp.Key.Equals(Path.Combine("test", "test664.txt"), StringComparison.OrdinalIgnoreCase) && kvp.Value == FileMode664);
                mockVirtualization.CreatedPlaceholders.ShouldContain(
                    kvp => kvp.Key.Equals(Path.Combine("test", "test755.txt"), StringComparison.OrdinalIgnoreCase) && kvp.Value == FileMode755);
                fileSystemCallbacks.Stop();
            }
        }

        [TestCase]
        public void OnGetFileStreamReturnsSuccessWhenFileStreamAvailable()
        {
            using (MockBackgroundFileSystemTaskRunner backgroundTaskRunner = new MockBackgroundFileSystemTaskRunner())
            using (MockVirtualizationInstance mockVirtualization = new MockVirtualizationInstance())
            using (MockGitIndexProjection gitIndexProjection = new MockGitIndexProjection(new[] { "test.txt" }))
            using (MacFileSystemVirtualizer virtualizer = new MacFileSystemVirtualizer(this.Repo.Context, this.Repo.GitObjects, mockVirtualization))
            using (FileSystemCallbacks fileSystemCallbacks = new FileSystemCallbacks(
                this.Repo.Context,
                this.Repo.GitObjects,
                RepoMetadata.Instance,
                new MockBlobSizes(),
                gitIndexProjection,
                backgroundFileSystemTaskRunner: backgroundTaskRunner,
                fileSystemVirtualizer: virtualizer))
            {
                string error;
                fileSystemCallbacks.TryStart(out error).ShouldEqual(true);

                byte[] contentId = FileSystemVirtualizer.ConvertShaToContentId("0123456789012345678901234567890123456789");
                byte[] placeholderVersion = MacFileSystemVirtualizer.PlaceholderVersionId;

                uint fileLength = 100;
                MockGVFSGitObjects mockGVFSGitObjects = this.Repo.GitObjects as MockGVFSGitObjects;
                mockGVFSGitObjects.FileLength = fileLength;
                mockVirtualization.WriteFileReturnResult = Result.Success;

                mockVirtualization.OnGetFileStream(
                    commandId: 1,
                    relativePath: "test.txt",
                    providerId: placeholderVersion,
                    contentId: contentId,
                    triggeringProcessId: 2,
                    triggeringProcessName: "UnitTest",
                    fileHandle: IntPtr.Zero).ShouldEqual(Result.Success);

                mockVirtualization.BytesWritten.ShouldEqual(fileLength);

                fileSystemCallbacks.Stop();
            }
        }

        [TestCase]
        [Category(CategoryConstants.ExceptionExpected)]
        public void OnGetFileStreamReturnsErrorWhenWriteFileContentsFails()
        {
            using (MockBackgroundFileSystemTaskRunner backgroundTaskRunner = new MockBackgroundFileSystemTaskRunner())
            using (MockVirtualizationInstance mockVirtualization = new MockVirtualizationInstance())
            using (MockGitIndexProjection gitIndexProjection = new MockGitIndexProjection(new[] { "test.txt" }))
            using (MacFileSystemVirtualizer virtualizer = new MacFileSystemVirtualizer(this.Repo.Context, this.Repo.GitObjects, mockVirtualization))
            using (FileSystemCallbacks fileSystemCallbacks = new FileSystemCallbacks(
                this.Repo.Context,
                this.Repo.GitObjects,
                RepoMetadata.Instance,
                new MockBlobSizes(),
                gitIndexProjection,
                backgroundFileSystemTaskRunner: backgroundTaskRunner,
                fileSystemVirtualizer: virtualizer))
            {
                string error;
                fileSystemCallbacks.TryStart(out error).ShouldEqual(true);

                byte[] contentId = FileSystemVirtualizer.ConvertShaToContentId("0123456789012345678901234567890123456789");
                byte[] placeholderVersion = MacFileSystemVirtualizer.PlaceholderVersionId;

                uint fileLength = 100;
                MockGVFSGitObjects mockGVFSGitObjects = this.Repo.GitObjects as MockGVFSGitObjects;
                mockGVFSGitObjects.FileLength = fileLength;
                mockVirtualization.WriteFileReturnResult = Result.EIOError;

                mockVirtualization.OnGetFileStream(
                    commandId: 1,
                    relativePath: "test.txt",
                    providerId: placeholderVersion,
                    contentId: contentId,
                    triggeringProcessId: 2,
                    triggeringProcessName: "UnitTest",
                    fileHandle: IntPtr.Zero).ShouldEqual(Result.EIOError);

                fileSystemCallbacks.Stop();
            }
        }
    }
}
