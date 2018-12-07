using GVFS.Common;
using GVFS.Common.FileSystem;
using GVFS.Common.Git;
using GVFS.Common.Maintenance;
using GVFS.Common.Tracing;
using GVFS.Tests.Should;
using GVFS.UnitTests.Mock.Common;
using GVFS.UnitTests.Mock.FileSystem;
using NUnit.Framework;

namespace GVFS.UnitTests.Maintenance
{
    [TestFixture]
    public class GitMaintenanceStepTests
    {
        private GVFSContext context;
        private GitObjects gitObjects;

        [TestCase]
        public void GitMaintenanceStepRunsGitAction()
        {
            this.TestSetup();

            CheckMethodStep step = new CheckMethodStep(this.context, this.gitObjects);
            step.Execute();

            step.SawWorkInvoked.ShouldBeTrue();
        }

        [TestCase]
        public void GitMaintenanceStepSkipsGitActionAfterStop()
        {
            this.TestSetup();

            CheckMethodStep step = new CheckMethodStep(this.context, this.gitObjects);

            step.Stop();
            step.Execute();

            step.SawWorkInvoked.ShouldBeFalse();
        }

        [TestCase]
        public void GitMaintenanceStepSkipsRunGitCommandAfterStop()
        {
            this.TestSetup();

            CheckStopStep step = new CheckStopStep(this.context, this.gitObjects);

            step.Execute();

            step.SawWorkInvoked.ShouldBeFalse();
        }

        private void TestSetup()
        {
            ITracer tracer = new MockTracer();
            GVFSEnlistment enlistment = new MockGVFSEnlistment();
            PhysicalFileSystem fileSystem = new MockFileSystem(new MockDirectory(enlistment.EnlistmentRoot, null, null));

            this.context = new GVFSContext(tracer, fileSystem, null, enlistment);
            this.gitObjects = new MockPhysicalGitObjects(tracer, fileSystem, enlistment, null);
        }

        public class CheckMethodStep : GitMaintenanceStep
        {
            public CheckMethodStep(GVFSContext context, GitObjects gitObjects)
                : base(context, gitObjects, requireObjectCacheLock: true)
            {
            }

            public bool SawWorkInvoked { get; set; }

            public override string Area => "CheckMethodStep";

            protected override bool PerformMaintenance()
            {
                this.RunGitCommand(process =>
                {
                    this.SawWorkInvoked = true;
                    return null;
                });

                return true;
            }
        }

        public class CheckStopStep : GitMaintenanceStep
        {
            public CheckStopStep(GVFSContext context, GitObjects gitObjects)
                : base(context, gitObjects, requireObjectCacheLock: true)
            {
            }

            public bool SawWorkInvoked { get; set; }

            public override string Area => "CheckMethodStep";

            protected override bool PerformMaintenance()
            {
                this.Stop();
                this.RunGitCommand(process =>
                {
                    this.SawWorkInvoked = true;
                    return null;
                });

                return true;
            }
        }
    }
}
