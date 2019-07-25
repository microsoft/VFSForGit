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

        public MockVirtualizationInstance MockVirtualization { get; private set; }
        public WindowsFileSystemVirtualizer WindowsVirtualizer { get; private set; }

        public void GetFileDataCallbackResultShouldEqual(HResult expectedResult)
        {
            this.MockVirtualization.WriteFileReturnResult = expectedResult;
            this.MockVirtualization.requiredCallbacks.GetFileDataCallback(
                commandId: 1,
                relativePath: "test.txt",
                byteOffset: 0,
                length: MockGVFSGitObjects.DefaultFileLength,
                dataStreamId: Guid.NewGuid(),
                contentId: CommonRepoSetup.DefaultContentId,
                providerId: WindowsFileSystemVirtualizer.PlaceholderVersionId,
                triggeringProcessId: 2,
                triggeringProcessImageFileName: "UnitTest").ShouldEqual(HResult.Pending);

            HResult result = this.MockVirtualization.WaitForCompletionStatus();
            result.ShouldEqual(this.MockVirtualization.WriteFileReturnResult);
        }

        protected override FileSystemVirtualizer CreateVirtualizer(CommonRepoSetup repo)
        {
            this.MockVirtualization = new MockVirtualizationInstance();
            this.WindowsVirtualizer = new WindowsFileSystemVirtualizer(repo.Context, repo.GitObjects, this.MockVirtualization, FileSystemVirtualizerTester.NumberOfWorkThreads);
            return this.WindowsVirtualizer;
        }
    }
}
