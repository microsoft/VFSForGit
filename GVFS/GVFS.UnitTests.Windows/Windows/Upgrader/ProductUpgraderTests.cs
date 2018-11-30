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
                remoteRing: GitHubReleasesUpgrader.RingType.Fast,
                remoteVersion: UpgradeTests.NewerThanLocalVersion,
                localRing: GitHubReleasesUpgrader.RingType.None,
                expectedReturn: true,
                expectedUpgradeVersion: null);
        }

        [TestCase]
        public void UpgradeAvailableOnSlowWhileOnLocalNoneRing()
        {
            this.SimulateUpgradeAvailable(
                remoteRing: GitHubReleasesUpgrader.RingType.Slow,
                remoteVersion: UpgradeTests.NewerThanLocalVersion,
                localRing: GitHubReleasesUpgrader.RingType.None,
                expectedReturn: true,
                expectedUpgradeVersion: null);
        }

        [TestCase]
        public void UpgradeAvailableOnFastWhileOnLocalSlowRing()
        {
            this.SimulateUpgradeAvailable(
                remoteRing: GitHubReleasesUpgrader.RingType.Fast,
                remoteVersion: UpgradeTests.NewerThanLocalVersion,
                localRing: GitHubReleasesUpgrader.RingType.Slow,
                expectedReturn: true,
                expectedUpgradeVersion: null);
        }

        [TestCase]
        public void UpgradeAvailableOnSlowWhileOnLocalSlowRing()
        {
            this.SimulateUpgradeAvailable(
                remoteRing: GitHubReleasesUpgrader.RingType.Slow,
                remoteVersion: UpgradeTests.NewerThanLocalVersion,
                localRing: GitHubReleasesUpgrader.RingType.Slow,
                expectedReturn: true,
                expectedUpgradeVersion: UpgradeTests.NewerThanLocalVersion);
        }

        [TestCase]
        public void UpgradeAvailableOnFastWhileOnLocalFastRing()
        {
            this.SimulateUpgradeAvailable(
                remoteRing: GitHubReleasesUpgrader.RingType.Fast,
                remoteVersion: UpgradeTests.NewerThanLocalVersion,
                localRing: GitHubReleasesUpgrader.RingType.Fast,
                expectedReturn: true,
                expectedUpgradeVersion: UpgradeTests.NewerThanLocalVersion);
        }

        [TestCase]
        public void UpgradeAvailableOnSlowWhileOnLocalFastRing()
        {
            this.SimulateUpgradeAvailable(
                remoteRing: GitHubReleasesUpgrader.RingType.Slow,
                remoteVersion: UpgradeTests.NewerThanLocalVersion,
                localRing:GitHubReleasesUpgrader.RingType.Fast,
                expectedReturn: true,
                expectedUpgradeVersion:UpgradeTests.NewerThanLocalVersion);
        }

        public override void NoneLocalRing()
        {
            throw new NotImplementedException();
        }

        public override void InvalidUpgradeRing()
        {
            throw new NotImplementedException();
        }

        public override void FetchReleaseInfo()
        {
            throw new NotImplementedException();
        }

        protected override void RunUpgrade()
        {
            throw new NotImplementedException();
        }

        protected override ReturnCode ExitCode()
        {
            return ReturnCode.Success;
        }

        private void SimulateUpgradeAvailable(
            GitHubReleasesUpgrader.RingType remoteRing,
            string remoteVersion,
            GitHubReleasesUpgrader.RingType localRing,
            bool expectedReturn,
            string expectedUpgradeVersion)
        {
            this.Upgrader.LocalRingConfig = localRing;
            this.Upgrader.PretendNewReleaseAvailableAtRemote(
                remoteVersion,
                remoteRing);

            Version newVersion;
            string errorMessage;
            this.Upgrader.TryGetNewerVersion(out newVersion, out errorMessage).ShouldEqual(expectedReturn);

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