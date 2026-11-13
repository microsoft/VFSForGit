using GVFS.Platform.Windows;
using GVFS.Tests.Should;
using GVFS.UnitTests.Mock.Git;
using GVFS.UnitTests.Virtual;
using GVFS.Virtualization.FileSystem;
using Microsoft.Windows.ProjFS;
using System;
using System.Globalization;

namespace GVFS.UnitTests.Windows.Mock
{
    public class WindowsFileSystemVirtualizerTester : FileSystemVirtualizerTester
    {
        public WindowsFileSystemVirtualizerTester(CommonRepoSetup repo)
            : base(repo)
        {
        }

        public WindowsFileSystemVirtualizerTester(CommonRepoSetup repo, string[] projectedFiles)
            : base(repo, projectedFiles)
        {
        }

        public WindowsFileSystemVirtualizerTester(CommonRepoSetup repo, bool enablePlaceholderLockWait)
            : this(repo, enablePlaceholderLockWait ? 2000 : 0)
        {
        }

        public WindowsFileSystemVirtualizerTester(CommonRepoSetup repo, int placeholderLockWaitMs)
            : base(ConfigurePlaceholderLockWait(repo, placeholderLockWaitMs))
        {
            this.GitIndexProjection.ProjectionParseComplete = true;
        }

        public MockVirtualizationInstance MockVirtualization { get; private set; }
        public WindowsFileSystemVirtualizer WindowsVirtualizer { get; private set; }

        public void InvokeGetFileDataCallback(HResult expectedResult = HResult.Pending, byte[] providerId = null, ulong byteOffset = 0, int commandId = 1)
        {
            if (providerId == null)
            {
                providerId = WindowsFileSystemVirtualizer.PlaceholderVersionId;
            }

            this.MockVirtualization.RequiredCallbacks.GetFileDataCallback(
                commandId: commandId,
                relativePath: "test.txt",
                byteOffset: byteOffset,
                length: MockGVFSGitObjects.DefaultFileLength,
                dataStreamId: Guid.NewGuid(),
                contentId: CommonRepoSetup.DefaultContentId,
                providerId: providerId,
                triggeringProcessId: 2,
                triggeringProcessImageFileName: "UnitTest").ShouldEqual(expectedResult);
        }

        protected override FileSystemVirtualizer CreateVirtualizer(CommonRepoSetup repo)
        {
            this.MockVirtualization = new MockVirtualizationInstance();
            this.WindowsVirtualizer = new WindowsFileSystemVirtualizer(
                repo.Context, repo.GitObjects, this.MockVirtualization, FileSystemVirtualizerTester.NumberOfWorkThreads,
                placeholderLockWaitMs: WindowsFileSystemVirtualizer.ReadPlaceholderLockWaitMs(
                    repo.Context.Tracer,
                    () => repo.Context.Repository.TryGetConfigValue(GVFS.Common.GVFSConstants.GitConfig.PlaceholderLockWaitMs, out string value) ? value : null))
            {
                IsRequestDescendant = (_, _, _) => false,
            };
            return this.WindowsVirtualizer;
        }

        private static CommonRepoSetup ConfigurePlaceholderLockWait(CommonRepoSetup repo, int milliseconds)
        {
            repo.Repository.ConfigValues[GVFS.Common.GVFSConstants.GitConfig.PlaceholderLockWaitMs] = milliseconds.ToString(CultureInfo.InvariantCulture);
            return repo;
        }
    }
}
