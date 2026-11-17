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
            this.environment.IsPending.ShouldBeFalse();
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

            // For example "git reset --soft HEAD~1" after a commit of the file.
            this.environment.DifferingPaths.Add("C/c.txt");
            this.environment.RepositoryStamp++;
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
            this.environment.IsPending.ShouldBeFalse();
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

        private string ConeFile => this.fileSystem.ReadAllText(SparseCheckoutPath);

        private AutoSparseIndexConeManager CreateManager(bool ignoreCase, int changeCheckDelayMs)
        {
            return new AutoSparseIndexConeManager(this.tracer, this.fileSystem, SparseCheckoutPath, ignoreCase, this.environment, changeCheckDelayMs);
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

                paths = this.DifferingPaths.ToList();
                error = null;
                return true;
            }

            public bool IsTracked(string gitPath)
            {
                return !this.UntrackedFiles.Contains(gitPath);
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
