using GVFS.Common;
using GVFS.Common.Cleanup;
using GVFS.Common.FileSystem;
using GVFS.Common.Git;
using GVFS.Common.Tracing;
using GVFS.Tests.Should;
using GVFS.UnitTests.Mock.Common;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Threading;

namespace GVFS.UnitTests.Cleanup
{
    [TestFixture]
    public class GitCleanupQueueTests
    {
        [TestCase]
        public void GitCleanupQueueEnlistmentRootReady()
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

            GitCleanupQueue queue = new GitCleanupQueue(context);
            queue.EnlistmentRootReady().ShouldBeTrue();

            fileSystem.Paths.Remove(enlistment.EnlistmentRoot);
            queue.EnlistmentRootReady().ShouldBeFalse();

            fileSystem.Paths.Remove(enlistment.GitObjectsRoot);
            queue.EnlistmentRootReady().ShouldBeFalse();

            fileSystem.Paths.Add(enlistment.EnlistmentRoot);
            queue.EnlistmentRootReady().ShouldBeFalse();

            fileSystem.Paths.Add(enlistment.GitObjectsRoot);
            queue.EnlistmentRootReady().ShouldBeTrue();
        }

        [TestCase]
        public void GitCleanupQueueHandlesTwoJobs()
        {
            int currentStep = 0;
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

            TestGitCleanupStep step1 = new TestGitCleanupStep(
                () =>
                {
                    currentStep = (currentStep == 0) ? 1 : -1;
                },
                context,
                gitObjects);
            TestGitCleanupStep step2 = new TestGitCleanupStep(
                () =>
                {
                    currentStep = (currentStep == 1) ? 2 : -2;
                },
                context,
                gitObjects);

            GitCleanupQueue queue = new GitCleanupQueue(context);

            queue.Enqueue(step1);
            queue.Enqueue(step2);
            queue.WaitForStepsToFinish();

            currentStep.ShouldEqual(2);
        }

        [TestCase]
        public void GitCleanupQueueStopSuceedsWhenQueueIsEmpty()
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
            
            GitCleanupQueue queue = new GitCleanupQueue(context);
            
            queue.Stop();
        }

        [TestCase]
        public void GitCleanupQueueStopsJob()
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

            GitCleanupQueue queue = new GitCleanupQueue(context);

            // This step stops the queue after the step is started,
            // then checks if Stop() was called.
            WatchForStopStep watchForStop = new WatchForStopStep(queue, context, gitObjects);

            queue.Enqueue(watchForStop);
            queue.WaitForStepsToFinish();
            watchForStop.SawStopping.ShouldBeTrue();

            // Ensure we don't start a job after the Stop() call
            WatchForStartStep watchForStart = new WatchForStartStep(context, gitObjects);
            queue.Enqueue(watchForStart);
            queue.WaitForStepsToFinish();

            watchForStart.Started.ShouldBeFalse();
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

        public class TestGitCleanupStep : GitCleanupStep
        {
            private Action action;

            public TestGitCleanupStep(Action action, GVFSContext context, GitObjects gitObjects)
                : base(context, gitObjects)
            {
                this.action = action;
            }

            public override string TelemetryKey => "TestGitCleanupStep";

            protected override void RunGitAction()
            {
                this.action.Invoke();
            }
        }

        private class WatchForStopStep : GitCleanupStep
        {
            public WatchForStopStep(GitCleanupQueue queue, GVFSContext context, GitObjects gitObjects)
                : base(context, gitObjects)
            {
                this.Queue = queue;
            }

            public GitCleanupQueue Queue { get; set; }

            public bool SawStopping { get; private set; }

            public override string TelemetryKey => "WatchForStopStep";

            protected override void RunGitAction()
            {
                this.Queue.Stop();

                this.SawStopping = this.Stopping;
            }
        }

        private class WatchForStartStep : GitCleanupStep
        {
            public WatchForStartStep(GVFSContext context, GitObjects gitObjects)
                : base(context, gitObjects)
            {
            }

            public bool Started { get; private set; }

            public override string TelemetryKey => "WatchForStopStep";

            protected override void RunGitAction()
            {
                this.Started = true;
            }
        }
    }
}
