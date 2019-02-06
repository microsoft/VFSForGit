using GVFS.CommandLine;
using GVFS.Common;
using GVFS.Tests.Should;
using GVFS.UnitTests.Category;
using GVFS.UnitTests.Mock.FileSystem;
using GVFS.UnitTests.Windows.Mock.Upgrader;
using NUnit.Framework;
using System.Collections.Generic;

namespace GVFS.UnitTests.Windows.Upgrader
{
    [TestFixture]
    public class UpgradeVerbTests : UpgradeTests
    {
        private MockProcessLauncher processLauncher;
        private UpgradeVerb upgradeVerb;

        [SetUp]
        public override void Setup()
        {
            base.Setup();

            this.processLauncher = new MockProcessLauncher(exitCode: 0, hasExited: true, startResult: true);
            this.upgradeVerb = new UpgradeVerb(
                this.Upgrader,
                this.Tracer,
                this.FileSystem,
                this.PrerunChecker,
                this.processLauncher,
                this.Output);
            this.upgradeVerb.Confirmed = false;
            this.PrerunChecker.SetCommandToRerun("`gvfs upgrade`");
        }

        [TestCase]
        public void UpgradeAvailabilityReporting()
        {
            this.ConfigureRunAndVerify(
                configure: () =>
                {
                    this.SetUpgradeRing("Slow");
                    this.Upgrader.PretendNewReleaseAvailableAtRemote(
                        upgradeVersion: NewerThanLocalVersion,
                        remoteRing: GitHubUpgrader.GitHubUpgraderConfig.RingType.Slow);
                },
                expectedReturn: ReturnCode.Success,
                expectedOutput: new List<string>
                {
                    "New GVFS version " + NewerThanLocalVersion + " available in ring Slow",
                    "When ready, run `gvfs upgrade --confirm` from an elevated command prompt."
                },
                expectedErrors: null);
        }

        [TestCase]
        public void DowngradePrevention()
        {
            this.ConfigureRunAndVerify(
                configure: () =>
                {
                    this.SetUpgradeRing("Slow");
                    this.Upgrader.PretendNewReleaseAvailableAtRemote(
                        upgradeVersion: OlderThanLocalVersion,
                        remoteRing: GitHubUpgrader.GitHubUpgraderConfig.RingType.Slow);
                },
                expectedReturn: ReturnCode.Success,
                expectedOutput: new List<string>
                {
                    "Checking for GVFS upgrades...Succeeded",
                    "Great news, you're all caught up on upgrades in the Slow ring!"
                },
                expectedErrors: null);
        }

        [TestCase]
        public void LaunchInstaller()
        {
            this.ConfigureRunAndVerify(
                configure: () =>
                {
                    this.SetUpgradeRing("Slow");
                    this.upgradeVerb.Confirmed = true;
                    this.PrerunChecker.SetCommandToRerun("`gvfs upgrade --confirm`");
                },
                expectedReturn: ReturnCode.Success,
                expectedOutput: new List<string>
                {
                    "New GVFS version " + NewerThanLocalVersion + " available in ring Slow",
                    "Launching upgrade tool..."
                },
                expectedErrors:null);

            this.processLauncher.IsLaunched.ShouldBeTrue();
        }

        [TestCase]
        [Category(CategoryConstants.ExceptionExpected)]
        public override void NoneLocalRing()
        {
            base.NoneLocalRing();
        }

        [TestCase]
        [Category(CategoryConstants.ExceptionExpected)]
        public override void InvalidUpgradeRing()
        {
            base.InvalidUpgradeRing();
        }

        [TestCase]
        [Category(CategoryConstants.ExceptionExpected)]
        public void CopyTools()
        {
            this.ConfigureRunAndVerify(
                configure: () =>
                {
                    this.SetUpgradeRing("Slow");
                    this.Upgrader.SetFailOnAction(MockGitHubUpgrader.ActionType.CopyTools);
                    this.upgradeVerb.Confirmed = true;
                    this.PrerunChecker.SetCommandToRerun("`gvfs upgrade --confirm`");
                },
                expectedReturn: ReturnCode.GenericError,
                expectedOutput: new List<string>
                {
                    "Could not launch upgrade tool. Unable to copy upgrader tools"
                },
                expectedErrors: new List<string>
                {
                    "Could not launch upgrade tool. Unable to copy upgrader tools"
                });
        }

        [TestCase]
        public void ProjFSPreCheck()
        {
            this.ConfigureRunAndVerify(
                configure: () =>
                {
                    this.upgradeVerb.Confirmed = true;
                    this.PrerunChecker.SetReturnFalseOnCheck(MockInstallerPrerunChecker.FailOnCheckType.ProjFSEnabled);
                },
                expectedReturn: ReturnCode.GenericError,
                expectedOutput: new List<string>
                {
                    "ERROR: `gvfs upgrade` is not supported because you have previously installed an out of band ProjFS driver.",
                    "Check your team's documentation for how to upgrade."
                },
                expectedErrors: new List<string>
                {
                    "`gvfs upgrade` is not supported because you have previously installed an out of band ProjFS driver."
                });
        }

        [TestCase]
        public void IsGVFSServiceRunningPreCheck()
        {
            this.PrerunChecker.SetCommandToRerun("`gvfs upgrade --confirm`");
            this.ConfigureRunAndVerify(
                configure: () =>
                {
                    this.upgradeVerb.Confirmed = true;
                    this.PrerunChecker.SetReturnTrueOnCheck(MockInstallerPrerunChecker.FailOnCheckType.IsServiceInstalledAndNotRunning);
                },
                expectedReturn: ReturnCode.GenericError,
                expectedOutput: new List<string>
                {
                    "GVFS Service is not running.",
                    "Run `sc start GVFS.Service` and run `gvfs upgrade --confirm` again from an elevated command prompt."
                },
                expectedErrors: new List<string>
                {
                    "GVFS Service is not running."
                });
        }

        [TestCase]
        public void ElevatedRunPreCheck()
        {
            this.PrerunChecker.SetCommandToRerun("`gvfs upgrade --confirm`");
            this.ConfigureRunAndVerify(
                configure: () =>
                {
                    this.upgradeVerb.Confirmed = true;
                    this.PrerunChecker.SetReturnFalseOnCheck(MockInstallerPrerunChecker.FailOnCheckType.IsElevated);
                },
                expectedReturn: ReturnCode.GenericError,
                expectedOutput: new List<string>
                {
                    "The installer needs to be run from an elevated command prompt.",
                    "Run `gvfs upgrade --confirm` again from an elevated command prompt."
                },
                expectedErrors: new List<string>
                {
                    "The installer needs to be run from an elevated command prompt."
                });
        }

        [TestCase]
        public void UnAttendedModePreCheck()
        {
            this.ConfigureRunAndVerify(
                configure: () =>
                {
                    this.upgradeVerb.Confirmed = true;
                    this.PrerunChecker.SetReturnTrueOnCheck(MockInstallerPrerunChecker.FailOnCheckType.UnattendedMode);
                },
                expectedReturn: ReturnCode.GenericError,
                expectedOutput: new List<string>
                {
                    "`gvfs upgrade` is not supported in unattended mode"
                },
                expectedErrors: new List<string>
                {
                    "`gvfs upgrade` is not supported in unattended mode"
                });
        }

        [TestCase]
        public void DryRunLaunchesUpgradeTool()
        {
            this.ConfigureRunAndVerify(
                configure: () =>
                {
                    this.upgradeVerb.DryRun = true;
                    this.SetUpgradeRing("Slow");
                    this.Upgrader.PretendNewReleaseAvailableAtRemote(
                        upgradeVersion: NewerThanLocalVersion,
                        remoteRing: GitHubUpgrader.GitHubUpgraderConfig.RingType.Slow);
                },
                expectedReturn: ReturnCode.Success,
                expectedOutput: new List<string>
                {
                    "Installer launched in a new window."
                },
                expectedErrors: null);
        }

        protected override ReturnCode RunUpgrade()
        {
            try
            {
                this.upgradeVerb.Execute();
            }
            catch (GVFSVerb.VerbAbortedException)
            {
                // ignore. exceptions are expected while simulating some failures.
            }

            return this.upgradeVerb.ReturnCode;
        }
    }
}