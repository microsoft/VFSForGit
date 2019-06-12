using GVFS.Common;
using GVFS.Platform.Linux;
using GVFS.Tests.Should;
using GVFS.UnitTests.Category;
using GVFS.UnitTests.Mock.Git;
using GVFS.UnitTests.Mock.Linux;
using GVFS.UnitTests.Mock.Virtualization.Background;
using GVFS.UnitTests.Mock.Virtualization.BlobSize;
using GVFS.UnitTests.Mock.Virtualization.Projection;
using GVFS.UnitTests.Virtual;
using GVFS.Virtualization;
using GVFS.Virtualization.FileSystem;
using GVFS.Virtualization.Projection;
using NUnit.Framework;
using PrjFSLib.Linux;
using System;
using System.Collections.Generic;
using System.IO;

namespace GVFS.UnitTests.Platform.Linux
{
    [TestFixture]
    public class LinuxFileSystemVirtualizerTests : TestsWithCommonRepo
    {
        private static readonly Dictionary<Result, FSResult> MappedResults = new Dictionary<Result, FSResult>()
        {
            { Result.Success, FSResult.Ok },
            { Result.EFileNotFound, FSResult.FileOrPathNotFound },
            { Result.EPathNotFound, FSResult.FileOrPathNotFound },
            { Result.EDirectoryNotEmpty, FSResult.DirectoryNotEmpty },
            { Result.EVirtualizationInvalidOperation, FSResult.VirtualizationInvalidOperation },
        };

        [TestCase]
        public void ResultToFSResultMapsHResults()
        {
            foreach (Result result in Enum.GetValues(typeof(Result)))
            {
                if (MappedResults.ContainsKey(result))
                {
                    LinuxFileSystemVirtualizer.ResultToFSResult(result).ShouldEqual(MappedResults[result]);
                }
                else
                {
                    LinuxFileSystemVirtualizer.ResultToFSResult(result).ShouldEqual(FSResult.IOError);
                }
            }
        }

        [TestCase]
        public void DeleteFile()
        {
            using (MockVirtualizationInstance mockVirtualization = new MockVirtualizationInstance())
            using (LinuxFileSystemVirtualizer virtualizer = new LinuxFileSystemVirtualizer(this.Repo.Context, this.Repo.GitObjects, mockVirtualization))
            {
                const string DeleteTestFileName = "deleteMe.txt";
                UpdateFailureReason failureReason = UpdateFailureReason.NoFailure;

                mockVirtualization.DeleteFileResult = Result.Success;
                mockVirtualization.DeleteFileUpdateFailureCause = UpdateFailureCause.NoFailure;
                virtualizer
                    .DeleteFile(DeleteTestFileName, UpdatePlaceholderType.AllowReadOnly, out failureReason)
                    .ShouldEqual(new FileSystemResult(FSResult.Ok, (int)mockVirtualization.DeleteFileResult));
                failureReason.ShouldEqual((UpdateFailureReason)mockVirtualization.DeleteFileUpdateFailureCause);

                mockVirtualization.DeleteFileResult = Result.EFileNotFound;
                mockVirtualization.DeleteFileUpdateFailureCause = UpdateFailureCause.NoFailure;
                virtualizer
                    .DeleteFile(DeleteTestFileName, UpdatePlaceholderType.AllowReadOnly, out failureReason)
                    .ShouldEqual(new FileSystemResult(FSResult.FileOrPathNotFound, (int)mockVirtualization.DeleteFileResult));
                failureReason.ShouldEqual((UpdateFailureReason)mockVirtualization.DeleteFileUpdateFailureCause);

                // TODO: What will the result be when the UpdateFailureCause is DirtyData
                mockVirtualization.DeleteFileResult = Result.EInvalidOperation;

                // TODO: The result should probably be VirtualizationInvalidOperation but for now it's IOError
                mockVirtualization.DeleteFileUpdateFailureCause = UpdateFailureCause.DirtyData;
                virtualizer
                    .DeleteFile(DeleteTestFileName, UpdatePlaceholderType.AllowReadOnly, out failureReason)
                    .ShouldEqual(new FileSystemResult(FSResult.IOError, (int)mockVirtualization.DeleteFileResult));
                failureReason.ShouldEqual((UpdateFailureReason)mockVirtualization.DeleteFileUpdateFailureCause);
            }
        }

        [TestCase]
        public void UpdatePlaceholderIfNeeded()
        {
            const string UpdatePlaceholderFileName = "testUpdatePlaceholder.txt";
            using (MockBackgroundFileSystemTaskRunner backgroundTaskRunner = new MockBackgroundFileSystemTaskRunner())
            using (MockVirtualizationInstance mockVirtualization = new MockVirtualizationInstance())
            using (MockGitIndexProjection gitIndexProjection = new MockGitIndexProjection(new[] { UpdatePlaceholderFileName }))
            using (LinuxFileSystemVirtualizer virtualizer = new LinuxFileSystemVirtualizer(this.Repo.Context, this.Repo.GitObjects, mockVirtualization))
            using (FileSystemCallbacks fileSystemCallbacks = new FileSystemCallbacks(
                this.Repo.Context,
                this.Repo.GitObjects,
                RepoMetadata.Instance,
                new MockBlobSizes(),
                gitIndexProjection,
                backgroundTaskRunner,
                virtualizer))
            {
                gitIndexProjection.MockFileTypesAndModes.TryAdd(
                    UpdatePlaceholderFileName,
                    ConvertFileTypeAndModeToIndexFormat(GitIndexProjection.FileType.Regular, GitIndexProjection.FileMode644));

                string error;
                fileSystemCallbacks.TryStart(out error).ShouldEqual(true);

                UpdateFailureReason failureReason = UpdateFailureReason.NoFailure;

                mockVirtualization.UpdatePlaceholderIfNeededResult = Result.Success;
                mockVirtualization.UpdatePlaceholderIfNeededFailureCause = UpdateFailureCause.NoFailure;
                virtualizer
                    .UpdatePlaceholderIfNeeded(
                        UpdatePlaceholderFileName,
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
                mockVirtualization.UpdatedPlaceholders.ShouldContain(path => path.Key.Equals(UpdatePlaceholderFileName) && path.Value == GitIndexProjection.FileMode644);
                mockVirtualization.UpdatedPlaceholders.Clear();

                mockVirtualization.UpdatePlaceholderIfNeededResult = Result.EFileNotFound;
                mockVirtualization.UpdatePlaceholderIfNeededFailureCause = UpdateFailureCause.NoFailure;
                virtualizer
                    .UpdatePlaceholderIfNeeded(
                        UpdatePlaceholderFileName,
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
                        UpdatePlaceholderFileName,
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
        public void WritePlaceholderForSymLink()
        {
            const string WriteSymLinkFileName = "testWriteSymLink.txt";
            using (MockBackgroundFileSystemTaskRunner backgroundTaskRunner = new MockBackgroundFileSystemTaskRunner())
            using (MockVirtualizationInstance mockVirtualization = new MockVirtualizationInstance())
            using (MockGitIndexProjection gitIndexProjection = new MockGitIndexProjection(new[] { WriteSymLinkFileName }))
            using (LinuxFileSystemVirtualizer virtualizer = new LinuxFileSystemVirtualizer(this.Repo.Context, this.Repo.GitObjects, mockVirtualization))
            using (FileSystemCallbacks fileSystemCallbacks = new FileSystemCallbacks(
                this.Repo.Context,
                this.Repo.GitObjects,
                RepoMetadata.Instance,
                new MockBlobSizes(),
                gitIndexProjection,
                backgroundTaskRunner,
                virtualizer))
            {
                gitIndexProjection.MockFileTypesAndModes.TryAdd(
                    WriteSymLinkFileName,
                    ConvertFileTypeAndModeToIndexFormat(GitIndexProjection.FileType.SymLink, fileMode: 0));

                string error;
                fileSystemCallbacks.TryStart(out error).ShouldEqual(true);

                virtualizer.WritePlaceholderFile(
                    WriteSymLinkFileName,
                    endOfFile: 0,
                    sha: string.Empty).ShouldEqual(new FileSystemResult(FSResult.Ok, (int)Result.Success));

                mockVirtualization.CreatedPlaceholders.ShouldBeEmpty();
                mockVirtualization.CreatedSymLinks.Count.ShouldEqual(1);
                mockVirtualization.CreatedSymLinks.ShouldContain(entry => entry.Equals(WriteSymLinkFileName));

                // Creating a symlink should schedule a background task
                backgroundTaskRunner.Count.ShouldEqual(1);
                backgroundTaskRunner.BackgroundTasks[0].Operation.ShouldEqual(GVFS.Virtualization.Background.FileSystemTask.OperationType.OnFileSymLinkCreated);
                backgroundTaskRunner.BackgroundTasks[0].VirtualPath.ShouldEqual(WriteSymLinkFileName);

                fileSystemCallbacks.Stop();
            }
        }

        [TestCase]
        public void UpdatePlaceholderToSymLink()
        {
            const string PlaceholderToLinkFileName = "testUpdatePlaceholderToLink.txt";
            using (MockBackgroundFileSystemTaskRunner backgroundTaskRunner = new MockBackgroundFileSystemTaskRunner())
            using (MockVirtualizationInstance mockVirtualization = new MockVirtualizationInstance())
            using (MockGitIndexProjection gitIndexProjection = new MockGitIndexProjection(new[] { PlaceholderToLinkFileName }))
            using (LinuxFileSystemVirtualizer virtualizer = new LinuxFileSystemVirtualizer(this.Repo.Context, this.Repo.GitObjects, mockVirtualization))
            using (FileSystemCallbacks fileSystemCallbacks = new FileSystemCallbacks(
                this.Repo.Context,
                this.Repo.GitObjects,
                RepoMetadata.Instance,
                new MockBlobSizes(),
                gitIndexProjection,
                backgroundTaskRunner,
                virtualizer))
            {
                gitIndexProjection.MockFileTypesAndModes.TryAdd(
                    PlaceholderToLinkFileName,
                    ConvertFileTypeAndModeToIndexFormat(GitIndexProjection.FileType.SymLink, fileMode: 0));

                string error;
                fileSystemCallbacks.TryStart(out error).ShouldEqual(true);

                UpdateFailureReason failureReason = UpdateFailureReason.NoFailure;

                mockVirtualization.UpdatePlaceholderIfNeededResult = Result.Success;
                mockVirtualization.UpdatePlaceholderIfNeededFailureCause = UpdateFailureCause.NoFailure;
                virtualizer
                    .UpdatePlaceholderIfNeeded(
                        PlaceholderToLinkFileName,
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
                mockVirtualization.UpdatedPlaceholders.Count.ShouldEqual(0, "UpdatePlaceholderIfNeeded should not be called when converting a placeholder to a link");
                mockVirtualization.CreatedSymLinks.Count.ShouldEqual(1);
                mockVirtualization.CreatedSymLinks.ShouldContain(entry => entry.Equals(PlaceholderToLinkFileName));

                // Creating a symlink should schedule a background task
                backgroundTaskRunner.Count.ShouldEqual(1);
                backgroundTaskRunner.BackgroundTasks[0].Operation.ShouldEqual(GVFS.Virtualization.Background.FileSystemTask.OperationType.OnFileSymLinkCreated);
                backgroundTaskRunner.BackgroundTasks[0].VirtualPath.ShouldEqual(PlaceholderToLinkFileName);

                fileSystemCallbacks.Stop();
            }
        }

        [TestCase]
        public void ClearNegativePathCacheIsNoOp()
        {
            using (MockVirtualizationInstance mockVirtualization = new MockVirtualizationInstance())
            using (LinuxFileSystemVirtualizer virtualizer = new LinuxFileSystemVirtualizer(this.Repo.Context, this.Repo.GitObjects, mockVirtualization))
            {
                uint totalEntryCount = 0;
                virtualizer.ClearNegativePathCache(out totalEntryCount).ShouldEqual(new FileSystemResult(FSResult.Ok, (int)Result.Success));
                totalEntryCount.ShouldEqual(0U);
            }
        }

        [TestCase]
        public void OnEnumerateDirectoryReturnsSuccessWhenResultsNotInMemory()
        {
            const string TestFileName = "test.txt";
            const string TestFolderName = "testFolder";
            string testFilePath = Path.Combine(TestFolderName, TestFileName);
            using (MockBackgroundFileSystemTaskRunner backgroundTaskRunner = new MockBackgroundFileSystemTaskRunner())
            using (MockVirtualizationInstance mockVirtualization = new MockVirtualizationInstance())

            // Don't include TestFolderName as MockGitIndexProjection returns the same list of files regardless of what folder name
            // it is passed
            using (MockGitIndexProjection gitIndexProjection = new MockGitIndexProjection(new[] { TestFileName }))
            using (LinuxFileSystemVirtualizer virtualizer = new LinuxFileSystemVirtualizer(this.Repo.Context, this.Repo.GitObjects, mockVirtualization))
            using (FileSystemCallbacks fileSystemCallbacks = new FileSystemCallbacks(
                this.Repo.Context,
                this.Repo.GitObjects,
                RepoMetadata.Instance,
                new MockBlobSizes(),
                gitIndexProjection,
                backgroundFileSystemTaskRunner: backgroundTaskRunner,
                fileSystemVirtualizer: virtualizer))
            {
                gitIndexProjection.MockFileTypesAndModes.TryAdd(
                    testFilePath,
                    ConvertFileTypeAndModeToIndexFormat(GitIndexProjection.FileType.Regular, GitIndexProjection.FileMode644));

                string error;
                fileSystemCallbacks.TryStart(out error).ShouldEqual(true);

                Guid enumerationGuid = Guid.NewGuid();
                gitIndexProjection.EnumerationInMemory = false;
                mockVirtualization.OnEnumerateDirectory(1, TestFolderName, triggeringProcessId: 1, triggeringProcessName: "UnitTests").ShouldEqual(Result.Success);
                mockVirtualization.CreatedPlaceholders.ShouldContain(
                    kvp => kvp.Key.Equals(testFilePath, StringComparison.Ordinal) && kvp.Value == GitIndexProjection.FileMode644);
                fileSystemCallbacks.Stop();
            }
        }

        [TestCase]
        public void OnEnumerateDirectoryReturnsSuccessWhenResultsInMemory()
        {
            const string TestFileName = "test.txt";
            const string TestFolderName = "testFolder";
            string testFilePath = Path.Combine(TestFolderName, TestFileName);
            using (MockBackgroundFileSystemTaskRunner backgroundTaskRunner = new MockBackgroundFileSystemTaskRunner())
            using (MockVirtualizationInstance mockVirtualization = new MockVirtualizationInstance())

            // Don't include TestFolderName as MockGitIndexProjection returns the same list of files regardless of what folder name
            // it is passed
            using (MockGitIndexProjection gitIndexProjection = new MockGitIndexProjection(new[] { TestFileName }))
            using (LinuxFileSystemVirtualizer virtualizer = new LinuxFileSystemVirtualizer(this.Repo.Context, this.Repo.GitObjects, mockVirtualization))
            using (FileSystemCallbacks fileSystemCallbacks = new FileSystemCallbacks(
                this.Repo.Context,
                this.Repo.GitObjects,
                RepoMetadata.Instance,
                new MockBlobSizes(),
                gitIndexProjection,
                backgroundFileSystemTaskRunner: backgroundTaskRunner,
                fileSystemVirtualizer: virtualizer))
            {
                gitIndexProjection.MockFileTypesAndModes.TryAdd(
                    testFilePath,
                    ConvertFileTypeAndModeToIndexFormat(GitIndexProjection.FileType.Regular, GitIndexProjection.FileMode644));

                string error;
                fileSystemCallbacks.TryStart(out error).ShouldEqual(true);

                Guid enumerationGuid = Guid.NewGuid();
                gitIndexProjection.EnumerationInMemory = true;
                mockVirtualization.OnEnumerateDirectory(1, TestFolderName, triggeringProcessId: 1, triggeringProcessName: "UnitTests").ShouldEqual(Result.Success);
                mockVirtualization.CreatedPlaceholders.ShouldContain(
                    kvp => kvp.Key.Equals(testFilePath, StringComparison.Ordinal) && kvp.Value == GitIndexProjection.FileMode644);
                gitIndexProjection.ExpandedFolders.ShouldMatchInOrder(TestFolderName);
                fileSystemCallbacks.Stop();
            }
        }

        [TestCase]
        public void OnEnumerateDirectorySetsFileModes()
        {
            const string TestFile644Name = "test644.txt";
            const string TestFile664Name = "test664.txt";
            const string TestFile755Name = "test755.txt";
            const string TestFolderName = "testFolder";
            string testFile644Path = Path.Combine(TestFolderName, TestFile644Name);
            string testFile664Path = Path.Combine(TestFolderName, TestFile664Name);
            string testFile755Path = Path.Combine(TestFolderName, TestFile755Name);
            using (MockBackgroundFileSystemTaskRunner backgroundTaskRunner = new MockBackgroundFileSystemTaskRunner())
            using (MockVirtualizationInstance mockVirtualization = new MockVirtualizationInstance())

            // Don't include TestFolderName as MockGitIndexProjection returns the same list of files regardless of what folder name
            // it is passed
            using (MockGitIndexProjection gitIndexProjection = new MockGitIndexProjection(new[] { TestFile644Name, TestFile664Name, TestFile755Name }))
            using (LinuxFileSystemVirtualizer virtualizer = new LinuxFileSystemVirtualizer(this.Repo.Context, this.Repo.GitObjects, mockVirtualization))
            using (FileSystemCallbacks fileSystemCallbacks = new FileSystemCallbacks(
                this.Repo.Context,
                this.Repo.GitObjects,
                RepoMetadata.Instance,
                new MockBlobSizes(),
                gitIndexProjection,
                backgroundFileSystemTaskRunner: backgroundTaskRunner,
                fileSystemVirtualizer: virtualizer))
            {
                gitIndexProjection.MockFileTypesAndModes.TryAdd(
                    testFile644Path,
                    ConvertFileTypeAndModeToIndexFormat(GitIndexProjection.FileType.Regular, GitIndexProjection.FileMode644));
                gitIndexProjection.MockFileTypesAndModes.TryAdd(
                    testFile664Path,
                    ConvertFileTypeAndModeToIndexFormat(GitIndexProjection.FileType.Regular, GitIndexProjection.FileMode664));
                gitIndexProjection.MockFileTypesAndModes.TryAdd(
                    testFile755Path,
                    ConvertFileTypeAndModeToIndexFormat(GitIndexProjection.FileType.Regular, GitIndexProjection.FileMode755));

                string error;
                fileSystemCallbacks.TryStart(out error).ShouldEqual(true);

                Guid enumerationGuid = Guid.NewGuid();
                gitIndexProjection.EnumerationInMemory = true;
                mockVirtualization.OnEnumerateDirectory(1, TestFolderName, triggeringProcessId: 1, triggeringProcessName: "UnitTests").ShouldEqual(Result.Success);
                mockVirtualization.CreatedPlaceholders.ShouldContain(
                    kvp => kvp.Key.Equals(testFile644Path, StringComparison.Ordinal) && kvp.Value == GitIndexProjection.FileMode644);
                mockVirtualization.CreatedPlaceholders.ShouldContain(
                    kvp => kvp.Key.Equals(testFile664Path, StringComparison.Ordinal) && kvp.Value == GitIndexProjection.FileMode664);
                mockVirtualization.CreatedPlaceholders.ShouldContain(
                    kvp => kvp.Key.Equals(testFile755Path, StringComparison.Ordinal) && kvp.Value == GitIndexProjection.FileMode755);
                fileSystemCallbacks.Stop();
            }
        }

        [TestCase]
        public void OnGetFileStreamReturnsSuccessWhenFileStreamAvailable()
        {
            const string TestFileName = "test.txt";
            using (MockBackgroundFileSystemTaskRunner backgroundTaskRunner = new MockBackgroundFileSystemTaskRunner())
            using (MockVirtualizationInstance mockVirtualization = new MockVirtualizationInstance())
            using (MockGitIndexProjection gitIndexProjection = new MockGitIndexProjection(new[] { TestFileName }))
            using (LinuxFileSystemVirtualizer virtualizer = new LinuxFileSystemVirtualizer(this.Repo.Context, this.Repo.GitObjects, mockVirtualization))
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
                byte[] placeholderVersion = LinuxFileSystemVirtualizer.PlaceholderVersionId;

                uint fileLength = 100;
                MockGVFSGitObjects mockGVFSGitObjects = this.Repo.GitObjects as MockGVFSGitObjects;
                mockGVFSGitObjects.FileLength = fileLength;
                mockVirtualization.WriteFileReturnResult = Result.Success;

                mockVirtualization.OnGetFileStream(
                    commandId: 1,
                    relativePath: TestFileName,
                    providerId: placeholderVersion,
                    contentId: contentId,
                    triggeringProcessId: 2,
                    triggeringProcessName: "UnitTest",
                    fd: 0).ShouldEqual(Result.Success);

                mockVirtualization.BytesWritten.ShouldEqual(fileLength);

                fileSystemCallbacks.Stop();
            }
        }

        [TestCase]
        [Category(CategoryConstants.ExceptionExpected)]
        public void OnGetFileStreamReturnsErrorWhenWriteFileContentsFails()
        {
            const string TestFileName = "test.txt";
            using (MockBackgroundFileSystemTaskRunner backgroundTaskRunner = new MockBackgroundFileSystemTaskRunner())
            using (MockVirtualizationInstance mockVirtualization = new MockVirtualizationInstance())
            using (MockGitIndexProjection gitIndexProjection = new MockGitIndexProjection(new[] { TestFileName }))
            using (LinuxFileSystemVirtualizer virtualizer = new LinuxFileSystemVirtualizer(this.Repo.Context, this.Repo.GitObjects, mockVirtualization))
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
                byte[] placeholderVersion = LinuxFileSystemVirtualizer.PlaceholderVersionId;

                uint fileLength = 100;
                MockGVFSGitObjects mockGVFSGitObjects = this.Repo.GitObjects as MockGVFSGitObjects;
                mockGVFSGitObjects.FileLength = fileLength;
                mockVirtualization.WriteFileReturnResult = Result.EIOError;

                mockVirtualization.OnGetFileStream(
                    commandId: 1,
                    relativePath: TestFileName,
                    providerId: placeholderVersion,
                    contentId: contentId,
                    triggeringProcessId: 2,
                    triggeringProcessName: "UnitTest",
                    fd: 0).ShouldEqual(Result.EIOError);

                fileSystemCallbacks.Stop();
            }
        }

        private static ushort ConvertFileTypeAndModeToIndexFormat(GitIndexProjection.FileType fileType, ushort fileMode)
        {
            // Values used in the index file to indicate the type of the file
            const ushort RegularFileIndexEntry = 0x8000;
            const ushort SymLinkFileIndexEntry = 0xA000;
            const ushort GitLinkFileIndexEntry = 0xE000;

            switch (fileType)
            {
                case GitIndexProjection.FileType.Regular:
                    return (ushort)(RegularFileIndexEntry | fileMode);

                case GitIndexProjection.FileType.SymLink:
                    return (ushort)(SymLinkFileIndexEntry | fileMode);

                case GitIndexProjection.FileType.GitLink:
                    return (ushort)(GitLinkFileIndexEntry | fileMode);

                default:
                    Assert.Fail($"Invalid fileType {fileType}");
                    return 0;
            }
        }
    }
}
