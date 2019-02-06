using GVFS.Common;
using GVFS.Common.FileSystem;
using GVFS.Common.NuGetUpgrader;
using GVFS.Tests.Should;
using GVFS.UnitTests.Category;
using GVFS.UnitTests.Mock.Common;
using GVFS.UnitTests.Mock.FileSystem;
using Moq;
using NuGet.Packaging.Core;
using NuGet.Protocol.Core.Types;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace GVFS.UnitTests.Common
{
    [TestFixture]
    public class NuGetUpgraderTests
    {
        private const string OlderVersion = "1.0.1185.0";
        private const string CurrentVersion = "1.5.1185.0";
        private const string NewerVersion = "1.6.1185.0";
        private const string NewerVersion2 = "1.7.1185.0";

        private const string NuGetFeedUrl = "feedUrlValue";
        private const string NuGetFeedName = "feedNameValue";

        private NuGetUpgrader upgrader;
        private MockTracer tracer;

        private NuGetUpgrader.NuGetUpgraderConfig upgraderConfig;

        private Mock<NuGetFeed> mockNuGetFeed;
        private MockFileSystem mockFileSystem;

        private string downloadDirectoryPath = @"mock:\GVFS.Upgrades\Download";

        [SetUp]
        public void SetUp()
        {
            this.upgraderConfig = new NuGetUpgrader.NuGetUpgraderConfig(this.tracer, null, NuGetFeedUrl, NuGetFeedName);

            this.tracer = new MockTracer();

            this.mockNuGetFeed = new Mock<NuGetFeed>(
                NuGetFeedUrl,
                NuGetFeedName,
                this.downloadDirectoryPath,
                null,
                this.tracer);
            this.mockFileSystem = new MockFileSystem(
                new MockDirectory(
                    Path.GetDirectoryName(this.downloadDirectoryPath),
                    new[] { new MockDirectory(this.downloadDirectoryPath, null, null) },
                    null));

            this.upgrader = new NuGetUpgrader(
                CurrentVersion,
                this.tracer,
                false,
                false,
                this.mockFileSystem,
                this.upgraderConfig,
                this.mockNuGetFeed.Object);
        }

        [TearDown]
        public void TearDown()
        {
            this.mockNuGetFeed.Object.Dispose();
            this.tracer.Dispose();
        }

        [TestCase]
        public void TryQueryNewestVersion_NewVersionAvailable()
        {
            Version newVersion;
            string message;
            List<IPackageSearchMetadata> availablePackages = new List<IPackageSearchMetadata>()
            {
                this.GeneratePackageSeachMetadata(new Version(CurrentVersion)),
                this.GeneratePackageSeachMetadata(new Version(NewerVersion)),
            };

            this.mockNuGetFeed.Setup(foo => foo.QueryFeedAsync(It.IsAny<string>())).ReturnsAsync(availablePackages);

            bool success = this.upgrader.TryQueryNewestVersion(out newVersion, out message);

            // Assert that we found the newer version
            success.ShouldBeTrue();
            newVersion.ShouldNotBeNull();
            newVersion.ShouldEqual<Version>(new Version(NewerVersion));
            message.ShouldNotBeNull();
        }

        [TestCase]
        public void TryQueryNewestVersion_MultipleNewVersionsAvailable()
        {
            Version newVersion;
            string message;
            List<IPackageSearchMetadata> availablePackages = new List<IPackageSearchMetadata>()
            {
                this.GeneratePackageSeachMetadata(new Version(CurrentVersion)),
                this.GeneratePackageSeachMetadata(new Version(NewerVersion)),
                this.GeneratePackageSeachMetadata(new Version(NewerVersion2)),
            };

            this.mockNuGetFeed.Setup(foo => foo.QueryFeedAsync(It.IsAny<string>())).ReturnsAsync(availablePackages);

            bool success = this.upgrader.TryQueryNewestVersion(out newVersion, out message);

            // Assert that we found the newest version
            success.ShouldBeTrue();
            newVersion.ShouldNotBeNull();
            newVersion.ShouldEqual<Version>(new Version(NewerVersion2));
            message.ShouldNotBeNull();
        }

        [TestCase]
        public void TryQueryNewestVersion_NoNewerVersionsAvailable()
        {
            Version newVersion;
            string message;
            List<IPackageSearchMetadata> availablePackages = new List<IPackageSearchMetadata>()
            {
                this.GeneratePackageSeachMetadata(new Version(OlderVersion)),
                this.GeneratePackageSeachMetadata(new Version(CurrentVersion)),
            };

            this.mockNuGetFeed.Setup(foo => foo.QueryFeedAsync(It.IsAny<string>())).ReturnsAsync(availablePackages);

            bool success = this.upgrader.TryQueryNewestVersion(out newVersion, out message);

            // Assert that no new version was returned
            success.ShouldBeTrue();
            newVersion.ShouldBeNull();
        }

        [TestCase]
        [Category(CategoryConstants.ExceptionExpected)]
        public void TryQueryNewestVersion_Exception()
        {
            Version newVersion;
            string message;
            List<IPackageSearchMetadata> availablePackages = new List<IPackageSearchMetadata>()
            {
                this.GeneratePackageSeachMetadata(new Version(OlderVersion)),
                this.GeneratePackageSeachMetadata(new Version(CurrentVersion)),
            };

            this.mockNuGetFeed.Setup(foo => foo.QueryFeedAsync(It.IsAny<string>())).Throws(new Exception("Network Error"));

            bool success = this.upgrader.TryQueryNewestVersion(out newVersion, out message);

            // Assert that no new version was returned
            success.ShouldBeFalse();
            newVersion.ShouldBeNull();
            message.ShouldNotBeNull();
            message.Any().ShouldBeTrue();
        }

        [TestCase]
        public void CanDownloadNewestVersion()
        {
            Version actualNewestVersion;
            string message;
            List<IPackageSearchMetadata> availablePackages = new List<IPackageSearchMetadata>()
            {
                this.GeneratePackageSeachMetadata(new Version(CurrentVersion)),
                this.GeneratePackageSeachMetadata(new Version(NewerVersion)),
            };

            string testDownloadPath = Path.Combine(this.downloadDirectoryPath, "testNuget.zip");
            IPackageSearchMetadata newestAvailableVersion = availablePackages.Last();
            this.mockNuGetFeed.Setup(foo => foo.QueryFeedAsync(NuGetFeedName)).ReturnsAsync(availablePackages);
            this.mockNuGetFeed.Setup(foo => foo.DownloadPackageAsync(It.Is<PackageIdentity>(packageIdentity => packageIdentity == newestAvailableVersion.Identity))).ReturnsAsync(testDownloadPath);

            bool success = this.upgrader.TryQueryNewestVersion(out actualNewestVersion, out message);

            // Assert that no new version was returned
            success.ShouldBeTrue($"Expecting TryQueryNewestVersion to have completed sucessfully. Error: {message}");
            actualNewestVersion.ShouldEqual(newestAvailableVersion.Identity.Version.Version, "Actual new version does not match expected new version.");

            bool downloadSuccessful = this.upgrader.TryDownloadNewestVersion(out message);
            downloadSuccessful.ShouldBeTrue();
            this.upgrader.DownloadedPackagePath.ShouldEqual(testDownloadPath);
        }

        [TestCase]
        public void CanDownloadNewestVersionFailsIfDownloadDirectoryCreationFails()
        {
            Version actualNewestVersion;
            string message;
            List<IPackageSearchMetadata> availablePackages = new List<IPackageSearchMetadata>()
            {
                this.GeneratePackageSeachMetadata(new Version(CurrentVersion)),
                this.GeneratePackageSeachMetadata(new Version(NewerVersion)),
            };

            string testDownloadPath = Path.Combine(this.downloadDirectoryPath, "testNuget.zip");
            IPackageSearchMetadata newestAvailableVersion = availablePackages.Last();
            this.mockNuGetFeed.Setup(foo => foo.QueryFeedAsync(NuGetFeedName)).ReturnsAsync(availablePackages);
            this.mockNuGetFeed.Setup(foo => foo.DownloadPackageAsync(It.Is<PackageIdentity>(packageIdentity => packageIdentity == newestAvailableVersion.Identity))).ReturnsAsync(testDownloadPath);

            bool success = this.upgrader.TryQueryNewestVersion(out actualNewestVersion, out message);

            // Assert that no new version was returned
            success.ShouldBeTrue($"Expecting TryQueryNewestVersion to have completed sucessfully. Error: {message}");
            actualNewestVersion.ShouldEqual(newestAvailableVersion.Identity.Version.Version, "Actual new version does not match expected new version.");

            MockPlatformFileSystem mockPlatformFileSystem = GVFSPlatform.Instance.FileSystem as MockPlatformFileSystem;
            mockPlatformFileSystem.TryCreateDirectoryWithAdminOnlyModifyShouldSucceed = false;
            bool downloadSuccessful = this.upgrader.TryDownloadNewestVersion(out message);
            mockPlatformFileSystem.TryCreateDirectoryWithAdminOnlyModifyShouldSucceed = true;
            downloadSuccessful.ShouldBeFalse();
        }

        [TestCase]
        [Category(CategoryConstants.ExceptionExpected)]
        public void DownloadNewestVersion_HandleException()
        {
            Version newVersion;
            string message;
            List<IPackageSearchMetadata> availablePackages = new List<IPackageSearchMetadata>()
            {
                this.GeneratePackageSeachMetadata(new Version(CurrentVersion)),
                this.GeneratePackageSeachMetadata(new Version(NewerVersion)),
            };

            this.mockNuGetFeed.Setup(foo => foo.QueryFeedAsync(It.IsAny<string>())).ReturnsAsync(availablePackages);
            this.mockNuGetFeed.Setup(foo => foo.DownloadPackageAsync(It.IsAny<PackageIdentity>())).Throws(new Exception("Network Error"));

            bool success = this.upgrader.TryQueryNewestVersion(out newVersion, out message);

            success.ShouldBeTrue($"Expecting TryQueryNewestVersion to have completed sucessfully. Error: {message}");
            newVersion.ShouldNotBeNull();

            bool downloadSuccessful = this.upgrader.TryDownloadNewestVersion(out message);
            downloadSuccessful.ShouldBeFalse();
        }

        [TestCase]
        public void AttemptingToDownloadBeforeQueryingFails()
        {
            string message;
            List<IPackageSearchMetadata> availablePackages = new List<IPackageSearchMetadata>()
            {
                this.GeneratePackageSeachMetadata(new Version(CurrentVersion)),
                this.GeneratePackageSeachMetadata(new Version(NewerVersion)),
            };

            IPackageSearchMetadata newestAvailableVersion = availablePackages.Last();

            string downloadPath = "c:\\test_download_path";
            this.mockNuGetFeed.Setup(foo => foo.QueryFeedAsync(NuGetFeedName)).ReturnsAsync(availablePackages);
            this.mockNuGetFeed.Setup(foo => foo.DownloadPackageAsync(It.Is<PackageIdentity>(packageIdentity => packageIdentity == newestAvailableVersion.Identity))).ReturnsAsync(downloadPath);

            bool downloadSuccessful = this.upgrader.TryDownloadNewestVersion(out message);
            downloadSuccessful.ShouldBeFalse();
        }

        [TestCase]
        public void TestUpgradeAllowed()
        {
            // Properly Configured NuGet config
            NuGetUpgrader.NuGetUpgraderConfig nuGetUpgraderConfig =
                new NuGetUpgrader.NuGetUpgraderConfig(this.tracer, null, NuGetFeedUrl, NuGetFeedName);

            NuGetUpgrader nuGetUpgrader = new NuGetUpgrader(
                CurrentVersion,
                this.tracer,
                false,
                false,
                this.mockFileSystem,
                nuGetUpgraderConfig,
                this.mockNuGetFeed.Object);

            nuGetUpgrader.UpgradeAllowed(out _).ShouldBeTrue("NuGetUpgrader config is complete: upgrade should be allowed.");

            // Empty FeedURL
            nuGetUpgraderConfig =
                new NuGetUpgrader.NuGetUpgraderConfig(this.tracer, null, string.Empty, NuGetFeedName);

             nuGetUpgrader = new NuGetUpgrader(
                CurrentVersion,
                this.tracer,
                false,
                false,
                this.mockFileSystem,
                nuGetUpgraderConfig,
                this.mockNuGetFeed.Object);

            nuGetUpgrader.UpgradeAllowed(out string _).ShouldBeFalse("Upgrade without FeedURL configured should not be allowed.");

            // Empty packageFeedName
            nuGetUpgraderConfig =
                new NuGetUpgrader.NuGetUpgraderConfig(this.tracer, null, NuGetFeedUrl, string.Empty);

            // Empty packageFeedName
            nuGetUpgrader = new NuGetUpgrader(
                CurrentVersion,
                this.tracer,
                false,
                false,
                this.mockFileSystem,
                nuGetUpgraderConfig,
                this.mockNuGetFeed.Object);

            nuGetUpgrader.UpgradeAllowed(out string _).ShouldBeFalse("Upgrade without FeedName configured should not be allowed.");
        }

        [TestCase]
        public void WellKnownArgumentTokensReplaced()
        {
            string noTokenSourceString = "/arg no_token log_directory installation_id";
            NuGetUpgrader.ReplaceArgTokens(noTokenSourceString, "unique_id").ShouldEqual(noTokenSourceString, "String with no tokens should not be modifed");

            string sourceStringWithTokens = "/arg /log {log_directory}_{installation_id}";
            string expectedProcessedString = "/arg /log " + ProductUpgraderInfo.GetLogDirectoryPath() + "_" + "unique_id";
            NuGetUpgrader.ReplaceArgTokens(sourceStringWithTokens, "unique_id").ShouldEqual(expectedProcessedString, "expected tokens have not been replaced");
        }

        [TestCase("https://pkgs.dev.azure.com/test-pat/_packaging/Test-GVFS-Installers-Custom/nuget/v3/index.json", "https://test-pat.visualstudio.com")]
        [TestCase("https://PKGS.DEV.azure.com/test-pat/_packaging/Test-GVFS-Installers-Custom/nuget/v3/index.json", "https://test-pat.visualstudio.com")]
        [TestCase("https://dev.azure.com/test-pat/_packaging/Test-GVFS-Installers-Custom/nuget/v3/index.json", null)]
        [TestCase("http://pkgs.dev.azure.com/test-pat/_packaging/Test-GVFS-Installers-Custom/nuget/v3/index.json", null)]
        public void CanConstructAzureDevOpsUrlFromPackageFeedUrl(string packageFeedUrl, string expectedAzureDevOpsUrl)
        {
            bool success = NuGetUpgrader.TryCreateAzDevOrgUrlFromPackageFeedUrl(
                packageFeedUrl,
                out string azureDevOpsUrl,
                out string error);

            if (expectedAzureDevOpsUrl != null)
            {
                success.ShouldBeTrue();
                azureDevOpsUrl.ShouldEqual(expectedAzureDevOpsUrl);
                error.ShouldBeNull();
            }
            else
            {
                success.ShouldBeFalse();
                azureDevOpsUrl.ShouldBeNull();
                error.ShouldNotBeNull();
            }
        }

        [TestCase]
        public void TrySetupToolsDirectoryFailsIfCreateToolsDirectoryFails()
        {
            MockPlatformFileSystem mockPlatformFileSystem = GVFSPlatform.Instance.FileSystem as MockPlatformFileSystem;
            mockPlatformFileSystem.TryCreateDirectoryWithAdminOnlyModifyShouldSucceed = false;
            this.upgrader.TrySetupToolsDirectory(out string upgraderToolsPath, out string error).ShouldBeFalse();
            mockPlatformFileSystem.TryCreateDirectoryWithAdminOnlyModifyShouldSucceed = true;
        }

        private IPackageSearchMetadata GeneratePackageSeachMetadata(Version version)
        {
            Mock<IPackageSearchMetadata> mockPackageSearchMetaData = new Mock<IPackageSearchMetadata>();
            NuGet.Versioning.NuGetVersion nuGetVersion = new NuGet.Versioning.NuGetVersion(version);
            mockPackageSearchMetaData.Setup(foo => foo.Identity).Returns(new NuGet.Packaging.Core.PackageIdentity("generatedPackedId", nuGetVersion));

            return mockPackageSearchMetaData.Object;
        }
    }
}
