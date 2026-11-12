using GVFS.Common;
using GVFS.Common.FileSystem;
using GVFS.Common.Git;
using GVFS.Common.Maintenance;
using GVFS.Common.Tracing;
using GVFS.Tests.Should;
using GVFS.UnitTests.Category;
using GVFS.UnitTests.Mock.Common;
using GVFS.UnitTests.Mock.FileSystem;
using Moq;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.IO;

namespace GVFS.UnitTests.Maintenance
{
    [TestFixture]
    public class GitMaintenanceStepTests
    {
        private GVFSContext context;

        public enum WhenToStop
        {
            Never,
            BeforeGitCommand,
            DuringGitCommand
        }

        [TestCase]
        public void GitMaintenanceStepRunsGitAction()
        {
            this.TestSetup();

            CheckMethodStep step = new CheckMethodStep(this.context, WhenToStop.Never);
            step.Execute();

            step.SawWorkInvoked.ShouldBeTrue();
            step.SawEndOfMethod.ShouldBeTrue();
        }

        [TestCase]
        [Category(CategoryConstants.ExceptionExpected)]
        public void GitMaintenanceStepSkipsGitActionAfterStop()
        {
            this.TestSetup();

            CheckMethodStep step = new CheckMethodStep(this.context, WhenToStop.Never);

            step.Stop();
            step.Execute();

            step.SawWorkInvoked.ShouldBeFalse();
            step.SawEndOfMethod.ShouldBeFalse();
        }

        [TestCase]
        [Category(CategoryConstants.ExceptionExpected)]
        public void GitMaintenanceStepSkipsRunGitCommandAfterStop()
        {
            this.TestSetup();

            CheckMethodStep step = new CheckMethodStep(this.context, WhenToStop.BeforeGitCommand);

            step.Execute();

            step.SawWorkInvoked.ShouldBeFalse();
            step.SawEndOfMethod.ShouldBeFalse();
        }

        [TestCase]
        [Category(CategoryConstants.ExceptionExpected)]
        public void GitMaintenanceStepThrowsIfStoppedDuringGitCommand()
        {
            this.TestSetup();
            CheckMethodStep step = new CheckMethodStep(this.context, WhenToStop.DuringGitCommand);

            step.Execute();

            step.SawWorkInvoked.ShouldBeTrue();
            step.SawEndOfMethod.ShouldBeFalse();
        }

        [TestCase]
        public void OffloadedPrefetchDeclinesIfTheBaselineDisappearsBeforeDownload()
        {
            using (MockTracer tracer = new MockTracer())
            {
                MockGVFSEnlistment enlistment = new MockGVFSEnlistment();
                Mock<PhysicalFileSystem> fileSystem = new Mock<PhysicalFileSystem>();
                string pack = Path.Combine(enlistment.GitPackRoot, "prefetch-1000-aabb.pack");
                fileSystem.Setup(fs => fs.DirectoryExists(enlistment.GitPackRoot)).Returns(true);
                fileSystem.Setup(fs => fs.GetFiles(enlistment.GitPackRoot, "prefetch-*.pack")).Returns(new[] { pack });
                fileSystem.Setup(fs => fs.FileExists(Path.ChangeExtension(pack, ".idx"))).Returns(true);
                fileSystem.Setup(fs => fs.GetAttributes(Path.ChangeExtension(pack, ".incomplete")))
                    .Throws(new FileNotFoundException());
                Mock<GitObjects> gitObjects = new Mock<GitObjects>(tracer, enlistment, null, fileSystem.Object);
                gitObjects.Setup(objects => objects.ReadPackFileNames(enlistment.GitPackRoot, "prefetch"))
                    .Returns(Array.Empty<string>());
                GVFSContext context = new GVFSContext(tracer, fileSystem.Object, null, enlistment);
                PrefetchStep step = new PrefetchStep(context, gitObjects.Object, requireCacheLock: false);

                Assert.That(step.TryPrefetchCommitsAndTreesForOffload(out string error, out bool declined), Is.False);
                Assert.That(declined, Is.True);
                Assert.That(error, Is.Null);
                List<string> indexes = null;
                gitObjects.Verify(objects => objects.TryDownloadPrefetchPacks(
                    It.IsAny<GitProcess>(), It.IsAny<long>(), It.IsAny<bool>(), out indexes), Times.Never);
            }
        }

        private void TestSetup()
        {
            ITracer tracer = new MockTracer();
            GVFSEnlistment enlistment = new MockGVFSEnlistment();
            PhysicalFileSystem fileSystem = new MockFileSystem(new MockDirectory(enlistment.PrimaryEnlistmentRoot, null, null));

            this.context = new GVFSContext(tracer, fileSystem, null, enlistment);
        }

        public class CheckMethodStep : GitMaintenanceStep
        {
            private WhenToStop when;

            public CheckMethodStep(GVFSContext context, WhenToStop when)
                : base(context, requireObjectCacheLock: true)
            {
                this.when = when;
            }

            public bool SawWorkInvoked { get; set; }
            public bool SawEndOfMethod { get; set; }

            public override string Area => "CheckMethodStep";

            protected override void PerformMaintenance()
            {
                if (this.when == WhenToStop.BeforeGitCommand)
                {
                    this.Stop();
                }

                this.RunGitCommand(
                    process =>
                    {
                        this.SawWorkInvoked = true;

                        if (this.when == WhenToStop.DuringGitCommand)
                        {
                            this.Stop();
                        }

                        return null;
                    },
                    nameof(this.SawWorkInvoked));

                this.SawEndOfMethod = true;
            }
        }
    }
}
