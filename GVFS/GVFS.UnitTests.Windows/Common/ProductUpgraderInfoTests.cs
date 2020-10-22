using GVFS.Common;
using GVFS.Common.FileSystem;
using GVFS.Common.Git;
using GVFS.Common.Tracing;
using GVFS.Tests.Should;
using GVFS.UnitTests.Mock.Common;
using Moq;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace GVFS.UnitTests.Common
{
    [TestFixture]
    public class ProductUpgraderInfoTests
    {
        private Mock<PhysicalFileSystem> mockFileSystem;
        private ProductUpgraderInfo productUpgraderInfo;

        private string upgradeDirectory;

        private string expectedNewVersionExistsFileName = "HighestAvailableVersion";
        private string expectedNewVersionExistsFilePath;
        private MockTracer tracer;

        [SetUp]
        public void SetUp()
        {
            this.upgradeDirectory = ProductUpgraderInfo.GetHighestAvailableVersionDirectory();
            this.expectedNewVersionExistsFilePath = Path.Combine(this.upgradeDirectory, this.expectedNewVersionExistsFileName);
            this.mockFileSystem = new Mock<PhysicalFileSystem>();

            this.mockFileSystem.Setup(fileSystem => fileSystem.WriteAllText(this.expectedNewVersionExistsFilePath, It.IsAny<string>()));

            this.tracer = new MockTracer();

            this.productUpgraderInfo = new ProductUpgraderInfo(
                this.tracer,
                this.mockFileSystem.Object);
        }

        [TearDown]
        public void TearDown()
        {
            this.mockFileSystem = null;
            this.productUpgraderInfo = null;
            this.tracer = null;
        }

        [TestCase]
        public void RecordHighestVersion()
        {
            this.productUpgraderInfo.RecordHighestAvailableVersion(new Version("1.0.0.0"));
            this.mockFileSystem.Verify(fileSystem => fileSystem.WriteAllText(this.expectedNewVersionExistsFilePath, It.IsAny<string>()), Times.Once());
        }

        [TestCase]
        public void RecordingEmptyVersionDeletesExistingHighestVersionFile()
        {
            this.mockFileSystem.Setup(fileSystem => fileSystem.FileExists(this.expectedNewVersionExistsFilePath)).Returns(true);

            this.productUpgraderInfo.RecordHighestAvailableVersion(null);

            this.mockFileSystem.Verify(fileSystem => fileSystem.FileExists(this.expectedNewVersionExistsFilePath), Times.Once());
            this.mockFileSystem.Verify(fileSystem => fileSystem.DeleteFile(this.expectedNewVersionExistsFilePath), Times.Once());
        }

        [TestCase]
        public void RecordingEmptyVersionDoesNotDeleteNonExistingHighestVersionFile()
        {
            this.mockFileSystem.Setup(fileSystem => fileSystem.FileExists(this.expectedNewVersionExistsFilePath)).Returns(false);

            this.productUpgraderInfo.RecordHighestAvailableVersion(null);

            this.mockFileSystem.Verify(fileSystem => fileSystem.FileExists(this.expectedNewVersionExistsFilePath), Times.Once());
            this.mockFileSystem.Verify(fileSystem => fileSystem.DeleteFile(this.expectedNewVersionExistsFilePath), Times.Never());
        }
    }
}
