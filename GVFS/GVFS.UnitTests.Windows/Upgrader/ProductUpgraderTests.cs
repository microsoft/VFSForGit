using GVFS.Common;
using GVFS.Tests.Should;
using NUnit.Framework;
using System;

namespace GVFS.UnitTests.Upgrader
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