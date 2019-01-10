using GVFS.Common;
using GVFS.Common.FileSystem;
using GVFS.Common.NuGetUpgrader;
using GVFS.Tests.Should;
using GVFS.UnitTests.Mock;
using GVFS.UnitTests.Mock.Common;
using Moq;
using NuGet.Packaging.Core;
using NuGet.Protocol;
using NuGet.Protocol.Core.Types;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace GVFS.UnitTests.Common
{
    [TestFixture]
    public class NuGetUpgraderTests
    {
        private NuGetUpgrader upgrader;
        private MockTracer tracer;

        private string olderVersion = "1.0.1185.0";
        private string currentVersion = "1.5.1185.0";
        private string newerVersion = "1.6.1185.0";
        private string newerVersion2 = "1.7.1185.0";

        private NuGetUpgrader.NugetUpgraderConfig upgraderConfig;
        private string downloadFolder;
        private string personalAccessToken;

        private Mock<NuGetFeed> mockNuGetFeed;
        private Mock<PhysicalFileSystem> mockFileSystem;

        [SetUp]
        public void SetUp()
        {
            string feedUrl = "feedUrl";
            string feedUrlForCredentials = "feedUrl";
            string feedName = "feedUrl";

            this.upgraderConfig = new NuGetUpgrader.NugetUpgraderConfig(this.tracer, null, feedUrl, feedName, feedUrlForCredentials);
            this.downloadFolder = "what";
            this.personalAccessToken = "what";

            this.tracer = new MockTracer();

            this.mockNuGetFeed = new Mock<NuGetFeed>(feedUrl, feedName, this.downloadFolder, this.personalAccessToken, this.tracer);
            this.mockFileSystem = new Mock<PhysicalFileSystem>();

            this.upgrader = new NuGetUpgrader(
                this.currentVersion,
                this.tracer,
                this.upgraderConfig,
                this.mockFileSystem.Object,
                this.mockNuGetFeed.Object,
                new LocalUpgraderServices(this.tracer, this.mockFileSystem.Object));
        }

        [TestCase]
        public void TryQueryNewestVersion_NewVersionAvailable()
        {
            Version newVersion;
            string message;
            List<IPackageSearchMetadata> availablePackages = new List<IPackageSearchMetadata>()
            {
                this.GeneratePackageSeachMetadata(new Version(this.currentVersion)),
                this.GeneratePackageSeachMetadata(new Version(this.newerVersion)),
            };

            this.mockNuGetFeed.Setup(foo => foo.QueryFeed(It.IsAny<string>())).Returns(Task.FromResult<IList<IPackageSearchMetadata>>(availablePackages));

            bool success = this.upgrader.TryQueryNewestVersion(out newVersion, out message);

            // Assert that we found the newer version
            success.ShouldBeTrue();
            newVersion.ShouldNotBeNull();
            newVersion.ShouldEqual<Version>(new Version(this.newerVersion));
            message.ShouldNotBeNull();
        }

        [TestCase]
        public void TryQueryNewestVersion_MultipleNewVersionsAvailable()
        {
            Version newVersion;
            string message;
            List<IPackageSearchMetadata> availablePackages = new List<IPackageSearchMetadata>()
            {
                this.GeneratePackageSeachMetadata(new Version(this.currentVersion)),
                this.GeneratePackageSeachMetadata(new Version(this.newerVersion)),
                this.GeneratePackageSeachMetadata(new Version(this.newerVersion2)),
            };

            this.mockNuGetFeed.Setup(foo => foo.QueryFeed(It.IsAny<string>())).Returns(Task.FromResult<IList<IPackageSearchMetadata>>(availablePackages));

            bool success = this.upgrader.TryQueryNewestVersion(out newVersion, out message);

            // Assert that we found the newest version
            success.ShouldBeTrue();
            newVersion.ShouldNotBeNull();
            newVersion.ShouldEqual<Version>(new Version(this.newerVersion2));
            message.ShouldNotBeNull();
        }

        [TestCase]
        public void TryQueryNewestVersion_NoNewerVersionsAvailable()
        {
            Version newVersion;
            string message;
            List<IPackageSearchMetadata> availablePackages = new List<IPackageSearchMetadata>()
            {
                this.GeneratePackageSeachMetadata(new Version(this.olderVersion)),
                this.GeneratePackageSeachMetadata(new Version(this.currentVersion)),
            };

            this.mockNuGetFeed.Setup(foo => foo.QueryFeed(It.IsAny<string>())).Returns(Task.FromResult<IList<IPackageSearchMetadata>>(availablePackages));

            bool success = this.upgrader.TryQueryNewestVersion(out newVersion, out message);

            // Assert that no new version was returned
            success.ShouldBeTrue();
            newVersion.ShouldBeNull();
        }

        [TestCase]
        public void TryQueryNewestVersion_Exception()
        {
            Version newVersion;
            string message;
            List<IPackageSearchMetadata> availablePackages = new List<IPackageSearchMetadata>()
            {
                this.GeneratePackageSeachMetadata(new Version(this.olderVersion)),
                this.GeneratePackageSeachMetadata(new Version(this.currentVersion)),
            };

            this.mockNuGetFeed.Setup(foo => foo.QueryFeed(It.IsAny<string>())).Throws(new Exception("Network Error"));

            bool success = this.upgrader.TryQueryNewestVersion(out newVersion, out message);

            // Assert that no new version was returned
            success.ShouldBeFalse();
            newVersion.ShouldBeNull();
        }

        [TestCase]
        public void CanDownloadNewestVersion()
        {
            Version newVersion;
            string message;
            List<IPackageSearchMetadata> availablePackages = new List<IPackageSearchMetadata>()
            {
                this.GeneratePackageSeachMetadata(new Version(this.currentVersion)),
                this.GeneratePackageSeachMetadata(new Version(this.newerVersion)),
            };

            this.mockNuGetFeed.Setup(foo => foo.QueryFeed(It.IsAny<string>())).Returns(Task.FromResult<IList<IPackageSearchMetadata>>(availablePackages));

            // TODO: verify expected argument
            this.mockNuGetFeed.Setup(foo => foo.DownloadPackage(It.IsAny<PackageIdentity>())).Returns(Task.FromResult("package_path"));

            bool success = this.upgrader.TryQueryNewestVersion(out newVersion, out message);

            // Assert that no new version was returned
            success.ShouldBeTrue($"Expecting TryQueryNewestVersion to have completed sucessfully. Error: {message}");
            newVersion.ShouldNotBeNull();

            bool downloadSuccessful = this.upgrader.TryDownloadNewestVersion(out message);
            downloadSuccessful.ShouldBeTrue();
        }

        [TestCase]
        public void DownloadNewestVersion_HandleException()
        {
            Version newVersion;
            string message;
            List<IPackageSearchMetadata> availablePackages = new List<IPackageSearchMetadata>()
            {
                this.GeneratePackageSeachMetadata(new Version(this.currentVersion)),
                this.GeneratePackageSeachMetadata(new Version(this.newerVersion)),
            };

            this.mockNuGetFeed.Setup(foo => foo.QueryFeed(It.IsAny<string>())).Returns(Task.FromResult<IList<IPackageSearchMetadata>>(availablePackages));

            // TODO: verify expected argument
            this.mockNuGetFeed.Setup(foo => foo.DownloadPackage(It.IsAny<PackageIdentity>())).Throws(new Exception("Network Error"));

            bool success = this.upgrader.TryQueryNewestVersion(out newVersion, out message);

            // Assert that no new version was returned
            success.ShouldBeTrue($"Expecting TryQueryNewestVersion to have completed sucessfully. Error: {message}");
            newVersion.ShouldNotBeNull();

            bool downloadSuccessful = this.upgrader.TryDownloadNewestVersion(out message);
            downloadSuccessful.ShouldBeFalse();
        }

        private IPackageSearchMetadata GeneratePackageSeachMetadata(Version version)
        {
            Mock<IPackageSearchMetadata> mockPackageSearchMetaData = new Mock<IPackageSearchMetadata>();
            string id = "id";
            NuGet.Versioning.NuGetVersion nuGetVersion = new NuGet.Versioning.NuGetVersion(version);
            mockPackageSearchMetaData.Setup(foo => foo.Identity).Returns(new NuGet.Packaging.Core.PackageIdentity(id, nuGetVersion));

            return mockPackageSearchMetaData.Object;
        }
    }
}
