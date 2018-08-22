using GVFS.Common;
using GVFS.Tests.Should;
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
        protected CallSequenceTracker CallSequenceTracker { get; private set; }
        protected MockProductUpgrader Upgrader { get; private set; }

        public virtual void Setup()
        {
            this.Tracer = new MockTracer();
            this.Output = new MockTextWriter();
            this.CallSequenceTracker = new CallSequenceTracker();
            this.PrerunChecker = new MockInstallerPrerunChecker(this.CallSequenceTracker, this.Tracer);
            this.Upgrader = new MockProductUpgrader(LocalGVFSVersion, this.CallSequenceTracker, this.Tracer);
     
            this.PrerunChecker.Reset();
            this.Upgrader.PretendNewReleaseAvailableAtRemote(
                upgradeVersion: NewerThanLocalVersion,
                remoteRing: ProductUpgrader.RingType.Slow);
            this.Upgrader.LocalRingConfig = ProductUpgrader.RingType.Slow;
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
                (line, expectedLine) => { return line.Equals(expectedLine, StringComparison.Ordinal); });
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
