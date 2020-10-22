using GVFS.Common;
using GVFS.Common.FileSystem;
using GVFS.Common.Git;
using GVFS.Common.NuGetUpgrade;
using GVFS.Tests.Should;
using GVFS.UnitTests.Mock.Common;
using Moq;
using NUnit.Framework;

namespace GVFS.UnitTests.Common
{
    public class TryCreateProductUpgradeTests
    {
        private static string defaultUpgradeFeedPackageName = "package";
        private static string defaultUpgradeFeedUrl = "https://pkgs.dev.azure.com/contoso/";
        private static string defaultOrgInfoServerUrl = "https://www.contoso.com";
        private static string defaultRing = "slow";

        private MockTracer tracer;
        private Mock<PhysicalFileSystem> fileSystemMock;
        private Mock<ICredentialStore> credentialStoreMock;

        [SetUp]
        public void Setup()
        {
            this.tracer = new MockTracer();

            // It is important that creating a new Upgrader does not
            // require credentials.  We must be able to create an
            // upgrader to query / check upgrade preconditions without
            // requiring authorization.  We create these mocks with
            // strict behavior to validate methods on them are called
            // unnecessarily.
            this.credentialStoreMock = new Mock<ICredentialStore>(MockBehavior.Strict);
            this.fileSystemMock = new Mock<PhysicalFileSystem>(MockBehavior.Strict);
        }

        [TearDown]
        public void TearDown()
        {
            this.credentialStoreMock.VerifyAll();
            this.fileSystemMock.VerifyAll();
        }

        [TestCase]
        public void CreatesNuGetUpgraderWhenConfigured()
        {
            MockLocalGVFSConfig gvfsConfig = this.ConstructDefaultMockNuGetConfigBuilder()
                .Build();

            bool success = ProductUpgrader.TryCreateUpgrader(
                this.tracer,
                this.fileSystemMock.Object,
                gvfsConfig,
                this.credentialStoreMock.Object,
                false,
                false,
                out ProductUpgrader productUpgrader,
                out string error);

            success.ShouldBeTrue();
            productUpgrader.ShouldNotBeNull();
            productUpgrader.ShouldBeOfType<NuGetUpgrader>();
            error.ShouldBeNull();
        }

        [TestCase]
        public void CreatesNuGetUpgraderWhenConfiguredWithNoRing()
        {
            MockLocalGVFSConfig gvfsConfig = this.ConstructDefaultMockNuGetConfigBuilder()
                .WithNoUpgradeRing()
                .Build();

            bool success = ProductUpgrader.TryCreateUpgrader(
                this.tracer,
                this.fileSystemMock.Object,
                gvfsConfig,
                this.credentialStoreMock.Object,
                false,
                false,
                out ProductUpgrader productUpgrader,
                out string error);

            success.ShouldBeTrue();
            productUpgrader.ShouldNotBeNull();
            productUpgrader.ShouldBeOfType<NuGetUpgrader>();
            error.ShouldBeNull();
        }

        [TestCase]
        public void CreatesGitHubUpgraderWhenConfigured()
        {
            MockLocalGVFSConfig gvfsConfig = this.ConstructDefaultGitHubConfigBuilder()
                .Build();

            bool success = ProductUpgrader.TryCreateUpgrader(
                this.tracer,
                this.fileSystemMock.Object,
                gvfsConfig,
                this.credentialStoreMock.Object,
                false,
                false,
                out ProductUpgrader productUpgrader,
                out string error);

            success.ShouldBeTrue();
            productUpgrader.ShouldNotBeNull();
            productUpgrader.ShouldBeOfType<GitHubUpgrader>();
            error.ShouldBeNull();
        }

        [TestCase]
        public void CreatesOrgNuGetUpgrader()
        {
            MockLocalGVFSConfig gvfsConfig = this.ConstructDefaultMockOrgNuGetConfigBuilder()
                .Build();

            bool success = ProductUpgrader.TryCreateUpgrader(
                this.tracer,
                this.fileSystemMock.Object,
                gvfsConfig,
                this.credentialStoreMock.Object,
                false,
                false,
                out ProductUpgrader productUpgrader,
                out string error);

            success.ShouldBeTrue();
            productUpgrader.ShouldNotBeNull();
            productUpgrader.ShouldBeOfType<OrgNuGetUpgrader>();
            error.ShouldBeNull();
        }

        [TestCase]
        public void NoUpgraderWhenNuGetFeedMissing()
        {
            MockLocalGVFSConfig gvfsConfig = this.ConstructDefaultMockNuGetConfigBuilder()
                .WithNoUpgradeFeedUrl()
                .Build();

            bool success = ProductUpgrader.TryCreateUpgrader(
                this.tracer,
                this.fileSystemMock.Object,
                gvfsConfig,
                this.credentialStoreMock.Object,
                false,
                false,
                out ProductUpgrader productUpgrader,
                out string error);

            success.ShouldBeFalse();
            productUpgrader.ShouldBeNull();
            error.ShouldNotBeNull();
        }

        [TestCase]
        public void NoOrgUpgraderWhenNuGetPackNameMissing()
        {
            MockLocalGVFSConfig gvfsConfig = this.ConstructDefaultMockOrgNuGetConfigBuilder()
                .WithNoUpgradeFeedPackageName()
                .Build();

            bool success = ProductUpgrader.TryCreateUpgrader(
                this.tracer,
                this.fileSystemMock.Object,
                gvfsConfig,
                this.credentialStoreMock.Object,
                false,
                false,
                out ProductUpgrader productUpgrader,
                out string error);

            success.ShouldBeFalse();
            productUpgrader.ShouldBeNull();
            error.ShouldNotBeNull();
        }

        [TestCase]
        public void NoOrgUpgraderWhenNuGetFeedMissing()
        {
            MockLocalGVFSConfig gvfsConfig = this.ConstructDefaultMockOrgNuGetConfigBuilder()
                .WithNoUpgradeFeedUrl()
                .Build();

            bool success = ProductUpgrader.TryCreateUpgrader(
                this.tracer,
                this.fileSystemMock.Object,
                gvfsConfig,
                this.credentialStoreMock.Object,
                false,
                false,
                out ProductUpgrader productUpgrader,
                out string error);

            success.ShouldBeFalse();
            productUpgrader.ShouldBeNull();
            error.ShouldNotBeNull();
        }

        [TestCase]
        public void NoUpgraderWhenNuGetPackNameMissing()
        {
            MockLocalGVFSConfig gvfsConfig = this.ConstructDefaultMockNuGetConfigBuilder()
                .WithNoUpgradeFeedPackageName()
                .Build();

            bool success = ProductUpgrader.TryCreateUpgrader(
                this.tracer,
                this.fileSystemMock.Object,
                gvfsConfig,
                this.credentialStoreMock.Object,
                false,
                false,
                out ProductUpgrader productUpgrader,
                out string error);

            success.ShouldBeFalse();
            productUpgrader.ShouldBeNull();
            error.ShouldNotBeNull();
        }

        private MockLocalGVFSConfigBuilder ConstructDefaultMockNuGetConfigBuilder()
        {
            MockLocalGVFSConfigBuilder configBuilder = this.ConstructMockLocalGVFSConfigBuilder()
                .WithUpgradeRing()
                .WithUpgradeFeedPackageName()
                .WithUpgradeFeedUrl();

            return configBuilder;
        }

        private MockLocalGVFSConfigBuilder ConstructDefaultMockOrgNuGetConfigBuilder()
        {
            MockLocalGVFSConfigBuilder configBuilder = this.ConstructMockLocalGVFSConfigBuilder()
                .WithUpgradeRing()
                .WithUpgradeFeedPackageName()
                .WithUpgradeFeedUrl()
                .WithOrgInfoServerUrl();

            return configBuilder;
        }

        private MockLocalGVFSConfigBuilder ConstructDefaultGitHubConfigBuilder()
        {
            MockLocalGVFSConfigBuilder configBuilder = this.ConstructMockLocalGVFSConfigBuilder()
                            .WithUpgradeRing();

            return configBuilder;
        }

        private MockLocalGVFSConfigBuilder ConstructMockLocalGVFSConfigBuilder()
        {
            return new MockLocalGVFSConfigBuilder(
                defaultRing,
                defaultUpgradeFeedUrl,
                defaultUpgradeFeedPackageName,
                defaultOrgInfoServerUrl);
        }
    }
}
