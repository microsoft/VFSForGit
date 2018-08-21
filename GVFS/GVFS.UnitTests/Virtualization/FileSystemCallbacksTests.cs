using GVFS.Common;
using GVFS.Common.NamedPipes;
using GVFS.Common.Tracing;
using GVFS.Tests.Should;
using GVFS.UnitTests.Mock.Common;
using GVFS.UnitTests.Mock.Virtualization.Background;
using GVFS.UnitTests.Mock.Virtualization.BlobSize;
using GVFS.UnitTests.Mock.Virtualization.FileSystem;
using GVFS.UnitTests.Mock.Virtualization.Projection;
using GVFS.UnitTests.Virtual;
using GVFS.Virtualization;
using GVFS.Virtualization.Background;
using NUnit.Framework;
using System;
using System.IO;

namespace GVFS.UnitTests.Virtualization
{
    [TestFixture]
    public class FileSystemCallbacksTests : TestsWithCommonRepo
    {
        [TestCase]
        public void EmptyStringIsNotInsideDotGitPath()
        {
            FileSystemCallbacks.IsPathInsideDotGit(string.Empty).ShouldEqual(false);
        }

        [TestCase]
        public void IsPathInsideDotGitIsTrueForDotGitPath()
        {
            FileSystemCallbacks.IsPathInsideDotGit(@".git" + Path.DirectorySeparatorChar).ShouldEqual(true);
            FileSystemCallbacks.IsPathInsideDotGit(@".GIT" + Path.DirectorySeparatorChar).ShouldEqual(true);
            FileSystemCallbacks.IsPathInsideDotGit(Path.Combine(".git", "test_file.txt")).ShouldEqual(true);
            FileSystemCallbacks.IsPathInsideDotGit(Path.Combine(".GIT", "test_file.txt")).ShouldEqual(true);
            FileSystemCallbacks.IsPathInsideDotGit(Path.Combine(".git", "test_folder", "test_file.txt")).ShouldEqual(true);
            FileSystemCallbacks.IsPathInsideDotGit(Path.Combine(".GIT", "test_folder", "test_file.txt")).ShouldEqual(true);
        }

        [TestCase]
        public void IsPathInsideDotGitIsFalseForNonDotGitPath()
        {
            FileSystemCallbacks.IsPathInsideDotGit(@".git").ShouldEqual(false);
            FileSystemCallbacks.IsPathInsideDotGit(@".GIT").ShouldEqual(false);
            FileSystemCallbacks.IsPathInsideDotGit(@".gitattributes").ShouldEqual(false);
            FileSystemCallbacks.IsPathInsideDotGit(@".gitignore").ShouldEqual(false);
            FileSystemCallbacks.IsPathInsideDotGit(@".gitsubfolder\").ShouldEqual(false);
            FileSystemCallbacks.IsPathInsideDotGit(@".gitsubfolder\test_file.txt").ShouldEqual(false);
            FileSystemCallbacks.IsPathInsideDotGit(@"test_file.txt").ShouldEqual(false);
            FileSystemCallbacks.IsPathInsideDotGit(@"test_folder\test_file.txt").ShouldEqual(false);
        }

        [TestCase]
        public void BackgroundOperationCountMatchesBackgroundFileSystemTaskRunner()
        {
            using (MockBackgroundFileSystemTaskRunner backgroundTaskRunner = new MockBackgroundFileSystemTaskRunner())
            using (FileSystemCallbacks fileSystemCallbacks = new FileSystemCallbacks(
                this.Repo.Context,
                this.Repo.GitObjects,
                RepoMetadata.Instance,
                new MockBlobSizes(),
                gitIndexProjection: null,
                backgroundFileSystemTaskRunner: backgroundTaskRunner,
                fileSystemVirtualizer: null))
            {
                fileSystemCallbacks.BackgroundOperationCount.ShouldEqual(backgroundTaskRunner.Count);

                fileSystemCallbacks.OnFileConvertedToFull("Path1.txt");
                fileSystemCallbacks.OnFileConvertedToFull("Path2.txt");
                backgroundTaskRunner.Count.ShouldEqual(2);
                fileSystemCallbacks.BackgroundOperationCount.ShouldEqual(backgroundTaskRunner.Count);
            }
        }

        [TestCase]
        public void StartingAndStoppingSetsMountedState()
        {
            using (MockBackgroundFileSystemTaskRunner backgroundTaskRunner = new MockBackgroundFileSystemTaskRunner())
            using (MockFileSystemVirtualizer fileSystemVirtualizer = new MockFileSystemVirtualizer(this.Repo.Context, this.Repo.GitObjects))
            using (MockGitIndexProjection gitIndexProjection = new MockGitIndexProjection(new[] { "test.txt" }))
            using (FileSystemCallbacks fileSystemCallbacks = new FileSystemCallbacks(
                this.Repo.Context,
                this.Repo.GitObjects,
                RepoMetadata.Instance,
                new MockBlobSizes(),
                gitIndexProjection: gitIndexProjection,
                backgroundFileSystemTaskRunner: backgroundTaskRunner,
                fileSystemVirtualizer: fileSystemVirtualizer))
            {
                fileSystemCallbacks.IsMounted.ShouldBeFalse();

                string error;
                fileSystemCallbacks.TryStart(out error).ShouldBeTrue();
                fileSystemCallbacks.IsMounted.ShouldBeTrue();

                fileSystemCallbacks.Stop();
                fileSystemCallbacks.IsMounted.ShouldBeFalse();
            }
        }

        [TestCase]
        public void GetMetadataForHeartBeatDoesNotChangeEventLevelWhenNoPlaceholderHaveBeenCreated()
        {
            using (MockBackgroundFileSystemTaskRunner backgroundTaskRunner = new MockBackgroundFileSystemTaskRunner())
            using (FileSystemCallbacks fileSystemCallbacks = new FileSystemCallbacks(
                this.Repo.Context,
                this.Repo.GitObjects,
                RepoMetadata.Instance,
                new MockBlobSizes(),
                gitIndexProjection: null,
                backgroundFileSystemTaskRunner: backgroundTaskRunner,
                fileSystemVirtualizer: null))
            {
                EventLevel eventLevel = EventLevel.Verbose;
                EventMetadata metadata = fileSystemCallbacks.GetMetadataForHeartBeat(ref eventLevel);
                eventLevel.ShouldEqual(EventLevel.Verbose);

                // "ModifiedPathsCount" should be 1 because ".gitattributes" is always present
                metadata.ShouldContain("ModifiedPathsCount", 1);
                metadata.ShouldContain("PlaceholderCount", 0);
                metadata.ShouldContain(nameof(RepoMetadata.Instance.EnlistmentId), RepoMetadata.Instance.EnlistmentId);
            }
        }

        [TestCase]
        public void GetMetadataForHeartBeatDoesSetsEventLevelWToInformationalWhenPlaceholdersHaveBeenCreated()
        {
            using (MockBackgroundFileSystemTaskRunner backgroundTaskRunner = new MockBackgroundFileSystemTaskRunner())
            using (FileSystemCallbacks fileSystemCallbacks = new FileSystemCallbacks(
                this.Repo.Context,
                this.Repo.GitObjects,
                RepoMetadata.Instance,
                new MockBlobSizes(),
                gitIndexProjection: null,
                backgroundFileSystemTaskRunner: backgroundTaskRunner,
                fileSystemVirtualizer: null))
            {
                fileSystemCallbacks.OnPlaceholderFileCreated("test.txt", "1111122222333334444455555666667777788888", "GVFS.UnitTests.exe");

                EventLevel eventLevel = EventLevel.Verbose;
                EventMetadata metadata = fileSystemCallbacks.GetMetadataForHeartBeat(ref eventLevel);
                eventLevel.ShouldEqual(EventLevel.Informational);

                // "ModifiedPathsCount" should be 1 because ".gitattributes" is always present
                metadata.Count.ShouldEqual(5);
                metadata.ShouldContain("ProcessName1", "GVFS.UnitTests.exe");
                metadata.ShouldContain("ProcessCount1", 1);
                metadata.ShouldContain("ModifiedPathsCount", 1);
                metadata.ShouldContain("PlaceholderCount", 1);
                metadata.ShouldContain(nameof(RepoMetadata.Instance.EnlistmentId), RepoMetadata.Instance.EnlistmentId);

                // Create more placeholders
                fileSystemCallbacks.OnPlaceholderFileCreated("test.txt", "2222233333444445555566666777778888899999", "GVFS.UnitTests.exe2");
                fileSystemCallbacks.OnPlaceholderFileCreated("test.txt", "3333344444555556666677777888889999900000", "GVFS.UnitTests.exe2");

                eventLevel = EventLevel.Verbose;
                metadata = fileSystemCallbacks.GetMetadataForHeartBeat(ref eventLevel);
                eventLevel.ShouldEqual(EventLevel.Informational);

                metadata.Count.ShouldEqual(5);

                // Only processes that have created placeholders since the last heartbeat should be named
                metadata.ShouldContain("ProcessName1", "GVFS.UnitTests.exe2");
                metadata.ShouldContain("ProcessCount1", 2);
                metadata.ShouldContain("ModifiedPathsCount", 1);
                metadata.ShouldContain("PlaceholderCount", 3);
                metadata.ShouldContain(nameof(RepoMetadata.Instance.EnlistmentId), RepoMetadata.Instance.EnlistmentId);
            }
        }

        [TestCase]
        public void IsReadyForExternalAcquireLockRequests()
        {
            using (MockBackgroundFileSystemTaskRunner backgroundTaskRunner = new MockBackgroundFileSystemTaskRunner())
            using (MockFileSystemVirtualizer fileSystemVirtualizer = new MockFileSystemVirtualizer(this.Repo.Context, this.Repo.GitObjects))
            using (MockGitIndexProjection gitIndexProjection = new MockGitIndexProjection(new[] { "test.txt" }))
            using (FileSystemCallbacks fileSystemCallbacks = new FileSystemCallbacks(
                this.Repo.Context,
                this.Repo.GitObjects,
                RepoMetadata.Instance,
                new MockBlobSizes(),
                gitIndexProjection: gitIndexProjection,
                backgroundFileSystemTaskRunner: backgroundTaskRunner,
                fileSystemVirtualizer: fileSystemVirtualizer))
            {
                string denyMessage;
                fileSystemCallbacks.IsReadyForExternalAcquireLockRequests(
                    new NamedPipeMessages.LockData(
                        pid: 0,
                        isElevated: false,
                        checkAvailabilityOnly: false,
                        parsedCommand: "git dummy-command"),
                    out denyMessage).ShouldBeFalse();
                denyMessage.ShouldEqual("Waiting for mount to complete");

                string error;
                fileSystemCallbacks.TryStart(out error).ShouldBeTrue();
                gitIndexProjection.ProjectionParseComplete = false;
                fileSystemCallbacks.IsReadyForExternalAcquireLockRequests(
                    new NamedPipeMessages.LockData(
                        pid: 0,
                        isElevated: false,
                        checkAvailabilityOnly: false,
                        parsedCommand: "git dummy-command"),
                    out denyMessage).ShouldBeFalse();
                denyMessage.ShouldEqual("Waiting for GVFS to parse index and update placeholder files");

                // Put something on the background queue
                fileSystemCallbacks.OnFileCreated("NewFilePath.txt");
                backgroundTaskRunner.Count.ShouldEqual(1);
                fileSystemCallbacks.IsReadyForExternalAcquireLockRequests(
                    new NamedPipeMessages.LockData(
                        pid: 0,
                        isElevated: false,
                        checkAvailabilityOnly: false,
                        parsedCommand: "git dummy-command"),
                    out denyMessage).ShouldBeFalse();
                denyMessage.ShouldEqual("Waiting for GVFS to release the lock");

                backgroundTaskRunner.BackgroundTasks.Clear();
                gitIndexProjection.ProjectionParseComplete = true;
                fileSystemCallbacks.IsReadyForExternalAcquireLockRequests(
                    new NamedPipeMessages.LockData(
                        pid: 0,
                        isElevated: false,
                        checkAvailabilityOnly: false,
                        parsedCommand: "git dummy-command"),
                    out denyMessage).ShouldBeTrue();
                denyMessage.ShouldEqual("Waiting for GVFS to release the lock");

                fileSystemCallbacks.Stop();
            }
        }

        [TestCase]
        public void FileAndFolderCallbacksScheduleBackgroundTasks()
        {
            using (MockBackgroundFileSystemTaskRunner backgroundTaskRunner = new MockBackgroundFileSystemTaskRunner())
            using (FileSystemCallbacks fileSystemCallbacks = new FileSystemCallbacks(
                this.Repo.Context,
                this.Repo.GitObjects,
                RepoMetadata.Instance,
                new MockBlobSizes(),
                gitIndexProjection: null,
                backgroundFileSystemTaskRunner: backgroundTaskRunner,
                fileSystemVirtualizer: null))
            {
                this.CallbackSchedulesBackgroundTask(
                    backgroundTaskRunner, 
                    (path) => fileSystemCallbacks.OnFileConvertedToFull(path), 
                    "OnFileConvertedToFull.txt", 
                    FileSystemTask.OperationType.OnFileConvertedToFull);

                this.CallbackSchedulesBackgroundTask(
                    backgroundTaskRunner,
                    (path) => fileSystemCallbacks.OnFileCreated(path),
                    "OnFileCreated.txt",
                    FileSystemTask.OperationType.OnFileCreated);

                this.CallbackSchedulesBackgroundTask(
                    backgroundTaskRunner,
                    (path) => fileSystemCallbacks.OnFileDeleted(path),
                    "OnFileDeleted.txt",
                    FileSystemTask.OperationType.OnFileDeleted);

                this.CallbackSchedulesBackgroundTask(
                    backgroundTaskRunner,
                    (path) => fileSystemCallbacks.OnFileOverwritten(path),
                    "OnFileOverwritten.txt",
                    FileSystemTask.OperationType.OnFileOverwritten);

                this.CallbackSchedulesBackgroundTask(
                    backgroundTaskRunner,
                    (oldPath, newPath) => fileSystemCallbacks.OnFileRenamed(oldPath, newPath),
                    "OnFileRenamed.txt",
                    "OnFileRenamed2.txt",
                    FileSystemTask.OperationType.OnFileRenamed);

                this.CallbackSchedulesBackgroundTask(
                    backgroundTaskRunner,
                    (path) => fileSystemCallbacks.OnFileHardLinkCreated(path),
                    "OnFileHardLinkCreated.txt",
                    FileSystemTask.OperationType.OnFileHardLinkCreated);

                this.CallbackSchedulesBackgroundTask(
                    backgroundTaskRunner,
                    (path) => fileSystemCallbacks.OnFileSuperseded(path),
                    "OnFileSuperseded.txt",
                    FileSystemTask.OperationType.OnFileSuperseded);

                this.CallbackSchedulesBackgroundTask(
                    backgroundTaskRunner,
                    (path) => fileSystemCallbacks.OnFolderCreated(path),
                    "OnFolderCreated.txt",
                    FileSystemTask.OperationType.OnFolderCreated);

                this.CallbackSchedulesBackgroundTask(
                    backgroundTaskRunner,
                    (path) => fileSystemCallbacks.OnFolderDeleted(path),
                    "OnFolderDeleted.txt",
                    FileSystemTask.OperationType.OnFolderDeleted);

                this.CallbackSchedulesBackgroundTask(
                    backgroundTaskRunner,
                    (oldPath, newPath) => fileSystemCallbacks.OnFolderRenamed(oldPath, newPath),
                    "OnFolderRenamed.txt",
                    "OnFolderRenamed2.txt",
                    FileSystemTask.OperationType.OnFolderRenamed);
            }
        }

        [TestCase]
        public void TestFileSystemOperationsInvalidateStatusCache()
        {
            using (MockBackgroundFileSystemTaskRunner backgroundTaskRunner = new MockBackgroundFileSystemTaskRunner())
            using (MockFileSystemVirtualizer fileSystemVirtualizer = new MockFileSystemVirtualizer(this.Repo.Context, this.Repo.GitObjects))
            using (MockGitIndexProjection gitIndexProjection = new MockGitIndexProjection(new[] { "test.txt" }))
            using (MockGitStatusCache gitStatusCache = new MockGitStatusCache(this.Repo.Context, TimeSpan.Zero))
            using (FileSystemCallbacks fileSystemCallbacks = new FileSystemCallbacks(
                this.Repo.Context,
                this.Repo.GitObjects,
                RepoMetadata.Instance,
                new MockBlobSizes(),
                gitIndexProjection: gitIndexProjection,
                backgroundFileSystemTaskRunner: backgroundTaskRunner,
                fileSystemVirtualizer: fileSystemVirtualizer,
                gitStatusCache: gitStatusCache))
            {
                this.ValidateActionInvalidatesStatusCache(backgroundTaskRunner, gitStatusCache, fileSystemCallbacks.OnFileConvertedToFull, "OnFileConvertedToFull.txt", FileSystemTask.OperationType.OnFileConvertedToFull);
                this.ValidateActionInvalidatesStatusCache(backgroundTaskRunner, gitStatusCache, fileSystemCallbacks.OnFileCreated, "OnFileCreated.txt", FileSystemTask.OperationType.OnFileCreated);
                this.ValidateActionInvalidatesStatusCache(backgroundTaskRunner, gitStatusCache, fileSystemCallbacks.OnFileDeleted, "OnFileDeleted.txt", FileSystemTask.OperationType.OnFileDeleted);
                this.ValidateActionInvalidatesStatusCache(backgroundTaskRunner, gitStatusCache, fileSystemCallbacks.OnFileOverwritten, "OnFileDeleted.txt", FileSystemTask.OperationType.OnFileOverwritten);
                this.ValidateActionInvalidatesStatusCache(backgroundTaskRunner, gitStatusCache, fileSystemCallbacks.OnFileSuperseded, "OnFileSuperseded.txt", FileSystemTask.OperationType.OnFileSuperseded);
                this.ValidateActionInvalidatesStatusCache(backgroundTaskRunner, gitStatusCache, fileSystemCallbacks.OnFolderCreated, "OnFileSuperseded.txt", FileSystemTask.OperationType.OnFolderCreated);
                this.ValidateActionInvalidatesStatusCache(backgroundTaskRunner, gitStatusCache, fileSystemCallbacks.OnFolderDeleted, "OnFileSuperseded.txt", FileSystemTask.OperationType.OnFolderDeleted);
                this.ValidateActionInvalidatesStatusCache(backgroundTaskRunner, gitStatusCache, fileSystemCallbacks.OnFileConvertedToFull, "OnFileConvertedToFull.txt", FileSystemTask.OperationType.OnFileConvertedToFull);
            }
        }

        private void ValidateActionInvalidatesStatusCache(
            MockBackgroundFileSystemTaskRunner backgroundTaskRunner,
            MockGitStatusCache gitStatusCache,
            Action<string> action,
            string path,
            FileSystemTask.OperationType operationType)
        {
            action(path);

            backgroundTaskRunner.Count.ShouldEqual(1);
            backgroundTaskRunner.BackgroundTasks[0].Operation.ShouldEqual(operationType);
            backgroundTaskRunner.BackgroundTasks[0].VirtualPath.ShouldEqual(path);

            backgroundTaskRunner.ProcessTasks();

            gitStatusCache.InvalidateCallCount.ShouldEqual(1);

            gitStatusCache.ResetCalls();
            backgroundTaskRunner.BackgroundTasks.Clear();
        }

        private void CallbackSchedulesBackgroundTask(
            MockBackgroundFileSystemTaskRunner backgroundTaskRunner, 
            Action<string> callback, 
            string path, 
            FileSystemTask.OperationType operationType)
        {
            callback(path);
            backgroundTaskRunner.Count.ShouldEqual(1);
            backgroundTaskRunner.BackgroundTasks[0].Operation.ShouldEqual(operationType);
            backgroundTaskRunner.BackgroundTasks[0].VirtualPath.ShouldEqual(path);
            backgroundTaskRunner.BackgroundTasks.Clear();
        }

        private void CallbackSchedulesBackgroundTask(
            MockBackgroundFileSystemTaskRunner backgroundTaskRunner,
            Action<string, string> callback,
            string oldPath,
            string newPath,
            FileSystemTask.OperationType operationType)
        {
            callback(oldPath, newPath);
            backgroundTaskRunner.Count.ShouldEqual(1);
            backgroundTaskRunner.BackgroundTasks[0].Operation.ShouldEqual(operationType);
            backgroundTaskRunner.BackgroundTasks[0].OldVirtualPath.ShouldEqual(oldPath);
            backgroundTaskRunner.BackgroundTasks[0].VirtualPath.ShouldEqual(newPath);
            backgroundTaskRunner.BackgroundTasks.Clear();
        }
    }
}
