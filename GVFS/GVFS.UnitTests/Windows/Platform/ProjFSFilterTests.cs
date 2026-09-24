using GVFS.Common;
using GVFS.Common.FileSystem;
using GVFS.Platform.Windows;
using GVFS.Tests.Should;
using GVFS.UnitTests.Mock.Common;
using Moq;
using NUnit.Framework;
using System;
using System.ComponentModel;
using System.IO;
using System.ServiceProcess;

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
        public void TryGetDisabledStartTypeError_ReturnsActionableMessageForDisabledService()
        {
            // A disabled driver service must produce the actionable enable-and-retry message,
            // not a generic start failure.
            bool isDisabled = ProjFSFilter.TryGetDisabledStartTypeError(ServiceStartMode.Disabled, out string error);
            isDisabled.ShouldBeTrue();
            error.ShouldContain("sc.exe config prjflt start= auto");
        }

        [TestCase(ServiceStartMode.Automatic)]
        [TestCase(ServiceStartMode.Manual)]
        [TestCase(ServiceStartMode.Boot)]
        [TestCase(ServiceStartMode.System)]
        public void TryGetDisabledStartTypeError_ReturnsFalseForEnabledStartTypes(ServiceStartMode startType)
        {
            bool isDisabled = ProjFSFilter.TryGetDisabledStartTypeError(startType, out string error);
            isDisabled.ShouldBeFalse();
            error.ShouldBeNull();
        }

        [TestCase(1058, ProjFSFilter.ServiceStartFailureReason.ServiceDisabled)]
        [TestCase(1060, ProjFSFilter.ServiceStartFailureReason.ServiceNotFound)]
        [TestCase(5, ProjFSFilter.ServiceStartFailureReason.StartFailed)]     // ERROR_ACCESS_DENIED
        [TestCase(1056, ProjFSFilter.ServiceStartFailureReason.StartFailed)]  // ERROR_SERVICE_ALREADY_RUNNING
        public void GetStartFailureReason_UnwrapsWin32ExceptionFromInvalidOperationException(int nativeErrorCode, ProjFSFilter.ServiceStartFailureReason expectedReason)
        {
            // ServiceController reports a failed native service call this way: an InvalidOperationException
            // that wraps the Win32Exception.
            InvalidOperationException wrapped = new InvalidOperationException("Cannot start service", new Win32Exception(nativeErrorCode));
            ProjFSFilter.GetStartFailureReason(wrapped).ShouldEqual(expectedReason);
        }

        [TestCase]
        public void GetStartFailureReason_HandlesBareWin32Exception()
        {
            ProjFSFilter.GetStartFailureReason(new Win32Exception(1058)).ShouldEqual(ProjFSFilter.ServiceStartFailureReason.ServiceDisabled);
        }

        [TestCase]
        public void GetStartFailureReason_ReturnsStartFailedWhenNoWin32ExceptionIsWrapped()
        {
            ProjFSFilter.GetStartFailureReason(new InvalidOperationException("no inner exception")).ShouldEqual(ProjFSFilter.ServiceStartFailureReason.StartFailed);
        }

        [TestCase]
        public void GetStartFailureMessage_ReturnsActionableMessageForDisabledService()
        {
            // A disabled driver service must produce the actionable enable-and-retry message,
            // not a generic start failure.
            ProjFSFilter.GetStartFailureMessage(ProjFSFilter.ServiceStartFailureReason.ServiceDisabled).ShouldContain("sc.exe config prjflt start= auto");
        }

        [TestCase(ProjFSFilter.ServiceStartFailureReason.ServiceNotFound, "was not found")]
        [TestCase(ProjFSFilter.ServiceStartFailureReason.StartFailed, "Failed to start")]
        public void GetStartFailureMessage_ReturnsDistinctMessageForOtherReasons(ProjFSFilter.ServiceStartFailureReason failureReason, string expectedText)
        {
            string error = ProjFSFilter.GetStartFailureMessage(failureReason);
            error.ShouldContain(expectedText);
            error.ShouldNotContain(false, "sc.exe config");
        }
    }
}
