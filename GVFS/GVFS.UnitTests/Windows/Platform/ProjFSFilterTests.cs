using GVFS.Common;
using GVFS.Common.FileSystem;
using GVFS.Platform.Windows;
using GVFS.Tests.Should;
using GVFS.UnitTests.Category;
using GVFS.UnitTests.Mock.Common;
using Moq;
using NUnit.Framework;
using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;

namespace GVFS.UnitTests.Windows.Platform
{
    [TestFixture]
    public class ProjFSFilterTests
    {
        private const string System32DriversRoot = @"%SystemRoot%\System32\drivers";
        private const string PrjFltDriverName = "prjflt.sys";
        private const string ProjFSNativeLibFileName = "ProjectedFSLib.dll";

        private readonly string system32NativeLibPath = Path.Combine(Environment.SystemDirectory, ProjFSNativeLibFileName);
        private readonly string nonInboxNativeLibInstallPath = Path.Combine(ProcessHelper.GetCurrentProcessLocation(), ProjFSNativeLibFileName);
        private readonly string packagedNativeLibPath = Path.Combine(ProcessHelper.GetCurrentProcessLocation(), "ProjFS", ProjFSNativeLibFileName);

        private readonly string packagedDriverPath = Path.Combine(ProcessHelper.GetCurrentProcessLocation(), "Filter", PrjFltDriverName);
        private readonly string system32DriverPath = Path.Combine(Environment.ExpandEnvironmentVariables(System32DriversRoot), PrjFltDriverName);

        // .NET doesn't allow us to create custom FileVersionInfos, and so use the version for our assembly and mock
        // the version comparison methods
        private readonly FileVersionInfo dummyVersionInfo = FileVersionInfo.GetVersionInfo(Assembly.GetExecutingAssembly().Location);

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
            this.mockFileSystem.Setup(fileSystem => fileSystem.FileExists(this.nonInboxNativeLibInstallPath)).Returns(false);
            ProjFSFilter.IsNativeLibInstalled(this.mockTracer, this.mockFileSystem.Object).ShouldBeTrue();
        }

        [TestCase]
        public void IsNativeLibInstalled_ReturnsTrueWhenLibInNonInboxInstallLocation()
        {
            this.mockFileSystem.Setup(fileSystem => fileSystem.FileExists(this.system32NativeLibPath)).Returns(false);
            this.mockFileSystem.Setup(fileSystem => fileSystem.FileExists(this.nonInboxNativeLibInstallPath)).Returns(true);
            ProjFSFilter.IsNativeLibInstalled(this.mockTracer, this.mockFileSystem.Object).ShouldBeTrue();
        }

        [TestCase]
        public void IsNativeLibInstalled_ReturnsFalseWhenNativeLibraryDoesNotExistInAnyInstallLocation()
        {
            this.mockFileSystem.Setup(fileSystem => fileSystem.FileExists(It.IsAny<string>())).Returns(false);
            ProjFSFilter.IsNativeLibInstalled(this.mockTracer, this.mockFileSystem.Object).ShouldBeFalse();
        }

        [TestCase]
        public void TryCopyNativeLibIfDriverVersionsMatch_ReturnsFalseWhenLibInSystem32()
        {
            this.mockFileSystem.Setup(fileSystem => fileSystem.FileExists(this.system32NativeLibPath)).Returns(true);
            ProjFSFilter.TryCopyNativeLibIfDriverVersionsMatch(this.mockTracer, this.mockFileSystem.Object, out string _).ShouldBeFalse();
        }

        [TestCase]
        public void TryCopyNativeLibIfDriverVersionsMatch_ReturnsFalseWhenLibAtNonInboxInstallLocation()
        {
            this.mockFileSystem.Setup(fileSystem => fileSystem.FileExists(this.system32NativeLibPath)).Returns(false);
            this.mockFileSystem.Setup(fileSystem => fileSystem.FileExists(this.nonInboxNativeLibInstallPath)).Returns(true);
            ProjFSFilter.TryCopyNativeLibIfDriverVersionsMatch(this.mockTracer, this.mockFileSystem.Object, out string _).ShouldBeFalse();
        }

        [TestCase]
        public void TryCopyNativeLibIfDriverVersionsMatch_ReturnsFalseWhenLibMissingFromPackagedLocation()
        {
            this.mockFileSystem.Setup(fileSystem => fileSystem.FileExists(this.system32NativeLibPath)).Returns(false);
            this.mockFileSystem.Setup(fileSystem => fileSystem.FileExists(this.nonInboxNativeLibInstallPath)).Returns(false);
            this.mockFileSystem.Setup(fileSystem => fileSystem.FileExists(this.packagedNativeLibPath)).Returns(false);
            ProjFSFilter.TryCopyNativeLibIfDriverVersionsMatch(this.mockTracer, this.mockFileSystem.Object, out string _).ShouldBeFalse();
        }

        [TestCase]
        public void TryCopyNativeLibIfDriverVersionsMatch_ReturnsFalseWhenDriverMissingFromPackagedLocation()
        {
            this.mockFileSystem.Setup(fileSystem => fileSystem.FileExists(this.system32NativeLibPath)).Returns(false);
            this.mockFileSystem.Setup(fileSystem => fileSystem.FileExists(this.nonInboxNativeLibInstallPath)).Returns(false);
            this.mockFileSystem.Setup(fileSystem => fileSystem.FileExists(this.packagedNativeLibPath)).Returns(true);
            this.mockFileSystem.Setup(fileSystem => fileSystem.FileExists(this.packagedDriverPath)).Returns(false);
            ProjFSFilter.TryCopyNativeLibIfDriverVersionsMatch(this.mockTracer, this.mockFileSystem.Object, out string _).ShouldBeFalse();
        }

        [TestCase]
        public void TryCopyNativeLibIfDriverVersionsMatch_ReturnsFalseWhenDriverMissingFromSystem32()
        {
            this.mockFileSystem.Setup(fileSystem => fileSystem.FileExists(this.system32NativeLibPath)).Returns(false);
            this.mockFileSystem.Setup(fileSystem => fileSystem.FileExists(this.nonInboxNativeLibInstallPath)).Returns(false);
            this.mockFileSystem.Setup(fileSystem => fileSystem.FileExists(this.packagedNativeLibPath)).Returns(true);
            this.mockFileSystem.Setup(fileSystem => fileSystem.FileExists(this.packagedDriverPath)).Returns(true);
            this.mockFileSystem.Setup(fileSystem => fileSystem.FileExists(this.system32DriverPath)).Returns(false);
            ProjFSFilter.TryCopyNativeLibIfDriverVersionsMatch(this.mockTracer, this.mockFileSystem.Object, out string _).ShouldBeFalse();
        }

        [TestCase]
        public void TryCopyNativeLibIfDriverVersionsMatch_ReturnsFalseWhenFileVersionDoesNotMatch()
        {
            this.mockFileSystem.Setup(fileSystem => fileSystem.FileExists(this.system32NativeLibPath)).Returns(false);
            this.mockFileSystem.Setup(fileSystem => fileSystem.FileExists(this.nonInboxNativeLibInstallPath)).Returns(false);
            this.mockFileSystem.Setup(fileSystem => fileSystem.FileExists(this.packagedNativeLibPath)).Returns(true);
            this.mockFileSystem.Setup(fileSystem => fileSystem.FileExists(this.packagedDriverPath)).Returns(true);
            this.mockFileSystem.Setup(fileSystem => fileSystem.FileExists(this.system32DriverPath)).Returns(true);

            this.mockFileSystem.Setup(fileSystem => fileSystem.GetVersionInfo(this.packagedDriverPath)).Returns(this.dummyVersionInfo);
            this.mockFileSystem.Setup(fileSystem => fileSystem.GetVersionInfo(this.system32DriverPath)).Returns(this.dummyVersionInfo);
            this.mockFileSystem.Setup(fileSystem => fileSystem.FileVersionsMatch(this.dummyVersionInfo, this.dummyVersionInfo)).Returns(false);
            ProjFSFilter.TryCopyNativeLibIfDriverVersionsMatch(this.mockTracer, this.mockFileSystem.Object, out string _).ShouldBeFalse();
        }

        [TestCase]
        public void TryCopyNativeLibIfDriverVersionsMatch_ReturnsFalseWhenProductVersionDoesNotMatch()
        {
            this.mockFileSystem.Setup(fileSystem => fileSystem.FileExists(this.system32NativeLibPath)).Returns(false);
            this.mockFileSystem.Setup(fileSystem => fileSystem.FileExists(this.nonInboxNativeLibInstallPath)).Returns(false);
            this.mockFileSystem.Setup(fileSystem => fileSystem.FileExists(this.packagedNativeLibPath)).Returns(true);
            this.mockFileSystem.Setup(fileSystem => fileSystem.FileExists(this.packagedDriverPath)).Returns(true);
            this.mockFileSystem.Setup(fileSystem => fileSystem.FileExists(this.system32DriverPath)).Returns(true);

            this.mockFileSystem.Setup(fileSystem => fileSystem.GetVersionInfo(this.packagedDriverPath)).Returns(this.dummyVersionInfo);
            this.mockFileSystem.Setup(fileSystem => fileSystem.GetVersionInfo(this.system32DriverPath)).Returns(this.dummyVersionInfo);
            this.mockFileSystem.Setup(fileSystem => fileSystem.FileVersionsMatch(this.dummyVersionInfo, this.dummyVersionInfo)).Returns(true);
            this.mockFileSystem.Setup(fileSystem => fileSystem.ProductVersionsMatch(this.dummyVersionInfo, this.dummyVersionInfo)).Returns(false);
            ProjFSFilter.TryCopyNativeLibIfDriverVersionsMatch(this.mockTracer, this.mockFileSystem.Object, out string _).ShouldBeFalse();
        }

        [TestCase]
        [Category(CategoryConstants.ExceptionExpected)]
        public void TryCopyNativeLibIfDriverVersionsMatch_ReturnsFalseWhenCopyingNativeLibFails()
        {
            this.mockFileSystem.Setup(fileSystem => fileSystem.FileExists(this.system32NativeLibPath)).Returns(false);
            this.mockFileSystem.Setup(fileSystem => fileSystem.FileExists(this.nonInboxNativeLibInstallPath)).Returns(false);
            this.mockFileSystem.Setup(fileSystem => fileSystem.FileExists(this.packagedNativeLibPath)).Returns(true);
            this.mockFileSystem.Setup(fileSystem => fileSystem.FileExists(this.packagedDriverPath)).Returns(true);
            this.mockFileSystem.Setup(fileSystem => fileSystem.FileExists(this.system32DriverPath)).Returns(true);

            this.mockFileSystem.Setup(fileSystem => fileSystem.GetVersionInfo(this.packagedDriverPath)).Returns(this.dummyVersionInfo);
            this.mockFileSystem.Setup(fileSystem => fileSystem.GetVersionInfo(this.system32DriverPath)).Returns(this.dummyVersionInfo);
            this.mockFileSystem.Setup(fileSystem => fileSystem.FileVersionsMatch(this.dummyVersionInfo, this.dummyVersionInfo)).Returns(true);
            this.mockFileSystem.Setup(fileSystem => fileSystem.ProductVersionsMatch(this.dummyVersionInfo, this.dummyVersionInfo)).Returns(true);

            this.mockFileSystem.Setup(fileSystem => fileSystem.CopyFile(this.packagedNativeLibPath, this.nonInboxNativeLibInstallPath, true)).Throws(new IOException());
            ProjFSFilter.TryCopyNativeLibIfDriverVersionsMatch(this.mockTracer, this.mockFileSystem.Object, out string _).ShouldBeFalse();
        }

        [TestCase]
        public void TryCopyNativeLibIfDriverVersionsMatch_ReturnsTrueOnSuccess()
        {
            this.mockFileSystem.Setup(fileSystem => fileSystem.FileExists(this.system32NativeLibPath)).Returns(false);
            this.mockFileSystem.Setup(fileSystem => fileSystem.FileExists(this.nonInboxNativeLibInstallPath)).Returns(false);
            this.mockFileSystem.Setup(fileSystem => fileSystem.FileExists(this.packagedNativeLibPath)).Returns(true);
            this.mockFileSystem.Setup(fileSystem => fileSystem.FileExists(this.packagedDriverPath)).Returns(true);
            this.mockFileSystem.Setup(fileSystem => fileSystem.FileExists(this.system32DriverPath)).Returns(true);

            this.mockFileSystem.Setup(fileSystem => fileSystem.GetVersionInfo(this.packagedDriverPath)).Returns(this.dummyVersionInfo);
            this.mockFileSystem.Setup(fileSystem => fileSystem.GetVersionInfo(this.system32DriverPath)).Returns(this.dummyVersionInfo);
            this.mockFileSystem.Setup(fileSystem => fileSystem.FileVersionsMatch(this.dummyVersionInfo, this.dummyVersionInfo)).Returns(true);
            this.mockFileSystem.Setup(fileSystem => fileSystem.ProductVersionsMatch(this.dummyVersionInfo, this.dummyVersionInfo)).Returns(true);

            this.mockFileSystem.Setup(fileSystem => fileSystem.CopyFile(this.packagedNativeLibPath, this.nonInboxNativeLibInstallPath, true));
            this.mockFileSystem.Setup(fileSystem => fileSystem.FlushFileBuffers(this.nonInboxNativeLibInstallPath));
            ProjFSFilter.TryCopyNativeLibIfDriverVersionsMatch(this.mockTracer, this.mockFileSystem.Object, out string _).ShouldBeTrue();
        }
    }
}
