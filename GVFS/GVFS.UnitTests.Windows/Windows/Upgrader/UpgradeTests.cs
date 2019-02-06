using GVFS.Common;
using GVFS.Tests.Should;
using GVFS.UnitTests.Category;
using GVFS.UnitTests.Mock.Common;
using GVFS.UnitTests.Mock.FileSystem;
using GVFS.UnitTests.Windows.Mock.Upgrader;
using NUnit.Framework;
using System;
using System.Collections.Generic;

namespace GVFS.UnitTests.Windows.Upgrader
{
    public abstract class UpgradeTests
    {
        protected const string OlderThanLocalVersion = "1.0.17000.1";
        protected const string LocalGVFSVersion = "1.0.18115.1";
        protected const string NewerThanLocalVersion = "1.1.18115.1";

        protected MockTracer Tracer { get; private set; }
        protected MockFileSystem FileSystem { get; private set; }
        protected MockTextWriter Output { get; private set; }
        protected MockInstallerPrerunChecker PrerunChecker { get; private set; }
        protected MockGitHubUpgrader Upgrader { get; private set; }
        protected MockLocalGVFSConfig LocalConfig { get; private set; }

        public virtual void Setup()
        {
            this.Tracer = new MockTracer();
            this.FileSystem = new MockFileSystem(new MockDirectory(@"mock:\GVFS.Upgrades\Download", null, null));
            this.Output = new MockTextWriter();
            this.PrerunChecker = new MockInstallerPrerunChecker(this.Tracer);
            this.LocalConfig = new MockLocalGVFSConfig();

            this.Upgrader = new MockGitHubUpgrader(
                LocalGVFSVersion,
                this.Tracer,
                this.FileSystem,
                new GitHubUpgrader.GitHubUpgraderConfig(this.Tracer, this.LocalConfig));

            this.PrerunChecker.Reset();
            this.Upgrader.PretendNewReleaseAvailableAtRemote(
                upgradeVersion: NewerThanLocalVersion,
                remoteRing: GitHubUpgrader.GitHubUpgraderConfig.RingType.Slow);
            this.SetUpgradeRing("Slow");
        }

        [TestCase]
        public virtual void NoneLocalRing()
        {
            string message = "Upgrade ring set to \"None\". No upgrade check was performed.";
            this.ConfigureRunAndVerify(
                configure: () =>
                {
                    this.SetUpgradeRing("None");
                },
                expectedReturn: ReturnCode.Success,
                expectedOutput: new List<string>
                {
                    message
                },
                expectedErrors: new List<string>
                {
                });
        }

        [TestCase]
        public virtual void InvalidUpgradeRing()
        {
            this.SetUpgradeRing("Invalid");

            string expectedError = "Invalid upgrade ring `Invalid` specified in gvfs config.";
            string errorString;
            GitHubUpgrader.Create(
                this.Tracer,
                this.FileSystem,
                dryRun: false,
                noVerify: false,
                localConfig: this.LocalConfig,
                error: out errorString).ShouldBeNull();

            errorString.ShouldContain(expectedError);
        }

        [TestCase]
        [Category(CategoryConstants.ExceptionExpected)]
        public virtual void FetchReleaseInfo()
        {
            string errorString = "Error fetching upgrade release info.";
            this.ConfigureRunAndVerify(
                configure: () =>
                {
                    this.SetUpgradeRing("Fast");
                    this.Upgrader.SetFailOnAction(MockGitHubUpgrader.ActionType.FetchReleaseInfo);
                },
                expectedReturn: ReturnCode.GenericError,
                expectedOutput: new List<string>
                {
                    errorString
                },
                expectedErrors: new List<string>
                {
                    errorString
                });
        }

        protected abstract ReturnCode RunUpgrade();

        protected void ConfigureRunAndVerify(
            Action configure,
            ReturnCode expectedReturn,
            List<string> expectedOutput,
            List<string> expectedErrors)
        {
            configure();

            this.RunUpgrade().ShouldEqual(expectedReturn);

            if (expectedOutput != null)
            {
                this.Output.AllLines.ShouldContain(
                    expectedOutput,
                    (line, expectedLine) => { return line.Contains(expectedLine); });
            }

            if (expectedErrors != null)
            {
                this.Tracer.RelatedErrorEvents.ShouldContain(
                    expectedErrors,
                    (error, expectedError) => { return error.Contains(expectedError); });
            }
        }

        protected void SetUpgradeRing(string ringName)
        {
            GitHubUpgrader.GitHubUpgraderConfig.RingType ring;
            if (!Enum.TryParse<GitHubUpgrader.GitHubUpgraderConfig.RingType>(ringName, ignoreCase: true, result: out ring))
            {
                ring = GitHubUpgrader.GitHubUpgraderConfig.RingType.Invalid;
            }

            string error;
            if (ring == GitHubUpgrader.GitHubUpgraderConfig.RingType.Slow ||
                ring == GitHubUpgrader.GitHubUpgraderConfig.RingType.Fast)
            {
                this.LocalConfig.TrySetConfig("upgrade.ring", ringName, out error);
                this.VerifyConfig(ring, isUpgradeAllowed: true, isConfigError: false);
                return;
            }

            if (ring == GitHubUpgrader.GitHubUpgraderConfig.RingType.None)
            {
                this.LocalConfig.TrySetConfig("upgrade.ring", ringName, out error);
                this.VerifyConfig(ring, isUpgradeAllowed: false, isConfigError: false);
                return;
            }

            if (ring == GitHubUpgrader.GitHubUpgraderConfig.RingType.Invalid)
            {
                this.LocalConfig.TrySetConfig("upgrade.ring", ringName, out error);
                this.VerifyConfig(ring, isUpgradeAllowed: false, isConfigError: true);
                return;
            }
        }

        protected void VerifyConfig(
            GVFS.Common.GitHubUpgrader.GitHubUpgraderConfig.RingType ring,
            bool isUpgradeAllowed,
            bool isConfigError)
        {
            string error;
            this.Upgrader.Config.TryLoad(out error).ShouldBeTrue();

            Assert.AreEqual(ring, this.Upgrader.Config.UpgradeRing);
            error.ShouldBeNull();

            bool upgradeAllowed = this.Upgrader.UpgradeAllowed(out _);
            bool configError = this.Upgrader.Config.ConfigError();

            upgradeAllowed.ShouldEqual(isUpgradeAllowed);
            configError.ShouldEqual(isConfigError);
        }
    }
}
