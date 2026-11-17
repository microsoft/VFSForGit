using GVFS.Common;
using GVFS.Common.Git;
using GVFS.Common.NamedPipes;
using GVFS.Common.Sparse;
using GVFS.Common.Tracing;
using GVFS.Tests.Should;
using GVFS.UnitTests.Mock.Common;
using GVFS.UnitTests.Mock.FileSystem;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace GVFS.UnitTests.Common.Sparse
{
    [TestFixture]
    public class AutoSparseIndexConeManagerTests
    {
        private const string RepoRoot = @"mock:\repo";
        private const string GitDir = @"mock:\repo\.git";
        private const string InfoDir = @"mock:\repo\.git\info";
        private const string SparseCheckoutPath = @"mock:\repo\.git\info\sparse-checkout";
        private const string BackupPath = SparseCheckoutPath + ConeFileWriter.BackupExtension;
        private const string Header = "/*\n!/*/\n";
        private const string ConeWithAB = Header + "/A/\n!/A/*/\n/A/B/\n!/A/B/*/\n";
        private const string ConeWithC = Header + "/C/\n!/C/*/\n";

        private const int FirstGit = 100;
        private const int SecondGit = 200;

        private MockTracer tracer;
        private TestFileSystem fileSystem;
        private MockEnvironment environment;
        private AutoSparseIndexConeManager manager;

        [SetUp]
        public void SetUp()
        {
            this.tracer = new MockTracer();
            this.fileSystem = new TestFileSystem();
            this.fileSystem.WriteAllText(SparseCheckoutPath, Header);
            this.environment = new MockEnvironment(this.fileSystem);
            this.environment.RunningProcesses[FirstGit] = 1;
            this.environment.RunningProcesses[SecondGit] = 2;
            this.manager = this.CreateManager(ignoreCase: false, changeCheckDelayMs: Timeout.Infinite);
        }

        [TearDown]
        public void TearDown()
        {
            this.manager.Stop();
        }

        [TestCase("git version 2.56.1.vfs.0.0.6\nfeature: fsmonitor--daemon\nfeature: vfs-sparse-index\n", 0, true)]
        [TestCase("git version 2.56.1.vfs.0.0.6\r\nfeature: vfs-sparse-index\r\n", 0, true)]
        [TestCase("git version 2.56.1.vfs.0.1\nfeature: fsmonitor--daemon\n", 0, false)]
        [TestCase("feature: vfs-sparse-index-v2\nfeatures: vfs-sparse-index\n", 0, false)]
        [TestCase("feature: vfs-sparse-index\n", 1, false)]
        [TestCase(null, 0, false)]
        public void GitSupportsSparseIndexOnlyWithTheFeatureLine(string output, int exitCode, bool expected)
        {
            AutoSparseIndexConeManager.GitSupportsSparseIndex(new GitProcess.Result(output, string.Empty, exitCode)).ShouldEqual(expected);
        }

        [TestCase]
        public void WidenWritesTheConeAndAppliesItToTheIndex()
        {
            this.Widen(FirstGit, "A/B/b.txt").ShouldBeTrue();

            this.ConeFile.ShouldEqual(ConeWithAB);
            this.environment.ConeFilesAtApply.ShouldMatchInOrder(ConeWithAB);
            this.fileSystem.FileExists(BackupPath).ShouldBeFalse("The backup must be deleted");
            this.LastEvent("ConeWiden").Level.ShouldEqual(EventLevel.Informational);
        }

        [TestCase]
        public void NarrowWritesTheConeOfTheModifiedPathsWithoutRunningGit()
        {
            this.environment.ModifiedPaths.Add("C/c.txt");
            this.Widen(FirstGit, "A/B/b.txt").ShouldBeTrue();
            this.ConeFile.ShouldEqual(Header + "/A/\n!/A/*/\n/A/B/\n!/A/B/*/\n/C/\n!/C/*/\n");

            this.Narrow(FirstGit).ShouldBeTrue();

            this.ConeFile.ShouldEqual(ConeWithC);
            this.environment.ConeFilesAtApply.Count.ShouldEqual(1, "The narrow must not run Git");
            this.fileSystem.FileExists(BackupPath).ShouldBeFalse("The backup must be deleted");
        }

        [TestCase]
        public void WidenAfterANarrowAppliesTheConeEvenWhenTheConeFileDoesNotChange()
        {
            this.Widen(FirstGit, "A/B/b.txt").ShouldBeTrue();
            this.Narrow(FirstGit).ShouldBeTrue();

            // A root file adds no directory, so the cone file stays the same. The index still
            // has the entries that the first widen expanded, so Git must collapse them.
            this.Widen(SecondGit, "root.txt").ShouldBeTrue();

            this.ConeFile.ShouldEqual(Header);
            this.environment.ConeFilesAtApply.ShouldMatchInOrder(ConeWithAB, Header);
        }

        [TestCase]
        public void WidenDoesNotRunGitWhenTheConeAlreadyIncludesThePaths()
        {
            this.Widen(FirstGit, "A/B/b.txt").ShouldBeTrue();

            this.Widen(SecondGit, "A/B/other.txt").ShouldBeTrue();

            this.environment.ConeFilesAtApply.Count.ShouldEqual(1);
        }

        [TestCase]
        public void DirectoryStaysInTheConeUntilTheLastCommandThatNeedsItNarrows()
        {
            this.Widen(FirstGit, "A/B/b.txt").ShouldBeTrue();
            this.Widen(SecondGit, "A/B/other.txt").ShouldBeTrue();

            this.Narrow(FirstGit).ShouldBeTrue();
            this.ConeFile.ShouldEqual(ConeWithAB);

            this.Narrow(SecondGit).ShouldBeTrue();
            this.ConeFile.ShouldEqual(Header);
        }

        [TestCase]
        public void EntriesOfACommandThatEndedWithoutANarrowAreRemovedAtTheNextRequest()
        {
            this.Widen(FirstGit, "A/B/b.txt").ShouldBeTrue();
            this.environment.RunningProcesses.Remove(FirstGit);

            this.Widen(SecondGit, "C/c.txt").ShouldBeTrue();

            this.ConeFile.ShouldEqual(ConeWithC);
        }

        [TestCase]
        public void NarrowOfAnotherCommandRemovesTheEntriesOfACommandThatEnded()
        {
            this.Widen(FirstGit, "A/B/b.txt").ShouldBeTrue();
            this.environment.RunningProcesses.Remove(FirstGit);

            this.Narrow(SecondGit).ShouldBeTrue();

            this.ConeFile.ShouldEqual(Header);
        }

        [TestCase]
        public void EntriesOfAProcessIdThatAnotherProcessReusesAreRemoved()
        {
            this.Widen(FirstGit, "A/B/b.txt").ShouldBeTrue();
            this.environment.RunningProcesses[FirstGit] = 99;

            this.Narrow(SecondGit).ShouldBeTrue();

            this.ConeFile.ShouldEqual(Header);
        }

        [TestCase]
        public void WidenThatArrivesAfterTheNarrowOfTheSameCommandIsRefused()
        {
            this.Narrow(FirstGit).ShouldBeTrue();

            this.Widen(FirstGit, "A/B/b.txt").ShouldBeFalse();

            this.ConeFile.ShouldEqual(Header);
            this.environment.ConeFilesAtApply.ShouldBeEmpty();
        }

        [TestCase]
        public void WidenFromACommandThatEndedIsRefused()
        {
            this.environment.RunningProcesses.Remove(FirstGit);

            this.Widen(FirstGit, "A/B/b.txt").ShouldBeFalse();

            this.ConeFile.ShouldEqual(Header);
        }

        [TestCase]
        public void WidenWithoutACommandIdIsRefused()
        {
            this.manager.TryWiden(new NamedPipeMessages.ConeManagement.WidenParameters(string.Empty, new[] { "A/B/b.txt" })).ShouldBeFalse();

            this.ConeFile.ShouldEqual(Header);
            this.environment.ConeFilesAtApply.ShouldBeEmpty();
        }

        [TestCase]
        public void WidenIsRefusedWhenGitCannotReplaceTheIndex()
        {
            this.environment.IndexWriteAllowed = false;

            this.Widen(FirstGit, "A/B/b.txt").ShouldBeFalse();

            this.ConeFile.ShouldEqual(Header);
            this.environment.ConeFilesAtApply.ShouldBeEmpty();
        }

        [TestCase]
        public void WidenIsRefusedWhenGitHoldsTheConeFileLock()
        {
            this.environment.ConeFileLocked = true;

            this.Widen(FirstGit, "A/B/b.txt").ShouldBeFalse();

            this.ConeFile.ShouldEqual(Header);
            this.environment.ConeFilesAtApply.ShouldBeEmpty();
            this.LastEvent("ConeWiden").Level.ShouldEqual(EventLevel.Warning);
        }

        [TestCase]
        public void ConeFileLockIsReleasedAfterEachRequest()
        {
            this.Widen(FirstGit, "A/B/b.txt").ShouldBeTrue();
            this.Narrow(FirstGit).ShouldBeTrue();

            this.environment.LockCount.ShouldEqual(2);
            this.environment.IsConeFileLockHeld.ShouldBeFalse();
        }

        [TestCase]
        public void WidenWithAPathThatIsNotValidIsRefused()
        {
            this.manager.TryWiden(new NamedPipeMessages.ConeManagement.WidenParameters(FirstGit.ToString(), new[] { "../outside.txt" })).ShouldBeFalse();

            this.ConeFile.ShouldEqual(Header);
            this.environment.ConeFilesAtApply.ShouldBeEmpty();
        }

        [TestCase]
        public void ModifiedPathThatAConeFileCannotContainKeepsTheConeFileAndTheIndex()
        {
            this.Widen(FirstGit, "A/B/b.txt").ShouldBeTrue();
            this.environment.ModifiedPaths.Add("D/*/d.txt");

            this.Widen(SecondGit, "C/c.txt").ShouldBeFalse();
            this.LastEvent("ConeWiden").Metadata["InvalidPathException"].ToString().ShouldContain("D/*");

            this.Narrow(FirstGit).ShouldBeFalse();

            this.ConeFile.ShouldEqual(ConeWithAB);
            this.environment.ConeFilesAtApply.Count.ShouldEqual(1);
        }

        [TestCase]
        public void ConeFileWriteFailureLeavesTheConeFileAndTheCommandEntriesUnchanged()
        {
            this.fileSystem.FailRenameTo = SparseCheckoutPath;

            this.Widen(FirstGit, "A/B/b.txt").ShouldBeFalse();

            this.ConeFile.ShouldEqual(Header);
            this.environment.ConeFilesAtApply.ShouldBeEmpty();
            this.fileSystem.FileExists(BackupPath).ShouldBeFalse("The backup must be deleted");

            this.fileSystem.FailRenameTo = null;
            this.Widen(SecondGit, "C/c.txt").ShouldBeTrue();
            this.ConeFile.ShouldEqual(ConeWithC);
        }

        [TestCase]
        public void ApplyFailureRestoresThePreviousConeFile()
        {
            this.environment.ApplyExitCode = 128;

            this.Widen(FirstGit, "A/B/b.txt").ShouldBeFalse();

            this.ConeFile.ShouldEqual(Header);
            this.fileSystem.FileExists(BackupPath).ShouldBeFalse("The backup must be deleted");
            this.LastEvent("ConeWiden").Metadata["Restored"].ShouldEqual(true);

            // The failed command has no entries, and the next widen applies the cone again.
            this.environment.ApplyExitCode = 0;
            this.Widen(SecondGit, "root.txt").ShouldBeTrue();
            this.environment.ConeFilesAtApply.ShouldMatchInOrder(ConeWithAB, Header);
        }

        [TestCase]
        public void ApplyExceptionRestoresThePreviousConeFile()
        {
            this.environment.ApplyException = new InvalidOperationException("Injected failure");

            this.Widen(FirstGit, "A/B/b.txt").ShouldBeFalse();

            this.ConeFile.ShouldEqual(Header);
            this.LastEvent("ConeWiden").Metadata["Result"].ShouldEqual("ApplyFailed");

            this.environment.ApplyException = null;
            this.Widen(SecondGit, "root.txt").ShouldBeTrue();
            this.environment.ConeFilesAtApply.ShouldMatchInOrder(ConeWithAB, Header);
        }

        [TestCase]
        public void NarrowWithoutEntriesDoesNotWriteTheConeFile()
        {
            // The first request of a mount writes the cone file, which can be stale.
            this.Narrow(FirstGit).ShouldBeTrue();
            this.environment.LockCount.ShouldEqual(1);

            this.Narrow(SecondGit).ShouldBeTrue();

            this.environment.LockCount.ShouldEqual(1);
        }

        [TestCase]
        public void WidenIsRefusedForACommandThatDoesNotHoldTheGVFSLock()
        {
            this.environment.LockHolder = SecondGit;

            this.Widen(FirstGit, "A/B/b.txt").ShouldBeFalse();

            this.ConeFile.ShouldEqual(Header);
            this.environment.ConeFilesAtApply.ShouldBeEmpty();
            this.LastEvent("ConeWiden").Metadata["Result"].ShouldEqual("NotLockHolder");
        }

        [TestCase]
        public void WidenWithTooManyPathsIsRefused()
        {
            string[] paths = Enumerable.Range(0, AutoSparseIndexConeManager.MaxWidenPaths + 1).Select(i => $"D{i}/f.txt").ToArray();

            this.Widen(FirstGit, paths).ShouldBeFalse();

            this.ConeFile.ShouldEqual(Header);
            this.environment.ConeFilesAtApply.ShouldBeEmpty();
        }

        [TestCase]
        public void WidenThatWaitedTooLongForEarlierRequestsDoesNotRunGit()
        {
            this.environment.CanWriteIndexDelayMs = 600;

            this.Widen(FirstGit, "A/B/b.txt").ShouldBeFalse();

            this.ConeFile.ShouldEqual(Header);
            this.environment.ConeFilesAtApply.ShouldBeEmpty();
            this.LastEvent("ConeWiden").Metadata["Result"].ShouldEqual("QueueWaitTooLong");
        }

        [TestCase]
        public void WidenIsRefusedWhenTheSparseDirectoriesAreNotKnown()
        {
            this.environment.ModifiedPaths.Add("D/deleted.txt");
            this.environment.DeletedFiles.Add("D/deleted.txt");
            this.environment.SparseDirectoriesFail = true;

            this.Widen(FirstGit, "A/B/b.txt").ShouldBeFalse();

            this.ConeFile.ShouldEqual(Header);
            this.environment.ConeFilesAtApply.ShouldBeEmpty();
        }

        [TestCase]
        public void WidenDoesNotListSparseDirectoriesWithoutADeletedFileOrModifiedFolder()
        {
            this.environment.ModifiedPaths.Add("C/edited.txt");
            this.environment.SparseDirectoriesFail = true;

            this.Widen(FirstGit, "A/B/b.txt").ShouldBeTrue();
        }

        [TestCase]
        public void WidenThatExpandsASparseDirectoryWithADeletedFileIsRefused()
        {
            // Git checks the deleted file out again when it expands A/.
            this.environment.SparseDirectories.Add("A/");
            this.environment.ModifiedPaths.Add("A/B/deleted.txt");
            this.environment.DeletedFiles.Add("A/B/deleted.txt");

            this.Widen(FirstGit, "A/B/b.txt").ShouldBeFalse();

            this.ConeFile.ShouldEqual(Header);
            this.environment.ConeFilesAtApply.ShouldBeEmpty();
            this.LastEvent("ConeWiden").Metadata["UnsafeModifiedPath"].ShouldEqual("A/B/deleted.txt");
        }

        [TestCase]
        public void WidenOfASiblingDirectoryOfADeletedFileIsApplied()
        {
            // A/X stays a sparse-directory entry, so Git does not check the deleted file out.
            this.environment.SparseDirectories.Add("A/");
            this.environment.ModifiedPaths.Add("A/X/deleted.txt");
            this.environment.DeletedFiles.Add("A/X/deleted.txt");

            this.Widen(FirstGit, "A/B/b.txt").ShouldBeTrue();

            this.ConeFile.ShouldEqual(ConeWithAB);
        }

        [TestCase]
        public void WidenThatExpandsASparseDirectoryWithAModifiedFolderIsRefused()
        {
            this.environment.SparseDirectories.Add("A/");
            this.environment.ModifiedPaths.Add("A/B/");

            this.Widen(FirstGit, "A/B/C/c.txt").ShouldBeFalse();

            this.environment.ConeFilesAtApply.ShouldBeEmpty();
        }

        [TestCase]
        public void ModifiedFileThatExistsInsideASparseDirectoryIsInTheCone()
        {
            // An existing file keeps its content when Git expands its directory.
            this.environment.SparseDirectories.Add("A/");
            this.environment.ModifiedPaths.Add("A/B/edited.txt");

            this.Widen(FirstGit, "A/B/b.txt").ShouldBeTrue();
            this.Narrow(FirstGit).ShouldBeTrue();

            this.ConeFile.ShouldEqual(ConeWithAB);
        }

        [TestCase]
        public void DeletedFileInsideASparseDirectoryStaysOutOfTheCone()
        {
            this.environment.SparseDirectories.Add("D/");
            this.environment.ModifiedPaths.Add("D/d.txt");
            this.environment.DeletedFiles.Add("D/d.txt");

            this.Widen(FirstGit, "A/B/b.txt").ShouldBeTrue();
            this.ConeFile.ShouldEqual(ConeWithAB);

            this.Narrow(FirstGit).ShouldBeTrue();
            this.ConeFile.ShouldEqual(Header);
        }

        [TestCase]
        public void ModifiedPathWithItsOwnIndexEntryIsInTheCone()
        {
            this.environment.SparseDirectories.Add("D/");
            this.environment.ModifiedPaths.Add("C/c.txt");

            this.Widen(FirstGit, "A/B/b.txt").ShouldBeTrue();

            this.ConeFile.ShouldEqual(Header + "/A/\n!/A/*/\n/A/B/\n!/A/B/*/\n/C/\n!/C/*/\n");
        }

        [TestCase]
        public void NarrowAfterAFailedNarrowWritesTheConeFile()
        {
            this.Widen(FirstGit, "A/B/b.txt").ShouldBeTrue();
            this.environment.ConeFileLocked = true;
            this.Narrow(FirstGit).ShouldBeFalse();
            this.ConeFile.ShouldEqual(ConeWithAB);

            this.environment.ConeFileLocked = false;
            this.Narrow(SecondGit).ShouldBeTrue();

            this.ConeFile.ShouldEqual(Header);
        }

        [TestCase]
        public void ApplyFailureDeletesAConeFileThatDidNotExistBefore()
        {
            this.fileSystem.DeleteFile(SparseCheckoutPath);
            this.environment.ApplyExitCode = 128;

            this.Widen(FirstGit, "A/B/b.txt").ShouldBeFalse();

            this.fileSystem.FileExists(SparseCheckoutPath).ShouldBeFalse();
        }

        [TestCase]
        public void FailedRestoreKeepsTheBackupAndTheNextRequestWritesTheConeFile()
        {
            this.environment.ApplyExitCode = 128;
            this.environment.DuringApply = () => this.fileSystem.FailRenameTo = SparseCheckoutPath;

            this.Widen(FirstGit, "A/B/b.txt").ShouldBeFalse();

            this.LastEvent("ConeWiden").Metadata["Restored"].ShouldEqual(false);
            this.fileSystem.FileExists(BackupPath).ShouldBeTrue("The backup must stay when the restore fails");
            this.ConeFile.ShouldEqual(ConeWithAB);

            this.fileSystem.FailRenameTo = null;
            this.Narrow(SecondGit).ShouldBeTrue();
            this.ConeFile.ShouldEqual(Header);
        }

        [TestCase]
        public void ApplyFailureAfterASuccessfulWidenMakesTheNextWidenRunGit()
        {
            this.Widen(FirstGit, "A/B/b.txt").ShouldBeTrue();
            this.environment.ApplyExitCode = 128;
            this.Widen(SecondGit, "C/c.txt").ShouldBeFalse();
            this.ConeFile.ShouldEqual(ConeWithAB);

            // The cone file does not change, but Git may have changed the index before it failed.
            this.environment.ApplyExitCode = 0;
            this.Widen(FirstGit, "A/B/other.txt").ShouldBeTrue();

            this.environment.ConeFilesAtApply.Count.ShouldEqual(3);
        }

        [TestCase]
        public void WidenFromAProcessThatTheMountCannotOpenIsRefused()
        {
            this.environment.StartTimeResults[FirstGit] = ProcessStartTimeResult.Inaccessible;

            this.Widen(FirstGit, "A/B/b.txt").ShouldBeFalse();

            this.ConeFile.ShouldEqual(Header);
        }

        [TestCase]
        public void EntriesOfAProcessThatBecameInaccessibleAreRemoved()
        {
            this.Widen(FirstGit, "A/B/b.txt").ShouldBeTrue();
            this.environment.StartTimeResults[FirstGit] = ProcessStartTimeResult.Inaccessible;

            this.Narrow(SecondGit).ShouldBeTrue();

            this.ConeFile.ShouldEqual(Header);
        }

        [TestCase]
        public void EntriesOfAProcessWithAnIndeterminateStateAreKept()
        {
            this.Widen(FirstGit, "A/B/b.txt").ShouldBeTrue();
            this.environment.StartTimeResults[FirstGit] = ProcessStartTimeResult.Indeterminate;

            this.Narrow(SecondGit).ShouldBeTrue();

            this.ConeFile.ShouldEqual(ConeWithAB);
        }

        [TestCase]
        public void HandleRequestRepliesFailureForARefusedWiden()
        {
            this.environment.LockHolder = SecondGit;
            NamedPipeMessages.Message widen = new NamedPipeMessages.Message(
                NamedPipeMessages.ConeManagement.WidenRequest,
                new NamedPipeMessages.ConeManagement.WidenParameters(FirstGit.ToString(), new[] { "A/B/b.txt" }).ToBody());

            AutoSparseIndexConeManager.HandleRequest(this.manager, this.tracer, widen).ShouldEqual(NamedPipeMessages.ConeManagement.FailureResult);
        }

        [TestCase]
        public void ConcurrentWidensAndNarrowsEndWithTheConeOfTheModifiedPaths()
        {
            const int CommandCount = 16;
            for (int i = 0; i < CommandCount; i++)
            {
                this.environment.RunningProcesses[1000 + i] = i;
            }

            Parallel.For(0, CommandCount, i =>
            {
                int processId = 1000 + i;
                this.Widen(processId, $"Dir{i % 4}/file{i}.txt").ShouldBeTrue();
                this.Narrow(processId).ShouldBeTrue();
            });

            this.ConeFile.ShouldEqual(Header);
        }

        [TestCase]
        public void HandleRequestRepliesNotEnabledWhenTheFeatureIsOff()
        {
            NamedPipeMessages.Message widen = new NamedPipeMessages.Message(
                NamedPipeMessages.ConeManagement.WidenRequest,
                new NamedPipeMessages.ConeManagement.WidenParameters(FirstGit.ToString(), new[] { "A/B/b.txt" }).ToBody());

            AutoSparseIndexConeManager.HandleRequest(null, this.tracer, widen).ShouldEqual(NamedPipeMessages.ConeManagement.NotEnabledResult);
            AutoSparseIndexConeManager.HandleRequest(null, this.tracer, new NamedPipeMessages.Message(NamedPipeMessages.ConeManagement.NarrowRequest, FirstGit.ToString()))
                .ShouldEqual(NamedPipeMessages.ConeManagement.NotEnabledResult);

            this.ConeFile.ShouldEqual(Header);
        }

        [TestCase]
        public void HandleRequestAppliesValidRequests()
        {
            NamedPipeMessages.Message widen = new NamedPipeMessages.Message(
                NamedPipeMessages.ConeManagement.WidenRequest,
                new NamedPipeMessages.ConeManagement.WidenParameters(FirstGit.ToString(), new[] { "A/B/b.txt" }).ToBody());

            AutoSparseIndexConeManager.HandleRequest(this.manager, this.tracer, widen).ShouldEqual(NamedPipeMessages.ConeManagement.SuccessResult);
            this.ConeFile.ShouldEqual(ConeWithAB);

            AutoSparseIndexConeManager.HandleRequest(this.manager, this.tracer, new NamedPipeMessages.Message(NamedPipeMessages.ConeManagement.NarrowRequest, FirstGit.ToString()))
                .ShouldEqual(NamedPipeMessages.ConeManagement.SuccessResult);
            this.ConeFile.ShouldEqual(Header);
        }

        [TestCase("100\0C:\\outside.txt")]
        [TestCase("100\0/abs/path.txt")]
        [TestCase("100\0A/../../outside.txt")]
        [TestCase("not-a-pid\0A/a.txt")]
        [TestCase("100")]
        public void HandleRequestRepliesFailureForAWidenBodyThatIsNotValid(string body)
        {
            AutoSparseIndexConeManager.HandleRequest(this.manager, this.tracer, new NamedPipeMessages.Message(NamedPipeMessages.ConeManagement.WidenRequest, body))
                .ShouldEqual(NamedPipeMessages.ConeManagement.FailureResult);

            this.ConeFile.ShouldEqual(Header);
            this.environment.ConeFilesAtApply.ShouldBeEmpty();
            this.tracer.RelatedWarningEvents.ShouldContain(message => message.Contains("not valid"));
        }

        [TestCase]
        public void HandleRequestRepliesFailureForANarrowBodyThatIsNotValid()
        {
            AutoSparseIndexConeManager.HandleRequest(this.manager, this.tracer, new NamedPipeMessages.Message(NamedPipeMessages.ConeManagement.NarrowRequest, "12345678901"))
                .ShouldEqual(NamedPipeMessages.ConeManagement.FailureResult);
        }

        [TestCase]
        public void StartMakesTheFirstCommandApplyTheConeOfTheModifiedPaths()
        {
            this.environment.ModifiedPaths.Add("C/c.txt");

            this.manager.Start();
            this.environment.IsPending.ShouldBeTrue();

            this.Apply(FirstGit).ShouldBeTrue();

            this.ConeFile.ShouldEqual(ConeWithC);
            this.environment.ConeFilesAtApply.ShouldMatchInOrder(ConeWithC);
            this.environment.IsPending.ShouldBeFalse();
            this.LastEvent("ConeApply").Level.ShouldEqual(EventLevel.Informational);
        }

        [TestCase]
        public void ChangeOutsideTheAppliedConeIsPendingUntilTheNextApply()
        {
            this.Apply(FirstGit).ShouldBeTrue();
            this.environment.IsPending.ShouldBeFalse();

            // The callback runs before the mount adds the path to the modified paths.
            this.manager.OnWorkingDirectoryPathChanged(@"C\c.txt", isFolder: false);
            this.environment.ModifiedPaths.Add("C/c.txt");
            this.environment.IsPending.ShouldBeTrue();

            this.Apply(FirstGit).ShouldBeTrue();

            this.ConeFile.ShouldEqual(ConeWithC);
            this.environment.ConeFilesAtApply.Last().ShouldEqual(ConeWithC);
            this.environment.IsPending.ShouldBeFalse();
        }

        [TestCase]
        public void ChangeInsideTheAppliedConeIsNotPending()
        {
            this.environment.ModifiedPaths.Add("C/c.txt");
            this.Apply(FirstGit).ShouldBeTrue();

            this.manager.OnWorkingDirectoryPathChanged(@"C\other.txt", isFolder: false);
            this.manager.OnWorkingDirectoryPathChanged("root.txt", isFolder: false);

            this.environment.IsPending.ShouldBeFalse();
        }

        [TestCase]
        public void FolderChangeIsPendingUntilTheConeIncludesItsWholeSubtree()
        {
            this.environment.ModifiedPaths.Add("C/c.txt");
            this.Apply(FirstGit).ShouldBeTrue();

            this.manager.OnWorkingDirectoryPathChanged(@"C\new", isFolder: true);
            this.environment.ModifiedPaths.Add("C/new/");
            this.environment.IsPending.ShouldBeTrue();

            this.Apply(FirstGit).ShouldBeTrue();
            this.ConeFile.ShouldEqual(ConeWithC + "/C/new/\n");
        }

        [TestCase]
        public void WidenAlsoAppliesThePendingChanges()
        {
            this.Apply(FirstGit).ShouldBeTrue();
            this.manager.OnWorkingDirectoryPathChanged(@"C\c.txt", isFolder: false);
            this.environment.ModifiedPaths.Add("C/c.txt");

            this.Widen(FirstGit, "A/B/b.txt").ShouldBeTrue();

            this.ConeFile.ShouldEqual(Header + "/A/\n!/A/*/\n/A/B/\n!/A/B/*/\n/C/\n!/C/*/\n");
            this.environment.IsPending.ShouldBeFalse();
        }

        [TestCase]
        public void ApplyForACommandThatDoesNotHoldTheGVFSLockKeepsTheChangePending()
        {
            this.manager.Start();
            this.environment.LockHolder = SecondGit;

            this.Apply(FirstGit).ShouldBeFalse();

            this.environment.IsPending.ShouldBeTrue();
            this.environment.ConeFilesAtApply.ShouldBeEmpty();
        }

        [TestCase]
        public void FailedApplyKeepsTheChangePendingUntilAnApplySucceeds()
        {
            this.manager.Start();
            this.environment.ApplyExitCode = 1;

            for (int attempt = 0; attempt < 5; attempt++)
            {
                this.Apply(FirstGit).ShouldBeFalse();
                this.environment.IsPending.ShouldBeTrue("Git has no fallback for a change that the index does not show");
            }

            this.environment.ApplyExitCode = 0;
            this.Apply(FirstGit).ShouldBeTrue();
            this.environment.IsPending.ShouldBeFalse();
        }

        [TestCase]
        public void ApplyIncludesAChangeBeforeTheModifiedPathsHaveIt()
        {
            this.Apply(FirstGit).ShouldBeTrue();

            // The background task that adds the modified path waits while Git holds the lock.
            this.manager.OnWorkingDirectoryPathChanged(@"C\c.txt", isFolder: false);
            this.Apply(FirstGit).ShouldBeTrue();
            this.ConeFile.ShouldEqual(ConeWithC);

            // The cone keeps the path until the modified paths have it.
            this.Widen(SecondGit, "A/B/b.txt").ShouldBeTrue();
            this.Narrow(SecondGit).ShouldBeTrue();
            this.ConeFile.ShouldEqual(ConeWithC);

            this.environment.ModifiedPaths.Add("C/c.txt");
            this.environment.DifferingPaths.Add("C/c.txt");
            this.manager.TryReconcile().ShouldBeTrue();
            this.ConeFile.ShouldEqual(ConeWithC);
        }

        [TestCase]
        public void ChangedPathThatIsNotAModifiedPathLeavesTheConeWhenItIsClean()
        {
            this.Apply(FirstGit).ShouldBeTrue();

            // For example, Git wrote the file with the content of HEAD.
            this.manager.OnWorkingDirectoryPathChanged(@"C\c.txt", isFolder: false);
            this.Apply(FirstGit).ShouldBeTrue();
            this.ConeFile.ShouldEqual(ConeWithC);

            this.manager.TryReconcile().ShouldBeTrue();

            this.ConeFile.ShouldEqual(Header);
        }

        [TestCase]
        public void PendingFileThatCannotBeDeletedIsDeletedByTheNextApply()
        {
            this.manager.Start();
            this.environment.PendingFileDeleteFails = true;

            this.Apply(FirstGit).ShouldBeTrue();
            this.environment.IsPending.ShouldBeTrue();

            this.environment.PendingFileDeleteFails = false;
            this.Apply(FirstGit).ShouldBeTrue();
            this.environment.IsPending.ShouldBeFalse();
        }

        [TestCase]
        public void PendingFileThatCannotBeCreatedIsCreatedByTheNextChange()
        {
            this.Apply(FirstGit).ShouldBeTrue();
            this.environment.PendingFileFails = true;

            this.manager.OnWorkingDirectoryPathChanged(@"C\c.txt", isFolder: false);
            this.environment.IsPending.ShouldBeFalse();

            this.environment.PendingFileFails = false;
            this.manager.OnWorkingDirectoryPathChanged(@"C\c.txt", isFolder: false);
            this.environment.IsPending.ShouldBeTrue();
        }

        [TestCase]
        public void ReconcileRemovesAModifiedFileThatMatchesHeadWithoutRunningGitToApply()
        {
            this.environment.ModifiedPaths.Add("C/c.txt");
            this.Apply(FirstGit).ShouldBeTrue();

            this.manager.TryReconcile().ShouldBeTrue();

            this.ConeFile.ShouldEqual(Header);
            this.environment.DiffCount.ShouldEqual(1);
            this.environment.ConeFilesAtApply.Count.ShouldEqual(1, "Only Git can apply a cone");
            this.environment.IsPending.ShouldBeTrue("The next apply returns the clean files to placeholders");
            this.environment.DehydrateCalls.ShouldBeEmpty();
            // The root file .gitattributes is clean too.
            this.LastEvent("ConeReconcile").Metadata["NewlyCleanCount"].ShouldEqual(2);
        }

        [TestCase]
        public void ReconcileKeepsFilesThatDifferFromHead()
        {
            // C differs, D is untracked, E is a deleted tracked file, and F was created and
            // deleted again.
            this.environment.ModifiedPaths.AddRange(new[] { "C/c.txt", "D/d.txt", "E/e.txt", "F/f.txt" });
            this.environment.DifferingPaths.Add("C/c.txt");
            this.environment.UntrackedFiles.Add("D/d.txt");
            this.environment.DeletedFiles.Add("E/e.txt");
            this.environment.DeletedFiles.Add("F/f.txt");
            this.environment.UntrackedFiles.Add("F/f.txt");
            this.environment.SparsityChangeIsIndexOnly = true;
            this.Apply(FirstGit).ShouldBeTrue();

            this.manager.TryReconcile().ShouldBeTrue();

            this.ConeFile.ShouldEqual(Header + "/C/\n!/C/*/\n/D/\n!/D/*/\n/E/\n!/E/*/\n");
        }

        [TestCase]
        public void ReconcileKeepsAFileInAnUntrackedFolderAndRemovesAnIgnoredFile()
        {
            // Git lists the untracked folder A/B/, not the file in it. Git does not list the
            // ignored file C/c.log.
            this.environment.ModifiedPaths.AddRange(new[] { "A/B/new.txt", "C/c.log" });
            this.environment.DifferingPaths.Add("A/B/");
            this.Apply(FirstGit).ShouldBeTrue();

            this.manager.TryReconcile().ShouldBeTrue();

            this.ConeFile.ShouldEqual(ConeWithAB);
        }

        [TestCase]
        public void ReconcileUsesTheRepositoryCaseRule()
        {
            this.manager = this.CreateManager(ignoreCase: true, changeCheckDelayMs: Timeout.Infinite);
            this.environment.ModifiedPaths.Add("C/c.txt");
            this.environment.DifferingPaths.Add("c/C.TXT");
            this.Apply(FirstGit).ShouldBeTrue();

            this.manager.TryReconcile().ShouldBeTrue();

            this.ConeFile.ShouldEqual(ConeWithC);
        }

        [TestCase]
        public void CleanFileThatChangesAgainIsPendingAndTheApplyWidensTheCone()
        {
            this.environment.ModifiedPaths.Add("C/c.txt");
            this.Apply(FirstGit).ShouldBeTrue();
            this.manager.TryReconcile().ShouldBeTrue();
            this.ConeFile.ShouldEqual(Header);

            this.manager.OnWorkingDirectoryPathChanged(@"C\c.txt", isFolder: false);
            this.environment.IsPending.ShouldBeTrue();
            this.Apply(FirstGit).ShouldBeTrue();

            this.ConeFile.ShouldEqual(ConeWithC);
            this.environment.ConeFilesAtApply.Last().ShouldEqual(ConeWithC);
        }

        [TestCase]
        public void CleanFileThatDiffersAfterAHeadChangeIsPending()
        {
            this.environment.ModifiedPaths.Add("C/c.txt");
            this.Apply(FirstGit).ShouldBeTrue();
            this.manager.TryReconcile().ShouldBeTrue();
            this.ConeFile.ShouldEqual(Header);

            // For example "git reset --soft HEAD~1" after a commit of the file. The check made a
            // dehydrate pending, so the check does not run until an apply.
            this.environment.DifferingPaths.Add("C/c.txt");
            this.environment.RepositoryStamp++;
            this.manager.TryReconcile().ShouldBeTrue();
            this.Apply(FirstGit).ShouldBeTrue();
            this.environment.DehydrateCalls.ShouldBeEmpty("The blobs are from before the HEAD change");

            this.manager.TryReconcile().ShouldBeTrue();
            this.environment.IsPending.ShouldBeTrue();
            this.Apply(FirstGit).ShouldBeTrue();
            this.ConeFile.ShouldEqual(ConeWithC);
        }

        [TestCase]
        public void ReconcileDoesNotRunGitWhileAChangeIsPendingOrWhenNothingChanged()
        {
            this.environment.ModifiedPaths.Add("C/c.txt");
            this.manager.Start();

            this.manager.TryReconcile().ShouldBeTrue();
            this.environment.DiffCount.ShouldEqual(0, "A pending change can be in a directory that the index does not expand");

            this.Apply(FirstGit).ShouldBeTrue();
            this.environment.DifferingPaths.Add("C/c.txt");
            this.manager.TryReconcile().ShouldBeTrue();
            this.manager.TryReconcile().ShouldBeTrue();
            this.environment.DiffCount.ShouldEqual(1);
        }

        [TestCase]
        public void ChangeDuringTheCheckKeepsTheFileInTheCone()
        {
            this.environment.ModifiedPaths.Add("C/c.txt");
            this.Apply(FirstGit).ShouldBeTrue();
            this.environment.DuringDiff = () => this.manager.OnWorkingDirectoryPathChanged(@"C\c.txt", isFolder: false);

            this.manager.TryReconcile().ShouldBeTrue();

            this.ConeFile.ShouldEqual(ConeWithC);
        }

        [TestCase]
        public void ChangeInsideTheAppliedConeStartsACheckWhenTheChangesStop()
        {
            this.manager.Stop();
            this.manager = this.CreateManager(ignoreCase: false, changeCheckDelayMs: 10);
            this.environment.ModifiedPaths.Add("C/c.txt");
            this.Apply(FirstGit).ShouldBeTrue();

            // For example, an editor wrote the content of HEAD back.
            this.manager.OnWorkingDirectoryPathChanged(@"C\c.txt", isFolder: false);

            SpinWait.SpinUntil(() => this.tracer.SnapshotRelatedEvents().Any(recorded => recorded.Name == "ConeReconcile"), TimeSpan.FromSeconds(10))
                .ShouldBeTrue("The check did not run");
            this.ConeFile.ShouldEqual(Header);
            this.environment.IsPending.ShouldBeTrue("The next apply returns the clean file to a placeholder");
        }

        [TestCase]
        public void CheckThatCannotWriteTheConeFileTriesAgainWithoutGit()
        {
            this.manager = this.CreateManager(ignoreCase: false, changeCheckDelayMs: 200);
            this.environment.ModifiedPaths.Add("C/c.txt");
            this.Apply(FirstGit).ShouldBeTrue();
            this.environment.ConeFileLocked = true;

            this.manager.TryReconcile().ShouldBeFalse();
            this.ConeFile.ShouldEqual(ConeWithC);
            this.environment.ConeFileLocked = false;

            SpinWait.SpinUntil(() => this.ConeFile == Header, TimeSpan.FromSeconds(10)).ShouldBeTrue("The check did not try again");
            this.environment.DiffCount.ShouldEqual(1);
        }

        [TestCase]
        public void ApplyRunsGitForANewModifiedPathThatACheckWroteToTheConeFile()
        {
            const string ConeWithD = Header + "/D/\n!/D/*/\n";
            this.environment.ModifiedPaths.Add("C/c.txt");
            this.Apply(FirstGit).ShouldBeTrue();
            this.environment.DuringDiff = () =>
            {
                this.environment.ModifiedPaths.Add("D/d.txt");
                this.manager.OnWorkingDirectoryPathChanged(@"D\d.txt", isFolder: false);
            };

            // The check writes D into the cone file, but the index does not expand D yet.
            this.manager.TryReconcile().ShouldBeTrue();
            this.ConeFile.ShouldEqual(ConeWithD);

            this.Apply(FirstGit).ShouldBeTrue();
            this.environment.ConeFilesAtApply.Last().ShouldEqual(ConeWithD);
        }

        [TestCase]
        public void ApplyDuringTheCheckDiscardsTheResult()
        {
            this.environment.ModifiedPaths.Add("C/c.txt");
            this.Apply(FirstGit).ShouldBeTrue();
            this.environment.DuringDiff = () => this.Apply(FirstGit).ShouldBeTrue();

            this.manager.TryReconcile().ShouldBeTrue();

            this.ConeFile.ShouldEqual(ConeWithC);
        }

        [TestCase]
        public void ReconcileFailureKeepsTheCone()
        {
            this.environment.ModifiedPaths.Add("C/c.txt");
            this.Apply(FirstGit).ShouldBeTrue();
            this.environment.DiffFails = true;

            this.manager.TryReconcile().ShouldBeFalse();

            this.ConeFile.ShouldEqual(ConeWithC);
            this.LastEvent("ConeReconcile").Level.ShouldEqual(EventLevel.Warning);
        }

        [TestCase]
        public void NarrowDoesNotPutACleanFileBackInTheCone()
        {
            this.environment.ModifiedPaths.Add("C/c.txt");
            this.Apply(FirstGit).ShouldBeTrue();
            this.manager.TryReconcile().ShouldBeTrue();

            this.Widen(SecondGit, "A/B/b.txt").ShouldBeTrue();
            this.Narrow(SecondGit).ShouldBeTrue();

            this.ConeFile.ShouldEqual(Header);
        }

        [TestCase]
        public void WhenGitChangesOnlyTheIndexADeletedFileInASparseDirectoryIsInTheCone()
        {
            this.environment.SparsityChangeIsIndexOnly = true;
            this.environment.SparseDirectories.Add("A/");
            this.environment.ModifiedPaths.Add("A/B/deleted.txt");
            this.environment.DeletedFiles.Add("A/B/deleted.txt");

            this.Widen(FirstGit, "A/B/b.txt").ShouldBeTrue();

            this.ConeFile.ShouldEqual(ConeWithAB);
            this.environment.ConeFilesAtApply.ShouldMatchInOrder(ConeWithAB);
        }

        [TestCase]
        public void DeletedFileInTheAppliedConeDoesNotNeedTheSparseDirectories()
        {
            this.environment.ModifiedPaths.Add("C/c.txt");
            this.Apply(FirstGit).ShouldBeTrue();
            this.environment.SparseDirectoriesFail = true;
            this.environment.ModifiedPaths.Add("C/deleted.txt");
            this.environment.DeletedFiles.Add("C/deleted.txt");

            this.Widen(FirstGit, "A/B/b.txt").ShouldBeTrue();

            this.ConeFile.ShouldEqual(Header + "/A/\n!/A/*/\n/A/B/\n!/A/B/*/\n/C/\n!/C/*/\n");
        }

        [TestCase]
        public void HandleRequestAppliesAnApplyRequest()
        {
            this.manager.Start();

            AutoSparseIndexConeManager.HandleRequest(this.manager, this.tracer, new NamedPipeMessages.Message(NamedPipeMessages.ConeManagement.ApplyRequest, FirstGit.ToString()))
                .ShouldEqual(NamedPipeMessages.ConeManagement.SuccessResult);
            AutoSparseIndexConeManager.HandleRequest(this.manager, this.tracer, new NamedPipeMessages.Message(NamedPipeMessages.ConeManagement.ApplyRequest, "x"))
                .ShouldEqual(NamedPipeMessages.ConeManagement.FailureResult);
            AutoSparseIndexConeManager.HandleRequest(null, this.tracer, new NamedPipeMessages.Message(NamedPipeMessages.ConeManagement.ApplyRequest, FirstGit.ToString()))
                .ShouldEqual(NamedPipeMessages.ConeManagement.NotEnabledResult);
            this.environment.IsPending.ShouldBeFalse();
        }

        [TestCase]
        public void ApplyAfterACheckDehydratesTheCleanFilesAndRunsGitWhenTheConeDoesNotChange()
        {
            this.environment.ModifiedPaths.Add("C/c.txt");
            this.Apply(FirstGit).ShouldBeTrue();
            this.manager.TryReconcile().ShouldBeTrue();
            this.ConeFile.ShouldEqual(Header);

            this.Apply(FirstGit).ShouldBeTrue();

            this.environment.DehydrateCalls.ShouldMatchInOrder(".gitattributes", "C/c.txt");
            this.environment.ModifiedPaths.ShouldBeEmpty();
            this.environment.ConeFilesAtApply.ShouldMatchInOrder(ConeWithC, Header);
            this.environment.ProjectionInvalidations.ShouldEqual(1);
            this.environment.RestoredPaths.ShouldBeEmpty();
            this.environment.IsPending.ShouldBeFalse();
            this.LastEvent("ConeApply").Metadata["DehydratedCount"].ShouldEqual(2);
        }

        [TestCase]
        public void ApplyDoesNotRebuildTheProjectionWhenItStillHasTheDehydratedFiles()
        {
            // No command rebuilt the projection while the files were modified paths.
            this.environment.ModifiedPaths.Add("C/c.txt");
            this.environment.ProjectedFiles.UnionWith(new[] { ".gitattributes", "C/c.txt" });
            this.Apply(FirstGit).ShouldBeTrue();
            this.manager.TryReconcile().ShouldBeTrue();

            this.Apply(FirstGit).ShouldBeTrue();

            this.environment.ModifiedPaths.ShouldBeEmpty();
            this.environment.ProjectionInvalidations.ShouldEqual(0);
            this.LastEvent("ConeApply").Metadata["ProjectionInvalidated"].ShouldEqual(false);
        }

        [TestCase]
        public void ApplyRebuildsTheProjectionOnceWhenItDoesNotHaveADehydratedFile()
        {
            this.environment.ModifiedPaths.AddRange(new[] { "C/c.txt", "D/d.txt" });
            this.environment.ProjectedFiles.UnionWith(new[] { ".gitattributes", "C/c.txt" });
            this.Apply(FirstGit).ShouldBeTrue();
            this.manager.TryReconcile().ShouldBeTrue();

            this.Apply(FirstGit).ShouldBeTrue();

            this.environment.ProjectionInvalidations.ShouldEqual(1);
        }

        [TestCase]
        public void ApplyDoesNotDehydrateAFileThatNoCheckFoundClean()
        {
            this.environment.ModifiedPaths.Add("C/c.txt");
            this.Apply(FirstGit).ShouldBeTrue();
            this.Apply(FirstGit).ShouldBeTrue();

            this.environment.BlobRequests.ShouldBeEmpty();
            this.environment.DehydrateCalls.ShouldBeEmpty();
        }

        [TestCase]
        public void WidenDoesNotDehydrateAndKeepsTheDehydratePending()
        {
            this.environment.ModifiedPaths.Add("C/c.txt");
            this.Apply(FirstGit).ShouldBeTrue();
            this.manager.TryReconcile().ShouldBeTrue();

            this.Widen(SecondGit, "A/B/b.txt").ShouldBeTrue();

            this.environment.DehydrateCalls.ShouldBeEmpty();
            this.environment.IsPending.ShouldBeTrue("Only an apply dehydrates");

            this.Apply(SecondGit).ShouldBeTrue();
            this.environment.DehydrateCalls.ShouldMatchInOrder(".gitattributes", "C/c.txt");
        }

        [TestCase]
        public void FileThatChangedAfterTheCheckIsNotDehydrated()
        {
            this.environment.ModifiedPaths.Add("C/c.txt");
            this.Apply(FirstGit).ShouldBeTrue();
            this.manager.TryReconcile().ShouldBeTrue();

            this.manager.OnWorkingDirectoryPathChanged(@"C\c.txt", isFolder: false);
            this.Apply(FirstGit).ShouldBeTrue();

            this.environment.DehydrateCalls.ShouldMatchInOrder(".gitattributes");
            this.environment.ModifiedPaths.ShouldContain(path => path == "C/c.txt");
            this.ConeFile.ShouldEqual(ConeWithC);
        }

        [TestCase]
        public void FileThatIsNotInTheIndexIsNeverDehydrated()
        {
            // For example, a created file that Git ignores: Git does not report it.
            this.environment.ModifiedPaths.Add("D/ignored.txt");
            this.environment.NotInIndex.Add("D/ignored.txt");
            this.Apply(FirstGit).ShouldBeTrue();
            this.manager.TryReconcile().ShouldBeTrue();

            this.Apply(FirstGit).ShouldBeTrue();
            this.Apply(FirstGit).ShouldBeTrue();

            this.environment.DehydrateCalls.ShouldMatchInOrder(".gitattributes");
            this.environment.BlobRequests.Count.ShouldEqual(1, "A file that is not in the index is not tried again until it changes");
            this.environment.ModifiedPaths.ShouldContain(path => path == "D/ignored.txt");
            this.ConeFile.ShouldEqual(Header);
        }

        [TestCase]
        public void FileInAnUntrackedFolderIsNotDehydrated()
        {
            this.environment.ModifiedPaths.AddRange(new[] { "D/", "D/d.txt" });
            this.environment.DifferingPaths.Add("D/");
            this.Apply(FirstGit).ShouldBeTrue();

            this.manager.TryReconcile().ShouldBeTrue();

            this.ConeFile.ShouldEqual(Header + "/D/\n");
            this.LastEvent("ConeReconcile").Metadata["NewlyCleanCount"].ShouldEqual(1, "Only .gitattributes is clean");
            this.Apply(FirstGit).ShouldBeTrue();
            this.environment.DehydrateCalls.ShouldMatchInOrder(".gitattributes");
        }

        [TestCase]
        public void FolderIsNeverDehydrated()
        {
            this.environment.ModifiedPaths.Add("D/");
            this.Apply(FirstGit).ShouldBeTrue();
            this.manager.TryReconcile().ShouldBeTrue();

            this.Apply(FirstGit).ShouldBeTrue();

            this.environment.BlobRequests.Single().ShouldMatchInOrder(".gitattributes");
            this.environment.ModifiedPaths.ShouldContain(path => path == "D/");
        }

        [TestCase]
        public void DehydrateWaitsUntilAMergeEnds()
        {
            this.environment.ModifiedPaths.Add("C/c.txt");
            this.Apply(FirstGit).ShouldBeTrue();
            this.environment.DehydrateBlocker = "MERGE_HEAD";
            this.manager.TryReconcile().ShouldBeTrue();
            this.environment.IsPending.ShouldBeFalse("A blocked dehydrate does not make the next command apply");

            // Another change makes an apply run during the merge.
            this.environment.ModifiedPaths.Add("D/d.txt");
            this.manager.OnWorkingDirectoryPathChanged(@"D\d.txt", isFolder: false);
            this.Apply(FirstGit).ShouldBeTrue();
            this.environment.BlobRequests.ShouldBeEmpty();
            this.environment.DehydrateCalls.ShouldBeEmpty();
            this.LastEvent("ConeApply").Metadata["DehydrateBlocker"].ShouldEqual("MERGE_HEAD");
            this.environment.IsPending.ShouldBeFalse();

            // The merge commit writes the index, so the next check runs.
            this.environment.DehydrateBlocker = null;
            this.environment.RepositoryStamp++;
            this.manager.TryReconcile().ShouldBeTrue();
            this.environment.IsPending.ShouldBeTrue();

            this.Apply(FirstGit).ShouldBeTrue();
            this.environment.DehydrateCalls.ShouldMatchInOrder(".gitattributes", "C/c.txt", "D/d.txt");
        }

        [TestCase(CleanFileDehydrateResult.InUse)]
        [TestCase(CleanFileDehydrateResult.Replaced)]
        [TestCase(CleanFileDehydrateResult.PlaceholderUpdateFailed)]
        public void FileThatFailsForAPassingReasonWaitsAndDoesNotBlockOtherFiles(CleanFileDehydrateResult failure)
        {
            this.environment.ModifiedPaths.Add("C/c.txt");
            this.environment.DehydrateResults["C/c.txt"] = failure;
            this.Apply(FirstGit).ShouldBeTrue();
            this.manager.TryReconcile().ShouldBeTrue();

            this.Apply(FirstGit).ShouldBeTrue();
            this.environment.ModifiedPaths.ShouldContain(path => path == "C/c.txt");
            this.LastEvent("ConeApply").Metadata["DehydrateSkipped"].ShouldEqual(failure + "=1");
            this.environment.IsPending.ShouldBeFalse("The retry is not due yet");

            // Another clean file is dehydrated, and the waiting file is not looked up again.
            this.environment.ModifiedPaths.Add("D/d.txt");
            this.manager.OnWorkingDirectoryPathChanged(@"D\d.txt", isFolder: false);
            this.Apply(FirstGit).ShouldBeTrue();
            this.manager.TryReconcile().ShouldBeTrue();
            this.Apply(FirstGit).ShouldBeTrue();

            this.environment.DehydrateCalls.ShouldMatchInOrder(".gitattributes", "C/c.txt", "D/d.txt");
            this.environment.BlobRequests.Last().ShouldMatchInOrder("D/d.txt");
            this.environment.ModifiedPaths.ShouldMatchInOrder("C/c.txt");
        }

        [TestCase]
        public void FileInUseIsTriedAgainWhenItsWaitEnds()
        {
            this.manager.Stop();
            this.manager = this.CreateManager(ignoreCase: false, changeCheckDelayMs: Timeout.Infinite, dehydrateRetryDelayMs: 1);
            this.environment.ModifiedPaths.Add("C/c.txt");
            this.environment.DehydrateResults["C/c.txt"] = CleanFileDehydrateResult.InUse;
            this.Apply(FirstGit).ShouldBeTrue();
            this.manager.TryReconcile().ShouldBeTrue();
            this.Apply(FirstGit).ShouldBeTrue();

            // The timer makes the next Git command apply.
            SpinWait.SpinUntil(() => this.environment.IsPending, TimeSpan.FromSeconds(10)).ShouldBeTrue();

            this.environment.DehydrateResults.Remove("C/c.txt");
            this.Apply(FirstGit).ShouldBeTrue();
            this.environment.DehydrateCalls.ShouldMatchInOrder(".gitattributes", "C/c.txt", "C/c.txt");
            this.environment.ModifiedPaths.ShouldBeEmpty();
        }

        [TestCase(CleanFileDehydrateResult.InUse)]
        [TestCase(CleanFileDehydrateResult.ContentDiffers)]
        public void FileThatAnAttemptKeptIsTriedAgainAfterAChangeThatTheCheckSaw(CleanFileDehydrateResult failure)
        {
            // D/d.txt differs from HEAD, so the cone includes D/ and a change to D/c.txt is
            // inside the applied cone. Only the check sees such a change.
            this.environment.ModifiedPaths.AddRange(new[] { "D/c.txt", "D/d.txt" });
            this.environment.DifferingPaths.Add("D/d.txt");
            this.environment.DehydrateResults["D/c.txt"] = failure;
            this.Apply(FirstGit).ShouldBeTrue();
            this.manager.TryReconcile().ShouldBeTrue();
            this.Apply(FirstGit).ShouldBeTrue();
            this.environment.IsPending.ShouldBeFalse();

            this.environment.DehydrateResults.Remove("D/c.txt");
            this.manager.OnWorkingDirectoryPathChanged(@"D\c.txt", isFolder: false);
            this.environment.IsPending.ShouldBeFalse("The applied cone includes the change");
            this.manager.TryReconcile().ShouldBeTrue();
            this.environment.IsPending.ShouldBeTrue();

            this.Apply(FirstGit).ShouldBeTrue();
            this.environment.DehydrateCalls.Count(path => path == "D/c.txt").ShouldEqual(2);
            this.environment.ModifiedPaths.ShouldNotContain(path => path == "D/c.txt");
        }

        [TestCase]
        public void FileWithOtherBytesIsNotTriedAgainUntilItChanges()
        {
            this.environment.ModifiedPaths.Add("C/c.txt");
            this.environment.DehydrateResults["C/c.txt"] = CleanFileDehydrateResult.ContentDiffers;
            this.Apply(FirstGit).ShouldBeTrue();
            this.manager.TryReconcile().ShouldBeTrue();

            this.Apply(FirstGit).ShouldBeTrue();
            this.Apply(FirstGit).ShouldBeTrue();

            this.environment.DehydrateCalls.Count(path => path == "C/c.txt").ShouldEqual(1);
        }

        [TestCase]
        public void LargeFileIsNotDehydrated()
        {
            this.environment.ModifiedPaths.Add("C/c.txt");
            this.environment.BlobSizes["C/c.txt"] = AutoSparseIndexConeManager.MaxDehydrateFileSize + 1;
            this.Apply(FirstGit).ShouldBeTrue();
            this.manager.TryReconcile().ShouldBeTrue();
            this.LastEvent("ConeReconcile").Metadata["DehydrateSkipped"].ShouldEqual("TooLarge=1");

            this.Apply(FirstGit).ShouldBeTrue();

            this.environment.DehydrateCalls.ShouldMatchInOrder(".gitattributes");
        }

        [TestCase]
        public void ApplyFailureMakesTheDehydratedFilesModifiedPathsAgain()
        {
            this.environment.ModifiedPaths.Add("C/c.txt");
            this.Apply(FirstGit).ShouldBeTrue();
            this.manager.TryReconcile().ShouldBeTrue();

            this.environment.ApplyExitCode = 1;
            this.Apply(FirstGit).ShouldBeFalse();

            this.environment.RestoredPaths.ShouldMatchInOrder(".gitattributes", "C/c.txt");
            this.environment.ModifiedPaths.ShouldContain(path => path == "C/c.txt");
            this.environment.ProjectionInvalidations.ShouldEqual(0);
            this.environment.IsPending.ShouldBeTrue();

            // They are still clean, so the next apply dehydrates them.
            this.environment.ApplyExitCode = 0;
            this.Apply(FirstGit).ShouldBeTrue();
            this.environment.DehydrateCalls.Count(path => path == "C/c.txt").ShouldEqual(2);
            this.environment.ModifiedPaths.ShouldNotContain(path => path == "C/c.txt");
        }

        [TestCase]
        public void ApplyExceptionMakesTheDehydratedFilesModifiedPathsAgain()
        {
            this.environment.ModifiedPaths.Add("C/c.txt");
            this.Apply(FirstGit).ShouldBeTrue();
            this.manager.TryReconcile().ShouldBeTrue();

            this.environment.ApplyException = new InvalidOperationException("Injected");
            this.Apply(FirstGit).ShouldBeFalse();

            this.environment.RestoredPaths.ShouldMatchInOrder(".gitattributes", "C/c.txt");
        }

        [TestCase]
        public void CheckReadsTheBlobsAndTheApplyDoesNotRunTheLookup()
        {
            this.environment.ModifiedPaths.Add("C/c.txt");
            this.Apply(FirstGit).ShouldBeTrue();

            this.manager.TryReconcile().ShouldBeTrue();
            this.environment.BlobRequests.Single().ShouldMatchInOrder(".gitattributes", "C/c.txt");
            this.environment.BlobTimeouts.Single().ShouldEqual(AutoSparseIndexConeManager.DehydrateLookupTimeoutMs);
            this.LastEvent("ConeReconcile").Metadata["RecordedBlobCount"].ShouldEqual(2);
            this.environment.IsPending.ShouldBeTrue();

            this.Apply(FirstGit).ShouldBeTrue();
            this.environment.BlobRequests.Count.ShouldEqual(1);
            this.environment.DehydrateCalls.ShouldMatchInOrder(".gitattributes", "C/c.txt");
        }

        [TestCase]
        public void ApplyAfterTheStampChangedDoesNotDehydrateAndTheNextCheckReadsTheBlobsAgain()
        {
            this.environment.ModifiedPaths.Add("C/c.txt");
            this.Apply(FirstGit).ShouldBeTrue();
            this.manager.TryReconcile().ShouldBeTrue();

            // For example, a Git command wrote the index after the check.
            this.environment.RepositoryStamp++;
            this.Apply(FirstGit).ShouldBeTrue();

            this.environment.DehydrateCalls.ShouldBeEmpty();
            this.LastEvent("ConeApply").Metadata["DehydrateStaleCount"].ShouldEqual(2);
            this.environment.IsPending.ShouldBeFalse("Only a check can read the blobs");

            this.manager.TryReconcile().ShouldBeTrue();
            this.environment.BlobRequests.Count.ShouldEqual(2);
            this.environment.IsPending.ShouldBeTrue();
            this.Apply(FirstGit).ShouldBeTrue();
            this.environment.DehydrateCalls.ShouldMatchInOrder(".gitattributes", "C/c.txt");
        }

        [TestCase]
        public void FileWhoseIndexBlobChangedAfterTheCheckIsNotDehydratedAndReturnsToTheCone()
        {
            this.environment.ModifiedPaths.Add("C/c.txt");
            this.Apply(FirstGit).ShouldBeTrue();
            this.manager.TryReconcile().ShouldBeTrue();
            this.ConeFile.ShouldEqual(Header);

            // For example, "git add" of other content, then the old content on disk again.
            this.environment.DifferingPaths.Add("C/c.txt");
            this.environment.RepositoryStamp++;
            this.Apply(FirstGit).ShouldBeTrue();
            this.environment.DehydrateCalls.ShouldBeEmpty();

            this.manager.TryReconcile().ShouldBeTrue();
            this.environment.IsPending.ShouldBeTrue();
            this.Apply(FirstGit).ShouldBeTrue();
            this.ConeFile.ShouldEqual(ConeWithC);

            // The other clean file is dehydrated after the next check.
            this.manager.TryReconcile().ShouldBeTrue();
            this.Apply(FirstGit).ShouldBeTrue();
            this.environment.DehydrateCalls.ShouldMatchInOrder(".gitattributes");
            this.environment.ModifiedPaths.ShouldContain(path => path == "C/c.txt");
        }

        [TestCase]
        public void ApplyStopsConvertingWhenTheRequestApproachesTheWaitOfTheHook()
        {
            this.environment.ModifiedPaths.Add("C/c.txt");
            this.Apply(FirstGit).ShouldBeTrue();
            this.manager.TryReconcile().ShouldBeTrue();
            this.environment.OnDehydrate = () => Thread.Sleep(AutoSparseIndexConeManager.DehydrateRequestBudgetMs + 50);

            this.Apply(FirstGit).ShouldBeTrue();

            this.environment.DehydrateCalls.ShouldMatchInOrder(".gitattributes");
            this.environment.IsPending.ShouldBeTrue("The blob of the other file is still valid");
        }

        [TestCase]
        public void BlobReadFailureRecordsNothingAndTheFilesWaitBeforeTheNextLookup()
        {
            this.environment.ModifiedPaths.Add("C/c.txt");
            this.Apply(FirstGit).ShouldBeTrue();
            this.environment.BlobsFail = true;

            this.manager.TryReconcile().ShouldBeTrue();

            this.LastEvent("ConeReconcile").Metadata.ContainsKey("DehydrateBlobError").ShouldBeTrue();
            this.environment.IsPending.ShouldBeFalse();
            this.Apply(FirstGit).ShouldBeTrue();
            this.environment.DehydrateCalls.ShouldBeEmpty();

            // The files wait before the next lookup, so each check does not repeat it.
            this.environment.RepositoryStamp++;
            this.manager.TryReconcile().ShouldBeTrue();
            this.environment.BlobRequests.Count.ShouldEqual(1);
        }

        [TestCase]
        public void FileIsDehydratedForTheBlobOfTheNewHeadAfterHeadMoved()
        {
            this.environment.ModifiedPaths.Add("C/c.txt");
            this.Apply(FirstGit).ShouldBeTrue();
            this.manager.TryReconcile().ShouldBeTrue();

            // HEAD moved to a commit that has another blob for the file, and the index and the
            // file match it.
            this.environment.RepositoryStamp++;
            this.environment.BlobShas["C/c.txt"] = new string('c', 40);
            this.Apply(FirstGit).ShouldBeTrue();
            this.environment.DehydrateCalls.ShouldBeEmpty();

            this.manager.TryReconcile().ShouldBeTrue();
            this.Apply(FirstGit).ShouldBeTrue();
            this.environment.DehydratedBlobs["C/c.txt"].Sha.ShouldEqual(new string('c', 40));
        }

        [TestCase]
        public void CheckDoesNotRecordBlobsWhenTheStampChangedDuringTheLookup()
        {
            this.environment.ModifiedPaths.Add("C/c.txt");
            this.Apply(FirstGit).ShouldBeTrue();
            this.environment.DuringBlobs = () => this.environment.RepositoryStamp++;

            this.manager.TryReconcile().ShouldBeTrue();

            this.LastEvent("ConeReconcile").Metadata["DehydrateBlobStale"].ShouldEqual(true);
            this.environment.IsPending.ShouldBeFalse();
            this.Apply(FirstGit).ShouldBeTrue();
            this.environment.DehydrateCalls.ShouldBeEmpty();

            this.environment.DuringBlobs = null;
            this.manager.TryReconcile().ShouldBeTrue();
            this.Apply(FirstGit).ShouldBeTrue();
            this.environment.DehydrateCalls.ShouldMatchInOrder(".gitattributes", "C/c.txt");
        }

        [TestCase]
        public void ApplyThatDehydratesIsTelemetry()
        {
            this.environment.ModifiedPaths.Add("C/c.txt");
            this.Apply(FirstGit).ShouldBeTrue();
            this.LastEvent("ConeApply").Keyword.HasFlag(Keywords.Telemetry).ShouldBeFalse();
            this.manager.TryReconcile().ShouldBeTrue();

            this.Apply(FirstGit).ShouldBeTrue();

            MockTracer.RecordedEvent applied = this.LastEvent("ConeApply");
            applied.Level.ShouldEqual(EventLevel.Informational);
            applied.Keyword.HasFlag(Keywords.Telemetry).ShouldBeTrue();
        }

        [TestCase]
        public void RestoreFailureAfterAnApplyFailureIsAnError()
        {
            this.environment.ModifiedPaths.Add("C/c.txt");
            this.Apply(FirstGit).ShouldBeTrue();
            this.manager.TryReconcile().ShouldBeTrue();
            this.environment.ApplyExitCode = 1;
            this.environment.RestoreFails = true;

            this.Apply(FirstGit).ShouldBeFalse();

            this.LastEvent("ConeApply").Metadata["DehydrateRestoreFailureCount"].ShouldEqual(2);
            this.tracer.RelatedErrorEvents.ShouldContain(error => error.Contains("DehydrateRestoreFailureCount"));
        }

        [TestCase]
        public void ApplyRebuildsTheProjectionWhenItHasADehydratedFileForAnotherBlob()
        {
            this.environment.ModifiedPaths.Add("C/c.txt");
            this.environment.ProjectedFiles.UnionWith(new[] { ".gitattributes", "C/c.txt" });
            this.environment.OtherProjectedShas["C/c.txt"] = new string('b', 40);
            this.Apply(FirstGit).ShouldBeTrue();
            this.manager.TryReconcile().ShouldBeTrue();

            this.Apply(FirstGit).ShouldBeTrue();

            this.environment.ProjectionInvalidations.ShouldEqual(1);
        }

        [TestCase]
        public void RequestApplyMakesTheNextGitCommandApplyTheCone()
        {
            this.Apply(FirstGit).ShouldBeTrue();
            this.Widen(SecondGit, "A/B/b.txt").ShouldBeTrue();
            this.Narrow(SecondGit).ShouldBeTrue();
            int applies = this.environment.ConeFilesAtApply.Count;

            this.manager.RequestApply();

            this.environment.IsPending.ShouldBeTrue();
            this.Apply(FirstGit).ShouldBeTrue();
            this.environment.ConeFilesAtApply.Count.ShouldEqual(applies + 1, "The index still expands A/B after the narrow");
        }

        [TestCase]
        public void ApplyStopsAfterTheTimeBudgetButDehydratesAtLeastOneFile()
        {
            this.environment.ModifiedPaths.Add("C/c.txt");
            this.Apply(FirstGit).ShouldBeTrue();
            this.manager.TryReconcile().ShouldBeTrue();
            this.environment.OnDehydrate = () => Thread.Sleep(AutoSparseIndexConeManager.DehydrateTimeBudgetMs + 50);

            this.Apply(FirstGit).ShouldBeTrue();

            this.environment.DehydrateCalls.ShouldMatchInOrder(".gitattributes");
            this.environment.IsPending.ShouldBeTrue("The next Git command continues");

            this.Apply(FirstGit).ShouldBeTrue();
            this.environment.DehydrateCalls.ShouldMatchInOrder(".gitattributes", "C/c.txt");
        }

        [TestCase]
        public void ApplyDehydratesAtMostTheBatchLimitAndKeepsTheRestPending()
        {
            for (int i = 0; i < AutoSparseIndexConeManager.MaxDehydrateFiles; i++)
            {
                this.environment.ModifiedPaths.Add("C/" + i.ToString("D5") + ".txt");
            }

            this.Apply(FirstGit).ShouldBeTrue();
            this.manager.TryReconcile().ShouldBeTrue();

            this.Apply(FirstGit).ShouldBeTrue();
            this.environment.DehydrateCalls.Count.ShouldEqual(AutoSparseIndexConeManager.MaxDehydrateFiles);
            this.environment.ModifiedPaths.Count.ShouldEqual(1);
            this.environment.IsPending.ShouldBeTrue();

            this.Apply(FirstGit).ShouldBeTrue();
            this.environment.ModifiedPaths.ShouldBeEmpty();
            this.environment.IsPending.ShouldBeFalse();
        }

        private string ConeFile => this.fileSystem.ReadAllText(SparseCheckoutPath);

        private AutoSparseIndexConeManager CreateManager(bool ignoreCase, int changeCheckDelayMs, int dehydrateRetryDelayMs = AutoSparseIndexConeManager.DefaultDehydrateRetryDelayMs)
        {
            return new AutoSparseIndexConeManager(this.tracer, this.fileSystem, SparseCheckoutPath, ignoreCase, this.environment, changeCheckDelayMs, dehydrateRetryDelayMs);
        }

        private bool Apply(int processId)
        {
            return this.manager.TryApply(new NamedPipeMessages.ConeManagement.ApplyParameters(processId.ToString()));
        }

        private bool Widen(int processId, params string[] paths)
        {
            return this.manager.TryWiden(new NamedPipeMessages.ConeManagement.WidenParameters(processId.ToString(), paths));
        }

        private bool Narrow(int processId)
        {
            return this.manager.TryNarrow(new NamedPipeMessages.ConeManagement.NarrowParameters(processId.ToString()));
        }

        private MockTracer.RecordedEvent LastEvent(string name)
        {
            return this.tracer.SnapshotRelatedEvents().Last(recorded => recorded.Name == name);
        }

        private sealed class TestFileSystem : MockFileSystem
        {
            public TestFileSystem()
                : base(CreateRoot())
            {
            }

            public string FailRenameTo { get; set; }

            public override void MoveAndOverwriteFile(string sourcePath, string destinationPath)
            {
                if (string.Equals(destinationPath, this.FailRenameTo, StringComparison.OrdinalIgnoreCase))
                {
                    throw new IOException("Injected rename failure");
                }

                base.MoveAndOverwriteFile(sourcePath, destinationPath);
            }

            private static MockDirectory CreateRoot()
            {
                MockDirectory info = new MockDirectory(InfoDir, folders: null, files: null);
                MockDirectory git = new MockDirectory(GitDir, new[] { info }, files: null);
                return new MockDirectory(RepoRoot, new[] { git }, files: null);
            }
        }

        private sealed class MockEnvironment : IConeManagerEnvironment
        {
            public static readonly string HeadSha = new string('a', 40);

            private readonly MockFileSystem fileSystem;
            private int lockHeld;

            public MockEnvironment(MockFileSystem fileSystem)
            {
                this.fileSystem = fileSystem;
            }

            public List<string> ModifiedPaths { get; } = new List<string> { ".gitattributes" };

            public Dictionary<int, long> RunningProcesses { get; } = new Dictionary<int, long>();

            public List<string> ConeFilesAtApply { get; } = new List<string>();

            public List<string> SparseDirectories { get; } = new List<string>();

            public HashSet<string> DeletedFiles { get; } = new HashSet<string>();

            public bool SparseDirectoriesFail { get; set; }

            public Dictionary<int, ProcessStartTimeResult> StartTimeResults { get; } = new Dictionary<int, ProcessStartTimeResult>();

            public bool IndexWriteAllowed { get; set; } = true;

            public int? LockHolder { get; set; }

            public int CanWriteIndexDelayMs { get; set; }

            public bool ConeFileLocked { get; set; }

            public int ApplyExitCode { get; set; }

            public Exception ApplyException { get; set; }

            public Action DuringApply { get; set; }

            public int LockCount { get; private set; }

            public bool IsConeFileLockHeld => this.lockHeld != 0;

            public bool SparsityChangeIsIndexOnly { get; set; }

            public bool IsPending { get; private set; }

            public int PendingFileCreateCount { get; private set; }

            public bool PendingFileFails { get; set; }

            public bool PendingFileDeleteFails { get; set; }

            public HashSet<string> DifferingPaths { get; } = new HashSet<string>();

            public HashSet<string> UntrackedFiles { get; } = new HashSet<string>();

            public bool DiffFails { get; set; }

            public int DiffCount { get; private set; }

            public Action DuringDiff { get; set; }

            public long RepositoryStamp { get; set; }

            public string DehydrateBlocker { get; set; }

            public HashSet<string> NotInIndex { get; } = new HashSet<string>();

            public bool BlobsFail { get; set; }

            public List<IReadOnlyList<string>> BlobRequests { get; } = new List<IReadOnlyList<string>>();

            public List<int> BlobTimeouts { get; } = new List<int>();

            public Action DuringBlobs { get; set; }

            // The blobs of HEAD and the index, when they are not HeadSha.
            public Dictionary<string, string> BlobShas { get; } = new Dictionary<string, string>();

            public Dictionary<string, HeadBlob> DehydratedBlobs { get; } = new Dictionary<string, HeadBlob>();

            public bool RestoreFails { get; set; }

            public Dictionary<string, CleanFileDehydrateResult> DehydrateResults { get; } = new Dictionary<string, CleanFileDehydrateResult>();

            public Dictionary<string, long> BlobSizes { get; } = new Dictionary<string, long>();

            public List<string> DehydrateCalls { get; } = new List<string>();

            public List<string> RestoredPaths { get; } = new List<string>();

            public int ProjectionInvalidations { get; private set; }

            public Action OnDehydrate { get; set; }

            public string GetDehydrateBlocker()
            {
                return this.DehydrateBlocker;
            }

            public bool TryGetBlobsThatMatchHead(IReadOnlyList<string> gitPaths, int timeoutMs, out IReadOnlyDictionary<string, HeadBlob> blobs, out IReadOnlyCollection<string> notInIndex, out string error)
            {
                this.BlobRequests.Add(gitPaths.ToList());
                this.BlobTimeouts.Add(timeoutMs);
                this.DuringBlobs?.Invoke();
                if (this.BlobsFail)
                {
                    blobs = null;
                    notInIndex = null;
                    error = "fatal: injected failure";
                    return false;
                }

                // A path that differs from HEAD in the index has another blob there.
                notInIndex = gitPaths.Where(path => this.NotInIndex.Contains(path)).ToList();
                blobs = gitPaths
                    .Where(path => !this.NotInIndex.Contains(path) && !this.DifferingPaths.Contains(path))
                    .ToDictionary(path => path, path => new HeadBlob(this.BlobShas.TryGetValue(path, out string sha) ? sha : HeadSha, this.BlobSizes.TryGetValue(path, out long size) ? size : 1));
                error = null;
                return true;
            }

            public CleanFileDehydrateResult TryDehydrateCleanFile(string gitPath, HeadBlob blob)
            {
                this.DehydrateCalls.Add(gitPath);
                this.OnDehydrate?.Invoke();
                if (!this.DehydrateResults.TryGetValue(gitPath, out CleanFileDehydrateResult result))
                {
                    result = CleanFileDehydrateResult.Dehydrated;
                }

                if (result == CleanFileDehydrateResult.Dehydrated)
                {
                    this.ModifiedPaths.Remove(gitPath);
                    this.DehydratedBlobs[gitPath] = blob;
                }

                return result;
            }

            public bool TryRestoreModifiedPath(string gitPath)
            {
                this.RestoredPaths.Add(gitPath);
                if (this.RestoreFails)
                {
                    return false;
                }

                this.ModifiedPaths.Add(gitPath);
                return true;
            }

            // The projected files, with the blob of HEAD unless OtherProjectedShas has the path.
            public HashSet<string> ProjectedFiles { get; } = new HashSet<string>();

            public Dictionary<string, string> OtherProjectedShas { get; } = new Dictionary<string, string>();

            public bool IsProjectedFile(string gitPath, string sha)
            {
                string projectedSha = this.OtherProjectedShas.TryGetValue(gitPath, out string other) ? other : HeadSha;
                return this.ProjectedFiles.Contains(gitPath) && projectedSha == sha;
            }

            public void InvalidateProjection()
            {
                this.ProjectionInvalidations++;
            }

            public bool TrySetConeChangePending(bool pending, out Exception error)
            {
                if (this.PendingFileFails || (!pending && this.PendingFileDeleteFails))
                {
                    error = new IOException("Injected pending file failure");
                    return false;
                }

                if (pending && !this.IsPending)
                {
                    this.PendingFileCreateCount++;
                }

                this.IsPending = pending;
                error = null;
                return true;
            }

            public bool TryGetPathsThatDifferFromHead(out ICollection<string> paths, out string error)
            {
                this.DiffCount++;
                this.DuringDiff?.Invoke();
                if (this.DiffFails)
                {
                    paths = null;
                    error = "fatal: injected failure";
                    return false;
                }

                // Like Git: an untracked file that exists, and a tracked file that was deleted.
                paths = this.DifferingPaths
                    .Concat(this.UntrackedFiles.Except(this.DeletedFiles))
                    .Concat(this.DeletedFiles.Except(this.UntrackedFiles))
                    .ToList();
                error = null;
                return true;
            }

            public long GetRepositoryStamp()
            {
                return this.RepositoryStamp;
            }

            public IEnumerable<string> GetModifiedPaths()
            {
                return this.ModifiedPaths.ToList();
            }

            public bool CanWriteIndex(int processId)
            {
                if (this.CanWriteIndexDelayMs > 0)
                {
                    Thread.Sleep(this.CanWriteIndexDelayMs);
                }

                return this.IndexWriteAllowed && (this.LockHolder == null || this.LockHolder == processId);
            }

            public bool TryGetSparseDirectories(out IReadOnlyList<string> sparseDirectories, out string error)
            {
                if (this.SparseDirectoriesFail)
                {
                    sparseDirectories = null;
                    error = "fatal: injected failure";
                    return false;
                }

                sparseDirectories = this.SparseDirectories.ToList();
                error = null;
                return true;
            }

            public bool IsFileInWorkingDirectory(string gitPath)
            {
                return !this.DeletedFiles.Contains(gitPath);
            }

            public bool TryLockConeFile(out IDisposable coneFileLock, out Exception error)
            {
                if (this.ConeFileLocked)
                {
                    coneFileLock = null;
                    error = new IOException("sparse-checkout.lock: File exists");
                    return false;
                }

                // The manager must serialize requests, so the lock is never held twice.
                Interlocked.Exchange(ref this.lockHeld, 1).ShouldEqual(0, "The cone file lock is already held");
                this.LockCount++;
                coneFileLock = new Releaser(() => Interlocked.Exchange(ref this.lockHeld, 0));
                error = null;
                return true;
            }

            public GitProcess.Result ApplyConeToIndex()
            {
                this.ConeFilesAtApply.Add(this.fileSystem.ReadAllText(SparseCheckoutPath));
                this.DuringApply?.Invoke();
                if (this.ApplyException != null)
                {
                    throw this.ApplyException;
                }

                return new GitProcess.Result(string.Empty, this.ApplyExitCode == 0 ? string.Empty : "fatal: Unable to write new index file", this.ApplyExitCode);
            }

            public ProcessStartTimeResult TryGetProcessStartTime(int processId, out long startTime)
            {
                lock (this.RunningProcesses)
                {
                    if (this.StartTimeResults.TryGetValue(processId, out ProcessStartTimeResult result))
                    {
                        startTime = 0;
                        return result;
                    }

                    if (this.RunningProcesses.TryGetValue(processId, out startTime))
                    {
                        return ProcessStartTimeResult.Success;
                    }
                }

                return ProcessStartTimeResult.ProcessNotFound;
            }

            private sealed class Releaser : IDisposable
            {
                private readonly Action release;

                public Releaser(Action release)
                {
                    this.release = release;
                }

                public void Dispose()
                {
                    this.release();
                }
            }
        }
    }
}
