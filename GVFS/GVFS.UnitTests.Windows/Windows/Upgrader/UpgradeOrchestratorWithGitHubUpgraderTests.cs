using GVFS.Common;
using GVFS.Tests.Should;
using GVFS.UnitTests.Windows.Mock.Upgrader;
using GVFS.Upgrader;
using NUnit.Framework;
using System.Collections.Generic;

namespace GVFS.UnitTests.Windows.Upgrader
{
    [TestFixture]
    public class UpgradeOrchestratorWithGitHubUpgraderTests : UpgradeTests
    {
        private UpgradeOrchestrator orchestrator;

        [SetUp]
        public override void Setup()
        {
            base.Setup();

            this.orchestrator = new UpgradeOrchestrator(
                this.Upgrader,
                this.Tracer,
                this.PrerunChecker,
                input: null,
                output: this.Output);
            this.PrerunChecker.SetCommandToRerun("`gvfs upgrade --confirm`");
        }

        [TestCase]
        public void UpgradeNoError()
        {
            this.RunUpgrade().ShouldEqual(ReturnCode.Success);
            this.Tracer.RelatedErrorEvents.ShouldBeEmpty();
        }

        [TestCase]
        public void AutoUnmountError()
        {
            this.ConfigureRunAndVerify(
                configure: () =>
                {
                    this.PrerunChecker.SetReturnFalseOnCheck(MockInstallerPrerunChecker.FailOnCheckType.UnMountRepos);
                },
                expectedReturn: ReturnCode.GenericError,
                expectedOutput: new List<string>
                {
                    "Unmount of some of the repositories failed."
                },
                expectedErrors: new List<string>
                {
                    "Unmount of some of the repositories failed."
                });
        }

        [TestCase]
        public void AbortOnBlockingProcess()
        {
            this.ConfigureRunAndVerify(
                configure: () =>
                {
                    this.PrerunChecker.SetReturnTrueOnCheck(MockInstallerPrerunChecker.FailOnCheckType.BlockingProcessesRunning);
                },
                expectedReturn: ReturnCode.GenericError,
                expectedOutput: new List<string>
                {
                    "ERROR: Blocking processes are running.",
                    $"Run `gvfs upgrade --confirm` again after quitting these processes - GVFS.Mount, git"
                },
                expectedErrors: new List<string>
                {
                    $"Run `gvfs upgrade --confirm` again after quitting these processes - GVFS.Mount, git"
                });
        }

        [TestCase]
        public void DownloadDirectoryCreationError()
        {
            this.ConfigureRunAndVerify(
                configure: () =>
                {
                    this.Upgrader.SetFailOnAction(MockGitHubUpgrader.ActionType.CreateDownloadDirectory);
                },
                expectedReturn: ReturnCode.GenericError,
                expectedOutput: new List<string>
                {
                    "Error creating download directory"
                },
                expectedErrors: new List<string>
                {
                    "Error creating download directory"
                });
        }

        [TestCase]
        public void GVFSDownloadError()
        {
            this.ConfigureRunAndVerify(
                configure: () =>
                {
                    this.Upgrader.SetFailOnAction(MockGitHubUpgrader.ActionType.GVFSDownload);
                },
                expectedReturn: ReturnCode.GenericError,
                expectedOutput: new List<string>
                {
                    "Error downloading GVFS from GitHub"
                },
                expectedErrors: new List<string>
                {
                    "Error downloading GVFS from GitHub"
                });
        }

        [TestCase]
        public void GitDownloadError()
        {
            this.ConfigureRunAndVerify(
                configure: () =>
                {
                    this.Upgrader.SetFailOnAction(MockGitHubUpgrader.ActionType.GitDownload);
                },
                expectedReturn: ReturnCode.GenericError,
                expectedOutput: new List<string>
                {
                    "Error downloading Git from GitHub"
                },
                expectedErrors: new List<string>
                {
                    "Error downloading Git from GitHub"
                });
        }

        [TestCase]
        public void GitInstallationArgs()
        {
            this.RunUpgrade().ShouldEqual(ReturnCode.Success);

            Dictionary<string, string> gitInstallerInfo;
            this.Upgrader.InstallerArgs.ShouldBeNonEmpty();
            this.Upgrader.InstallerArgs.TryGetValue("Git", out gitInstallerInfo).ShouldBeTrue();

            string args;
            gitInstallerInfo.TryGetValue("Args", out args).ShouldBeTrue();
            args.ShouldContain(new string[] { "/VERYSILENT", "/CLOSEAPPLICATIONS", "/SUPPRESSMSGBOXES", "/NORESTART", "/Log" });
        }

        [TestCase]
        public void GitInstallError()
        {
            this.ConfigureRunAndVerify(
                configure: () =>
                {
                    this.Upgrader.SetFailOnAction(MockGitHubUpgrader.ActionType.GitInstall);
                },
                expectedReturn: ReturnCode.GenericError,
                expectedOutput: new List<string>
                {
                    "Git installation failed"
                },
                expectedErrors: new List<string>
                {
                    "Git installation failed"
                });
        }

        [TestCase]
        public void GitInstallerAuthenticodeError()
        {
            this.ConfigureRunAndVerify(
                configure: () =>
                {
                    this.Upgrader.SetFailOnAction(MockGitHubUpgrader.ActionType.GitAuthenticodeCheck);
                },
                expectedReturn: ReturnCode.GenericError,
                expectedOutput: new List<string>
                {
                    "hash of the file does not match the hash stored in the digital signature"
                },
                expectedErrors: new List<string>
                {
                    "hash of the file does not match the hash stored in the digital signature"
                });
        }

        [TestCase]
        public void GVFSInstallationArgs()
        {
            this.RunUpgrade().ShouldEqual(ReturnCode.Success);

            Dictionary<string, string> gitInstallerInfo;
            this.Upgrader.InstallerArgs.ShouldBeNonEmpty();
            this.Upgrader.InstallerArgs.TryGetValue("GVFS", out gitInstallerInfo).ShouldBeTrue();

            string args;
            gitInstallerInfo.TryGetValue("Args", out args).ShouldBeTrue();
            args.ShouldContain(new string[] { "/VERYSILENT", "/CLOSEAPPLICATIONS", "/SUPPRESSMSGBOXES", "/NORESTART", "/Log", "/REMOUNTREPOS=false" });
        }

        [TestCase]
        public void GVFSInstallError()
        {
            this.ConfigureRunAndVerify(
                configure: () =>
                {
                    this.Upgrader.SetFailOnAction(MockGitHubUpgrader.ActionType.GVFSInstall);
                },
                expectedReturn: ReturnCode.GenericError,
                expectedOutput: new List<string>
                {
                    "GVFS installation failed"
                },
                expectedErrors: new List<string>
                {
                    "GVFS installation failed"
                });
        }

        [TestCase]
        public void GVFSInstallerAuthenticodeError()
        {
            this.ConfigureRunAndVerify(
                configure: () =>
                {
                    this.Upgrader.SetFailOnAction(MockGitHubUpgrader.ActionType.GVFSAuthenticodeCheck);
                },
                expectedReturn: ReturnCode.GenericError,
                expectedOutput: new List<string>
                {
                    "hash of the file does not match the hash stored in the digital signature"
                },
                expectedErrors: new List<string>
                {
                    "hash of the file does not match the hash stored in the digital signature"
                });
        }

        [TestCase]
        public void GVFSCleanupError()
        {
            this.ConfigureRunAndVerify(
                configure: () =>
                {
                    this.Upgrader.SetFailOnAction(MockGitHubUpgrader.ActionType.GVFSCleanup);
                },
                expectedReturn: ReturnCode.Success,
                expectedOutput: new List<string>
                {
                },
                expectedErrors: new List<string>
                {
                    "Error deleting downloaded GVFS installer."
                });
        }

        [TestCase]
        public void GitCleanupError()
        {
            this.ConfigureRunAndVerify(
                configure: () =>
                {
                    this.Upgrader.SetFailOnAction(MockGitHubUpgrader.ActionType.GitCleanup);
                },
                expectedReturn: ReturnCode.Success,
                expectedOutput: new List<string>
                {
                },
                expectedErrors: new List<string>
                {
                    "Error deleting downloaded Git installer."
                });
        }

        [TestCase]
        public void RemountReposError()
        {
            this.ConfigureRunAndVerify(
                configure: () =>
                {
                    this.PrerunChecker.SetReturnFalseOnCheck(MockInstallerPrerunChecker.FailOnCheckType.RemountRepos);
                },
                expectedReturn: ReturnCode.Success,
                expectedOutput: new List<string>
                {
                    "Auto remount failed."
                },
                expectedErrors: new List<string>
                {
                    "Auto remount failed."
                });
        }

        [TestCase]
        public void DryRunDoesNotRunInstallerExes()
        {
            this.ConfigureRunAndVerify(
                configure: () =>
                {
                    this.Upgrader.SetDryRun(true);
                    this.Upgrader.InstallerExeLaunched = false;
                    this.SetUpgradeRing("Slow");
                    this.Upgrader.PretendNewReleaseAvailableAtRemote(
                        upgradeVersion: NewerThanLocalVersion,
                        remoteRing: GitHubUpgrader.GitHubUpgraderConfig.RingType.Slow);
                },
                expectedReturn: ReturnCode.Success,
                expectedOutput: new List<string>
                {
                    "Installing Git",
                    "Installing GVFS",
                    "Upgrade completed successfully."
                },
                expectedErrors: null);

            this.Upgrader.InstallerExeLaunched.ShouldBeFalse();
        }

        protected override ReturnCode RunUpgrade()
        {
            this.orchestrator.Execute();
            return this.orchestrator.ExitCode;
        }
    }
}
