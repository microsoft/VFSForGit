using GVFS.Platform.Windows;
using GVFS.Tests.Should;
using GVFS.UnitTests.Mock.Git;
using GVFS.UnitTests.Virtual;
using GVFS.Virtualization.FileSystem;
using Microsoft.Windows.ProjFS;
using System;

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
            : base(ConfigurePlaceholderLockWait(repo, enablePlaceholderLockWait))
        {
            this.GitIndexProjection.ProjectionParseComplete = true;
        }

        public MockVirtualizationInstance MockVirtualization { get; private set; }
        public WindowsFileSystemVirtualizer WindowsVirtualizer { get; private set; }

        public void InvokeGetFileDataCallback(HResult expectedResult = HResult.Pending, byte[] providerId = null, ulong byteOffset = 0)
        {
            if (providerId == null)
            {
                providerId = WindowsFileSystemVirtualizer.PlaceholderVersionId;
            }

            this.MockVirtualization.RequiredCallbacks.GetFileDataCallback(
                commandId: 1,
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
                enablePlaceholderLockWait: repo.Context.Repository.TryGetConfigValue("gvfs.placeholder-lock-wait", out string enabled) && enabled == "true");
            return this.WindowsVirtualizer;
        }

        private static CommonRepoSetup ConfigurePlaceholderLockWait(CommonRepoSetup repo, bool enabled)
        {
            repo.Repository.ConfigValues["gvfs.placeholder-lock-wait"] = enabled ? "true" : "false";
            return repo;
        }
    }
}
