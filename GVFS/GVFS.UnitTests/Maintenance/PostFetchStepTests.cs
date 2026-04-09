using GVFS.Common;
using GVFS.Common.FileSystem;
using GVFS.Common.Git;
using GVFS.Common.Maintenance;
using GVFS.Tests.Should;
using GVFS.UnitTests.Mock.Common;
using GVFS.UnitTests.Mock.FileSystem;
using GVFS.UnitTests.Mock.Git;
using NUnit.Framework;
using System.Collections.Generic;

namespace GVFS.UnitTests.Maintenance
{
    [TestFixture]
    public class PostFetchStepTests
    {
        private MockTracer tracer;
        private MockGitProcess gitProcess;
        private GVFSContext context;

        private string CommitGraphWriteCommand => $"commit-graph write --stdin-packs --split --size-multiple=4 --expire-time={GitProcess.ExpireTimeDateString} --object-dir \"{this.context.Enlistment.GitObjectsRoot}\"";
        private string CommitGraphVerifyCommand => $"commit-graph verify --shallow --object-dir \"{this.context.Enlistment.GitObjectsRoot}\"";

        [TestCase]
        public void DontWriteGraphOnEmptyPacks()
        {
            this.TestSetup();

            PostFetchStep step = new PostFetchStep(this.context, new List<string>());
            step.Execute();

            this.tracer.RelatedInfoEvents.Count.ShouldEqual(1);

            List<string> commands = this.gitProcess.CommandsRun;
            commands.Count.ShouldEqual(0);
        }

        [TestCase]
        public void WriteGraphWithPacks()
        {
            this.TestSetup();

            this.gitProcess.SetExpectedCommandResult(
                this.CommitGraphWriteCommand,
                () => new GitProcess.Result(string.Empty, string.Empty, GitProcess.Result.SuccessCode));
            this.gitProcess.SetExpectedCommandResult(
                this.CommitGraphVerifyCommand,
                () => new GitProcess.Result(string.Empty, string.Empty, GitProcess.Result.SuccessCode));

            PostFetchStep step = new PostFetchStep(this.context, new List<string>() { "pack" }, requireObjectCacheLock: false);
            step.Execute();

            this.tracer.RelatedInfoEvents.Count.ShouldEqual(0);

            List<string> commands = this.gitProcess.CommandsRun;

            commands.Count.ShouldEqual(2);
            commands[0].ShouldEqual(this.CommitGraphWriteCommand);
            commands[1].ShouldEqual(this.CommitGraphVerifyCommand);
        }

        [TestCase]
        public void RewriteCommitGraphOnBadVerify()
        {
            this.TestSetup();

            this.gitProcess.SetExpectedCommandResult(
                this.CommitGraphWriteCommand,
                () => new GitProcess.Result(string.Empty, string.Empty, GitProcess.Result.SuccessCode));
            this.gitProcess.SetExpectedCommandResult(
                this.CommitGraphVerifyCommand,
                () => new GitProcess.Result(string.Empty, string.Empty, GitProcess.Result.GenericFailureCode));

            PostFetchStep step = new PostFetchStep(this.context, new List<string>() { "pack" }, requireObjectCacheLock: false);
            step.Execute();

            this.tracer.StartActivityTracer.RelatedErrorEvents.Count.ShouldEqual(0);
            this.tracer.StartActivityTracer.RelatedWarningEvents.Count.ShouldEqual(1);

            List<string> commands = this.gitProcess.CommandsRun;
            commands.Count.ShouldEqual(3);
            commands[0].ShouldEqual(this.CommitGraphWriteCommand);
            commands[1].ShouldEqual(this.CommitGraphVerifyCommand);
            commands[2].ShouldEqual(this.CommitGraphWriteCommand);
        }

        private void TestSetup()
        {
            this.gitProcess = new MockGitProcess();

            // Create enlistment using git process
            GVFSEnlistment enlistment = new MockGVFSEnlistment(this.gitProcess);

            PhysicalFileSystem fileSystem = new MockFileSystem(new MockDirectory(enlistment.EnlistmentRoot, null, null));

            // Create and return Context
            this.tracer = new MockTracer();
            this.context = new GVFSContext(this.tracer, fileSystem, repository: null, enlistment: enlistment);
        }
    }
}
