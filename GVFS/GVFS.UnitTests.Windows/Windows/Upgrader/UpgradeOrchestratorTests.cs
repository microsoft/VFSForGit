using GVFS.Common;
using GVFS.Common.Git;
using GVFS.Tests.Should;
using GVFS.UnitTests.Mock.Common;
using GVFS.UnitTests.Windows.Mock.Upgrader;
using GVFS.Upgrader;
using Moq;
using NUnit.Framework;
using System;
using System.Collections.Generic;

namespace GVFS.UnitTests.Upgrader
{
    [TestFixture]
    public class UpgradeOrchestratorTests
    {
        private const string GVFSVersion = "1.1.18115.1";

        private delegate void TryGetNewerVersionCallback(out System.Version version, out string message);
        private delegate void UpgradeAllowedCallback(out string message);
        private delegate void TryRunPreUpgradeChecksCallback(out string delegateMessage);
        private delegate void TryDownloadNewestVersionCallback(out string message);
        private delegate void TryRunInstallerCallback(InstallActionWrapper installActionWrapper, out string error);

        private MockTracer Tracer { get; set; }
        private MockTextWriter Output { get; set; }
        private MockInstallerPrerunChecker PreRunChecker { get; set; }
        private Mock<LocalGVFSConfig> MoqLocalConfig { get; set; }
        private Mock<ProductUpgrader> MoqUpgrader { get; set; }

        private UpgradeOrchestrator orchestrator { get; set; }

        [SetUp]
        public void Setup()
        {
            this.Tracer = new MockTracer();
            this.Output = new MockTextWriter();
            this.PreRunChecker = new MockInstallerPrerunChecker(this.Tracer);
            this.PreRunChecker.Reset();
            this.MoqUpgrader = this.DefaultUpgrader();
            this.orchestrator = new UpgradeOrchestrator(
                this.MoqUpgrader.Object,
                this.Tracer,
                this.PreRunChecker,
                input: null,
                output: this.Output);

            this.SetUpgradeAvailable(new Version(GVFSVersion), error: null);
        }

        [TestCase]
        public void ExecuteSucceedsWhenUpgradeAvailable()
        {
            this.orchestrator.Execute();

            this.VerifyOrchestratorInvokes(
                upgradeAllowed: true,
                queryNewestVersion: true,
                downloadNewestVersion: true,
                installNewestVersion: true,
                cleanup: true);

            this.orchestrator.ExitCode.ShouldEqual(ReturnCode.Success);
        }

        [TestCase]
        public void ExecuteSucceedsWhenOnLatestVersion()
        {
            this.SetUpgradeAvailable(newVersion: null, error: null);

            this.orchestrator.Execute();

            this.VerifyOrchestratorInvokes(
                upgradeAllowed: true,
                queryNewestVersion: true,
                downloadNewestVersion: false,
                installNewestVersion: false,
                cleanup: true);

            this.orchestrator.ExitCode.ShouldEqual(ReturnCode.Success);
        }

        [TestCase]
        public void ExecuteFailsWhenGetNewVersionFails()
        {
            string errorMessage = "Authentication error.";

            this.SetUpgradeAvailable(newVersion: null, error: errorMessage);

            this.orchestrator.Execute();

            this.VerifyOrchestratorInvokes(
                upgradeAllowed: true,
                queryNewestVersion: true,
                downloadNewestVersion: false,
                installNewestVersion: false,
                cleanup: true);

            this.VerifyOutput("ERROR: Authentication error.");

            this.orchestrator.ExitCode.ShouldEqual(ReturnCode.GenericError);
        }

        [TestCase]
        public void ExecuteFailsWhenPrecheckFails()
        {
            this.PreRunChecker.SetReturnFalseOnCheck(MockInstallerPrerunChecker.FailOnCheckType.IsElevated);

            this.orchestrator.Execute();

            this.VerifyOrchestratorInvokes(
                upgradeAllowed: true,
                queryNewestVersion: false,
                downloadNewestVersion: false,
                installNewestVersion: false,
                cleanup: true);

            this.orchestrator.ExitCode.ShouldEqual(ReturnCode.GenericError);
        }

        [TestCase]
        public void ExecuteFailsWhenDownloadFails()
        {
            this.MoqUpgrader.Setup(upgrader => upgrader.TryDownloadNewestVersion(out It.Ref<string>.IsAny))
                .Callback(new TryDownloadNewestVersionCallback((out string delegateMessage) =>
                {
                    delegateMessage = "Download error.";
                }))
                .Returns(false);

            this.orchestrator.Execute();

            this.VerifyOrchestratorInvokes(
                upgradeAllowed: true,
                queryNewestVersion: true,
                downloadNewestVersion: true,
                installNewestVersion: false,
                cleanup: true);

            this.VerifyOutput("ERROR: Download error.");

            this.orchestrator.ExitCode.ShouldEqual(ReturnCode.GenericError);
        }

        [TestCase]
        public void ExecuteFailsWhenRunInstallerFails()
        {
            this.MoqUpgrader.Setup(upgrader => upgrader.TryRunInstaller(It.IsAny<InstallActionWrapper>(), out It.Ref<string>.IsAny))
                .Callback(new TryRunInstallerCallback((InstallActionWrapper installActionWrapper, out string delegateMessage) =>
                {
                    delegateMessage = "Installer error.";
                }))
                .Returns(false);

            this.orchestrator.Execute();

            this.VerifyOrchestratorInvokes(
                upgradeAllowed: true,
                queryNewestVersion: true,
                downloadNewestVersion: true,
                installNewestVersion: true,
                cleanup: true);

            this.VerifyOutput("ERROR: Installer error.");

            this.orchestrator.ExitCode.ShouldEqual(ReturnCode.GenericError);
        }

        public Mock<ProductUpgrader> DefaultUpgrader()
        {
            Mock<ProductUpgrader> mockUpgrader = new Mock<ProductUpgrader>();

            mockUpgrader.Setup(upgrader => upgrader.UpgradeAllowed(out It.Ref<string>.IsAny))
                .Callback(new UpgradeAllowedCallback((out string delegateMessage) =>
                {
                    delegateMessage = string.Empty;
                }))
                .Returns(true);

            string message = string.Empty;
            mockUpgrader.Setup(upgrader => upgrader.TryDownloadNewestVersion(out It.Ref<string>.IsAny)).Returns(true);
            mockUpgrader.Setup(upgrader => upgrader.TryRunInstaller(It.IsAny<InstallActionWrapper>(), out message)).Returns(true);
            mockUpgrader.Setup(upgrader => upgrader.TryCleanup(out It.Ref<string>.IsAny)).Returns(true);

            return mockUpgrader;
        }

        public void SetUpgradeAvailable(Version newVersion, string error)
        {
            bool upgradeResult = string.IsNullOrEmpty(error);

            this.MoqUpgrader.Setup(upgrader => upgrader.TryQueryNewestVersion(out It.Ref<System.Version>.IsAny, out It.Ref<string>.IsAny))
                .Callback(new TryGetNewerVersionCallback((out System.Version delegateVersion, out string delegateMessage) =>
                {
                    delegateVersion = newVersion;
                    delegateMessage = error;
                }))
                .Returns(upgradeResult);
        }

        public void VerifyOrchestratorInvokes(
            bool upgradeAllowed,
            bool queryNewestVersion,
            bool downloadNewestVersion,
            bool installNewestVersion,
            bool cleanup)
        {
            this.MoqUpgrader.Verify(
                upgrader => upgrader.UpgradeAllowed(
                    out It.Ref<string>.IsAny),
                    upgradeAllowed ? Times.Once() : Times.Never());

            this.MoqUpgrader.Verify(
                upgrader => upgrader.TryQueryNewestVersion(
                    out It.Ref<System.Version>.IsAny,
                    out It.Ref<string>.IsAny),
                    queryNewestVersion ? Times.Once() : Times.Never());

            this.MoqUpgrader.Verify(
                upgrader => upgrader.TryDownloadNewestVersion(
                    out It.Ref<string>.IsAny),
                    downloadNewestVersion ? Times.Once() : Times.Never());

            this.MoqUpgrader.Verify(
                upgrader => upgrader.TryRunInstaller(
                    It.IsAny<InstallActionWrapper>(),
                    out It.Ref<string>.IsAny),
                    installNewestVersion ? Times.Once() : Times.Never());

            this.MoqUpgrader.Verify(
                upgrader => upgrader.TryCleanup(
                    out It.Ref<string>.IsAny),
                    cleanup ? Times.Once() : Times.Never());
        }

        public void VerifyOutput(string expectedMessage)
        {
            this.Output.AllLines.ShouldContain(
                    new List<string>() { expectedMessage },
                    (line, expectedLine) => { return line.Contains(expectedLine); });
        }
    }
}
