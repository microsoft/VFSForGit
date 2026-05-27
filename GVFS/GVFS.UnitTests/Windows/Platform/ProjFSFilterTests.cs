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
    }
}
