using GVFS.Common;
using GVFS.Common.Git;
using GVFS.Common.NamedPipes;
using GVFS.Tests.Should;
using GVFS.UnitTests.Category;
using GVFS.UnitTests.Mock.Common;
using GVFS.UnitTests.Mock.FileSystem;
using GVFS.UnitTests.Mock.Git;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.IO;

namespace GVFS.UnitTests.Common
{
    [TestFixture]
    public class GitStatusCacheTests
    {
        private static NamedPipeMessages.LockData statusCommandLockData = new NamedPipeMessages.LockData(123, false, false, "git status", "123");

        private MockFileSystem fileSystem;
        private MockGitProcess gitProcess;
        private GVFSContext context;
        private string gitParentPath;
        private string gvfsMetadataPath;
        private MockDirectory enlistmentDirectory;

        public static IEnumerable<Exception> ExceptionsThrownByCreateDirectory
        {
            get
            {
                yield return new IOException("Error creating directory");
                yield return new UnauthorizedAccessException("Error creating directory");
            }
        }

        [SetUp]
        public void SetUp()
        {
            MockTracer tracer = new MockTracer();

            string enlistmentRoot = Path.Combine("mock:", "GVFS", "UnitTests", "Repo");
            string statusCachePath = Path.Combine("mock:", "GVFS", "UnitTests", "Repo", GVFSPlatform.Instance.Constants.DotGVFSRoot, "gitStatusCache");

            this.gitProcess = new MockGitProcess();
            this.gitProcess.SetExpectedCommandResult($"--no-optional-locks status \"--serialize={statusCachePath}", () => new GitProcess.Result(string.Empty, string.Empty, 0), true);
            MockGVFSEnlistment enlistment = new MockGVFSEnlistment(enlistmentRoot, "fake://repoUrl", "fake://gitBinPath", this.gitProcess);
            enlistment.InitializeCachePathsFromKey("fake:\\gvfsSharedCache", "fakeCacheKey");

            this.gitParentPath = enlistment.WorkingDirectoryBackingRoot;
            this.gvfsMetadataPath = enlistment.DotGVFSRoot;

            this.enlistmentDirectory = new MockDirectory(
                enlistmentRoot,
                new MockDirectory[]
                {
                    new MockDirectory(this.gitParentPath, folders: null, files: null),
                },
                null);

            this.enlistmentDirectory.CreateFile(Path.Combine(this.gitParentPath, ".git", "config"), ".git config Contents", createDirectories: true);
            this.enlistmentDirectory.CreateFile(Path.Combine(this.gitParentPath, ".git", "HEAD"), ".git HEAD Contents", createDirectories: true);
            this.enlistmentDirectory.CreateFile(Path.Combine(this.gitParentPath, ".git", "logs", "HEAD"), "HEAD Contents", createDirectories: true);
            this.enlistmentDirectory.CreateFile(Path.Combine(this.gitParentPath, ".git", "info", "always_exclude"), "always_exclude Contents", createDirectories: true);
            this.enlistmentDirectory.CreateDirectory(Path.Combine(this.gitParentPath, ".git", "objects", "pack"));

            this.fileSystem = new MockFileSystem(this.enlistmentDirectory);
            this.fileSystem.AllowMoveFile = true;
            this.fileSystem.DeleteNonExistentFileThrowsException = false;

            this.context = new GVFSContext(
                tracer,
                this.fileSystem,
                new MockGitRepo(tracer, enlistment, this.fileSystem),
                enlistment);
            GitStatusCache.TEST_EnableHydrationSummary = false;
        }

        [TearDown]
        public void TearDown()
        {
            this.fileSystem = null;
            this.gitProcess = null;
            this.context = null;
            this.gitParentPath = null;
            this.gvfsMetadataPath = null;
            this.enlistmentDirectory = null;
            GitStatusCache.TEST_EnableHydrationSummary = true;
        }

        [TestCase]
        public void CanInvalidateCleanCache()
        {
            this.enlistmentDirectory.CreateFile(Path.Combine(this.gvfsMetadataPath, GVFSConstants.DotGVFS.GitStatusCache.CachePath), "Git status cache contents", createDirectories: true);
            using (GitStatusCache statusCache = new GitStatusCache(this.context, TimeSpan.Zero))
            {
                statusCache.Initialize();
                statusCache.IsCacheReadyAndUpToDate().ShouldBeFalse();

                // Refresh the cache to put it into the clean state.
                statusCache.RefreshAndWait();

                bool result = statusCache.IsReadyForExternalAcquireLockRequests(statusCommandLockData, out _);

                result.ShouldBeTrue();
                statusCache.IsCacheReadyAndUpToDate().ShouldBeTrue();

                // Invalidate the cache, and make sure that it transistions into
                // the dirty state, and that commands are still allowed through.
                statusCache.Invalidate();
                statusCache.IsCacheReadyAndUpToDate().ShouldBeFalse();

                result = statusCache.IsReadyForExternalAcquireLockRequests(statusCommandLockData, out _);
                result.ShouldBeTrue();

                // After checking if we are ready for external lock requests, cache should still be dirty
                statusCache.IsCacheReadyAndUpToDate().ShouldBeFalse();

                statusCache.Shutdown();
            }
        }

        [TestCase]
        [Category(CategoryConstants.ExceptionExpected)]
        public void CacheFileErrorShouldBlock()
        {
            this.fileSystem.DeleteFileThrowsException = true;
            this.enlistmentDirectory.CreateFile(Path.Combine(this.gvfsMetadataPath, GVFSConstants.DotGVFS.GitStatusCache.CachePath), "Git status cache contents", createDirectories: true);

            using (GitStatusCache statusCache = new GitStatusCache(this.context, TimeSpan.Zero))
            {
                statusCache.Initialize();

                statusCache.IsCacheReadyAndUpToDate().ShouldBeFalse();

                bool isReady = statusCache.IsReadyForExternalAcquireLockRequests(statusCommandLockData, out _);
                isReady.ShouldBeFalse();

                statusCache.IsCacheReadyAndUpToDate().ShouldBeFalse();

                statusCache.Shutdown();
            }
        }

        [TestCase]
        public void CanRefreshCache()
        {
            this.enlistmentDirectory.CreateFile(Path.Combine(this.gvfsMetadataPath, GVFSConstants.DotGVFS.GitStatusCache.CachePath), "Git status cache contents", createDirectories: true);
            using (GitStatusCache statusCache = new GitStatusCache(this.context, TimeSpan.Zero))
            {
                statusCache.Initialize();

                statusCache.IsCacheReadyAndUpToDate().ShouldBeFalse();

                string message;
                bool result = statusCache.IsReadyForExternalAcquireLockRequests(statusCommandLockData, out message);
                result.ShouldBeTrue();

                statusCache.RefreshAndWait();

                result = statusCache.IsReadyForExternalAcquireLockRequests(statusCommandLockData, out message);
                result.ShouldBeTrue();

                statusCache.IsCacheReadyAndUpToDate().ShouldBeTrue();

                statusCache.Shutdown();
            }
        }

        [TestCaseSource("ExceptionsThrownByCreateDirectory")]
        [Category(CategoryConstants.ExceptionExpected)]
        public void HandlesExceptionsCreatingDirectory(Exception exceptionToThrow)
        {
            this.enlistmentDirectory.CreateFile(Path.Combine(this.gvfsMetadataPath, GVFSConstants.DotGVFS.GitStatusCache.CachePath), "Git status cache contents", createDirectories: true);
            this.fileSystem.ExceptionThrownByCreateDirectory = exceptionToThrow;
            using (GitStatusCache statusCache = new GitStatusCache(this.context, TimeSpan.Zero))
            {
                statusCache.Initialize();

                statusCache.IsCacheReadyAndUpToDate().ShouldBeFalse();

                string message;
                bool result = statusCache.IsReadyForExternalAcquireLockRequests(statusCommandLockData, out message);
                result.ShouldBeTrue();

                statusCache.RefreshAndWait();

                result = statusCache.IsReadyForExternalAcquireLockRequests(statusCommandLockData, out message);
                result.ShouldBeTrue();

                statusCache.IsCacheReadyAndUpToDate().ShouldBeFalse();

                statusCache.Shutdown();
            }
        }
    }
}
