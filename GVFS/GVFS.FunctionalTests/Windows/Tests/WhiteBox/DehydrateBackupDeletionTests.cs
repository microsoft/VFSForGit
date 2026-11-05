using Microsoft.Windows.ProjFS;
using NUnit.Framework;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;

namespace GVFS.FunctionalTests.Tests.WhiteBox
{
    /// <summary>
    /// White-box tests for backup deletion with a real ProjFS provider.
    /// </summary>
    [TestFixture]
    [Category(Categories.WhiteBox)]
    public class DehydrateBackupDeletionTests
    {
        private string enlistment;

        [SetUp]
        public void SetUp()
        {
            this.enlistment = Path.Combine(Path.GetTempPath(), nameof(DehydrateBackupDeletionTests) + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(this.enlistment);
        }

        [TearDown]
        public void TearDown()
        {
            Directory.Delete(this.enlistment, recursive: true);
        }

        [TestCase("control")]
        [TestCase("delete")]
        public void RejectsHelperRootsOutsideTheDisposableFixture(string operation)
        {
            string root = Path.Combine(this.enlistment, "wrong");
            string source = Path.Combine(root, "src");
            Directory.CreateDirectory(source);
            string sentinel = Path.Combine(source, DehydrateBackupProcess.NotificationControl);
            File.WriteAllText(sentinel, "Keep");
            string backup = Path.Combine(root, "dehydrate_backup", "20261030_101814");
            Directory.CreateDirectory(backup);
            string backupSentinel = Path.Combine(backup, "keep.txt");
            File.WriteAllText(backupSentinel, "Keep backup");
            Assert.Throws<ArgumentException>(() => DehydrateBackupProcess.Run(
                new[] { DehydrateBackupProcess.Switch, operation, root, operation == "control" ? string.Empty : "20261030_101814" }));
            Assert.That(File.ReadAllText(sentinel), Is.EqualTo("Keep"));
            Assert.That(File.ReadAllText(backupSentinel), Is.EqualTo("Keep backup"));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void RejectsControlAliasesWithoutDeletingTheirTarget(bool rootAlias)
        {
            string outside = Path.Combine(this.enlistment, "outside");
            string source = Path.Combine(this.enlistment, "src");
            Directory.CreateDirectory(rootAlias ? source : outside);
            string sentinel = Path.Combine(rootAlias ? source : outside, DehydrateBackupProcess.NotificationControl);
            File.WriteAllText(sentinel, "Keep");
            string alias = rootAlias
                ? Path.Combine(Path.GetTempPath(), nameof(DehydrateBackupDeletionTests) + Guid.NewGuid().ToString("N"))
                : source;
            Directory.CreateSymbolicLink(alias, rootAlias ? this.enlistment : outside);
            try
            {
                Assert.Throws<IOException>(() => DehydrateBackupProcess.Run(
                    new[] { DehydrateBackupProcess.Switch, "control", rootAlias ? alias : this.enlistment, string.Empty }));
                Assert.That(File.ReadAllText(sentinel), Is.EqualTo("Keep"));
            }
            finally
            {
                Directory.Delete(alias);
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void RejectsHelperParentAndGuidViolations(bool wrongParent)
        {
            string root = wrongParent
                ? Path.Combine(this.enlistment, nameof(DehydrateBackupDeletionTests) + Guid.NewGuid().ToString("N"))
                : Path.Combine(Path.GetTempPath(), nameof(DehydrateBackupDeletionTests) + Guid.NewGuid().ToString("N") + "-invalid");
            Assert.Throws<ArgumentException>(() => DehydrateBackupProcess.Run(
                new[] { DehydrateBackupProcess.Switch, "control", root, string.Empty }));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void RejectsMalformedHelperArguments(bool wrongOperation)
        {
            string[] args = wrongOperation
                ? new[] { DehydrateBackupProcess.Switch, "invalid", this.enlistment, string.Empty }
                : new[] { DehydrateBackupProcess.Switch, "control", this.enlistment };
            Assert.Throws<ArgumentException>(() => DehydrateBackupProcess.Run(args));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void DeletesMovedPhysicalEntriesWithoutStoppingTheProvider(bool fullBackup)
        {
            const string BackupName = "20261030_101814";
            string enlistment = this.enlistment;
            string root = Path.Combine(enlistment, "src");
            string backup = Path.Combine(enlistment, "dehydrate_backup", BackupName);
            Directory.CreateDirectory(root);
            Directory.CreateDirectory(backup);
            Projection provider = new Projection(root);
            using (provider)
            {
                try
                {
                    provider.Start();
                    Assert.That(provider.WritePlaceholder("tree"), Is.EqualTo(HResult.Ok));
                    Assert.That(provider.WritePlaceholder(@"tree\cached"), Is.EqualTo(HResult.Ok));
                    Assert.That(provider.WritePlaceholder(@"tree\cached\hot.bin"), Is.EqualTo(HResult.Ok));
                    Assert.That(provider.WritePlaceholder(@"tree\cached\cold.bin"), Is.EqualTo(HResult.Ok));
                    Assert.That(provider.WritePlaceholder(@"tree\cached\deleted.bin"), Is.EqualTo(HResult.Ok));
                    Assert.That(provider.WritePlaceholder(@"tree\cached\recall-control.bin"), Is.EqualTo(HResult.Ok));
                    Assert.That(provider.WritePlaceholder(@"tree\cached\deleted-directory"), Is.EqualTo(HResult.Ok));
                    if (!fullBackup)
                    {
                        Assert.That(provider.WritePlaceholder(@"tree\cached\notification-control.bin"), Is.EqualTo(HResult.Ok));
                    }
                    Assert.That(File.ReadAllBytes(Path.Combine(root, @"tree\cached\hot.bin")), Is.EqualTo(Projection.Content));
                    File.Delete(Path.Combine(root, @"tree\cached\deleted.bin"));
                    Directory.Delete(Path.Combine(root, @"tree\cached\deleted-directory"));
                    provider.Stop();
                    if (fullBackup)
                    {
                        Directory.Move(root, Path.Combine(backup, "src"));
                        Directory.CreateDirectory(root);
                    }
                    else
                    {
                        Directory.Move(Path.Combine(root, "tree"), Path.Combine(backup, "tree"));
                    }

                    provider.Start();

                    Assert.That(provider.WritePlaceholder(DehydrateBackupProcess.NotificationControl), Is.EqualTo(HResult.Ok));
                    DehydrateBackupProcess.DeleteControl(enlistment);
                    Assert.That(SpinWait.SpinUntil(
                        () => provider.Notifications.Contains("Deleted:" + DehydrateBackupProcess.NotificationControl),
                        TimeSpan.FromSeconds(10)), Is.True, "The live provider did not observe the notification control.");
                    provider.Notifications.Clear();

                    if (!fullBackup)
                    {
                        DehydrateBackupProcess.DeleteMovedControl(enlistment, BackupName);
                        SpinWait.SpinUntil(() => !provider.Notifications.IsEmpty, TimeSpan.FromSeconds(1));
                        TestContext.WriteLine("Moved-placeholder client-delete notifications observed: " + provider.Notifications.Count);
                        provider.Notifications.Clear();
                    }

                    if (!fullBackup)
                    {
                        string[] names = Directory.EnumerateDirectories(Path.Combine(backup, "tree")).Select(Path.GetFileName).ToArray();
                        Assert.That(names, Does.Contain("cached"));
                        Assert.That(names, Does.Contain("virtual-only"));
                        IOException unavailable = Assert.Throws<IOException>(() =>
                            Directory.GetFileSystemEntries(Path.Combine(backup, @"tree\virtual-only")));
                        Assert.That(unavailable.HResult & 0xFFFF, Is.EqualTo(369));
                    }

                    string control = Path.Combine(backup, fullBackup ? @"src\tree\cached\recall-control.bin" : @"tree\cached\recall-control.bin");
                    int recallsBeforeControl = provider.DataCallbacks;
                    if (fullBackup)
                    {
                        IOException unavailable = Assert.Throws<IOException>(() => File.ReadAllBytes(control));
                        Assert.That(unavailable.HResult & 0xFFFF, Is.EqualTo(372));
                        Assert.That(provider.DataCallbacks, Is.EqualTo(recallsBeforeControl));
                    }
                    else
                    {
                        Assert.That(File.ReadAllBytes(control), Is.EqualTo(Projection.Content));
                        Assert.That(provider.DataCallbacks, Is.EqualTo(recallsBeforeControl + 1));
                    }

                    Assert.That(File.ReadAllBytes(Path.Combine(root, "health.bin")), Is.EqualTo(Projection.Content));
                    int recallsBeforeDelete = provider.DataCallbacks;
                    DehydrateBackupProcess.Summary result = DehydrateBackupProcess.Delete(enlistment, BackupName);
                    Assert.That(result.Failures, Is.Empty);
                    Assert.That(result.FilesDeleted, Is.EqualTo(4));
                    Assert.That(result.DirectoriesDeleted, Is.EqualTo(fullBackup ? 5 : 4));

                    Assert.That(result.LiveReads, Is.GreaterThan(0));
                    Assert.That(provider.DataCallbacks, Is.EqualTo(recallsBeforeDelete + result.LiveReads));
                    Assert.That(Directory.Exists(backup), Is.False);
                    Assert.That(File.ReadAllBytes(Path.Combine(root, "health.bin")), Is.EqualTo(Projection.Content));
                    provider.Stop();
                    Assert.That(provider.Notifications, Is.Empty);
                    Assert.That(provider.Errors, Is.Empty);
                }
                finally
                {
                    provider.Stop();
                }
            }
        }

        private sealed class Projection : IRequiredCallbacks, IDisposable
        {
            public static readonly byte[] Content = DehydrateBackupProcess.Content;
            private readonly Dictionary<string, bool> paths = new Dictionary<string, bool>
            {
                { "tree", true },
                { @"tree\cached", true },
                { @"tree\cached\hot.bin", false },
                { @"tree\cached\cold.bin", false },
                { @"tree\cached\deleted.bin", false },
                { @"tree\cached\recall-control.bin", false },
                { @"tree\cached\deleted-directory", true },
                { @"tree\cached\notification-control.bin", false },
                { @"tree\virtual-only", true },
                { "health.bin", false },
                { DehydrateBackupProcess.NotificationControl, false },
            };
            private readonly ConcurrentDictionary<Guid, EnumerationState> enumerations = new ConcurrentDictionary<Guid, EnumerationState>();
            private readonly string root;
            private VirtualizationInstance instance;
            private int dataCallbacks;
            private bool started;

            public Projection(string root)
            {
                this.root = root;
                for (int index = 0; index < 16; ++index)
                {
                    this.paths.Add($"health-{index}.bin", false);
                }

                this.instance = this.CreateInstance();
            }

            public ConcurrentQueue<string> Errors { get; } = new ConcurrentQueue<string>();
            public ConcurrentQueue<string> Notifications { get; } = new ConcurrentQueue<string>();
            public int DataCallbacks => Volatile.Read(ref this.dataCallbacks);

            public void Start()
            {
                this.instance ??= this.CreateInstance();
                Assert.That(this.instance.StartVirtualizing(this), Is.EqualTo(HResult.Ok));
                this.started = true;
            }

            public void Stop()
            {
                if (this.started)
                {
                    this.instance.StopVirtualizing();
                    this.instance = null;
                    this.started = false;
                }
            }

            public void Dispose()
            {
                this.Stop();
                this.instance?.Dispose();
            }

            private VirtualizationInstance CreateInstance()
            {
                VirtualizationInstance instance = new VirtualizationInstance(
                    this.root, poolThreadCount: 2, concurrentThreadCount: 2, enableNegativePathCache: false,
                    notificationMappings: new List<NotificationMapping>
                    {
                        new NotificationMapping(
                            NotificationType.PreDelete | NotificationType.FileRenamed | NotificationType.FileHandleClosedFileDeleted,
                            string.Empty),
                    });
                instance.OnQueryFileName = path => this.paths.ContainsKey(path) ? HResult.Ok : HResult.FileNotFound;
                instance.OnNotifyPreDelete = this.NotifyPreDelete;
                instance.OnNotifyFileRenamed = this.NotifyRenamed;
                instance.OnNotifyFileHandleClosedFileModifiedOrDeleted = this.NotifyClosed;
                return instance;
            }

            private bool NotifyPreDelete(string path, bool directory, uint processId, string image)
            {
                this.Notifications.Enqueue("PreDelete:" + path);
                return true;
            }

            private void NotifyRenamed(string path, string destination, bool directory, uint processId, string image, out NotificationType mask)
            {
                mask = NotificationType.UseExistingMask;
                this.Notifications.Enqueue("Renamed:" + path + "->" + destination);
            }

            private void NotifyClosed(string path, bool directory, bool modified, bool deleted, uint processId, string image)
            {
                if (deleted)
                {
                    this.Notifications.Enqueue("Deleted:" + path);
                }
            }

            public HResult WritePlaceholder(string path)
            {
                DateTime now = DateTime.UtcNow;
                bool directory = this.paths[path];
                HResult result = this.instance.WritePlaceholderInfo(
                    path, now, now, now, now, directory ? FileAttributes.Directory : FileAttributes.Archive,
                    directory ? 0 : Content.Length, directory, new byte[] { 1 }, new byte[] { 1 });
                if (result != HResult.Ok)
                {
                    this.Errors.Enqueue($"WritePlaceholderInfo({path}): {result}");
                }

                return result;
            }

            public HResult StartDirectoryEnumerationCallback(
                int commandId, Guid enumerationId, string virtualPath, uint triggeringProcessId, string triggeringProcessImageFileName)
            {
                this.enumerations[enumerationId] = new EnumerationState(virtualPath == @"tree\cached\deleted-directory");
                return HResult.Ok;
            }

            public HResult EndDirectoryEnumerationCallback(Guid enumerationId)
            {
                this.enumerations.TryRemove(enumerationId, out _);
                return HResult.Ok;
            }

            public HResult GetDirectoryEnumerationCallback(
                int commandId, Guid enumerationId, string filterFileName, bool restartScan, IDirectoryEnumerationResults results)
            {
                // Inject projected names except in the empty directory used to create a tombstone.
                EnumerationState enumeration = this.enumerations[enumerationId];
                string[] children = enumeration.Empty ? Array.Empty<string>() : new[] { "cached", "virtual-only" };
                int index = restartScan ? 0 : enumeration.Index;
                DateTime now = DateTime.UtcNow;
                while (index < children.Length)
                {
                    if (!results.Add(children[index], 0, true, FileAttributes.Directory, now, now, now, now))
                    {
                        break;
                    }

                    enumeration.Index = ++index;
                }

                return HResult.Ok;
            }

            private sealed class EnumerationState
            {
                public EnumerationState(bool empty)
                {
                    this.Empty = empty;
                }

                public bool Empty { get; }
                public int Index { get; set; }
            }

            public HResult GetPlaceholderInfoCallback(
                int commandId, string virtualPath, uint triggeringProcessId, string triggeringProcessImageFileName)
            {
                return this.paths.ContainsKey(virtualPath) ? this.WritePlaceholder(virtualPath) : HResult.FileNotFound;
            }

            public HResult GetFileDataCallback(
                int commandId, string virtualPath, ulong byteOffset, uint length, Guid dataStreamId,
                byte[] contentId, byte[] providerId, uint triggeringProcessId, string triggeringProcessImageFileName)
            {
                Interlocked.Increment(ref this.dataCallbacks);
                using (IWriteBuffer buffer = this.instance.CreateWriteBuffer((uint)Content.Length))
                {
                    buffer.Stream.Write(Content, 0, Content.Length);
                    HResult result = this.instance.WriteFileData(dataStreamId, buffer, 0, (uint)Content.Length);
                    if (result != HResult.Ok)
                    {
                        this.Errors.Enqueue($"WriteFileData({virtualPath}): {result}");
                    }

                    return result;
                }
            }
        }
    }
}
