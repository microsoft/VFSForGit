using GVFS.Common;
using GVFS.Tests.Should;
using NUnit.Framework;
using System;

namespace GVFS.UnitTests.Windows.Upgrader
{
    [TestFixture]
    public class ProductUpgraderTests : UpgradeTests
    {
        [SetUp]
        public override void Setup()
        {
            base.Setup();
        }

        [TestCase]
        public void UpgradeAvailableOnFastWhileOnLocalNoneRing()
        {
            this.SimulateUpgradeAvailable(
                remoteRing: GitHubUpgrader.GitHubUpgraderConfig.RingType.Fast,
                remoteVersion: UpgradeTests.NewerThanLocalVersion,
                localRing: GitHubUpgrader.GitHubUpgraderConfig.RingType.None,
                expectedReturn: true,
                expectedUpgradeVersion: null);
        }

        [TestCase]
        public void UpgradeAvailableOnSlowWhileOnLocalNoneRing()
        {
            this.SimulateUpgradeAvailable(
                remoteRing: GitHubUpgrader.GitHubUpgraderConfig.RingType.Slow,
                remoteVersion: UpgradeTests.NewerThanLocalVersion,
                localRing: GitHubUpgrader.GitHubUpgraderConfig.RingType.None,
                expectedReturn: true,
                expectedUpgradeVersion: null);
        }

        [TestCase]
        public void UpgradeAvailableOnFastWhileOnLocalSlowRing()
        {
            this.SimulateUpgradeAvailable(
                remoteRing: GitHubUpgrader.GitHubUpgraderConfig.RingType.Fast,
                remoteVersion: UpgradeTests.NewerThanLocalVersion,
                localRing: GitHubUpgrader.GitHubUpgraderConfig.RingType.Slow,
                expectedReturn: true,
                expectedUpgradeVersion: null);
        }

        [TestCase]
        public void UpgradeAvailableOnSlowWhileOnLocalSlowRing()
        {
            this.SimulateUpgradeAvailable(
                remoteRing: GitHubUpgrader.GitHubUpgraderConfig.RingType.Slow,
                remoteVersion: UpgradeTests.NewerThanLocalVersion,
                localRing: GitHubUpgrader.GitHubUpgraderConfig.RingType.Slow,
                expectedReturn: true,
                expectedUpgradeVersion: UpgradeTests.NewerThanLocalVersion);
        }

        [TestCase]
        public void UpgradeAvailableOnFastWhileOnLocalFastRing()
        {
            this.SimulateUpgradeAvailable(
                remoteRing: GitHubUpgrader.GitHubUpgraderConfig.RingType.Fast,
                remoteVersion: UpgradeTests.NewerThanLocalVersion,
                localRing: GitHubUpgrader.GitHubUpgraderConfig.RingType.Fast,
                expectedReturn: true,
                expectedUpgradeVersion: UpgradeTests.NewerThanLocalVersion);
        }

        [TestCase]
        public void UpgradeAvailableOnSlowWhileOnLocalFastRing()
        {
            this.SimulateUpgradeAvailable(
                remoteRing: GitHubUpgrader.GitHubUpgraderConfig.RingType.Slow,
                remoteVersion: UpgradeTests.NewerThanLocalVersion,
                localRing: GitHubUpgrader.GitHubUpgraderConfig.RingType.Fast,
                expectedReturn: true,
                expectedUpgradeVersion:UpgradeTests.NewerThanLocalVersion);
        }

        [TestCase]
        public void RingInNugetFeedURLOverridesUpgradeRing()
        {
            // Pretend there is an upgrade available in Fast ring. Set upgrade.ring
            // to fast and verify that Upgrader returns the new version.
            Version newVersion;
            string error;
            this.SetUpgradeRing(GitHubUpgrader.GitHubUpgraderConfig.RingType.Fast.ToString());

            // Replace pretend upgrade Release set by UpgradeTests.Setup() method
            this.Upgrader.PretendNewReleaseAvailableAtRemote(UpgradeTests.NewerThanLocalVersion, GitHubUpgrader.GitHubUpgraderConfig.RingType.Fast);

            this.Upgrader.TryQueryNewestVersion(out newVersion, out error).ShouldBeTrue();
            newVersion.ShouldNotBeNull();
            newVersion.ToString().ShouldEqual(UpgradeTests.NewerThanLocalVersion);

            // Now add upgrade.feedurl with Slow ring info. The Slow ring in upgrade.feedurl should
            // override the Fast that is set already (in the steps above) in upgrade.ring. Since
            // there is no upgrade available in Slow, Verify that Upgrader returns Null upgrade
            // this time.
            string feedUrlWithSlowRing = "https://foo.bar.visualstudio.com/helloworld/_packaging/GVFS@Slow/nuget/v3/index.json";
            this.LocalConfig.TrySetConfig("upgrade.feedurl", feedUrlWithSlowRing, out error);
            this.Upgrader.Config.TryLoad(out error).ShouldBeTrue();

            this.Upgrader.TryQueryNewestVersion(out newVersion, out error).ShouldBeTrue();
            newVersion.ShouldBeNull();
            error.ShouldContain("Great news");
        }

        [TestCase]
        public void FastUpgradeRingAndNoRingInNugetFeedURLReturnsNoUpgrade()
        {
            // Pretend there is an upgrade available in Fast ring. Set upgrade.ring
            // to fast and verify that Upgrader returns the new version.
            Version newVersion;
            string error;
            this.SetUpgradeRing(GitHubUpgrader.GitHubUpgraderConfig.RingType.Fast.ToString());

            // Replace pretend upgrade Release set by UpgradeTests.Setup() method
            this.Upgrader.PretendNewReleaseAvailableAtRemote(UpgradeTests.NewerThanLocalVersion, GitHubUpgrader.GitHubUpgraderConfig.RingType.Fast);

            this.Upgrader.TryQueryNewestVersion(out newVersion, out error).ShouldBeTrue();
            newVersion.ShouldNotBeNull();

            // Now add upgrade.feedurl with no ring info. The presence of upgrade.feedurl config
            // should force upgrader to reset its ring to the one specified in upgrade.feedurl.
            // But since there is no ring specified in upgrade.feedurl, upgrader should have no valid
            // ring now. Verify that Upgrader returns Null upgrade this time.
            string feedUrlWithNoRing = "https://foo.bar.visualstudio.com/helloworld/GVFS/nuget/v3/index.json";
            this.LocalConfig.TrySetConfig("upgrade.feedurl", feedUrlWithNoRing, out error);
            this.Upgrader.Config.TryLoad(out error).ShouldBeTrue();

            this.Upgrader.TryQueryNewestVersion(out newVersion, out error).ShouldBeTrue();
            newVersion.ShouldBeNull();
        }

        [TestCase]
        public void NoRingInNugetFeedURLReturnsNullUpgrade()
        {
            string error;
            string feedUrlWithNoRing = "https://foo.bar.visualstudio.com/helloworld/_packaging/GVFS/nuget/v3/index.json";
            this.LocalConfig.TrySetConfig("upgrade.feedurl", feedUrlWithNoRing, out error);
            this.Upgrader.Config.TryLoad(out error).ShouldBeTrue();

            Version newVersion;
            this.Upgrader.TryQueryNewestVersion(out newVersion, out error).ShouldBeTrue();
            newVersion.ShouldBeNull();
        }

        public override void NoneLocalRing()
        {
            throw new NotSupportedException();
        }

        public override void InvalidUpgradeRing()
        {
            throw new NotSupportedException();
        }

        public override void FetchReleaseInfo()
        {
            throw new NotSupportedException();
        }

        protected override ReturnCode RunUpgrade()
        {
            throw new NotSupportedException();
        }

        private void SimulateUpgradeAvailable(
            GitHubUpgrader.GitHubUpgraderConfig.RingType remoteRing,
            string remoteVersion,
            GitHubUpgrader.GitHubUpgraderConfig.RingType localRing,
            bool expectedReturn,
            string expectedUpgradeVersion)
        {
            this.SetUpgradeRing(localRing.ToString());
            this.Upgrader.PretendNewReleaseAvailableAtRemote(
                remoteVersion,
                remoteRing);

            Version newVersion;
            string message;
            this.Upgrader.TryQueryNewestVersion(out newVersion, out message).ShouldEqual(expectedReturn);

            if (string.IsNullOrEmpty(expectedUpgradeVersion))
            {
                newVersion.ShouldBeNull();
            }
            else
            {
                newVersion.ShouldNotBeNull();
                newVersion.ShouldEqual(new Version(expectedUpgradeVersion));
            }
        }
    }
}