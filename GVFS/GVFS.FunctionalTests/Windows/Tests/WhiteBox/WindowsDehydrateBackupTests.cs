using GVFS.Common.Tracing;
using GVFS.Platform.Windows;
using Microsoft.Win32.SafeHandles;
using Moq;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;

namespace GVFS.FunctionalTests.Tests.WhiteBox
{
    /// <summary>
    /// White-box tests for backup scope and deletion through real Windows filesystem handles.
    /// </summary>
    [TestFixture]
    [NUnit.Framework.Category(Categories.WhiteBox)]
    public class WindowsDehydrateBackupTests
    {
        private const string BackupName = "20261030_101814";
        private string enlistment;
        private string container;
        private string backup;
        private Mock<ITracer> tracer;

        [SetUp]
        public void SetUp()
        {
            this.enlistment = Path.Combine(Path.GetTempPath(), nameof(WindowsDehydrateBackupTests) + Guid.NewGuid().ToString("N"));
            this.container = Path.Combine(this.enlistment, "dehydrate_backup");
            this.backup = Path.Combine(this.container, BackupName);
            Directory.CreateDirectory(this.backup);
            this.tracer = new Mock<ITracer>();
        }

        [TearDown]
        public void TearDown()
        {
            foreach (string file in Directory.EnumerateFiles(this.enlistment, "*", SearchOption.AllDirectories))
            {
                File.SetAttributes(file, FileAttributes.Normal);
            }

            Directory.Delete(this.enlistment, recursive: true);
        }

        [TestCase]
        public void DeletesOnlyTheSelectedBackupIncludingReadonlyAndLongUnicodePaths()
        {
            string nested = this.backup;
            for (int index = 0; index < 6; ++index)
            {
                nested = Path.Combine(nested, new string('a', 45));
            }

            Directory.CreateDirectory(nested);
            string file = Path.Combine(nested, "name-\u00e9.txt");
            File.WriteAllText(file, "Backup contents");
            File.SetAttributes(file, FileAttributes.ReadOnly);
            Directory.CreateDirectory(Path.Combine(this.backup, "empty"));
            string otherBackup = Path.Combine(this.container, "20261030_111814");
            Directory.CreateDirectory(otherBackup);
            File.WriteAllText(Path.Combine(otherBackup, "keep.txt"), "Other backup");
            string utility = Path.Combine(this.container, "cleanup.exe");
            File.WriteAllText(utility, "Keep the utility");
            string workingDirectory = Path.Combine(this.enlistment, "src");
            Directory.CreateDirectory(workingDirectory);
            File.WriteAllText(Path.Combine(workingDirectory, "keep.txt"), "Working file");

            using (WindowsDehydrateBackup scope = this.Open())
            {
                WindowsDehydrateBackup.DeletionResult result = scope.Delete();
                Assert.That(result.Succeeded, Is.True, this.Failures(result));
                Assert.That(result.FilesDeleted, Is.EqualTo(1));
                Assert.That(result.DirectoriesDeleted, Is.EqualTo(8));
                Assert.That(Directory.Exists(this.backup), Is.False);
            }

            Assert.That(File.ReadAllText(utility), Is.EqualTo("Keep the utility"));
            Assert.That(File.ReadAllText(Path.Combine(otherBackup, "keep.txt")), Is.EqualTo("Other backup"));
            Assert.That(File.ReadAllText(Path.Combine(workingDirectory, "keep.txt")), Is.EqualTo("Working file"));
        }

        [TestCase]
        public void RetainsBusyEntriesAndRetriesWithoutDeletingTheContainer()
        {
            string busy = Path.Combine(this.backup, "busy.txt");
            File.WriteAllText(busy, "Busy file");
            File.WriteAllText(Path.Combine(this.backup, "other.txt"), "Other file");
            using (WindowsDehydrateBackup scope = this.Open())
            {
                using (new FileStream(busy, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    WindowsDehydrateBackup.DeletionResult first = scope.Delete();
                    Assert.That(first.Succeeded, Is.False);
                    Assert.That(first.FilesDeleted, Is.EqualTo(1));
                    Assert.That(first.FailureCount, Is.EqualTo(1));
                    Assert.That(first.FailureSamples.Single().Path, Is.EqualTo(busy));
                    Assert.That(((Win32Exception)first.FailureSamples.Single().Exception).NativeErrorCode, Is.EqualTo(32));
                    Assert.That(File.ReadAllText(busy), Is.EqualTo("Busy file"));
                }

                WindowsDehydrateBackup.DeletionResult retry = scope.Delete();
                Assert.That(retry.Succeeded, Is.True, this.Failures(retry));
                Assert.That(retry.FilesDeleted, Is.EqualTo(1));
                Assert.That(scope.Delete().Succeeded, Is.True);
            }

            Assert.That(Directory.Exists(this.container), Is.True);
            this.tracer.Verify(
                tracer => tracer.RelatedWarning(It.IsAny<EventMetadata>(), It.IsAny<string>()),
                Times.AtLeastOnce);
        }

        [TestCase(true)]
        [TestCase(false)]
        public void DeletesNamesWithUnpairedUtf16CodeUnits(bool highSurrogate)
        {
            string name = highSurrogate ? "name-\uD800.txt" : "name-\uDC00.txt";
            File.WriteAllText(Path.Combine(this.backup, name), "Backup");
            using (WindowsDehydrateBackup scope = this.Open())
            {
                WindowsDehydrateBackup.DeletionResult result = scope.Delete();
                Assert.That(result.Succeeded, Is.True, this.Failures(result));
                Assert.That(result.FilesDeleted, Is.EqualTo(1));
                Assert.That(result.EntriesAlreadyAbsent, Is.Zero);
            }
        }

        [TestCase]
        public void PreservesRawUtf16FailureResultsWithJsonTracing()
        {
            string file = Path.Combine(this.backup, "busy-\uD800.txt");
            string log = Path.Combine(this.enlistment, "events.log");
            File.WriteAllText(file, "Backup");
            using (JsonTracer tracer = new JsonTracer("DehydrateBackupTests", "RetainedEntry", disableTelemetry: true))
            {
                tracer.AddLogFileEventListener(log, EventLevel.Verbose, Keywords.Any);
                using (WindowsDehydrateBackup scope = WindowsDehydrateBackup.Open(tracer, this.enlistment, BackupName))
                {
                    using (new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read))
                    {
                        WindowsDehydrateBackup.DeletionResult result = scope.Delete();
                        Assert.That(result.Succeeded, Is.False);
                        Assert.That(result.FailureCount, Is.EqualTo(1));
                        Assert.That(result.FailureSamples.Single().Path, Is.EqualTo(file));
                    }

                    Assert.That(scope.Delete().Succeeded, Is.True);
                }
            }

            Assert.That(File.ReadAllText(log), Does.Contain("Dehydrate backup cleanup retained an entry."));
        }

        [TestCase]
        public void BoundsFailureSamplesWithoutCappingTheFailureCount()
        {
            const int BusyFileCount = 17;
            List<FileStream> readers = new List<FileStream>();
            using (WindowsDehydrateBackup scope = this.Open())
            {
                try
                {
                    for (int index = 0; index < BusyFileCount; ++index)
                    {
                        string file = Path.Combine(this.backup, $"busy-{index}.txt");
                        File.WriteAllText(file, "Backup");
                        readers.Add(new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read));
                    }

                    WindowsDehydrateBackup.DeletionResult result = scope.Delete();
                    Assert.That(result.Succeeded, Is.False);
                    Assert.That(result.FailureCount, Is.EqualTo(BusyFileCount));
                    Assert.That(result.FailureSamples.Count, Is.EqualTo(16));
                    Assert.That(result.FilesDeleted, Is.Zero);
                    foreach (FileStream reader in readers)
                    {
                        Assert.That(File.Exists(reader.Name), Is.True);
                    }
                }
                finally
                {
                    foreach (FileStream reader in readers)
                    {
                        reader.Dispose();
                    }
                }

                WindowsDehydrateBackup.DeletionResult retry = scope.Delete();
                Assert.That(retry.Succeeded, Is.True, this.Failures(retry));
                Assert.That(retry.FilesDeleted, Is.EqualTo(BusyFileCount));
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void CompatibleReadersDoNotLeaveDeletedEntriesInTheNamespace(bool directoryReader)
        {
            string file = Path.Combine(this.backup, "contents.txt");
            File.WriteAllText(file, "Backup");
            using (SafeFileHandle reader = CreateFileW(
                directoryReader ? this.backup : file,
                (uint)GVFS.Common.NativeMethods.FileAccess.FILE_READ_DATA,
                FileShare.ReadWrite | FileShare.Delete,
                IntPtr.Zero,
                FileMode.Open,
                (uint)GVFS.Common.NativeMethods.FileAttributes.FILE_FLAG_BACKUP_SEMANTICS,
                IntPtr.Zero))
            {
                Assert.That(reader.IsInvalid, Is.False, $"Open reader failed: {Marshal.GetLastWin32Error()}");
                using (WindowsDehydrateBackup scope = this.Open())
                {
                    WindowsDehydrateBackup.DeletionResult result = scope.Delete();
                    Assert.That(result.Succeeded, Is.True, this.Failures(result));
                    Assert.That(Directory.Exists(this.backup), Is.False);
                }
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void SupportsCurrentTimestampNamesAndWorktreeBackupLayout(bool worktree)
        {
            const string CurrentName = "20261030_101814_1234567";
            string backupParentRoot = worktree ? Path.Combine(this.enlistment, "worktrees") : this.enlistment;
            string liveRoot = Path.Combine(backupParentRoot, worktree ? "feature" : "src");
            Directory.CreateDirectory(liveRoot);
            string sentinel = Path.Combine(liveRoot, "keep.txt");
            File.WriteAllText(sentinel, "Live worktree");
            string parent = worktree
                ? Path.Combine(backupParentRoot, "dehydrate_backup", Path.GetFileName(liveRoot))
                : this.container;
            Directory.CreateDirectory(parent);
            string selected = Path.Combine(parent, CurrentName);
            Directory.Move(this.backup, selected);
            File.WriteAllText(Path.Combine(selected, "contents.txt"), "Backup");
            using (WindowsDehydrateBackup scope = WindowsDehydrateBackup.Open(
                this.tracer.Object, backupParentRoot, CurrentName, worktreeName: worktree ? Path.GetFileName(liveRoot) : null))
            {
                WindowsDehydrateBackup.DeletionResult result = scope.Delete();
                Assert.That(result.Succeeded, Is.True, this.Failures(result));
            }

            Assert.That(Directory.Exists(selected), Is.False);
            Assert.That(Directory.Exists(parent), Is.True);
            Assert.That(Directory.Exists(this.container), Is.True);
            Assert.That(File.ReadAllText(sentinel), Is.EqualTo("Live worktree"));
        }

        [TestCase("relative")]
        [TestCase(@"\relative")]
        [TestCase(@"C:\")]
        [TestCase(@"\\server\share\enlistment")]
        public void RejectsUnsupportedBackupParentPaths(string backupParentRoot)
        {
            Assert.Throws<ArgumentException>(() => WindowsDehydrateBackup.Open(
                this.tracer.Object, backupParentRoot, BackupName));
            Assert.That(Directory.Exists(this.backup), Is.True);
        }

        [TestCase(@"..\src")]
        [TestCase(@"feature\child")]
        [TestCase("feature.")]
        public void RejectsWorktreeNamesThatCanEscapeTheContainer(string worktreeName)
        {
            Assert.Throws<ArgumentException>(() => WindowsDehydrateBackup.Open(
                this.tracer.Object, this.enlistment, BackupName, worktreeName: worktreeName));
            Assert.That(Directory.Exists(this.backup), Is.True);
        }

        [TestCase]
        public void CancellationPreservesProgressAndAllowsRetry()
        {
            File.WriteAllText(Path.Combine(this.backup, "first.txt"), "First");
            File.WriteAllText(Path.Combine(this.backup, "second.txt"), "Second");
            using (WindowsDehydrateBackup scope = this.Open())
            using (CancellationTokenSource cancellation = new CancellationTokenSource())
            {
                WindowsDehydrateBackup.DeletionResult beforeStart = scope.Delete(new CancellationToken(canceled: true));
                Assert.That(beforeStart.Cancelled, Is.True);
                Assert.That(beforeStart.FilesDeleted, Is.Zero);
                WindowsDehydrateBackup.DeletionResult partial = scope.Delete(cancellation.Token, progress => cancellation.Cancel());
                Assert.That(partial.Cancelled, Is.True);
                Assert.That(partial.FilesDeleted, Is.EqualTo(1));
                Assert.That(partial.BackupDeleted, Is.False);
                Assert.That(partial.FailureCount, Is.Zero);
                Assert.That(scope.Delete().Succeeded, Is.True);
            }
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        public void ProgressErrorsStopDeletionAndAllowRetry(int errorKind)
        {
            string child = Path.Combine(this.backup, "child");
            Directory.CreateDirectory(child);
            File.WriteAllText(Path.Combine(child, "a.txt"), "First");
            string remaining = Path.Combine(child, "z.txt");
            File.WriteAllText(remaining, "Second");
            Exception error = errorKind == 0 ? new IOException("Progress failed") :
                errorKind == 1 ? new Win32Exception(2, "Progress failed") :
                new InvalidOperationException("Progress failed");

            using (WindowsDehydrateBackup scope = this.Open())
            {
                Exception actual = Assert.Catch(() => scope.Delete(progress: _ => throw error));
                Assert.That(actual, Is.SameAs(error));
                Assert.That(File.ReadAllText(remaining), Is.EqualTo("Second"));
                this.tracer.Verify(
                    tracer => tracer.RelatedWarning(It.IsAny<EventMetadata>(), It.IsAny<string>()),
                    Times.Never);
                WindowsDehydrateBackup.DeletionResult retry = scope.Delete();
                Assert.That(retry.Succeeded, Is.True, this.Failures(retry));
                Assert.That(retry.FilesDeleted, Is.EqualTo(1));
            }
        }

        [TestCase]
        public void CompletionCallbackErrorsDoNotInvalidateTheDeletedScope()
        {
            IOException error = new IOException("Progress failed");
            using (WindowsDehydrateBackup scope = this.Open())
            {
                Exception actual = Assert.Catch(() => scope.Delete(progress: _ =>
                {
                    Assert.That(Directory.Exists(this.backup), Is.False);
                    throw error;
                }));
                Assert.That(actual, Is.SameAs(error));
                WindowsDehydrateBackup.DeletionResult retry = scope.Delete();
                Assert.That(retry.Succeeded, Is.True, this.Failures(retry));
                Assert.That(retry.DirectoriesDeleted, Is.Zero);
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void RejectsReentrantOperationsFromProgressCallbacks(bool dispose)
        {
            File.WriteAllText(Path.Combine(this.backup, "contents.txt"), "Backup");
            using (WindowsDehydrateBackup scope = this.Open())
            {
                WindowsDehydrateBackup.DeletionResult result = scope.Delete(progress: _ =>
                {
                    if (dispose)
                    {
                        Assert.Throws<InvalidOperationException>(() => scope.Dispose());
                    }
                    else
                    {
                        Assert.Throws<InvalidOperationException>(() => scope.Delete());
                    }
                });
                Assert.That(result.Succeeded, Is.True, this.Failures(result));
            }
        }

        [TestCase(255, true)]
        [TestCase(256, false)]
        public void ReportsUnsupportedDepthWithoutDeletingDeeperEntries(int depth, bool supported)
        {
            string nested = this.backup;
            for (int index = 0; index < depth; ++index)
            {
                nested = Path.Combine(nested, "d");
            }

            Directory.CreateDirectory(nested);
            string file = Path.Combine(nested, "keep.txt");
            File.WriteAllText(file, "Backup");
            using (WindowsDehydrateBackup scope = this.Open())
            {
                WindowsDehydrateBackup.DeletionResult result = scope.Delete();
                Assert.That(result.Succeeded, Is.EqualTo(supported), this.Failures(result));
                if (!supported)
                {
                    Assert.That(result.FailureCount, Is.EqualTo(1));
                    Assert.That(result.FailureSamples.Single().Path, Is.EqualTo(nested));
                    Assert.That(File.ReadAllText(file), Is.EqualTo("Backup"));
                }
            }
        }

        [TestCase("src")]
        [TestCase("..")]
        [TestCase(@"..\src")]
        [TestCase(@"20261030_101814\child")]
        [TestCase(@"20261030_101814:stream")]
        [TestCase("20260230_101814")]
        [TestCase("20261030_101814 ")]
        public void RejectsNamesOutsideTheTimestampedBackupContract(string name)
        {
            Assert.Throws<ArgumentException>(() => WindowsDehydrateBackup.Open(this.tracer.Object, this.enlistment, name));
            Assert.That(Directory.Exists(this.backup), Is.True);
        }

        [TestCase]
        public void RejectsAReplacementBackupWithTheSameName()
        {
            string identity;
            using (WindowsDehydrateBackup original = this.Open())
            {
                identity = original.Identity;
            }

            Directory.Move(this.backup, Path.Combine(this.container, "saved"));
            Directory.CreateDirectory(this.backup);
            string replacement = Path.Combine(this.backup, "replacement.txt");
            File.WriteAllText(replacement, "Not authorized");
            Assert.Throws<IOException>(() => WindowsDehydrateBackup.Open(
                this.tracer.Object, this.enlistment, BackupName, identity));
            Assert.That(File.ReadAllText(replacement), Is.EqualTo("Not authorized"));
        }

        [TestCase]
        public void DeletesABackupWithTheExpectedIdentity()
        {
            string identity;
            using (WindowsDehydrateBackup original = this.Open())
            {
                identity = original.Identity;
            }

            using (WindowsDehydrateBackup scope = WindowsDehydrateBackup.Open(
                this.tracer.Object, this.enlistment, BackupName, expectedIdentity: identity))
            {
                WindowsDehydrateBackup.DeletionResult result = scope.Delete();
                Assert.That(result.Succeeded, Is.True, this.Failures(result));
            }
        }

        [TestCase]
        public void DoesNotFollowAChildReplacedAfterEnumeration()
        {
            File.WriteAllText(Path.Combine(this.backup, "a.txt"), "First");
            string child = Path.Combine(this.backup, "z-child");
            Directory.CreateDirectory(child);
            File.WriteAllText(Path.Combine(child, "old.txt"), "Original");
            bool replaced = false;
            using (WindowsDehydrateBackup scope = this.Open())
            {
                WindowsDehydrateBackup.DeletionResult result = scope.Delete(progress: progress =>
                {
                    if (!replaced && progress.FilesDeleted == 1)
                    {
                        Directory.Move(child, Path.Combine(this.container, "saved-child"));
                        Directory.CreateDirectory(child);
                        File.WriteAllText(Path.Combine(child, "replacement.txt"), "Not authorized");
                        replaced = true;
                    }
                });
                Assert.That(replaced, Is.True);
                Assert.That(result.Succeeded, Is.False);
                Assert.That(result.FailureCount, Is.EqualTo(1));
                Assert.That(result.FailureSamples.Single().Path, Is.EqualTo(child));
                Assert.That(File.ReadAllText(Path.Combine(child, "replacement.txt")), Is.EqualTo("Not authorized"));
            }
        }

        [TestCase]
        public void EnumeratesMultipleNativeBuffersWithoutSkippingDeletedEntries()
        {
            const int FileCount = 2500;
            for (int index = 0; index < FileCount; ++index)
            {
                File.WriteAllText(Path.Combine(this.backup, $"file-{index:D4}.txt"), "Backup");
            }

            using (WindowsDehydrateBackup scope = this.Open())
            {
                WindowsDehydrateBackup.DeletionResult result = scope.Delete();
                Assert.That(result.Succeeded, Is.True, this.Failures(result));
                Assert.That(result.FilesDeleted, Is.EqualTo(FileCount));
                Assert.That(Directory.Exists(this.backup), Is.False);
            }
        }

        [TestCase]
        public void KeepsExclusiveBackupOwnershipUntilTheScopeIsDisposed()
        {
            using (WindowsDehydrateBackup scope = this.Open())
            {
                Win32Exception busy = Assert.Throws<Win32Exception>(() => this.Open());
                Assert.That(busy.NativeErrorCode, Is.EqualTo(32));
                Assert.Throws<IOException>(() => Directory.Move(this.backup, Path.Combine(this.container, "renamed")));
            }

            using (WindowsDehydrateBackup retry = this.Open())
            {
                Assert.That(retry.Delete().Succeeded, Is.True);
            }
        }

        [TestCase]
        public void CountsAnEntryRemovedByAnotherProcessWithoutFollowingNewPaths()
        {
            File.WriteAllText(Path.Combine(this.backup, "a.txt"), "First");
            string removed = Path.Combine(this.backup, "z.txt");
            File.WriteAllText(removed, "Second");
            bool removedExternally = false;
            using (WindowsDehydrateBackup scope = this.Open())
            {
                WindowsDehydrateBackup.DeletionResult result = scope.Delete(progress: progress =>
                {
                    if (!removedExternally && progress.FilesDeleted == 1)
                    {
                        File.Delete(removed);
                        removedExternally = true;
                    }
                });
                Assert.That(result.Succeeded, Is.True, this.Failures(result));
                Assert.That(result.FilesDeleted, Is.EqualTo(1));
                Assert.That(result.EntriesAlreadyAbsent, Is.EqualTo(1));
            }
        }

        [TestCase]
        public void RejectsAnEnlistmentAliasThroughAnAncestorSymlink()
        {
            string alias = Path.Combine(this.enlistment, "alias");
            this.CreateDirectoryLink(alias, this.enlistment);
            try
            {
                Assert.Throws<IOException>(() => WindowsDehydrateBackup.Open(this.tracer.Object, alias, BackupName));
                Assert.That(Directory.Exists(this.backup), Is.True);
            }
            finally
            {
                Directory.Delete(alias);
            }
        }

        [TestCase]
        public void RejectsDirectorySymlinksWithoutTouchingTheTarget()
        {
            string external = Path.Combine(this.enlistment, "outside");
            Directory.CreateDirectory(external);
            string sentinel = Path.Combine(external, "keep.txt");
            File.WriteAllText(sentinel, "Do not touch");
            string link = Path.Combine(this.backup, "link");
            this.CreateDirectoryLink(link, external);
            try
            {
                using (WindowsDehydrateBackup scope = this.Open())
                {
                    WindowsDehydrateBackup.DeletionResult result = scope.Delete();
                    Assert.That(result.Succeeded, Is.False);
                    Assert.That(result.FailureCount, Is.EqualTo(1));
                    Assert.That(result.FailureSamples.Single().Path, Is.EqualTo(link));
                }

                Assert.That(File.ReadAllText(sentinel), Is.EqualTo("Do not touch"));
            }
            finally
            {
                Directory.Delete(link);
            }
        }

        [TestCase]
        public void RejectsAContainerSymlink()
        {
            string external = Path.Combine(this.enlistment, "outside");
            Directory.Move(this.container, external);
            this.CreateDirectoryLink(this.container, external);
            try
            {
                Assert.Throws<IOException>(() => this.Open());
                Assert.That(Directory.Exists(Path.Combine(external, BackupName)), Is.True);
            }
            finally
            {
                Directory.Delete(this.container);
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void RejectsReparsePointsAtTheBackupAndWorktreeScope(bool worktree)
        {
            string external = Path.Combine(this.enlistment, "outside");
            Directory.Move(this.backup, external);
            string target = worktree ? Path.Combine(external, BackupName) : external;
            Directory.CreateDirectory(target);
            string sentinel = Path.Combine(target, "keep.txt");
            File.WriteAllText(sentinel, "Not authorized");
            string link = worktree ? Path.Combine(this.container, "feature") : this.backup;
            this.CreateDirectoryLink(link, external);
            try
            {
                Assert.Throws<IOException>(() => WindowsDehydrateBackup.Open(
                    this.tracer.Object, this.enlistment, BackupName, worktreeName: worktree ? "feature" : null));
                Assert.That(File.ReadAllText(sentinel), Is.EqualTo("Not authorized"));
            }
            finally
            {
                Directory.Delete(link);
            }
        }

        private WindowsDehydrateBackup Open() =>
            WindowsDehydrateBackup.Open(this.tracer.Object, this.enlistment, BackupName);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern SafeFileHandle CreateFileW(
            string path, uint access, FileShare share, IntPtr security, FileMode mode, uint flags, IntPtr template);

        private string Failures(WindowsDehydrateBackup.DeletionResult result) =>
            string.Join(Environment.NewLine, result.FailureSamples.Select(failure => failure.Exception.ToString()));

        private void CreateDirectoryLink(string path, string target)
        {
            try
            {
                Directory.CreateSymbolicLink(path, target);
            }
            catch (UnauthorizedAccessException)
            {
                Assert.Ignore("The test requires permission to create directory symbolic links.");
            }
            catch (IOException exception) when ((exception.HResult & 0xFFFF) == 1314)
            {
                Assert.Ignore("The test requires permission to create directory symbolic links.");
            }
        }
    }
}
