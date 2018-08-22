using GVFS.Common;
using GVFS.Tests.Should;
using GVFS.UnitTests.Windows.Mock.Upgrader;
using GVFS.Upgrader;
using NUnit.Framework;
using System.Collections.Generic;

namespace GVFS.UnitTests.Windows.Upgrader
{
    [TestFixture]
    public class UpgradeOrchestratorTests : UpgradeTests
    {
        private UpgradeOrchestrator Orchestrator { get; set; }

        [SetUp]
        public override void Setup()
        {
            base.Setup();

            this.Orchestrator = new UpgradeOrchestrator(
                this.Upgrader,
                this.Tracer,
                this.PrerunChecker,
                input: null,
                output: this.Output,
                shouldExit: false);
        }

        [TestCase]
        public void UpgradeSequence()
        {
            // Expected sequence-
            // 1. Read upgrade ring from git system config
            // 2. fetch upgrade info from GitHub/Release, 
            // 3. pre-run checks, 
            // 4. repository unmount,
            // 5. download of assets, 
            // 6. installation 
            // 7. repository re-mount

            this.Orchestrator.Execute();

            this.CallSequenceTracker.VerifyMethodsCalledInSequence(
                new List<string>
                {
                    "LoadRingConfig",
                    "FetchReleaseInfo",
                    "PreCheck_Unattended",
                    "PreCheck_DevelopmentVersion",
                    "PreCheck_Elevated",
                    "PreCheck_ProjFSInboxed",
                    "UnmountAll",
                    "PreCheck_BlockingProcessRunning",
                    "DownloadAsset_GVFS",
                    "DownloadAsset_Git",
                    "InstallAsset_Git",
                    "InstallAsset_GVFS",
                    "MountAll"
                });

            this.Orchestrator.ExitCode.ShouldEqual(ReturnCode.Success);
        }

        [TestCase]
        public void InvalidUpgradeRing()
        {
            this.ConfigureRunAndVerify(
                configure: () =>
                {
                    this.Upgrader.LocalRingConfig = GVFS.Common.ProductUpgrader.RingType.Invalid;
                },
                expectedReturn: ReturnCode.GenericError,
                expectedOutput: new List<string>
                {
                    "Invalid upgrade ring type(Invalid) specified in Git config."
                },
                expectedErrors: new List<string>
                {
                    "Invalid upgrade ring type(Invalid) specified in Git config."
                });
        }

        [TestCase]
        public void FetchReleaseInfoError()
        {
            this.ConfigureRunAndVerify(
                configure: () =>
                {
                    this.Upgrader.SetFailOnAction(MockProductUpgrader.ActionType.FetchReleaseInfo);
                },
                expectedReturn: ReturnCode.GenericError,
                expectedOutput: new List<string>
                {
                    "Error fetching upgrade release info."
                },
                expectedErrors: new List<string>
                {
                    "Error fetching upgrade release info."
                });

            this.CallSequenceTracker.VerifyMethodsNotCalled(
                new List<string>()
                {
                    "PreCheck_Unattended",
                    "PreCheck_DevelopmentVersion",
                    "PreCheck_Elevated",
                    "PreCheck_ProjFSInboxed",
                    "UnmountAll",
                    "PreCheck_BlockingProcessRunning",
                    "DownloadAsset_GVFS",
                    "DownloadAsset_Git",
                    "InstallAsset_Git",
                    "InstallAsset_GVFS",
                    "MountAll"
                });
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

            this.CallSequenceTracker.VerifyMethodsNotCalled(
                new List<string>()
                {
                    "DownloadAsset_GVFS",
                    "DownloadAsset_Git",
                    "InstallAsset_Git",
                    "InstallAsset_GVFS",
                    "MountAll"
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
                    "Please retry after quitting these processes - GVFS.Mount, git"
                },
                expectedErrors: new List<string>
                {
                    "Please retry after quitting these processes - GVFS.Mount, git"
                });

            this.CallSequenceTracker.VerifyMethodsNotCalled(
                new List<string>()
                {
                    "DownloadAsset_GVFS",
                    "DownloadAsset_Git",
                    "InstallAsset_Git",
                    "InstallAsset_GVFS",
                    "MountAll"
                });
        }

        [TestCase]
        public void GVFSDownloadError()
        {
            this.ConfigureRunAndVerify(
                configure: () =>
                {
                    this.Upgrader.SetFailOnAction(MockProductUpgrader.ActionType.GVFSDownload);
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
                    this.Upgrader.SetFailOnAction(MockProductUpgrader.ActionType.GitDownload);
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
            this.Orchestrator.Execute();

            this.Orchestrator.ExitCode.ShouldEqual(ReturnCode.Success);

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
                    this.Upgrader.SetFailOnAction(MockProductUpgrader.ActionType.GitInstall);
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
        public void GVFSInstallationArgs()
        {
           this.Orchestrator.Execute();

            this.Orchestrator.ExitCode.ShouldEqual(ReturnCode.Success);

            Dictionary<string, string> gitInstallerInfo;
            this.Upgrader.InstallerArgs.ShouldBeNonEmpty();
            this.Upgrader.InstallerArgs.TryGetValue("GVFS", out gitInstallerInfo).ShouldBeTrue();

            string args;
            gitInstallerInfo.TryGetValue("Args", out args).ShouldBeTrue();
            args.ShouldContain(new string[] { "/VERYSILENT", "/CLOSEAPPLICATIONS", "/SUPPRESSMSGBOXES", "/NORESTART", "/Log", "/MOUNTREPOS=false" });
        }

        [TestCase]
        public void GVFSInstallError()
        {
            this.ConfigureRunAndVerify(
                configure: () =>
                {
                    this.Upgrader.SetFailOnAction(MockProductUpgrader.ActionType.GVFSInstall);
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
        public void RemountReposError()
        {
            this.ConfigureRunAndVerify(
                configure: () =>
                {
                    this.PrerunChecker.SetReturnFalseOnCheck(MockInstallerPrerunChecker.FailOnCheckType.RemountRepos);
                },
                expectedReturn: ReturnCode.GenericError,
                expectedOutput: new List<string>
                {
                    "Auto remount failed."
                },
                expectedErrors: new List<string>
                {
                    "Auto remount failed."
                });
        }

        protected override void RunUpgrade()
        {
            this.Orchestrator.Execute();
        }

        protected override ReturnCode ExitCode()
        {
            return this.Orchestrator.ExitCode;
        }
    }
}