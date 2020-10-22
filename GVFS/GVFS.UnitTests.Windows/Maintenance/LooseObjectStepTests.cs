using GVFS.Common;
using GVFS.Common.FileSystem;
using GVFS.Common.Git;
using GVFS.Common.Maintenance;
using GVFS.Tests.Should;
using GVFS.UnitTests.Mock.Common;
using GVFS.UnitTests.Mock.FileSystem;
using GVFS.UnitTests.Mock.Git;
using Moq;
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
        private string packCommand;
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
            commands.Count.ShouldEqual(2);
            commands[0].ShouldEqual(PrunePackedCommand);
            commands[1].ShouldEqual(this.packCommand);
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

            Mock<GitProcessChecker> mockChecker = new Mock<GitProcessChecker>();
            mockChecker.Setup(checker => checker.GetRunningGitProcessIds())
                       .Returns(Array.Empty<int>());

            LooseObjectsStep step = new LooseObjectsStep(
                                            this.context,
                                            requireCacheLock: false,
                                            forceRun: false,
                                            gitProcessChecker: mockChecker.Object);
            step.Execute();

            mockChecker.Verify(checker => checker.GetRunningGitProcessIds(), Times.Once());

            this.tracer.StartActivityTracer.RelatedErrorEvents.Count.ShouldEqual(0);
            this.tracer.StartActivityTracer.RelatedWarningEvents.Count.ShouldEqual(0);
            List<string> commands = this.gitProcess.CommandsRun;
            commands.Count.ShouldEqual(2);
            commands[0].ShouldEqual(PrunePackedCommand);
            commands[1].ShouldEqual(this.packCommand);
        }

        [TestCase]
        public void LooseObjectsFailGitProcessIds()
        {
            this.TestSetup(DateTime.UtcNow.AddDays(-7));

            Mock<GitProcessChecker> mockChecker = new Mock<GitProcessChecker>();
            mockChecker.Setup(checker => checker.GetRunningGitProcessIds())
                       .Returns(new int[] { 1 });

            LooseObjectsStep step = new LooseObjectsStep(
                                            this.context,
                                            requireCacheLock: false,
                                            forceRun: false,
                                            gitProcessChecker: mockChecker.Object);
            step.Execute();

            mockChecker.Verify(checker => checker.GetRunningGitProcessIds(), Times.Once());

            this.tracer.StartActivityTracer.RelatedErrorEvents.Count.ShouldEqual(0);
            this.tracer.StartActivityTracer.RelatedWarningEvents.Count.ShouldEqual(1);
            List<string> commands = this.gitProcess.CommandsRun;
            commands.Count.ShouldEqual(0);
        }

        [TestCase]
        public void LooseObjectsLimitPackCount()
        {
            this.TestSetup(DateTime.UtcNow.AddDays(-7));

            // Verify with default limit
            LooseObjectsStep step = new LooseObjectsStep(this.context, requireCacheLock: false, forceRun: false);
            step.WriteLooseObjectIds(new StreamWriter(new MemoryStream())).ShouldEqual(3);

            // Verify with limit of 2
            step.MaxLooseObjectsInPack = 2;
            step.WriteLooseObjectIds(new StreamWriter(new MemoryStream())).ShouldEqual(2);
        }

        [TestCase]
        public void SkipInvalidLooseObjects()
        {
            this.TestSetup(DateTime.UtcNow.AddDays(-7));

            // Verify with valid Objects
            LooseObjectsStep step = new LooseObjectsStep(this.context, requireCacheLock: false, forceRun: false);
            step.WriteLooseObjectIds(new StreamWriter(new MemoryStream())).ShouldEqual(3);
            this.tracer.RelatedErrorEvents.Count.ShouldEqual(0);
            this.tracer.RelatedWarningEvents.Count.ShouldEqual(0);

            // Write an ObjectId file with an invalid name
            this.context.FileSystem.WriteAllText(Path.Combine(this.context.Enlistment.GitObjectsRoot, "AA", "NOT_A_SHA"), string.Empty);

            // Verify it wasn't added and a warning exists
            step.WriteLooseObjectIds(new StreamWriter(new MemoryStream())).ShouldEqual(3);
            this.tracer.RelatedErrorEvents.Count.ShouldEqual(0);
            this.tracer.RelatedWarningEvents.Count.ShouldEqual(1);
        }

        [TestCase]
        public void LooseObjectsCount()
        {
            this.TestSetup(DateTime.UtcNow.AddDays(-7));

            LooseObjectsStep step = new LooseObjectsStep(this.context, requireCacheLock: false, forceRun: false);
            step.CountLooseObjects(out int count, out long size);

            count.ShouldEqual(3);
            size.ShouldEqual("one".Length + "two".Length + "three".Length);
        }

        [TestCase]
        public void LooseObjectId()
        {
            this.TestSetup(DateTime.UtcNow.AddDays(-7));

            LooseObjectsStep step = new LooseObjectsStep(this.context, requireCacheLock: false, forceRun: false);
            string directoryName = "AB";
            string fileName = "830bb79cd4fadb2e73e780e452dc71db909001";
            step.TryGetLooseObjectId(
                directoryName,
                Path.Combine(this.context.Enlistment.GitObjectsRoot, directoryName, fileName),
                out string objectId).ShouldBeTrue();
            objectId.ShouldEqual(directoryName + fileName);

            directoryName = "AB";
            fileName = "BAD_FILE_NAME";
            step.TryGetLooseObjectId(
                directoryName,
                Path.Combine(this.context.Enlistment.GitObjectsRoot, directoryName, fileName),
                out objectId).ShouldBeFalse();
        }

        [TestCase]
        public void LooseObjectFileName()
        {
            this.TestSetup(DateTime.UtcNow);
            LooseObjectsStep step = new LooseObjectsStep(this.context, requireCacheLock: false, forceRun: false);

            step.GetLooseObjectFileName("0123456789012345678901234567890123456789")
                .ShouldEqual(Path.Combine(this.context.Enlistment.GitObjectsRoot, "01", "23456789012345678901234567890123456789"));
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

            string packPrefix = Path.Combine(enlistment.GitPackRoot, "from-loose");
            this.packCommand = $"pack-objects {packPrefix} --non-empty --window=0 --depth=0 -q";

            this.gitProcess.SetExpectedCommandResult(
                this.packCommand,
                () => new GitProcess.Result(string.Empty, string.Empty, GitProcess.Result.SuccessCode));

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
                     new MockFile(Path.Combine(enlistment.GitObjectsRoot, "AA", "1156f4f2b850673090c285289ea8475d629fe1"), "one")
                });

            // Create Hex Folder 2 with 2 Files
            MockDirectory hex2 = new MockDirectory(
                Path.Combine(enlistment.GitObjectsRoot, "F1"),
                null,
                new List<MockFile>()
                {
                     new MockFile(Path.Combine(enlistment.GitObjectsRoot, "F1", "1156f4f2b850673090c285289ea8475d629fe2"), "two"),
                     new MockFile(Path.Combine(enlistment.GitObjectsRoot, "F1", "1156f4f2b850673090c285289ea8475d629fe3"), "three")
                });

            // Create NonHex Folder with 4 Files
            MockDirectory nonhex = new MockDirectory(
                Path.Combine(enlistment.GitObjectsRoot, "ZZ"),
                null,
                new List<MockFile>()
                {
                     new MockFile(Path.Combine(enlistment.GitObjectsRoot, "ZZ", "1156f4f2b850673090c285289ea8475d629fe4"), "4"),
                     new MockFile(Path.Combine(enlistment.GitObjectsRoot, "ZZ", "1156f4f2b850673090c285289ea8475d629fe5"), "5"),
                     new MockFile(Path.Combine(enlistment.GitObjectsRoot, "ZZ", "1156f4f2b850673090c285289ea8475d629fe6"), "6"),
                     new MockFile(Path.Combine(enlistment.GitObjectsRoot, "ZZ", "1156f4f2b850673090c285289ea8475d629fe7"), "7")
                });

            MockDirectory pack = new MockDirectory(
                enlistment.GitPackRoot,
                null,
                new List<MockFile>());

            // Create git objects directory
            MockDirectory gitObjectsRoot = new MockDirectory(enlistment.GitObjectsRoot, new List<MockDirectory>() { infoRoot, hex1, hex2, nonhex, pack }, null);

            // Add object directory to file System
            List<MockDirectory> directories = new List<MockDirectory>() { gitObjectsRoot };
            PhysicalFileSystem fileSystem = new MockFileSystem(new MockDirectory(enlistment.EnlistmentRoot, directories, null));

            // Create and return Context
            this.tracer = new MockTracer();
            this.context = new GVFSContext(this.tracer, fileSystem, repository: null, enlistment: enlistment);
        }
    }
}
