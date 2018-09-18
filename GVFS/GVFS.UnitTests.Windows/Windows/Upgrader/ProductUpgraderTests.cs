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
                remoteRing: ProductUpgrader.RingType.Fast,
                remoteVersion: UpgradeTests.NewerThanLocalVersion,
                localRing: ProductUpgrader.RingType.None,
                expectedReturn: true,
                expectedUpgradeVersion: null);
        }

        [TestCase]
        public void UpgradeAvailableOnSlowWhileOnLocalNoneRing()
        {
            this.SimulateUpgradeAvailable(
                remoteRing: ProductUpgrader.RingType.Slow,
                remoteVersion: UpgradeTests.NewerThanLocalVersion,
                localRing: ProductUpgrader.RingType.None,
                expectedReturn: true,
                expectedUpgradeVersion: null);
        }

        [TestCase]
        public void UpgradeAvailableOnFastWhileOnLocalSlowRing()
        {
            this.SimulateUpgradeAvailable(
                remoteRing: ProductUpgrader.RingType.Fast,
                remoteVersion: UpgradeTests.NewerThanLocalVersion,
                localRing: ProductUpgrader.RingType.Slow,
                expectedReturn: true,
                expectedUpgradeVersion: null);
        }

        [TestCase]
        public void UpgradeAvailableOnSlowWhileOnLocalSlowRing()
        {
            this.SimulateUpgradeAvailable(
                remoteRing: ProductUpgrader.RingType.Slow,
                remoteVersion: UpgradeTests.NewerThanLocalVersion,
                localRing: ProductUpgrader.RingType.Slow,
                expectedReturn: true,
                expectedUpgradeVersion: UpgradeTests.NewerThanLocalVersion);
        }

        [TestCase]
        public void UpgradeAvailableOnFastWhileOnLocalFastRing()
        {
            this.SimulateUpgradeAvailable(
                remoteRing: ProductUpgrader.RingType.Fast,
                remoteVersion: UpgradeTests.NewerThanLocalVersion,
                localRing: ProductUpgrader.RingType.Fast,
                expectedReturn: true,
                expectedUpgradeVersion: UpgradeTests.NewerThanLocalVersion);
        }

        [TestCase]
        public void UpgradeAvailableOnSlowWhileOnLocalFastRing()
        {
            this.SimulateUpgradeAvailable(
                remoteRing: ProductUpgrader.RingType.Slow,
                remoteVersion: UpgradeTests.NewerThanLocalVersion,
                localRing:ProductUpgrader.RingType.Fast,
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
            ProductUpgrader.RingType remoteRing,
            string remoteVersion,
            ProductUpgrader.RingType localRing,
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