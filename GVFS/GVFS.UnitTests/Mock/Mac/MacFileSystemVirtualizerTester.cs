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

        public void InvokeUpdatePlaceholderIfNeeded(string fileName, FileSystemResult expectedResult, UpdateFailureCause expectedFailureCause)
        {
            UpdateFailureReason failureReason = UpdateFailureReason.NoFailure;
            this.MacVirtualizer.UpdatePlaceholderIfNeeded(
                    fileName,
                    DateTime.Now,
                    DateTime.Now,
                    DateTime.Now,
                    DateTime.Now,
                    0,
                    15,
                    string.Empty,
                    UpdatePlaceholderType.AllowReadOnly,
                    out failureReason)
                .ShouldEqual(expectedResult);
            failureReason.ShouldEqual((UpdateFailureReason)expectedFailureCause);
        }

        protected override FileSystemVirtualizer CreateVirtualizer(CommonRepoSetup repo)
        {
            this.MockVirtualization = new MockVirtualizationInstance();
            this.MacVirtualizer = new MacFileSystemVirtualizer(repo.Context, repo.GitObjects, this.MockVirtualization);
            return this.MacVirtualizer;
        }
    }
}
