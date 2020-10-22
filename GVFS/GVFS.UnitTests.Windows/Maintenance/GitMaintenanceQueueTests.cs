using GVFS.Common;
using GVFS.Common.FileSystem;
using GVFS.Common.Git;
using GVFS.Common.Maintenance;
using GVFS.Common.Tracing;
using GVFS.Tests.Should;
using GVFS.UnitTests.Mock.Common;
using NUnit.Framework;
using System.Collections.Generic;
using System.Threading;

namespace GVFS.UnitTests.Maintenance
{
    [TestFixture]
    public class GitMaintenanceQueueTests
    {
        private int maxWaitTime = 500;
        private ReadyFileSystem fileSystem;
        private GVFSEnlistment enlistment;
        private GVFSContext context;
        private GitObjects gitObjects;

        [TestCase]
        public void GitMaintenanceQueueEnlistmentRootReady()
        {
            this.TestSetup();

            GitMaintenanceQueue queue = new GitMaintenanceQueue(this.context);
            queue.EnlistmentRootReady().ShouldBeTrue();

            this.fileSystem.Paths.Remove(this.enlistment.EnlistmentRoot);
            queue.EnlistmentRootReady().ShouldBeFalse();

            this.fileSystem.Paths.Remove(this.enlistment.GitObjectsRoot);
            queue.EnlistmentRootReady().ShouldBeFalse();

            this.fileSystem.Paths.Add(this.enlistment.EnlistmentRoot);
            queue.EnlistmentRootReady().ShouldBeFalse();

            this.fileSystem.Paths.Add(this.enlistment.GitObjectsRoot);
            queue.EnlistmentRootReady().ShouldBeTrue();

            queue.Stop();
        }

        [TestCase]
        public void GitMaintenanceQueueHandlesTwoJobs()
        {
            this.TestSetup();

            TestGitMaintenanceStep step1 = new TestGitMaintenanceStep(this.context);
            TestGitMaintenanceStep step2 = new TestGitMaintenanceStep(this.context);

            GitMaintenanceQueue queue = new GitMaintenanceQueue(this.context);

            queue.TryEnqueue(step1);
            queue.TryEnqueue(step2);

            step1.EventTriggered.WaitOne(this.maxWaitTime).ShouldBeTrue();
            step2.EventTriggered.WaitOne(this.maxWaitTime).ShouldBeTrue();

            queue.Stop();

            step1.NumberOfExecutions.ShouldEqual(1);
            step2.NumberOfExecutions.ShouldEqual(1);
        }

        [TestCase]
        public void GitMaintenanceQueueStopSuceedsWhenQueueIsEmpty()
        {
            this.TestSetup();

            GitMaintenanceQueue queue = new GitMaintenanceQueue(this.context);

            queue.Stop();

            TestGitMaintenanceStep step = new TestGitMaintenanceStep(this.context);
            queue.TryEnqueue(step).ShouldEqual(false);
        }

        [TestCase]
        public void GitMaintenanceQueueStopsJob()
        {
            this.TestSetup();

            GitMaintenanceQueue queue = new GitMaintenanceQueue(this.context);

            // This step stops the queue after the step is started,
            // then checks if Stop() was called.
            WatchForStopStep watchForStop = new WatchForStopStep(queue, this.context);

            queue.TryEnqueue(watchForStop);
            Assert.IsTrue(watchForStop.EventTriggered.WaitOne(this.maxWaitTime));
            watchForStop.SawStopping.ShouldBeTrue();

            // Ensure we don't start a job after the Stop() call
            TestGitMaintenanceStep watchForStart = new TestGitMaintenanceStep(this.context);
            queue.TryEnqueue(watchForStart).ShouldBeFalse();

            // This only ensures the event didn't happen within maxWaitTime
            Assert.IsFalse(watchForStart.EventTriggered.WaitOne(this.maxWaitTime));

            queue.Stop();
        }

        private void TestSetup()
        {
            ITracer tracer = new MockTracer();
            this.enlistment = new MockGVFSEnlistment();

            // We need to have the EnlistmentRoot and GitObjectsRoot available for jobs to run
            this.fileSystem = new ReadyFileSystem(new string[]
            {
                this.enlistment.EnlistmentRoot,
                this.enlistment.GitObjectsRoot
            });

            this.context = new GVFSContext(tracer, this.fileSystem, null, this.enlistment);
            this.gitObjects = new MockPhysicalGitObjects(tracer, this.fileSystem, this.enlistment, null);
        }

        public class ReadyFileSystem : PhysicalFileSystem
        {
            public ReadyFileSystem(IEnumerable<string> paths)
            {
                this.Paths = new HashSet<string>(paths);
            }

            public HashSet<string> Paths { get; }

            public override bool DirectoryExists(string path)
            {
                return this.Paths.Contains(path);
            }
        }

        public class TestGitMaintenanceStep : GitMaintenanceStep
        {
            public TestGitMaintenanceStep(GVFSContext context)
                : base(context, requireObjectCacheLock: true)
            {
                this.EventTriggered = new ManualResetEvent(initialState: false);
            }

            public ManualResetEvent EventTriggered { get; set; }
            public int NumberOfExecutions { get; set; }

            public override string Area => "TestGitMaintenanceStep";

            protected override void PerformMaintenance()
            {
                this.NumberOfExecutions++;
                this.EventTriggered.Set();
            }
        }

        private class WatchForStopStep : GitMaintenanceStep
        {
            public WatchForStopStep(GitMaintenanceQueue queue, GVFSContext context)
                : base(context, requireObjectCacheLock: true)
            {
                this.Queue = queue;
                this.EventTriggered = new ManualResetEvent(false);
            }

            public GitMaintenanceQueue Queue { get; set; }

            public bool SawStopping { get; private set; }

            public ManualResetEvent EventTriggered { get; private set; }

            public override string Area => "WatchForStopStep";

            protected override void PerformMaintenance()
            {
                this.Queue.Stop();

                this.SawStopping = this.Stopping;

                this.EventTriggered.Set();
            }
        }
    }
}
