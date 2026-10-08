using GVFS.Common;
using GVFS.Common.FileSystem;
using GVFS.Platform.Windows;
using GVFS.Tests.Should;
using GVFS.UnitTests.Mock.Common;
using Moq;
using NUnit.Framework;
using System;
using System.IO;

namespace GVFS.UnitTests.Windows.Platform
{
    [TestFixture]
    public class ProjFSFilterTests
    {
        private const string ProjFSNativeLibFileName = "ProjectedFSLib.dll";

        private readonly string system32NativeLibPath = Path.Combine(Environment.SystemDirectory, ProjFSNativeLibFileName);
        private readonly string appLocalNativeLibPath = Path.Combine(ProcessHelper.GetCurrentProcessLocation(), ProjFSNativeLibFileName);

        private Mock<PhysicalFileSystem> mockFileSystem;
        private MockTracer mockTracer;

        [SetUp]
        public void Setup()
        {
            this.mockFileSystem = new Mock<PhysicalFileSystem>(MockBehavior.Strict);
            this.mockTracer = new MockTracer();
        }

        [TearDown]
        public void TearDown()
        {
            this.mockFileSystem.VerifyAll();
        }

        [TestCase]
        public void IsNativeLibInstalled_ReturnsTrueWhenLibInSystem32()
        {
            this.mockFileSystem.Setup(fileSystem => fileSystem.FileExists(this.system32NativeLibPath)).Returns(true);
            this.mockFileSystem.Setup(fileSystem => fileSystem.FileExists(this.appLocalNativeLibPath)).Returns(false);
            ProjFSFilter.IsNativeLibInstalled(this.mockTracer, this.mockFileSystem.Object).ShouldBeTrue();
        }

        [TestCase]
        public void IsNativeLibInstalled_ReturnsFalseWhenLibNotInSystem32()
        {
            this.mockFileSystem.Setup(fileSystem => fileSystem.FileExists(this.system32NativeLibPath)).Returns(false);
            this.mockFileSystem.Setup(fileSystem => fileSystem.FileExists(this.appLocalNativeLibPath)).Returns(false);
            ProjFSFilter.IsNativeLibInstalled(this.mockTracer, this.mockFileSystem.Object).ShouldBeFalse();
        }

        [TestCase]
        public void IsNativeLibInstalled_ReturnsFalseWhenOnlyAppLocalLibExists()
        {
            // App-local lib from a legacy non-inbox install should NOT count as installed.
            // Only the System32 copy (from the Windows optional feature) is valid.
            this.mockFileSystem.Setup(fileSystem => fileSystem.FileExists(this.system32NativeLibPath)).Returns(false);
            this.mockFileSystem.Setup(fileSystem => fileSystem.FileExists(this.appLocalNativeLibPath)).Returns(true);
            ProjFSFilter.IsNativeLibInstalled(this.mockTracer, this.mockFileSystem.Object).ShouldBeFalse();
        }

        [TestCase]
        public void GetStartServiceFailureError_ReturnsActionableMessageForDisabledService()
        {
            // ERROR_SERVICE_DISABLED (1058) is the case that today surfaces a dead-end "failed to start" error.
            string error = ProjFSFilter.GetStartServiceFailureError(1058);
            error.ShouldContain("sc.exe config prjflt start= auto");
        }

        [TestCase(0)]
        [TestCase(5)]     // ERROR_ACCESS_DENIED
        [TestCase(1056)]  // ERROR_SERVICE_ALREADY_RUNNING
        public void GetStartServiceFailureError_ReturnsGenericMessageForOtherErrors(int nativeErrorCode)
        {
            string error = ProjFSFilter.GetStartServiceFailureError(nativeErrorCode);
            error.ShouldContain("Failed to start");
            error.ShouldNotContain(false, "sc.exe config");
        }
    }
}
