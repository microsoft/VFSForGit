using GVFS.Platform.Mac;
using GVFS.Tests.Should;
using NUnit.Framework;

namespace GVFS.UnitTests.Platform.Mac
{
    [TestFixture]
    public class MacGitInstallationTests
    {
        [TestCase]
        public void GetInstalledGitBinPathWorks()
        {
            string gitBinPath = new MacGitInstallation().GetInstalledGitBinPath();
            gitBinPath.ShouldNotBeNull();
            gitBinPath.EndsWith("/git", System.StringComparison.Ordinal).ShouldBeTrue();
        }
    }
}
