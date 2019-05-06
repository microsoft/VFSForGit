using GVFS.Common;
using GVFS.Common.Git;
using GVFS.Common.NuGetUpgrade;
using GVFS.Common.Tracing;
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

namespace GVFS.UnitTests.Common.NuGetUpgrade
{
    [TestFixture]
    public class NuGetUpgraderTests
    {
        private const string OlderVersion = "1.0.1185.0";
        private const string CurrentVersion = "1.5.1185.0";
        private const string NewerVersion = "1.6.1185.0";
        private const string NewerVersion2 = "1.7.1185.0";

        private const string NuGetFeedUrl = "https://pkgs.dev.azure.com/contoso/packages";
        private const string NuGetFeedName = "feedNameValue";

        private static Exception httpRequestAuthException = new System.Net.Http.HttpRequestException("Response status code does not indicate success: 401 (Unauthorized).");
        private static Exception fatalProtocolAuthException = new FatalProtocolException("Unable to load the service index for source.", httpRequestAuthException);

        private static Exception[] networkAuthFailures =
        {
            httpRequestAuthException,
            fatalProtocolAuthException
        };

        private NuGetUpgrader upgrader;
        private MockTracer tracer;

        private NuGetUpgrader.NuGetUpgraderConfig upgraderConfig;

        private Mock<NuGetFeed> mockNuGetFeed;
        private MockFileSystem mockFileSystem;
        private Mock<ICredentialStore> mockCredentialManager;

        private string downloadDirectoryPath = Path.Combine(
            $"mock:{Path.DirectorySeparatorChar}",
            ProductUpgraderInfo.UpgradeDirectoryName,
            ProductUpgraderInfo.DownloadDirectory);

        private delegate void DownloadPackageAsyncCallback(PackageIdentity packageIdentity);

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
            this.mockNuGetFeed.Setup(feed => feed.SetCredentials(It.IsAny<string>()));

            this.mockFileSystem = new MockFileSystem(
                new MockDirectory(
                    Path.GetDirectoryName(this.downloadDirectoryPath),
                    new[] { new MockDirectory(this.downloadDirectoryPath, null, null) },
                    null));

            this.mockCredentialManager = new Mock<ICredentialStore>();
            string credentialManagerString = "value";
            string emptyString = string.Empty;
            this.mockCredentialManager.Setup(foo => foo.TryGetCredential(It.IsAny<ITracer>(), It.IsAny<string>(), out credentialManagerString, out credentialManagerString, out credentialManagerString)).Returns(true);

            this.upgrader = new NuGetUpgrader(
                CurrentVersion,
                this.tracer,
                false,
                false,
                this.mockFileSystem,
                this.upgraderConfig,
                this.mockNuGetFeed.Object,
                this.mockCredentialManager.Object);
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
            this.mockNuGetFeed.Setup(foo => foo.VerifyPackage(It.IsAny<string>())).Returns(true);

            bool success = this.upgrader.TryQueryNewestVersion(out actualNewestVersion, out message);

            // Assert that no new version was returned
            success.ShouldBeTrue($"Expecting TryQueryNewestVersion to have completed sucessfully. Error: {message}");
            actualNewestVersion.ShouldEqual(newestAvailableVersion.Identity.Version.Version, "Actual new version does not match expected new version.");

            bool downloadSuccessful = this.upgrader.TryDownloadNewestVersion(out message);
            downloadSuccessful.ShouldBeTrue();
            this.upgrader.DownloadedPackagePath.ShouldEqual(testDownloadPath);
            this.mockNuGetFeed.Verify(nuGetFeed => nuGetFeed.VerifyPackage(It.IsAny<string>()), Times.Once());
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

            this.mockFileSystem.TryCreateOrUpdateDirectoryToAdminModifyPermissionsShouldSucceed = false;
            bool downloadSuccessful = this.upgrader.TryDownloadNewestVersion(out message);
            this.mockFileSystem.TryCreateOrUpdateDirectoryToAdminModifyPermissionsShouldSucceed = true;
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
            this.mockNuGetFeed.Setup(foo => foo.VerifyPackage(It.IsAny<string>())).Returns(true);

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
                this.mockNuGetFeed.Object,
                this.mockCredentialManager.Object);

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
                this.mockNuGetFeed.Object,
                this.mockCredentialManager.Object);

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
                this.mockNuGetFeed.Object,
                this.mockCredentialManager.Object);

            nuGetUpgrader.UpgradeAllowed(out string _).ShouldBeFalse("Upgrade without FeedName configured should not be allowed.");
        }

        [TestCaseSource("networkAuthFailures")]
        public void QueryNewestVersionReacquiresCredentialsOnAuthFailure(Exception exception)
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
            this.mockNuGetFeed.SetupSequence(foo => foo.QueryFeedAsync(It.IsAny<string>()))
                .Throws(exception)
                .ReturnsAsync(availablePackages);

            // Setup the credential manager
            string emptyString = string.Empty;
            this.mockCredentialManager.Setup(foo => foo.TryDeleteCredential(It.IsAny<ITracer>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), out emptyString)).Returns(true);

            bool success = this.upgrader.TryQueryNewestVersion(out actualNewestVersion, out message);

            // Verify expectations
            success.ShouldBeTrue($"Expecting TryQueryNewestVersion to have completed sucessfully. Error: {message}");
            actualNewestVersion.ShouldEqual(newestAvailableVersion.Identity.Version.Version, "Actual new version does not match expected new version.");

            this.mockNuGetFeed.Verify(nuGetFeed => nuGetFeed.QueryFeedAsync(It.IsAny<string>()), Times.Exactly(2));

            string outString = string.Empty;
            this.mockCredentialManager.Verify(credentialManager => credentialManager.TryGetCredential(It.IsAny<ITracer>(), It.IsAny<string>(), out outString, out outString, out outString), Times.Exactly(2));
            this.mockCredentialManager.Verify(credentialManager => credentialManager.TryDeleteCredential(It.IsAny<ITracer>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), out outString), Times.Exactly(1));
        }

        [TestCase]
        public void WellKnownArgumentTokensReplaced()
        {
            string logDirectory = "mock:\\test_log_directory";
            string noTokenSourceString = "/arg no_token log_directory installation_id";
            NuGetUpgrader.ReplaceArgTokens(noTokenSourceString, "unique_id", logDirectory).ShouldEqual(noTokenSourceString, "String with no tokens should not be modifed");

            string sourceStringWithTokens = "/arg /log {log_directory}_{installation_id}";
            string expectedProcessedString = "/arg /log " + logDirectory + "_" + "unique_id";
            NuGetUpgrader.ReplaceArgTokens(sourceStringWithTokens, "unique_id", logDirectory).ShouldEqual(expectedProcessedString, "expected tokens have not been replaced");
        }

        [TestCase]
        public void TrySetupToolsDirectoryFailsIfCreateToolsDirectoryFails()
        {
            this.mockFileSystem.TryCreateOrUpdateDirectoryToAdminModifyPermissionsShouldSucceed = false;
            this.upgrader.TrySetupToolsDirectory(out string upgraderToolsPath, out string error).ShouldBeFalse();
            this.mockFileSystem.TryCreateOrUpdateDirectoryToAdminModifyPermissionsShouldSucceed = true;
        }

        [TestCase]
        public void DownloadFailsOnNuGetPackageVerificationFailure()
        {
            Version actualNewestVersion;
            string message;
            List<IPackageSearchMetadata> availablePackages = new List<IPackageSearchMetadata>()
            {
                this.GeneratePackageSeachMetadata(new Version(CurrentVersion)),
                this.GeneratePackageSeachMetadata(new Version(NewerVersion)),
            };

            IPackageSearchMetadata newestAvailableVersion = availablePackages.Last();

            string testDownloadPath = Path.Combine(this.downloadDirectoryPath, "testNuget.zip");
            this.mockNuGetFeed.Setup(foo => foo.QueryFeedAsync(NuGetFeedName)).ReturnsAsync(availablePackages);
            this.mockNuGetFeed.Setup(foo => foo.DownloadPackageAsync(It.Is<PackageIdentity>(packageIdentity => packageIdentity == newestAvailableVersion.Identity)))
                .Callback(new DownloadPackageAsyncCallback(
                    (packageIdentity) => this.mockFileSystem.WriteAllText(testDownloadPath, "Package contents that will fail validation")))
                .ReturnsAsync(testDownloadPath);
            this.mockNuGetFeed.Setup(foo => foo.VerifyPackage(It.IsAny<string>())).Returns(false);

            bool success = this.upgrader.TryQueryNewestVersion(out actualNewestVersion, out message);
            success.ShouldBeTrue($"Expecting TryQueryNewestVersion to have completed sucessfully. Error: {message}");
            actualNewestVersion.ShouldEqual(newestAvailableVersion.Identity.Version.Version, "Actual new version does not match expected new version.");

            bool downloadSuccessful = this.upgrader.TryDownloadNewestVersion(out message);
            this.mockNuGetFeed.Verify(nuGetFeed => nuGetFeed.VerifyPackage(this.upgrader.DownloadedPackagePath), Times.Once());
            downloadSuccessful.ShouldBeFalse("Failure to verify NuGet package should cause download to fail.");
            this.mockFileSystem.FileExists(testDownloadPath).ShouldBeFalse("VerifyPackage should delete invalid packages");
        }

        [TestCase]
        public void DoNotVerifyNuGetPackageWhenNoVerifyIsSpecified()
        {
            NuGetUpgrader.NuGetUpgraderConfig nuGetUpgraderConfig =
                new NuGetUpgrader.NuGetUpgraderConfig(this.tracer, null, NuGetFeedUrl, NuGetFeedName);

            NuGetUpgrader nuGetUpgrader = new NuGetUpgrader(
                CurrentVersion,
                this.tracer,
                false,
                true,
                this.mockFileSystem,
                nuGetUpgraderConfig,
                this.mockNuGetFeed.Object,
                this.mockCredentialManager.Object);

            Version actualNewestVersion;
            string message;
            List<IPackageSearchMetadata> availablePackages = new List<IPackageSearchMetadata>()
            {
                this.GeneratePackageSeachMetadata(new Version(CurrentVersion)),
                this.GeneratePackageSeachMetadata(new Version(NewerVersion)),
            };

            IPackageSearchMetadata newestAvailableVersion = availablePackages.Last();

            string testDownloadPath = Path.Combine(this.downloadDirectoryPath, "testNuget.zip");
            this.mockNuGetFeed.Setup(foo => foo.QueryFeedAsync(NuGetFeedName)).ReturnsAsync(availablePackages);
            this.mockNuGetFeed.Setup(foo => foo.DownloadPackageAsync(It.Is<PackageIdentity>(packageIdentity => packageIdentity == newestAvailableVersion.Identity))).ReturnsAsync(testDownloadPath);
            this.mockNuGetFeed.Setup(foo => foo.VerifyPackage(It.IsAny<string>())).Returns(false);

            bool success = nuGetUpgrader.TryQueryNewestVersion(out actualNewestVersion, out message);
            success.ShouldBeTrue($"Expecting TryQueryNewestVersion to have completed sucessfully. Error: {message}");
            actualNewestVersion.ShouldEqual(newestAvailableVersion.Identity.Version.Version, "Actual new version does not match expected new version.");

            bool downloadSuccessful = nuGetUpgrader.TryDownloadNewestVersion(out message);
            this.mockNuGetFeed.Verify(nuGetFeed => nuGetFeed.VerifyPackage(It.IsAny<string>()), Times.Never());
            downloadSuccessful.ShouldBeTrue("Should be able to download package with verification issues when noVerify is specified");
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
