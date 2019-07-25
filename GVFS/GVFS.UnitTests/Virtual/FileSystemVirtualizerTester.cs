using GVFS.Common;
using GVFS.Common.Database;
using GVFS.Tests.Should;
using GVFS.UnitTests.Mock.Virtualization.Background;
using GVFS.UnitTests.Mock.Virtualization.BlobSize;
using GVFS.UnitTests.Mock.Virtualization.Projection;
using GVFS.Virtualization;
using GVFS.Virtualization.FileSystem;
using Moq;
using System;
using System.Collections.Generic;
using System.Text;

namespace GVFS.UnitTests.Virtual
{
    public abstract class FileSystemVirtualizerTester : IDisposable
    {
        public const int NumberOfWorkThreads = 1;

        public FileSystemVirtualizerTester(CommonRepoSetup repo)
            : this(repo, new[] { "test.txt" })
        {
        }

        public FileSystemVirtualizerTester(CommonRepoSetup repo, string[] projectedFiles)
        {
            this.Repo = repo;
            this.MockPlaceholderDb = new Mock<IPlaceholderCollection>(MockBehavior.Strict);
            this.MockPlaceholderDb.Setup(x => x.GetCount()).Returns(1);
            this.MockSparseDb = new Mock<ISparseCollection>(MockBehavior.Strict);
            this.BackgroundTaskRunner = new MockBackgroundFileSystemTaskRunner();
            this.GitIndexProjection = new MockGitIndexProjection(projectedFiles);
            this.Virtualizer = this.CreateVirtualizer(repo);
            this.FileSystemCallbacks = new FileSystemCallbacks(
                repo.Context,
                repo.GitObjects,
                RepoMetadata.Instance,
                new MockBlobSizes(),
                this.GitIndexProjection,
                this.BackgroundTaskRunner,
                this.Virtualizer,
                this.MockPlaceholderDb.Object,
                this.MockSparseDb.Object);

            this.FileSystemCallbacks.TryStart(out string error).ShouldEqual(true);
        }

        public CommonRepoSetup Repo { get; }
        public Mock<IPlaceholderCollection> MockPlaceholderDb { get; }
        public Mock<ISparseCollection> MockSparseDb { get; }

        public MockBackgroundFileSystemTaskRunner BackgroundTaskRunner { get; }
        public MockGitIndexProjection GitIndexProjection { get; }
        public FileSystemVirtualizer Virtualizer { get; }
        public FileSystemCallbacks FileSystemCallbacks { get; }

        public virtual void Dispose()
        {
            this.FileSystemCallbacks?.Stop();
            this.MockPlaceholderDb.VerifyAll();
            this.MockSparseDb.VerifyAll();
            this.FileSystemCallbacks?.Dispose();
            this.Virtualizer?.Dispose();
            this.GitIndexProjection?.Dispose();
            this.BackgroundTaskRunner?.Dispose();
        }

        protected abstract FileSystemVirtualizer CreateVirtualizer(CommonRepoSetup repo);
    }
}
