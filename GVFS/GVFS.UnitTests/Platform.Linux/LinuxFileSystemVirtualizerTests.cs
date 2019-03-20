using GVFS.Common;
using GVFS.Platform.Linux;
using GVFS.Tests.Should;
using GVFS.UnitTests.Category;
using GVFS.UnitTests.Mock.Git;
using GVFS.UnitTests.Mock.Linux;
using GVFS.UnitTests.Virtual;
using GVFS.Virtualization.Background;
using GVFS.Virtualization.FileSystem;
using GVFS.Virtualization.Projection;
using NUnit.Framework;
using PrjFSLib.Linux;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

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
            using (LinuxFileSystemVirtualizerTester tester = new LinuxFileSystemVirtualizerTester(this.Repo, new[] { UpdatePlaceholderFileName }))
            {
                tester.GitIndexProjection.MockFileTypesAndModes.TryAdd(
                    UpdatePlaceholderFileName,
                    ConvertFileTypeAndModeToIndexFormat(GitIndexProjection.FileType.Regular, GitIndexProjection.FileMode644));

                tester.MockVirtualization.UpdatePlaceholderIfNeededResult = Result.Success;
                tester.MockVirtualization.UpdatePlaceholderIfNeededFailureCause = UpdateFailureCause.NoFailure;
                tester.InvokeUpdatePlaceholderIfNeeded(
                    UpdatePlaceholderFileName,
                    expectedResult: new FileSystemResult(FSResult.Ok, (int)Result.Success),
                    expectedFailureCause: UpdateFailureCause.NoFailure);

                tester.MockVirtualization.UpdatedPlaceholders.ShouldContain(path => path.Key.Equals(UpdatePlaceholderFileName) && path.Value == GitIndexProjection.FileMode644);
                tester.MockVirtualization.UpdatedPlaceholders.Clear();

                tester.MockVirtualization.UpdatePlaceholderIfNeededResult = Result.EFileNotFound;
                tester.MockVirtualization.UpdatePlaceholderIfNeededFailureCause = UpdateFailureCause.NoFailure;
                tester.InvokeUpdatePlaceholderIfNeeded(
                    UpdatePlaceholderFileName,
                    expectedResult: new FileSystemResult(FSResult.FileOrPathNotFound, (int)Result.EFileNotFound),
                    expectedFailureCause: UpdateFailureCause.NoFailure);

                // TODO: What will the result be when the UpdateFailureCause is DirtyData
                tester.MockVirtualization.UpdatePlaceholderIfNeededResult = Result.EInvalidOperation;
                tester.MockVirtualization.UpdatePlaceholderIfNeededFailureCause = UpdateFailureCause.DirtyData;

                // TODO: The result should probably be VirtualizationInvalidOperation but for now it's IOError
                tester.InvokeUpdatePlaceholderIfNeeded(
                    UpdatePlaceholderFileName,
                    expectedResult: new FileSystemResult(FSResult.IOError, (int)Result.EInvalidOperation),
                    expectedFailureCause: UpdateFailureCause.DirtyData);
            }
        }

        [TestCase]
        public void WritePlaceholderForSymLink()
        {
            const string WriteSymLinkFileName = "testWriteSymLink.txt";
            using (LinuxFileSystemVirtualizerTester tester = new LinuxFileSystemVirtualizerTester(this.Repo, new[] { WriteSymLinkFileName }))
            {
                tester.GitIndexProjection.MockFileTypesAndModes.TryAdd(
                    WriteSymLinkFileName,
                    ConvertFileTypeAndModeToIndexFormat(GitIndexProjection.FileType.SymLink, fileMode: 0));

                tester.Virtualizer.WritePlaceholderFile(
                    WriteSymLinkFileName,
                    endOfFile: 0,
                    sha: string.Empty).ShouldEqual(new FileSystemResult(FSResult.Ok, (int)Result.Success));

                tester.MockVirtualization.CreatedPlaceholders.ShouldBeEmpty();
                tester.MockVirtualization.CreatedSymLinks.Count.ShouldEqual(1);
                tester.MockVirtualization.CreatedSymLinks.ShouldContain(entry => entry.Equals(WriteSymLinkFileName));

                tester.BackgroundTaskShouldBeScheduled(WriteSymLinkFileName, FileSystemTask.OperationType.OnFileSymLinkCreated);
            }
        }

        [TestCase]
        public void UpdatePlaceholderToSymLink()
        {
            const string PlaceholderToLinkFileName = "testUpdatePlaceholderToLink.txt";
            using (LinuxFileSystemVirtualizerTester tester = new LinuxFileSystemVirtualizerTester(this.Repo, new[] { PlaceholderToLinkFileName }))
            {
                tester.GitIndexProjection.MockFileTypesAndModes.TryAdd(
                    PlaceholderToLinkFileName,
                    ConvertFileTypeAndModeToIndexFormat(GitIndexProjection.FileType.SymLink, fileMode: 0));

                tester.MockVirtualization.UpdatePlaceholderIfNeededResult = Result.Success;
                tester.MockVirtualization.UpdatePlaceholderIfNeededFailureCause = UpdateFailureCause.NoFailure;
                tester.InvokeUpdatePlaceholderIfNeeded(
                    PlaceholderToLinkFileName,
                    expectedResult: new FileSystemResult(FSResult.Ok, (int)Result.Success),
                    expectedFailureCause: UpdateFailureCause.NoFailure);

                tester.MockVirtualization.UpdatedPlaceholders.Count.ShouldEqual(0, "UpdatePlaceholderIfNeeded should not be called when converting a placeholder to a link");
                tester.MockVirtualization.CreatedSymLinks.Count.ShouldEqual(1);
                tester.MockVirtualization.CreatedSymLinks.ShouldContain(entry => entry.Equals(PlaceholderToLinkFileName));

                tester.BackgroundTaskShouldBeScheduled(PlaceholderToLinkFileName, FileSystemTask.OperationType.OnFileSymLinkCreated);
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

            // Don't include TestFolderName as MockGitIndexProjection returns the same list of files regardless of what folder name
            // it is passed
            using (LinuxFileSystemVirtualizerTester tester = new LinuxFileSystemVirtualizerTester(this.Repo))
            {
                tester.GitIndexProjection.MockFileTypesAndModes.TryAdd(
                    testFilePath,
                    ConvertFileTypeAndModeToIndexFormat(GitIndexProjection.FileType.Regular, GitIndexProjection.FileMode644));

                tester.GitIndexProjection.EnumerationInMemory = false;
                tester.MockVirtualization.OnEnumerateDirectory(1, TestFolderName, triggeringProcessId: 1, triggeringProcessName: "UnitTests").ShouldEqual(Result.Success);
                tester.MockVirtualization.CreatedPlaceholders.ShouldContain(
                    kvp => kvp.Key.Equals(testFilePath, GVFSPlatform.Instance.Constants.PathComparison) && kvp.Value == GitIndexProjection.FileMode644);
            }
        }

        [TestCase]
        public void OnEnumerateDirectoryReturnsSuccessWhenResultsInMemory()
        {
            const string TestFileName = "test.txt";
            const string TestFolderName = "testFolder";
            string testFilePath = Path.Combine(TestFolderName, TestFileName);

            // Don't include TestFolderName as MockGitIndexProjection returns the same list of files regardless of what folder name
            // it is passed
            using (LinuxFileSystemVirtualizerTester tester = new LinuxFileSystemVirtualizerTester(this.Repo))
            {
                tester.GitIndexProjection.MockFileTypesAndModes.TryAdd(
                    testFilePath,
                    ConvertFileTypeAndModeToIndexFormat(GitIndexProjection.FileType.Regular, GitIndexProjection.FileMode644));

                tester.GitIndexProjection.EnumerationInMemory = true;
                tester.MockVirtualization.OnEnumerateDirectory(1, TestFolderName, triggeringProcessId: 1, triggeringProcessName: "UnitTests").ShouldEqual(Result.Success);
                tester.MockVirtualization.CreatedPlaceholders.ShouldContain(
                    kvp => kvp.Key.Equals(testFilePath, GVFSPlatform.Instance.Constants.PathComparison) && kvp.Value == GitIndexProjection.FileMode644);
                tester.GitIndexProjection.ExpandedFolders.ShouldMatchInOrder(TestFolderName);
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

            // Don't include TestFolderName as MockGitIndexProjection returns the same list of files regardless of what folder name
            // it is passed
            using (LinuxFileSystemVirtualizerTester tester = new LinuxFileSystemVirtualizerTester(this.Repo, new[] { TestFile644Name, TestFile664Name, TestFile755Name }))
            {
                tester.GitIndexProjection.MockFileTypesAndModes.TryAdd(
                    testFile644Path,
                    ConvertFileTypeAndModeToIndexFormat(GitIndexProjection.FileType.Regular, GitIndexProjection.FileMode644));
                tester.GitIndexProjection.MockFileTypesAndModes.TryAdd(
                    testFile664Path,
                    ConvertFileTypeAndModeToIndexFormat(GitIndexProjection.FileType.Regular, GitIndexProjection.FileMode664));
                tester.GitIndexProjection.MockFileTypesAndModes.TryAdd(
                    testFile755Path,
                    ConvertFileTypeAndModeToIndexFormat(GitIndexProjection.FileType.Regular, GitIndexProjection.FileMode755));

                tester.GitIndexProjection.EnumerationInMemory = true;
                tester.MockVirtualization.OnEnumerateDirectory(1, TestFolderName, triggeringProcessId: 1, triggeringProcessName: "UnitTests").ShouldEqual(Result.Success);
                tester.MockVirtualization.CreatedPlaceholders.ShouldContain(
                    kvp => kvp.Key.Equals(testFile644Path, GVFSPlatform.Instance.Constants.PathComparison) && kvp.Value == GitIndexProjection.FileMode644);
                tester.MockVirtualization.CreatedPlaceholders.ShouldContain(
                    kvp => kvp.Key.Equals(testFile664Path, GVFSPlatform.Instance.Constants.PathComparison) && kvp.Value == GitIndexProjection.FileMode664);
                tester.MockVirtualization.CreatedPlaceholders.ShouldContain(
                    kvp => kvp.Key.Equals(testFile755Path, GVFSPlatform.Instance.Constants.PathComparison) && kvp.Value == GitIndexProjection.FileMode755);
            }
        }

        [TestCase]
        public void OnGetFileStreamReturnsSuccessWhenFileStreamAvailable()
        {
            using (LinuxFileSystemVirtualizerTester tester = new LinuxFileSystemVirtualizerTester(this.Repo))
            {
                tester.MockVirtualization.WriteFileReturnResult = Result.Success;
                tester.InvokeOnGetFileStream(expectedResult: Result.Success);
                tester.MockVirtualization.BytesWritten.ShouldEqual(MockGVFSGitObjects.DefaultFileLength);
            }
        }

        [TestCase]
        [Category(CategoryConstants.ExceptionExpected)]
        public void OnGetFileStreamReturnsErrorWhenWriteFileContentsFails()
        {
            using (LinuxFileSystemVirtualizerTester tester = new LinuxFileSystemVirtualizerTester(this.Repo))
            {
                tester.MockVirtualization.WriteFileReturnResult = Result.EIOError;
                tester.InvokeOnGetFileStream(expectedResult: Result.EIOError);
            }
        }

        [TestCase]
        public void OnNewFileCreatedInsideDotGitDirectoryShouldNotScheduleBackgroundTask()
        {
            using (LinuxFileSystemVirtualizerTester tester = new LinuxFileSystemVirtualizerTester(this.Repo))
            {
                tester.MockVirtualization.OnNewFileCreated(Path.Combine(".git", "testing.txt"), isDirectory: false);
                tester.BackgroundTaskRunner.Count.ShouldEqual(0);
            }
        }

        [TestCase]
        public void OnNewFileCreatedFileShouldScheduleBackgroundTask()
        {
            using (LinuxFileSystemVirtualizerTester tester = new LinuxFileSystemVirtualizerTester(this.Repo))
            {
                tester.MockVirtualization.OnNewFileCreated("testing.txt", isDirectory: false);
                tester.BackgroundTaskShouldBeScheduled("testing.txt", FileSystemTask.OperationType.OnFileCreated);
                tester.GitIndexProjection.SparseEntries.Count.ShouldEqual(0);
            }
        }

        [TestCase]
        public void OnNewFileCreatedDirectoryIncludedShouldScheduleBackgroundTask()
        {
            using (LinuxFileSystemVirtualizerTester tester = new LinuxFileSystemVirtualizerTester(this.Repo))
            {
                tester.GitIndexProjection.GetFolderPathSparseStateValue = GitIndexProjection.PathSparseState.Included;
                tester.MockVirtualization.OnNewFileCreated("testing", isDirectory: true);
                tester.BackgroundTaskShouldBeScheduled("testing", FileSystemTask.OperationType.OnFolderCreated);
                tester.GitIndexProjection.SparseEntries.Count.ShouldEqual(0);
            }
        }

        [TestCase]
        public void OnNewFileCreatedDirectoryExcludedShouldNotScheduleBackgroundTask()
        {
            const string TestFileName = "test.txt";
            const string TestFolderName = "testFolder";
            string testFilePath = Path.Combine(TestFolderName, TestFileName);

            using (LinuxFileSystemVirtualizerTester tester = new LinuxFileSystemVirtualizerTester(this.Repo))
            {
                tester.GitIndexProjection.MockFileTypesAndModes.TryAdd(
                    testFilePath,
                    ConvertFileTypeAndModeToIndexFormat(GitIndexProjection.FileType.Regular, GitIndexProjection.FileMode644));

                tester.GitIndexProjection.GetFolderPathSparseStateValue = GitIndexProjection.PathSparseState.Excluded;
                tester.MockVirtualization.OnNewFileCreated(TestFolderName, isDirectory: true);
                tester.BackgroundTaskRunner.Count.ShouldEqual(0);
                tester.GitIndexProjection.SparseEntries.Count.ShouldEqual(1);
                tester.GitIndexProjection.SparseEntries.First().ShouldEqual(TestFolderName);
                tester.MockVirtualization.CreatedPlaceholders.ShouldContain(
                    kvp => kvp.Key.Equals(testFilePath, GVFSPlatform.Instance.Constants.PathComparison) && kvp.Value == GitIndexProjection.FileMode644);
            }
        }

        [TestCase]
        public void OnNewFileCreatedDirectoryNotFoundShouldScheduleBackgroundTask()
        {
            using (LinuxFileSystemVirtualizerTester tester = new LinuxFileSystemVirtualizerTester(this.Repo))
            {
                tester.GitIndexProjection.GetFolderPathSparseStateValue = GitIndexProjection.PathSparseState.NotFound;
                tester.MockVirtualization.OnNewFileCreated("testing", isDirectory: true);
                tester.BackgroundTaskShouldBeScheduled("testing", FileSystemTask.OperationType.OnFolderCreated);
                tester.GitIndexProjection.SparseEntries.Count.ShouldEqual(0);
            }
        }

        [TestCase]
        public void OnNewFileCreatedDirectoryExcludedTryAddSparseFolderFailureShouldScheduleBackgroundTask()
        {
            using (LinuxFileSystemVirtualizerTester tester = new LinuxFileSystemVirtualizerTester(this.Repo))
            {
                tester.GitIndexProjection.GetFolderPathSparseStateValue = GitIndexProjection.PathSparseState.Excluded;
                tester.GitIndexProjection.TryAddSparseFolderReturnValue = false;
                tester.MockVirtualization.OnNewFileCreated("testing", isDirectory: true);
                tester.BackgroundTaskShouldBeScheduled("testing", FileSystemTask.OperationType.OnFolderCreated);
                tester.GitIndexProjection.SparseEntries.Count.ShouldEqual(0);
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
