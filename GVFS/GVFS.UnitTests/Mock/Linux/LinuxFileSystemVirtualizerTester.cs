using GVFS.Platform.Linux;
using GVFS.Tests.Should;
using GVFS.UnitTests.Virtual;
using GVFS.Virtualization.FileSystem;
using PrjFSLib.Linux;
using System;
using System.IO;

namespace GVFS.UnitTests.Mock.Linux
{
    public class LinuxFileSystemVirtualizerTester : FileSystemVirtualizerTester
    {
        public LinuxFileSystemVirtualizerTester(CommonRepoSetup repo)
            : base(repo)
        {
        }

        public LinuxFileSystemVirtualizerTester(CommonRepoSetup repo, string[] projectedFiles)
            : base(repo, projectedFiles)
        {
        }

        public MockVirtualizationInstance MockVirtualization { get; private set; }
        public LinuxFileSystemVirtualizer LinuxVirtualizer { get; private set; }

        public void InvokeOnGetFileStream(Result expectedResult = Result.Pending, byte[] providerId = null)
        {
            if (providerId == null)
            {
                providerId = LinuxFileSystemVirtualizer.PlaceholderVersionId;
            }

            this.MockVirtualization.OnGetFileStream(
                commandId: 1,
                relativePath: "test.txt",
                providerId: LinuxFileSystemVirtualizer.PlaceholderVersionId,
                contentId: CommonRepoSetup.DefaultContentId,
                triggeringProcessId: 2,
                triggeringProcessName: "UnitTest",
                fd: 0).ShouldEqual(expectedResult);
        }

        public void InvokeUpdatePlaceholderIfNeeded(string fileName, FileSystemResult expectedResult, UpdateFailureCause expectedFailureCause)
        {
            UpdateFailureReason failureReason = UpdateFailureReason.NoFailure;
            this.LinuxVirtualizer.UpdatePlaceholderIfNeeded(
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
            this.LinuxVirtualizer = new LinuxFileSystemVirtualizer(repo.Context, repo.GitObjects, this.MockVirtualization);
            return this.LinuxVirtualizer;
        }
    }
}
