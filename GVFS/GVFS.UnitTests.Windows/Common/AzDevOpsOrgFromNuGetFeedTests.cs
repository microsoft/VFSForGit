using GVFS.Common;
using GVFS.Tests.Should;
using NUnit.Framework;

namespace GVFS.UnitTests.Common
{
    [TestFixture]
    public class NuGetUpgraderTests
    {
        [TestCase("https://pkgs.dev.azure.com/test-pat/_packaging/Test-GVFS-Installers-Custom/nuget/v3/index.json", "https://test-pat.visualstudio.com")]
        [TestCase("https://PKGS.DEV.azure.com/test-pat/_packaging/Test-GVFS-Installers-Custom/nuget/v3/index.json", "https://test-pat.visualstudio.com")]
        [TestCase("https://dev.azure.com/test-pat/_packaging/Test-GVFS-Installers-Custom/nuget/v3/index.json", null)]
        [TestCase("http://pkgs.dev.azure.com/test-pat/_packaging/Test-GVFS-Installers-Custom/nuget/v3/index.json", null)]
        public void CanConstructAzureDevOpsUrlFromPackageFeedUrl(string packageFeedUrl, string expectedAzureDevOpsUrl)
        {
            bool success = AzDevOpsOrgFromNuGetFeed.TryCreateCredentialQueryUrl(
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
    }
}
