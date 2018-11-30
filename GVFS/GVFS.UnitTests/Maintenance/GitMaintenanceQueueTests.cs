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

        [TestCase]
        public void GitMaintenanceQueueEnlistmentRootReady()
        {
            ITracer tracer = new MockTracer();
            GVFSEnlistment enlistment = new MockGVFSEnlistment();

            // We need to have the EnlistmentRoot and GitObjectsRoot available for jobs to run
            ReadyFileSystem fileSystem = new ReadyFileSystem(new string[]
            {
                enlistment.EnlistmentRoot,
                enlistment.GitObjectsRoot
            });

            GVFSContext context = new GVFSContext(tracer, fileSystem, null, enlistment);
            GitObjects gitObjects = new MockPhysicalGitObjects(tracer, null, null, null);

            GitMaintenanceQueue queue = new GitMaintenanceQueue(context);
            queue.EnlistmentRootReady().ShouldBeTrue();

            fileSystem.Paths.Remove(enlistment.EnlistmentRoot);
            queue.EnlistmentRootReady().ShouldBeFalse();

            fileSystem.Paths.Remove(enlistment.GitObjectsRoot);
            queue.EnlistmentRootReady().ShouldBeFalse();

            fileSystem.Paths.Add(enlistment.EnlistmentRoot);
            queue.EnlistmentRootReady().ShouldBeFalse();

            fileSystem.Paths.Add(enlistment.GitObjectsRoot);
            queue.EnlistmentRootReady().ShouldBeTrue();

            queue.Stop();
        }

        [TestCase]
        public void GitMaintenanceQueueHandlesTwoJobs()
        {
            ITracer tracer = new MockTracer();
            GVFSEnlistment enlistment = new MockGVFSEnlistment();

            // We need to have the EnlistmentRoot and GitObjectsRoot available for jobs to run
            ReadyFileSystem fileSystem = new ReadyFileSystem(new string[]
            {
                enlistment.EnlistmentRoot,
                enlistment.GitObjectsRoot
            });

            GVFSContext context = new GVFSContext(tracer, fileSystem, null, enlistment);
            GitObjects gitObjects = new MockPhysicalGitObjects(tracer, null, null, null);

            TestGitMaintenanceStep step1 = new TestGitMaintenanceStep(context, gitObjects);
            TestGitMaintenanceStep step2 = new TestGitMaintenanceStep(context, gitObjects);

            GitMaintenanceQueue queue = new GitMaintenanceQueue(context);

            queue.Enqueue(step1);
            queue.Enqueue(step2);

            Assert.IsTrue(step1.EventTriggered.WaitOne(this.maxWaitTime)
                && step2.EventTriggered.WaitOne(this.maxWaitTime));

            queue.Stop();
        }

        [TestCase]
        public void GitMaintenanceQueueStopSuceedsWhenQueueIsEmpty()
        {
            ITracer tracer = new MockTracer();
            GVFSEnlistment enlistment = new MockGVFSEnlistment();

            // We need to have the EnlistmentRoot and GitObjectsRoot available for jobs to run
            ReadyFileSystem fileSystem = new ReadyFileSystem(new string[]
            {
                enlistment.EnlistmentRoot,
                enlistment.GitObjectsRoot
            });

            GVFSContext context = new GVFSContext(tracer, fileSystem, null, enlistment);
            GitObjects gitObjects = new MockPhysicalGitObjects(tracer, null, null, null);

            GitMaintenanceQueue queue = new GitMaintenanceQueue(context);

            queue.Stop();
        }

        [TestCase]
        public void GitMaintenanceQueueStopsJob()
        {
            ITracer tracer = new MockTracer();
            GVFSEnlistment enlistment = new MockGVFSEnlistment();

            // We need to have the EnlistmentRoot and GitObjectsRoot available for jobs to run
            ReadyFileSystem fileSystem = new ReadyFileSystem(new string[]
            {
                enlistment.EnlistmentRoot,
                enlistment.GitObjectsRoot
            });

            GVFSContext context = new GVFSContext(tracer, fileSystem, null, enlistment);
            GitObjects gitObjects = new MockPhysicalGitObjects(tracer, null, null, null);

            GitMaintenanceQueue queue = new GitMaintenanceQueue(context);

            // This step stops the queue after the step is started,
            // then checks if Stop() was called.
            WatchForStopStep watchForStop = new WatchForStopStep(queue, context, gitObjects);

            queue.Enqueue(watchForStop);
            Assert.IsTrue(watchForStop.EventTriggered.WaitOne(this.maxWaitTime));
            watchForStop.SawStopping.ShouldBeTrue();

            // Ensure we don't start a job after the Stop() call
            TestGitMaintenanceStep watchForStart = new TestGitMaintenanceStep(context, gitObjects);
            queue.Enqueue(watchForStart);

            // This only ensure the event didn't happen within maxWaitTime
            Assert.IsFalse(watchForStart.EventTriggered.WaitOne(this.maxWaitTime));

            queue.Stop();
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
            public TestGitMaintenanceStep(GVFSContext context, GitObjects gitObjects)
                : base(context, gitObjects)
            {
                this.EventTriggered = new ManualResetEvent(initialState: false);
            }

            public ManualResetEvent EventTriggered { get; set; }

            public override string TelemetryKey => "TestGitMaintenanceStep";

            protected override void RunGitAction()
            {
                this.EventTriggered.Set();
            }
        }

        private class WatchForStopStep : GitMaintenanceStep
        {
            public WatchForStopStep(GitMaintenanceQueue queue, GVFSContext context, GitObjects gitObjects)
                : base(context, gitObjects)
            {
                this.Queue = queue;
                this.EventTriggered = new ManualResetEvent(false);
            }

            public GitMaintenanceQueue Queue { get; set; }

            public bool SawStopping { get; private set; }

            public ManualResetEvent EventTriggered { get; private set; }

            public override string TelemetryKey => "WatchForStopStep";

            protected override void RunGitAction()
            {
                this.Queue.Stop();

                this.SawStopping = this.Stopping;

                this.EventTriggered.Set();
            }
        }
    }
}
