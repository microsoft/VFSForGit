using GVFS.Common;
using GVFS.Platform.Windows;
using GVFS.Tests.Should;
using GVFS.UnitTests.Common.NuGetUpgrade;
using Moq;
using NuGet.Packaging.Core;
using NuGet.Protocol.Core.Types;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace GVFS.UnitTests.Windows.Common.Upgrader
{
    [TestFixture]
    public class WindowsNuGetUpgraderTests : NuGetUpgraderTests
    {
        public override ProductUpgraderPlatformStrategy CreateProductUpgraderPlatformStrategy()
        {
            return new WindowsProductUpgraderPlatformStrategy(this.mockFileSystem, this.tracer);
        }

        [TestCase]
        public void TrySetupUpgradeApplicationDirectoryFailsIfCreateToolsDirectoryFails()
        {
            this.mockFileSystem.TryCreateOrUpdateDirectoryToAdminModifyPermissionsShouldSucceed = false;
            this.upgrader.TrySetupUpgradeApplicationDirectory(out string _, out string _).ShouldBeFalse();
            this.mockFileSystem.TryCreateOrUpdateDirectoryToAdminModifyPermissionsShouldSucceed = true;
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
    }
}
