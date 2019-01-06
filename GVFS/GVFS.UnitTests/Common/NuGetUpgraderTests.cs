using GVFS.Common;
using GVFS.Tests.Should;
using GVFS.UnitTests.Mock.Common;
using Moq;
using NuGet.Protocol.Core.Types;
using NUnit.Framework;
using System;
using System.Collections.Generic;
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

        private Mock<NuGetWrapper> mockNuGetWrapper;

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

            this.mockNuGetWrapper = new Mock<NuGetWrapper>(feedUrl, feedName, this.downloadFolder, this.personalAccessToken, this.tracer);

            this.upgrader = new NuGetUpgrader(this.currentVersion, this.tracer, this.upgraderConfig, this.downloadFolder, this.personalAccessToken, this.mockNuGetWrapper.Object);
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

            this.mockNuGetWrapper.Setup(foo => foo.QueryFeed(It.IsAny<string>())).Returns(Task.FromResult<IList<IPackageSearchMetadata>>(availablePackages));

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

            this.mockNuGetWrapper.Setup(foo => foo.QueryFeed(It.IsAny<string>())).Returns(Task.FromResult<IList<IPackageSearchMetadata>>(availablePackages));

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

            this.mockNuGetWrapper.Setup(foo => foo.QueryFeed(It.IsAny<string>())).Returns(Task.FromResult<IList<IPackageSearchMetadata>>(availablePackages));

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

            this.mockNuGetWrapper.Setup(foo => foo.QueryFeed(It.IsAny<string>())).Throws(new Exception("Network Error"));

            bool success = this.upgrader.TryQueryNewestVersion(out newVersion, out message);

            // Assert that no new version was returned
            success.ShouldBeFalse();
            newVersion.ShouldBeNull();
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
