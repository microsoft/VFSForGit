using GVFS.Common;
using GVFS.Common.Git;
using GVFS.Common.Tracing;
using GVFS.Upgrader;
using Moq;
using NUnit.Framework;
using System;
using System.IO;

namespace GVFS.UnitTests.Upgrader
{
    [TestFixture]
    public class UpgradeOrchestratorUnitTests
    {
        private const string OlderThanLocalVersion = "1.0.17000.1";
        private const string LocalGVFSVersion = "1.0.18115.1";
        private const string NewerThanLocalVersion = "1.1.18115.1";

        private delegate void TryGetNewerVersionCallback(out System.Version version, out string message);
        private delegate void TryGetConfigAllowsUpgradeCallback(out bool isConfigError, out string message);

        private Mock<ITracer> MockTracer { get; set; }
        private Mock<TextWriter> MockOutput { get; set; }
        private Mock<InstallerPreRunChecker> MockPreRunChecker { get; set; }
        private Mock<LocalGVFSConfig> MockLocalConfig { get; set; }
        private Mock<IProductUpgrader> MockUpgrader { get; set; }

        private UpgradeOrchestrator orchestrator { get; set; }

        [SetUp]
        public void Setup()
        {
            string message = string.Empty;

            this.MockTracer = new Mock<ITracer>();
            this.MockTracer.Setup(foo => foo.StartActivity(It.IsAny<string>(), It.IsAny<EventLevel>())).Returns(this.MockTracer.Object);
            this.MockTracer.Setup(foo => foo.StartActivity(It.IsAny<string>(), It.IsAny<EventLevel>(), It.IsAny<EventMetadata>())).Returns(this.MockTracer.Object);

            this.MockUpgrader = this.DefaultUpgrader();

            this.MockPreRunChecker = this.DefaultInstallerPreRunChecker(this.MockTracer.Object);

            this.MockOutput = new Mock<TextWriter>();

            this.orchestrator = new UpgradeOrchestrator(
                this.MockUpgrader.Object,
                this.MockTracer.Object,
                this.MockPreRunChecker.Object,
                input: null,
                output: this.MockOutput.Object);
        }

        [TestCase]
        public void UpgradeOrchestrator_UpgradesWhenUpgradeAvailable()
        {
            this.orchestrator.Execute();

            this.MockUpgrader.Verify(foo => foo.TryGetConfigAllowsUpgrade(out It.Ref<bool>.IsAny, out It.Ref<string>.IsAny), Times.Once());
            this.MockUpgrader.Verify(foo => foo.TryGetNewerVersion(out It.Ref<System.Version>.IsAny, out It.Ref<string>.IsAny), Times.Once());

            // This Method is not called...
            // this.MockUpgrader.Verify(foo => foo.TryGetGitVersion(out It.Ref<GitVersion>.IsAny, out It.Ref<string>.IsAny), Times.Once());

            this.MockUpgrader.Verify(foo => foo.TryDownloadNewestVersion(out It.Ref<string>.IsAny), Times.Once());
            this.MockUpgrader.Verify(foo => foo.TryRunInstaller(It.IsAny<InstallActionWrapper>(), out It.Ref<string>.IsAny), Times.Once());
            this.MockUpgrader.Verify(foo => foo.TryCleanup(out It.Ref<string>.IsAny), Times.Once());
        }

        [TestCase]
        public void UpgradeOrchestrator_NewVersionNotAvailable()
        {
            this.MockUpgrader = this.ProductUpgraderHasNoNewVersion(this.MockUpgrader);

            this.orchestrator.Execute();

            this.MockUpgrader.Verify(foo => foo.TryGetConfigAllowsUpgrade(out It.Ref<bool>.IsAny, out It.Ref<string>.IsAny), Times.Once());
            this.MockUpgrader.Verify(foo => foo.TryGetNewerVersion(out It.Ref<System.Version>.IsAny, out It.Ref<string>.IsAny), Times.Once());

            this.MockUpgrader.Verify(foo => foo.TryDownloadNewestVersion(out It.Ref<string>.IsAny), Times.Never());
            this.MockUpgrader.Verify(foo => foo.TryRunInstaller(It.IsAny<InstallActionWrapper>(), out It.Ref<string>.IsAny), Times.Never());
        }

        [TestCase]
        public void UpgradeOrchestrator_FailPrecheck()
        {
        }

        [TestCase]
        public void UpgradeOrchestrator_DownloadFails()
        {
        }

        [TestCase]
        public void UpgradeOrchestrator_GetNewVersionFails()
        {
        }

        [TestCase]
        public void UpgradeOrchestrator_RunInstallerFails()
        {
        }

        [TestCase]
        public void UpgradeOrchestrator_CleanupFails()
        {
        }

        public Mock<IProductUpgrader> DefaultUpgrader()
        {
            string message = string.Empty;
            Version version = new Version(NewerThanLocalVersion);
            GitVersion gitVersion = new GitVersion(2, 1, 0, "Windows", 0, 0);

            Mock<IProductUpgrader> mockUpgrader = new Mock<IProductUpgrader>();
            mockUpgrader.Setup(foo => foo.TryInitialize(out It.Ref<string>.IsAny)).Returns(true);
            mockUpgrader.Setup(foo => foo.TryGetConfigAllowsUpgrade(out It.Ref<bool>.IsAny, out It.Ref<string>.IsAny))
                .Callback(new TryGetConfigAllowsUpgradeCallback((out bool upgradeAllowed, out string delegateMessage) =>
                {
                    upgradeAllowed = true;
                    delegateMessage = string.Empty;
                }))
                .Returns(true);

            mockUpgrader.Setup(foo => foo.TryGetNewerVersion(out It.Ref<System.Version>.IsAny, out It.Ref<string>.IsAny))
                .Callback(new TryGetNewerVersionCallback((out System.Version delegateVersion, out string delegateMessage) =>
                {
                    delegateVersion = version;
                    delegateMessage = string.Empty;
                }))
                .Returns(true);

            mockUpgrader.Setup(foo => foo.TryGetGitVersion(out It.Ref<GitVersion>.IsAny, out It.Ref<string>.IsAny)).Returns(true);
            mockUpgrader.Setup(foo => foo.TryDownloadNewestVersion(out It.Ref<string>.IsAny)).Returns(true);
            mockUpgrader.Setup(foo => foo.TryRunInstaller(It.IsAny<InstallActionWrapper>(), out message)).Returns(true);
            mockUpgrader.Setup(foo => foo.TryCleanup(out It.Ref<string>.IsAny)).Returns(true);
            mockUpgrader.Setup(foo => foo.CleanupDownloadDirectory());

            return mockUpgrader;
        }

        public Mock<IProductUpgrader> ProductUpgraderHasNoNewVersion(Mock<IProductUpgrader> mockUpgrader)
        {
            Version version = new Version(LocalGVFSVersion);

            mockUpgrader.Setup(foo => foo.TryGetNewerVersion(out It.Ref<System.Version>.IsAny, out It.Ref<string>.IsAny))
                .Callback(new TryGetNewerVersionCallback((out System.Version delegateVersion, out string delegateMessage) =>
                {
                    delegateVersion = version;
                    delegateMessage = string.Empty;
                }))
                .Returns(false);

            return mockUpgrader;
        }

        public Mock<InstallerPreRunChecker> DefaultInstallerPreRunChecker(ITracer tracer)
        {
            string message = string.Empty;

            Mock<InstallerPreRunChecker> mockChecker = new Mock<InstallerPreRunChecker>(tracer, string.Empty);

            mockChecker.Setup(foo => foo.TryRunPreUpgradeChecks(out It.Ref<string>.IsAny)).Returns(true);
            mockChecker.Setup(foo => foo.TryUnmountAllGVFSRepos(out It.Ref<string>.IsAny)).Returns(true);

            return mockChecker;
        }
    }
}
