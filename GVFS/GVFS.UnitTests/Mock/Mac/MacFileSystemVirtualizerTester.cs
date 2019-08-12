using GVFS.Platform.Mac;
using GVFS.Tests.Should;
using GVFS.UnitTests.Virtual;
using GVFS.Virtualization.FileSystem;
using PrjFSLib.Mac;
using System;

namespace GVFS.UnitTests.Mock.Mac
{
    public class MacFileSystemVirtualizerTester : FileSystemVirtualizerTester
    {
        public MacFileSystemVirtualizerTester(CommonRepoSetup repo)
            : base(repo)
        {
        }

        public MacFileSystemVirtualizerTester(CommonRepoSetup repo, string[] projectedFiles)
            : base(repo, projectedFiles)
        {
        }

        public MockVirtualizationInstance MockVirtualization { get; private set; }
        public MacFileSystemVirtualizer MacVirtualizer { get; private set; }

        public void InvokeOnGetFileStream(Result expectedResult = Result.Pending, byte[] providerId = null)
        {
            if (providerId == null)
            {
                providerId = MacFileSystemVirtualizer.PlaceholderVersionId;
            }

            this.MockVirtualization.OnGetFileStream(
                commandId: 1,
                relativePath: "test.txt",
                providerId: MacFileSystemVirtualizer.PlaceholderVersionId,
                contentId: CommonRepoSetup.DefaultContentId,
                triggeringProcessId: 2,
                triggeringProcessName: "UnitTest",
                fileHandle: IntPtr.Zero).ShouldEqual(expectedResult);
        }

        protected override FileSystemVirtualizer CreateVirtualizer(CommonRepoSetup repo)
        {
            this.MockVirtualization = new MockVirtualizationInstance();
            this.MacVirtualizer = new MacFileSystemVirtualizer(repo.Context, repo.GitObjects, this.MockVirtualization);
            return this.MacVirtualizer;
        }
    }
}
