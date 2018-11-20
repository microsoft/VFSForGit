using GVFS.Common;
using GVFS.Common.Cleanup;
using GVFS.Common.FileSystem;
using GVFS.Common.Git;
using GVFS.Common.Tracing;
using GVFS.Tests.Should;
using GVFS.UnitTests.Mock.Common;
using GVFS.UnitTests.Mock.FileSystem;
using NUnit.Framework;

namespace GVFS.UnitTests.Cleanup
{
    [TestFixture]
    public class GitCleanupStepTests
    {
        [TestCase]
        public void GitCleanupStepRunsGitAction()
        {
            ITracer tracer = new MockTracer();
            GVFSEnlistment enlistment = new MockGVFSEnlistment();
            PhysicalFileSystem fileSystem = new MockFileSystem(new MockDirectory(enlistment.EnlistmentRoot, null, null));

            GVFSContext context = new GVFSContext(tracer, fileSystem, null, enlistment);
            GitObjects gitObjects = new MockPhysicalGitObjects(tracer, fileSystem, enlistment, null);

            CheckMethodStep step = new CheckMethodStep(context, gitObjects);
            step.Execute();

            step.SawWorkInvoked.ShouldBeTrue();
        }

        [TestCase]
        public void GitCleanupStepSkipsGitActionAfterStop()
        {
            ITracer tracer = new MockTracer();
            GVFSEnlistment enlistment = new MockGVFSEnlistment();
            PhysicalFileSystem fileSystem = new MockFileSystem(new MockDirectory(enlistment.EnlistmentRoot, null, null));

            GVFSContext context = new GVFSContext(tracer, fileSystem, null, enlistment);
            GitObjects gitObjects = new MockPhysicalGitObjects(tracer, fileSystem, enlistment, null);

            CheckMethodStep step = new CheckMethodStep(context, gitObjects);

            step.Stop();
            step.Execute();

            step.SawWorkInvoked.ShouldBeFalse();
        }

        [TestCase]
        public void GitCleanupStepSkipsRunGitCommandAfterStop()
        {
            ITracer tracer = new MockTracer();
            GVFSEnlistment enlistment = new MockGVFSEnlistment();
            PhysicalFileSystem fileSystem = new MockFileSystem(new MockDirectory(enlistment.EnlistmentRoot, null, null));

            GVFSContext context = new GVFSContext(tracer, fileSystem, null, enlistment);
            GitObjects gitObjects = new MockPhysicalGitObjects(tracer, fileSystem, enlistment, null);

            CheckStopStep step = new CheckStopStep(context, gitObjects);

            step.Execute();

            step.SawWorkInvoked.ShouldBeFalse();
        }

        public class CheckMethodStep : GitCleanupStep
        {
            public CheckMethodStep(GVFSContext context, GitObjects gitObjects)
                : base(context, gitObjects)
            {
            }

            public bool SawWorkInvoked { get; set; }

            public override string TelemetryKey => "CheckMethodStep";

            protected override void RunGitAction()
            {
                this.RunGitCommand(process =>
                {
                    this.SawWorkInvoked = true;
                    return null;
                });
            }
        }

        public class CheckStopStep : GitCleanupStep
        {
            public CheckStopStep(GVFSContext context, GitObjects gitObjects)
                : base(context, gitObjects)
            {
            }

            public bool SawWorkInvoked { get; set; }

            public override string TelemetryKey => "CheckMethodStep";

            protected override void RunGitAction()
            {
                this.Stop();
                this.RunGitCommand(process =>
                {
                    this.SawWorkInvoked = true;
                    return null;
                });
            }
        }
    }
}
