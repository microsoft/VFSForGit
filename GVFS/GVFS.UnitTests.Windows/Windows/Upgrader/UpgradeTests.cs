using GVFS.Common;
using GVFS.Tests.Should;
using GVFS.UnitTests.Category;
using GVFS.UnitTests.Mock.Common;
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
        protected MockTextWriter Output { get; private set; }
        protected MockInstallerPrerunChecker PrerunChecker { get; private set; }
        protected MockProductUpgrader Upgrader { get; private set; }

        public virtual void Setup()
        {
            this.Tracer = new MockTracer();
            this.Output = new MockTextWriter();
            this.PrerunChecker = new MockInstallerPrerunChecker(this.Tracer);
            this.Upgrader = new MockProductUpgrader(LocalGVFSVersion, this.Tracer);
     
            this.PrerunChecker.Reset();
            this.Upgrader.PretendNewReleaseAvailableAtRemote(
                upgradeVersion: NewerThanLocalVersion,
                remoteRing: ProductUpgrader.RingType.Slow);
            this.Upgrader.LocalRingConfig = ProductUpgrader.RingType.Slow;
        }

        [TestCase]
        public virtual void NoneLocalRing()
        {
            string message = "Upgrade ring set to \"None\". No upgrade check was performed.";
            this.ConfigureRunAndVerify(
                configure: () =>
                {
                    this.Upgrader.LocalRingConfig = ProductUpgrader.RingType.None;
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
            string errorString = "Invalid upgrade ring `Invalid` specified in gvfs config.";
            this.ConfigureRunAndVerify(
                configure: () =>
                {
                    this.Upgrader.LocalRingConfig = GVFS.Common.ProductUpgrader.RingType.Invalid;
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

        [TestCase]
        [Category(CategoryConstants.ExceptionExpected)]
        public virtual void FetchReleaseInfo()
        {
            string errorString = "Error fetching upgrade release info.";
            this.ConfigureRunAndVerify(
                configure: () =>
                {
                    this.Upgrader.SetFailOnAction(MockProductUpgrader.ActionType.FetchReleaseInfo);
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

        protected abstract void RunUpgrade();

        protected abstract ReturnCode ExitCode();

        protected void ConfigureRunAndVerify(
            Action configure,
            ReturnCode expectedReturn,
            List<string> expectedOutput,
            List<string> expectedErrors)
        {
            configure();

            this.RunUpgrade();

            this.ExitCode().ShouldEqual(expectedReturn);

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
    }
}
