using GVFS.Common;
using GVFS.Common.FileSystem;
using GVFS.Common.Git;
using GVFS.Common.Maintenance;
using GVFS.Tests.Should;
using GVFS.UnitTests.Mock.Common;
using GVFS.UnitTests.Mock.FileSystem;
using GVFS.UnitTests.Mock.Git;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.IO;

namespace GVFS.UnitTests.Maintenance
{
    [TestFixture]
    public class LooseObjectStepTests
    {
        private const string PrunePackedCommand = "prune-packed -q";
        private MockTracer tracer;
        private MockGitProcess gitProcess;
        private GVFSContext context;

        [TestCase]
        public void LooseObjectsIgnoreTimeRestriction()
        {
            this.TestSetup(DateTime.UtcNow);

            LooseObjectsStep step = new LooseObjectsStep(this.context, requireCacheLock: false, forceRun: true);
            step.Execute();

            this.tracer.StartActivityTracer.RelatedErrorEvents.Count.ShouldEqual(0);
            this.tracer.StartActivityTracer.RelatedWarningEvents.Count.ShouldEqual(0);
            List<string> commands = this.gitProcess.CommandsRun;
            commands.Count.ShouldEqual(1);
            commands[0].ShouldEqual(PrunePackedCommand);
        }

        [TestCase]
        public void LooseObjectsFailTimeRestriction()
        {
            this.TestSetup(DateTime.UtcNow);

            LooseObjectsStep step = new LooseObjectsStep(this.context, requireCacheLock: false, forceRun: false);
            step.Execute();

            this.tracer.StartActivityTracer.RelatedErrorEvents.Count.ShouldEqual(0);
            this.tracer.StartActivityTracer.RelatedWarningEvents.Count.ShouldEqual(1);
            List<string> commands = this.gitProcess.CommandsRun;
            commands.Count.ShouldEqual(0);
        }

        [TestCase]
        public void LooseObjectsPassTimeRestriction()
        {
            this.TestSetup(DateTime.UtcNow.AddDays(-7));

            LooseObjectsStep step = new LooseObjectsStep(this.context, requireCacheLock: false, forceRun: false);
            step.Execute();

            this.tracer.StartActivityTracer.RelatedErrorEvents.Count.ShouldEqual(0);
            this.tracer.StartActivityTracer.RelatedWarningEvents.Count.ShouldEqual(0);
            List<string> commands = this.gitProcess.CommandsRun;
            commands.Count.ShouldEqual(1);
            commands[0].ShouldEqual(PrunePackedCommand);
        }

        [TestCase]
        public void LooseObjectsCount()
        {
            this.TestSetup(DateTime.Now.AddDays(-7));

            LooseObjectsStep step = new LooseObjectsStep(this.context, requireCacheLock: false, forceRun: false);
            int count = step.CountLooseObjects();

            count.ShouldEqual(3);
        }

        private void TestSetup(DateTime lastRun)
        {
            string lastRunTime = EpochConverter.ToUnixEpochSeconds(lastRun).ToString();

            // Create GitProcess
            this.gitProcess = new MockGitProcess();
            this.gitProcess.SetExpectedCommandResult(
                PrunePackedCommand,
                () => new GitProcess.Result(string.Empty, string.Empty, GitProcess.Result.SuccessCode));

            // Create enlistment using git process
            GVFSEnlistment enlistment = new MockGVFSEnlistment(this.gitProcess);

            // Create a last run time file
            MockFile timeFile = new MockFile(Path.Combine(enlistment.GitObjectsRoot, "info", LooseObjectsStep.LooseObjectsLastRunFileName), lastRunTime);

            // Create info directory to hold last run time file
            MockDirectory infoRoot = new MockDirectory(Path.Combine(enlistment.GitObjectsRoot, "info"), null, new List<MockFile>() { timeFile });

            // Create Hex Folder 1 with 1 File
            MockDirectory hex1 = new MockDirectory(
                Path.Combine(enlistment.GitObjectsRoot, "AA"), 
                null, 
                new List<MockFile>()
                {
                     new MockFile(Path.Combine(enlistment.GitObjectsRoot, "AA", "test"), string.Empty)
                });

            // Create Hex Folder 2 with 2 Files
            MockDirectory hex2 = new MockDirectory(
                Path.Combine(enlistment.GitObjectsRoot, "F1"), 
                null, 
                new List<MockFile>()
                {
                     new MockFile(Path.Combine(enlistment.GitObjectsRoot, "F1", "test1"), string.Empty),
                     new MockFile(Path.Combine(enlistment.GitObjectsRoot, "F2", "test2"), string.Empty)
                });

            // Create NonHex Folder 2 with 2 Files
            MockDirectory nonhex = new MockDirectory(
                Path.Combine(enlistment.GitObjectsRoot, "ZZ"), 
                null, 
                new List<MockFile>()
                {
                     new MockFile(Path.Combine(enlistment.GitObjectsRoot, "ZZ", "test1"), string.Empty),
                     new MockFile(Path.Combine(enlistment.GitObjectsRoot, "ZZ", "test2"), string.Empty),
                     new MockFile(Path.Combine(enlistment.GitObjectsRoot, "ZZ", "test3"), string.Empty),
                     new MockFile(Path.Combine(enlistment.GitObjectsRoot, "ZZ", "test4"), string.Empty)
                });

            // Create git objects directory
            MockDirectory gitObjectsRoot = new MockDirectory(enlistment.GitObjectsRoot, new List<MockDirectory>() { infoRoot, hex1, hex2, nonhex }, null);

            // Add object directory to file System
            List<MockDirectory> directories = new List<MockDirectory>() { gitObjectsRoot };
            PhysicalFileSystem fileSystem = new MockFileSystem(new MockDirectory(enlistment.EnlistmentRoot, directories, null));

            // Create and return Context
            this.tracer = new MockTracer();
            this.context = new GVFSContext(this.tracer, fileSystem, repository: null, enlistment: enlistment);
        }
    }
}
