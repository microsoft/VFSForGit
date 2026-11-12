using GVFS.Common.NamedPipes;
using GVFS.Platform.Windows;
using GVFS.Tests.Should;
using GVFS.UnitTests.Category;
using GVFS.UnitTests.Mock.Common;
using GVFS.UnitTests.Mock.Git;
using GVFS.UnitTests.Virtual;
using GVFS.UnitTests.Windows.Mock;
using GVFS.Virtualization.FileSystem;
using Microsoft.Windows.ProjFS;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Linq;
using GVFS.Common.Tracing;
using GVFS.Common;
using GVFS.Common.Git;
using GVFS.UnitTests.Mock.Virtualization.BlobSize;
using GVFS.Virtualization.Projection;
using System.Reflection;

namespace GVFS.UnitTests.Windows.Virtualization
{
    [TestFixture]
    public class WindowsFileSystemVirtualizerTests : TestsWithCommonRepo
    {
        private const uint TriggeringProcessId = 1;
        private const string TriggeringProcessImageFileName = "UnitTests";

        private static readonly Dictionary<HResult, FSResult> MappedHResults = new Dictionary<HResult, FSResult>()
        {
            { HResult.Ok, FSResult.Ok },
            { HResult.DirNotEmpty, FSResult.DirectoryNotEmpty },
            { HResult.FileNotFound, FSResult.FileOrPathNotFound },
            { HResult.PathNotFound, FSResult.FileOrPathNotFound },
            { (HResult)HResultExtensions.HResultFromNtStatus.IoReparseTagNotHandled, FSResult.IoReparseTagNotHandled },
            { HResult.VirtualizationInvalidOp, FSResult.VirtualizationInvalidOperation },
        };

        private static int numWorkThreads = 1;

        [TestCase(null, 0, false)]
        [TestCase("0", 0, false)]
        [TestCase("1", 1, false)]
        [TestCase("500", 500, false)]
        [TestCase(" 500 ", 500, false)]
        [TestCase("60000", 60000, false)]
        [TestCase("", 0, true)]
        [TestCase("true", 0, true)]
        [TestCase("false", 0, true)]
        [TestCase("-1", 0, true)]
        [TestCase("500.5", 0, true)]
        [TestCase("60001", 0, true)]
        [TestCase("2147483648", 0, true)]
        public void PlaceholderWaitConfigControlsAdmissionAndReportsTheEffectiveBudget(string value, int expectedMs, bool warning)
        {
            this.Repo.Repository.ConfigValues["gvfs.placeholder-lock-wait"] = "true";
            if (value != null)
            {
                this.Repo.Repository.ConfigValues["gvfs.placeholder-lock-wait-ms"] = value;
            }

            MockTracer tracer = (MockTracer)this.Repo.Context.Tracer;
            int previousWarnings = tracer.RelatedWarningEvents.Count;
            using (WindowsFileSystemVirtualizerTester tester = new WindowsFileSystemVirtualizerTester(this.Repo))
            {
                EventMetadata metadata = new EventMetadata();
                tester.WindowsVirtualizer.AddHeartbeatMetadata(metadata);
                ((int)metadata["PlaceholderLockWaitMs"]).ShouldEqual(expectedMs);
                metadata.ContainsKey("PlaceholderLockWaitEnabled").ShouldEqual(expectedMs > 0);
                metadata.ContainsKey("PlaceholderLockWaitStarted").ShouldEqual(expectedMs > 0);
                (tracer.RelatedWarningEvents.Count - previousWarnings).ShouldEqual(warning ? 1 : 0);
                if (warning)
                {
                    tracer.RelatedWarningEvents.Last().ShouldContain("gvfs.placeholder-lock-wait-ms");
                }

                if (expectedMs == 0)
                {
                    this.HoldCheckoutLock();
                    try
                    {
                        tester.MockVirtualization.RequiredCallbacks.GetPlaceholderInfoCallback(1, "test.txt", 1, "reader").ShouldEqual(HResult.FileNotFound);
                        tester.MockVirtualization.CommandCompletions.Count.ShouldEqual(0);
                    }
                    finally
                    {
                        this.Repo.Repository.GVFSLock.ReleaseLockHeldByExternalProcess(1234);
                    }
                }
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void PlaceholderWaitConfigReadFailuresWarnAndDisableTheFeature(bool invalidRepository)
        {
            MockTracer tracer = (MockTracer)this.Repo.Context.Tracer;
            int previousWarnings = tracer.RelatedWarningEvents.Count;
            WindowsFileSystemVirtualizer.ReadPlaceholderLockWaitMs(tracer, () =>
            {
                if (invalidRepository)
                {
                    throw new InvalidDataException("Config read failure");
                }

                throw new LibGit2Exception("Config read failure");
            }).ShouldEqual(0);
            (tracer.RelatedWarningEvents.Count - previousWarnings).ShouldEqual(1);
            tracer.RelatedWarningEvents.Last().ShouldContain("Config read failure", "gvfs.placeholder-lock-wait-ms");
        }

        [TestCase("placeholder-lock-wait-ms = 500", 500, 0)]
        [TestCase("placeholder-lock-wait-ms", 0, 1)]
        [TestCase("placeholder-lock-wait-ms = 60001", 0, 1)]
        public void PlaceholderWaitReadsTheNativeConfigIncludingValuelessEntries(string config, int expectedMs, int expectedWarnings)
        {
            string root = Path.Combine(Path.GetTempPath(), "PlaceholderWaitConfig" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(root, ".git", "objects"));
            Directory.CreateDirectory(Path.Combine(root, ".git", "refs", "heads"));
            try
            {
                File.WriteAllText(Path.Combine(root, ".git", "HEAD"), "ref: refs/heads/main\n");
                File.WriteAllText(Path.Combine(root, ".git", "config"),
                    "[core]\nrepositoryformatversion = 0\nbare = false\n[gvfs]\n" + config + "\n");
                MockTracer tracer = new MockTracer();
                WindowsFileSystemVirtualizer.ReadPlaceholderLockWaitMs(tracer, root).ShouldEqual(expectedMs);
                tracer.RelatedWarningEvents.Count.ShouldEqual(expectedWarnings);
                if (expectedWarnings > 0)
                {
                    tracer.RelatedWarningEvents.Last().ShouldContain("gvfs.placeholder-lock-wait-ms");
                }
            }
            finally
            {
                Directory.Delete(root, recursive: true);
            }
        }

        [TestCase(-1)]
        [TestCase(60001)]
        public void PlaceholderWaitConstructorRejectsOutOfRangeBudgets(int milliseconds)
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new WindowsFileSystemVirtualizer(
                this.Repo.Context, this.Repo.GitObjects, new MockVirtualizationInstance(), 1, placeholderLockWaitMs: milliseconds));
        }

        [TestCase(250)]
        [TestCase(500)]
        public void PlaceholderWaitUsesTheMountTimeBudgetForExpiration(int milliseconds)
        {
            using (WindowsFileSystemVirtualizerTester tester = new WindowsFileSystemVirtualizerTester(this.Repo, milliseconds))
            {
                this.Repo.Repository.ConfigValues["gvfs.placeholder-lock-wait-ms"] = "0";
                EventMetadata metadata = new EventMetadata();
                tester.WindowsVirtualizer.AddHeartbeatMetadata(metadata);
                ((int)metadata["PlaceholderLockWaitMs"]).ShouldEqual(milliseconds);
                this.HoldCheckoutLock();
                try
                {
                    long start = Environment.TickCount64;
                    tester.MockVirtualization.RequiredCallbacks.GetPlaceholderInfoCallback(1, "test.txt", 1, "reader").ShouldEqual(HResult.Pending);
                    tester.MockVirtualization.WaitForCompletionStatus().ShouldEqual((HResult)HResultExtensions.HResultFromNtStatus.FileNotAvailable);
                    long elapsed = Environment.TickCount64 - start;
                    (elapsed >= milliseconds - 25).ShouldBeTrue();
                    (elapsed < milliseconds + 1000).ShouldBeTrue();
                    tester.MockVirtualization.CreatedPlaceholders.Count.ShouldEqual(0);
                    EventMetadata nextInterval = new EventMetadata();
                    tester.WindowsVirtualizer.AddHeartbeatMetadata(nextInterval);
                    ((int)nextInterval["PlaceholderLockWaitMs"]).ShouldEqual(milliseconds);
                    ((long)nextInterval["PlaceholderLockWaitTimedOut"]).ShouldEqual(1);
                }
                finally
                {
                    this.Repo.Repository.GVFSLock.ReleaseLockHeldByExternalProcess(1234);
                }
            }
        }

        [TestCase]
        public void PlaceholderAncestryCacheSharesOneLookupAcrossOneHundredFiles()
        {
            string[] files = Enumerable.Range(1, 100).Select(id => "file" + id + ".txt").ToArray();
            this.Repo.Repository.ConfigValues[GVFSConstants.GitConfig.PlaceholderLockWaitMs] = "2000";
            using (WindowsFileSystemVirtualizerTester tester = new WindowsFileSystemVirtualizerTester(this.Repo, files))
            {
                tester.GitIndexProjection.ProjectionParseComplete = true;
                int lookups = 0;
                PlaceholderRequestProcessTree tree = new PlaceholderRequestProcessTree(
                    id => id, (_, _, _) => { Interlocked.Increment(ref lookups); return false; });
                tester.WindowsVirtualizer.IsRequestDescendant = tree.IsDescendant;
                this.HoldCheckoutLock();
                try
                {
                    for (int id = 1; id <= 100; ++id)
                    {
                        tester.MockVirtualization.RequiredCallbacks.GetPlaceholderInfoCallback(id, files[id - 1], 1, "reader").ShouldEqual(HResult.Pending);
                    }

                    lookups.ShouldEqual(1);
                    tester.WindowsVirtualizer.PrepareToStop();
                    tester.MockVirtualization.CommandCompletions.Count.ShouldEqual(100);
                    tester.MockVirtualization.CreatedPlaceholders.Count.ShouldEqual(0);
                }
                finally
                {
                    this.Repo.Repository.GVFSLock.ReleaseLockHeldByExternalProcess(1234);
                }
            }
        }

        [TestCase(true)]
        [TestCase(false)]
        public void PlaceholderAncestryCacheKeysBothIdentitiesAndTheLockAcquisition(bool related)
        {
            long readerStart = 20;
            long ownerStart = 10;
            int lookups = 0;
            PlaceholderRequestProcessTree tree = new PlaceholderRequestProcessTree(
                id => id == 10 ? ownerStart : readerStart,
                (_, _, _) => { ++lookups; return related; });
            NamedPipeMessages.LockData holder = new NamedPipeMessages.LockData(10, false, false, "git checkout", string.Empty);
            tree.IsDescendant(20, holder, 10).ShouldEqual(related);
            tree.IsDescendant(20, holder, 10).ShouldEqual(related);
            lookups.ShouldEqual(1);
            ++readerStart;
            tree.IsDescendant(20, holder, 10).ShouldEqual(related);
            lookups.ShouldEqual(2);
            holder = new NamedPipeMessages.LockData(10, false, false, "git checkout", string.Empty);
            tree.IsDescendant(20, holder, 10).ShouldEqual(related);
            lookups.ShouldEqual(3);
            ++ownerStart;
            tree.IsDescendant(20, holder, 10).ShouldEqual(null);
            lookups.ShouldEqual(3);
            tree.IsDescendant(20, holder, ownerStart).ShouldEqual(related);
            lookups.ShouldEqual(4);
        }

        [TestCase]
        public void PlaceholderAncestryCacheRetriesUnknownAndFailedLookups()
        {
            int lookups = 0;
            PlaceholderRequestProcessTree tree = new PlaceholderRequestProcessTree(
                id => id, (_, _, _) =>
                {
                    ++lookups;
                    if (lookups == 1)
                    {
                        return null;
                    }

                    if (lookups == 2)
                    {
                        throw new IOException("Ancestry probe failure");
                    }

                    return false;
                });
            NamedPipeMessages.LockData holder = new NamedPipeMessages.LockData(10, false, false, "git checkout", string.Empty);
            tree.IsDescendant(20, holder, 10).ShouldEqual(null);
            Assert.Throws<IOException>(() => tree.IsDescendant(20, holder, 10));
            tree.IsDescendant(20, holder, 10).ShouldEqual(false);
            tree.IsDescendant(20, holder, 10).ShouldEqual(false);
            lookups.ShouldEqual(3);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void PlaceholderAncestryCacheRejectsAnIdentityChangeDuringLookupAndAnUnreadableHit(bool ownerChanges)
        {
            long? readerStart = 20;
            long? ownerStart = 10;
            int lookups = 0;
            PlaceholderRequestProcessTree tree = new PlaceholderRequestProcessTree(
                id => id == 10 ? ownerStart : readerStart,
                (_, _, _) =>
                {
                    ++lookups;
                    if (ownerChanges)
                    {
                        ownerStart = 11;
                    }
                    else
                    {
                        readerStart = 21;
                    }

                    return false;
                });
            NamedPipeMessages.LockData holder = new NamedPipeMessages.LockData(10, false, false, "git checkout", string.Empty);
            tree.IsDescendant(20, holder, 10).ShouldEqual(null);
            tree.IsDescendant(20, holder, ownerStart).ShouldEqual(false);
            lookups.ShouldEqual(2);
            if (ownerChanges)
            {
                ownerStart = null;
            }
            else
            {
                readerStart = null;
            }

            tree.IsDescendant(20, holder, ownerChanges ? 11 : 10).ShouldEqual(null);
            lookups.ShouldEqual(2);
        }

        [TestCase]
        public void PlaceholderAncestryCacheEvictsTheLeastRecentlyUsedIdentity()
        {
            int lookups = 0;
            PlaceholderRequestProcessTree tree = new PlaceholderRequestProcessTree(
                id => id, (_, _, _) => { ++lookups; return false; });
            NamedPipeMessages.LockData holder = new NamedPipeMessages.LockData(10, false, false, "git checkout", string.Empty);
            for (uint id = 200; id < 264; ++id)
            {
                tree.IsDescendant(id, holder, 10).ShouldEqual(false);
            }

            lookups.ShouldEqual(64);
            tree.IsDescendant(200, holder, 10).ShouldEqual(false);
            tree.IsDescendant(264, holder, 10).ShouldEqual(false);
            tree.IsDescendant(200, holder, 10).ShouldEqual(false);
            lookups.ShouldEqual(65);
            tree.IsDescendant(201, holder, 10).ShouldEqual(false);
            lookups.ShouldEqual(66);
        }

        [TestCase]
        public void EvictedUnknownAncestryLookupCannotRemoveItsReplacement()
        {
            using (ManualResetEventSlim lookupStarted = new ManualResetEventSlim(false))
            using (ManualResetEventSlim releaseLookup = new ManualResetEventSlim(false))
            {
                int lookups = 0;
                PlaceholderRequestProcessTree tree = new PlaceholderRequestProcessTree(
                    id => id, (id, _, _) =>
                    {
                        int count = Interlocked.Increment(ref lookups);
                        if (count == 1)
                        {
                            lookupStarted.Set();
                            releaseLookup.Wait(TimeSpan.FromSeconds(10)).ShouldBeTrue();
                            return null;
                        }

                        return false;
                    });
                NamedPipeMessages.LockData holder = new NamedPipeMessages.LockData(10, false, false, "git checkout", string.Empty);
                Task<bool?> first = Task.Factory.StartNew(
                    () => tree.IsDescendant(200, holder, 10), TaskCreationOptions.LongRunning);
                try
                {
                    lookupStarted.Wait(TimeSpan.FromSeconds(10)).ShouldBeTrue();
                    for (uint id = 201; id <= 264; ++id)
                    {
                        tree.IsDescendant(id, holder, 10).ShouldEqual(false);
                    }

                    tree.IsDescendant(200, holder, 10).ShouldEqual(false);
                }
                finally
                {
                    releaseLookup.Set();
                    first.Wait(TimeSpan.FromSeconds(10)).ShouldBeTrue();
                }

                first.Result.ShouldEqual(null);
                tree.IsDescendant(200, holder, 10).ShouldEqual(false);
                lookups.ShouldEqual(66);
            }
        }

        [TestCase]
        public void PlaceholderAncestryCacheCoalescesConcurrentMissesWithoutBlockingOtherIdentities()
        {
            using (ManualResetEventSlim lookupStarted = new ManualResetEventSlim(false))
            using (ManualResetEventSlim releaseLookup = new ManualResetEventSlim(false))
            using (CountdownEvent callersStarted = new CountdownEvent(8))
            {
                int lookups = 0;
                PlaceholderRequestProcessTree tree = new PlaceholderRequestProcessTree(
                    id => id, (id, _, _) =>
                    {
                        Interlocked.Increment(ref lookups);
                        if (id == 20)
                        {
                            lookupStarted.Set();
                            releaseLookup.Wait(TimeSpan.FromSeconds(10)).ShouldBeTrue();
                        }

                        return false;
                    });
                NamedPipeMessages.LockData holder = new NamedPipeMessages.LockData(10, false, false, "git checkout", string.Empty);
                Task[] calls = Enumerable.Range(0, 8).Select(_ => Task.Factory.StartNew(
                    () =>
                    {
                        callersStarted.Signal();
                        tree.IsDescendant(20, holder, 10).ShouldEqual(false);
                    }, TaskCreationOptions.LongRunning)).ToArray();
                try
                {
                    callersStarted.Wait(TimeSpan.FromSeconds(10)).ShouldBeTrue();
                    lookupStarted.Wait(TimeSpan.FromSeconds(10)).ShouldBeTrue();
                    Task<bool?> other = Task.Factory.StartNew(
                        () => tree.IsDescendant(21, holder, 10), TaskCreationOptions.LongRunning);
                    other.Wait(TimeSpan.FromSeconds(10)).ShouldBeTrue();
                    other.Result.ShouldEqual(false);
                }
                finally
                {
                    releaseLookup.Set();
                    Task.WaitAll(calls, TimeSpan.FromSeconds(10)).ShouldBeTrue();
                }

                lookups.ShouldEqual(2);
            }
        }

        [TestCase(1234u, false)]
        [TestCase(2345u, true)]
        [TestCase(2345u, null)]
        public void PlaceholderWaitPreservesOwnerDependentAndUnknownRejections(uint processId, bool? related)
        {
            using (WindowsFileSystemVirtualizerTester tester = new WindowsFileSystemVirtualizerTester(this.Repo, true))
            {
                tester.WindowsVirtualizer.IsRequestDescendant = (_, _, _) => related;
                this.HoldCheckoutLock();
                try
                {
                    tester.MockVirtualization.RequiredCallbacks.GetPlaceholderInfoCallback(1, "test.txt", processId, "reader").ShouldEqual(HResult.Pending);
                    tester.MockVirtualization.WaitForCompletionStatus().ShouldEqual(HResult.FileNotFound);
                    tester.BackgroundTaskRunner.Count.ShouldEqual(1);
                    tester.GitIndexProjection.FileInfoRequests.ShouldEqual(0);
                    EventMetadata metadata = new EventMetadata();
                    tester.WindowsVirtualizer.AddHeartbeatMetadata(metadata);
                    ((long)metadata["PlaceholderLockWaitStarted"]).ShouldEqual(0);
                }
                finally
                {
                    this.Repo.Repository.GVFSLock.ReleaseLockHeldByExternalProcess(1234);
                }
            }
        }

        [TestCase]
        public void PlaceholderRequestAncestryHandlesDescendantsUnrelatedAndUnknownChains()
        {
            Dictionary<uint, uint> parents = new Dictionary<uint, uint>
            {
                { 1, 0 }, { 10, 1 }, { 11, 10 }, { 12, 11 }, { 20, 1 },
                { 30, 99 }, { 40, 41 }, { 41, 40 },
            };
            PlaceholderRequestProcessTree.IsDescendant(parents, 12, 10).ShouldEqual(true);
            PlaceholderRequestProcessTree.IsDescendant(parents, 20, 10).ShouldEqual(false);
            PlaceholderRequestProcessTree.IsDescendant(parents, 1, 10).ShouldEqual(false);
            PlaceholderRequestProcessTree.IsDescendant(parents, 30, 10).ShouldEqual(null);
            PlaceholderRequestProcessTree.IsDescendant(parents, 40, 10).ShouldEqual(null);
            PlaceholderRequestProcessTree.IsDescendant(parents, 12, 99).ShouldEqual(null);
            parents[1] = 99;
            PlaceholderRequestProcessTree.IsDescendant(parents, 20, 10).ShouldEqual(false);
            PlaceholderRequestProcessTree.IsDescendant(parents, 1, 10).ShouldEqual(false);
            PlaceholderRequestProcessTree.IsDescendant(parents, 12, 10).ShouldEqual(true);
            PlaceholderRequestProcessTree.IsDescendant(parents, 30, 10).ShouldEqual(null);
        }

        [TestCase(50L, false)]
        [TestCase(500L, null)]
        [TestCase(null, null)]
        public void PlaceholderRequestAncestryRejectsMissingOrReusedSharedParents(long? parentStart, bool? expected)
        {
            Dictionary<uint, uint> parents = new Dictionary<uint, uint>
            {
                { 1, 99 }, { 10, 1 }, { 20, 1 },
            };
            PlaceholderRequestProcessTree.IsDescendant(
                parents, 20, 10, id => id == 10 ? 100L : id == 20 ? 200L : id == 1 ? parentStart : null).ShouldEqual(expected);
        }

        [TestCase("none")]
        [TestCase("gvfs")]
        [TestCase("status")]
        public void UncontendedAndAllowedFirstOpensDoNotUseTheWaitQueue(string holder)
        {
            using (WindowsFileSystemVirtualizerTester tester = new WindowsFileSystemVirtualizerTester(this.Repo, true))
            {
                if (holder == "gvfs")
                {
                    this.Repo.Repository.GVFSLock.TryAcquireLockForGVFS().ShouldBeTrue();
                }
                else if (holder == "status")
                {
                    ((MockPlatform)GVFSPlatform.Instance).ActiveProcesses.Add(1234);
                    this.Repo.Repository.GVFSLock.TryAcquireLockForExternalRequestor(
                        new NamedPipeMessages.LockData(1234, false, false, "git status", string.Empty), out _).ShouldBeTrue();
                }

                tester.GitIndexProjection.BlockGetProjectedFileInfo(willWaitForRequest: true);
                try
                {
                    for (int id = 1; id <= 129; ++id)
                    {
                        tester.MockVirtualization.RequiredCallbacks.GetPlaceholderInfoCallback(id, "test.txt", 1, "reader").ShouldEqual(HResult.Pending);
                    }

                    tester.GitIndexProjection.WaitForGetProjectedFileInfo();
                    tester.MockVirtualization.CommandCompletions.Count.ShouldEqual(0);
                    tester.GitIndexProjection.UnblockGetProjectedFileInfo();
                    SpinWait.SpinUntil(() => tester.MockVirtualization.CommandCompletions.Count == 129, 10000).ShouldBeTrue();
                    tester.MockVirtualization.CommandCompletions.All(entry => entry.Result == HResult.Ok).ShouldBeTrue();
                    ((MockTracer)this.Repo.Context.Tracer).SnapshotRelatedEvents()
                        .Any(entry => entry.Name == "PlaceholderLockWait").ShouldBeFalse();
                }
                finally
                {
                    tester.GitIndexProjection.UnblockGetProjectedFileInfo();
                    if (holder == "gvfs")
                    {
                        this.Repo.Repository.GVFSLock.ReleaseLockHeldByGVFS();
                    }
                    else if (holder == "status")
                    {
                        this.Repo.Repository.GVFSLock.ReleaseLockHeldByExternalProcess(1234);
                    }
                }
            }
        }

        [TestCase(".gitattributes", false)]
        [TestCase(".gitignore", false)]
        [TestCase("folder", true)]
        public void PlaceholderWaitPreservesSpecialFileAndFolderAllowances(string path, bool folder)
        {
            this.Repo.Repository.ConfigValues["gvfs.placeholder-lock-wait-ms"] = "2000";
            using (WindowsFileSystemVirtualizerTester tester = new WindowsFileSystemVirtualizerTester(this.Repo, new[] { path }))
            {
                tester.GitIndexProjection.ProjectedPathIsFolder = folder;
                this.HoldCheckoutLock();
                try
                {
                    tester.MockVirtualization.RequiredCallbacks.GetPlaceholderInfoCallback(1, path, 1, "reader").ShouldEqual(HResult.Pending);
                    tester.MockVirtualization.WaitForCompletionStatus().ShouldEqual(HResult.Ok);
                    tester.MockVirtualization.CreatedPlaceholders.ShouldContain(created => created == path);
                }
                finally
                {
                    this.Repo.Repository.GVFSLock.ReleaseLockHeldByExternalProcess(1234);
                }
            }
        }

        [TestCase]
        public void RealProjectionBarrierRejectsInvalidGenerationsAndHoldsTheReadLockThroughCreation()
        {
            using (WindowsFileSystemVirtualizerTester tester = new WindowsFileSystemVirtualizerTester(this.Repo))
            {
                tester.MockSparseDb.Setup(sparse => sparse.GetAll()).Returns(new HashSet<string>());
                using GitIndexProjection projection = new GitIndexProjection(
                    this.Repo.Context, this.Repo.GitObjects, new MockBlobSizes(), RepoMetadata.Instance,
                    tester.Virtualizer, tester.MockPlaceholderDb.Object, tester.MockSparseDb.Object, null);
                ManualResetEventSlim parsed = (ManualResetEventSlim)typeof(GitIndexProjection)
                    .GetField("projectionParseComplete", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(projection);
                ReaderWriterLockSlim projectionLock = (ReaderWriterLockSlim)typeof(GitIndexProjection)
                    .GetField("projectionReadWriteLock", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(projection);
                bool created = false;
                projection.TryCreatePlaceholder(projection.ProjectionGeneration, () => created = true).ShouldBeFalse();
                parsed.Set();
                long oldGeneration = projection.ProjectionGeneration;
                projection.TryCreatePlaceholder(oldGeneration, () =>
                {
                    Task<bool> writer = Task.Factory.StartNew(() =>
                    {
                        bool entered = projectionLock.TryEnterWriteLock(0);
                        if (entered)
                        {
                            projectionLock.ExitWriteLock();
                        }

                        return entered;
                    }, TaskCreationOptions.LongRunning);
                    writer.Wait(TimeSpan.FromSeconds(10)).ShouldBeTrue();
                    writer.Result.ShouldBeFalse();
                    created = true;
                }).ShouldBeTrue();
                created.ShouldBeTrue();
                this.Repo.FileSystem.RootDirectory.CreateFile(
                    Path.Combine(this.Repo.Context.Enlistment.DotGVFSRoot, GitIndexProjection.ProjectionIndexBackupName),
                    string.Empty,
                    createDirectories: true);
                projection.InvalidateProjection();
                projection.ProjectionGeneration.ShouldEqual(oldGeneration + 1);
                parsed.Set();
                created = false;
                projection.TryCreatePlaceholder(oldGeneration, () => created = true).ShouldBeFalse();
                created.ShouldBeFalse();
            }
        }

        [TestCase]
        public void CancellationFreesPlaceholderWaitSlotsWithoutShutdown()
        {
            using (WindowsFileSystemVirtualizerTester tester = new WindowsFileSystemVirtualizerTester(this.Repo, true))
            {
                MockTracer tracer = (MockTracer)this.Repo.Context.Tracer;
                this.HoldCheckoutLock();
                try
                {
                    tester.MockVirtualization.RequiredCallbacks.GetPlaceholderInfoCallback(1, "test.txt", 1, "reader").ShouldEqual(HResult.Pending);
                    tester.MockVirtualization.OnCancelCommand(1);
                    SpinWait.SpinUntil(() => tracer.SnapshotRelatedEvents().Any(
                        entry => entry.Name == "PlaceholderLockWait" && (string)entry.Metadata["outcome"] == "Cancelled"), 10000).ShouldBeTrue();
                    for (int id = 2; id <= 129; ++id)
                    {
                        tester.MockVirtualization.RequiredCallbacks.GetPlaceholderInfoCallback(id, "test.txt", 1, "reader").ShouldEqual(HResult.Pending);
                    }

                    tester.MockVirtualization.CommandCompletions.Count.ShouldEqual(0);
                    for (int id = 2; id <= 129; ++id)
                    {
                        tester.MockVirtualization.OnCancelCommand(id);
                    }

                    SpinWait.SpinUntil(() => tracer.SnapshotRelatedEvents().Count(
                        entry => entry.Name == "PlaceholderLockWait" && (string)entry.Metadata["outcome"] == "Cancelled") == 129, 10000).ShouldBeTrue();
                    tester.MockVirtualization.CreatedPlaceholders.Count.ShouldEqual(0);
                }
                finally
                {
                    this.Repo.Repository.GVFSLock.ReleaseLockHeldByExternalProcess(1234);
                }
            }
        }

        [TestCase]
        public void CancellationDuringScheduledMetadataDoesNotCreateAPlaceholder()
        {
            using (WindowsFileSystemVirtualizerTester tester = new WindowsFileSystemVirtualizerTester(this.Repo, true))
            {
                tester.GitIndexProjection.BlockGetProjectedFileInfo(willWaitForRequest: true);
                this.HoldCheckoutLock();
                tester.MockVirtualization.RequiredCallbacks.GetPlaceholderInfoCallback(1, "test.txt", 1, "reader").ShouldEqual(HResult.Pending);
                this.Repo.Repository.GVFSLock.ReleaseLockHeldByExternalProcess(1234).ShouldBeTrue();
                try
                {
                    tester.GitIndexProjection.WaitForGetProjectedFileInfo();
                    tester.MockVirtualization.OnCancelCommand(1);
                }
                finally
                {
                    tester.GitIndexProjection.UnblockGetProjectedFileInfo();
                }

                MockTracer tracer = (MockTracer)this.Repo.Context.Tracer;
                SpinWait.SpinUntil(() => tracer.SnapshotRelatedEvents().Any(
                    entry => entry.Name == "PlaceholderLockWait" && (string)entry.Metadata["outcome"] == "Cancelled"), 10000).ShouldBeTrue();
                tester.MockVirtualization.CommandCompletions.Count.ShouldEqual(0);
                tester.MockVirtualization.CreatedPlaceholders.Count.ShouldEqual(0);
            }
        }

        [TestCase]
        public void ShutdownCompletesAnUnfinishedRetryBeforeTheStopPass()
        {
            using (WindowsFileSystemVirtualizerTester tester = new WindowsFileSystemVirtualizerTester(this.Repo, true))
            {
                this.HoldCheckoutLock();
                try
                {
                    tester.MockVirtualization.RequiredCallbacks.GetPlaceholderInfoCallback(1, "test.txt", 1, "reader").ShouldEqual(HResult.Pending);
                    BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
                    object queue = typeof(WindowsFileSystemVirtualizer).GetField("placeholderWaitQueue", flags).GetValue(tester.WindowsVirtualizer);
                    object gate = queue.GetType().GetField("gate", flags).GetValue(queue);
                    lock (gate)
                    {
                        System.Collections.IDictionary requests = (System.Collections.IDictionary)queue.GetType().GetField("requests", flags).GetValue(queue);
                        object request = requests[1];
                        request.GetType().GetProperty("Scheduled").SetValue(request, true);
                        queue.GetType().GetField("stopping", flags).SetValue(queue, true);
                        queue.GetType().GetMethod("FinishAttempt", flags).Invoke(queue, new object[] { request, false, "Completed" });
                        tester.MockVirtualization.WaitForCompletionStatus().ShouldEqual((HResult)HResultExtensions.HResultFromNtStatus.DeviceNotReady);
                    }

                    tester.WindowsVirtualizer.PrepareToStop();
                    tester.MockVirtualization.CommandCompletions.Count.ShouldEqual(1);
                    tester.MockVirtualization.CreatedPlaceholders.Count.ShouldEqual(0);
                }
                finally
                {
                    this.Repo.Repository.GVFSLock.ReleaseLockHeldByExternalProcess(1234);
                }
            }
        }

        [TestCase]
        public void ShutdownCannotCompleteSuccessWhenCancellationSkipsThePlaceholderWrite()
        {
            using (WindowsFileSystemVirtualizerTester tester = new WindowsFileSystemVirtualizerTester(this.Repo, true))
            using (ManualResetEventSlim stopPaused = new ManualResetEventSlim(false))
            using (ManualResetEventSlim releaseStop = new ManualResetEventSlim(false))
            {
                MockTracer tracer = (MockTracer)this.Repo.Context.Tracer;
                tracer.RelatedEventCallback = (name, metadata) =>
                {
                    if (name == "PlaceholderLockWait" && (string)metadata["outcome"] == "Stopping")
                    {
                        stopPaused.Set();
                        releaseStop.Wait();
                    }
                };
                tester.GitIndexProjection.BlockGetProjectedFileInfo(willWaitForRequest: true);
                this.HoldCheckoutLock();
                tester.MockVirtualization.RequiredCallbacks.GetPlaceholderInfoCallback(1, "test.txt", 1, "reader").ShouldEqual(HResult.Pending);
                this.Repo.Repository.GVFSLock.ReleaseLockHeldByExternalProcess(1234).ShouldBeTrue();
                tester.GitIndexProjection.WaitForGetProjectedFileInfo();
                Task stopping = Task.Factory.StartNew(tester.WindowsVirtualizer.PrepareToStop, TaskCreationOptions.LongRunning);
                try
                {
                    stopPaused.Wait(TimeSpan.FromSeconds(10)).ShouldBeTrue();
                    tester.GitIndexProjection.UnblockGetProjectedFileInfo();
                    tester.InvokeGetFileDataCallback(commandId: 2);
                    tester.MockVirtualization.WaitForCompletionStatus().ShouldEqual(HResult.Ok);
                    tester.MockVirtualization.CommandCompletions.Single().CommandId.ShouldEqual(2);
                }
                finally
                {
                    tester.GitIndexProjection.UnblockGetProjectedFileInfo();
                    releaseStop.Set();
                    stopping.Wait(TimeSpan.FromSeconds(10)).ShouldBeTrue();
                    tracer.RelatedEventCallback = null;
                }

                tester.MockVirtualization.WaitForCompletionStatus().ShouldEqual((HResult)HResultExtensions.HResultFromNtStatus.DeviceNotReady);
                tester.MockVirtualization.CommandCompletions.Count.ShouldEqual(2);
                tester.MockVirtualization.CreatedPlaceholders.Count.ShouldEqual(0);
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void DeferredNullMetadataRevalidatesProjectionBeforeReportingAbsence(bool parseIncomplete)
        {
            using (WindowsFileSystemVirtualizerTester tester = new WindowsFileSystemVirtualizerTester(this.Repo, true))
            {
                tester.GitIndexProjection.BlockGetProjectedFileInfo(willWaitForRequest: true);
                this.HoldCheckoutLock();
                tester.MockVirtualization.RequiredCallbacks.GetPlaceholderInfoCallback(1, "test.txt", 1, "reader").ShouldEqual(HResult.Pending);
                this.Repo.Repository.GVFSLock.ReleaseLockHeldByExternalProcess(1234).ShouldBeTrue();
                try
                {
                    tester.GitIndexProjection.WaitForGetProjectedFileInfo();
                    tester.GitIndexProjection.RemoveProjectedFile("test.txt");
                    if (parseIncomplete)
                    {
                        tester.GitIndexProjection.ProjectionParseComplete = false;
                    }
                    else
                    {
                        ++tester.GitIndexProjection.Generation;
                    }
                }
                finally
                {
                    tester.GitIndexProjection.UnblockGetProjectedFileInfo();
                }

                tester.MockVirtualization.WaitForCompletionStatus().ShouldEqual(parseIncomplete ?
                    (HResult)HResultExtensions.HResultFromNtStatus.FileNotAvailable : HResult.FileNotFound);
                tester.GitIndexProjection.FileInfoRequests.ShouldEqual(parseIncomplete ? 1 : 2);
                tester.MockVirtualization.CreatedPlaceholders.Count.ShouldEqual(0);
            }
        }

        [TestCase]
        public void ProjectionWaitDoesNotScheduleMetadataWorkersUntilReady()
        {
            using (WindowsFileSystemVirtualizerTester tester = new WindowsFileSystemVirtualizerTester(this.Repo, true))
            {
                this.HoldCheckoutLock();
                tester.MockVirtualization.RequiredCallbacks.GetPlaceholderInfoCallback(1, "test.txt", 1, "reader").ShouldEqual(HResult.Pending);
                tester.GitIndexProjection.ProjectionParseComplete = false;
                this.Repo.Repository.GVFSLock.ReleaseLockHeldByExternalProcess(1234).ShouldBeTrue();
                tester.MockVirtualization.WaitForCompletionStatus().ShouldEqual((HResult)HResultExtensions.HResultFromNtStatus.FileNotAvailable);
                tester.GitIndexProjection.FileInfoRequests.ShouldEqual(0);
            }
        }

        [TestCase]
        public void EnabledPlaceholderWaitKeepsAbsenceAndShutdownAsDistinctFailures()
        {
            using (WindowsFileSystemVirtualizerTester tester = new WindowsFileSystemVirtualizerTester(this.Repo, true))
            {
                tester.MockVirtualization.RequiredCallbacks.GetPlaceholderInfoCallback(1, "not-projected.txt", 1, "reader").ShouldEqual(HResult.Pending);
                tester.MockVirtualization.WaitForCompletionStatus().ShouldEqual(HResult.FileNotFound);
                tester.WindowsVirtualizer.PrepareToStop();
                tester.MockVirtualization.RequiredCallbacks.GetPlaceholderInfoCallback(2, "test.txt", 1, "reader").ShouldEqual(HResult.Pending);
                tester.MockVirtualization.WaitForCompletionStatus().ShouldEqual((HResult)HResultExtensions.HResultFromNtStatus.DeviceNotReady);
                tester.MockVirtualization.CommandCompletions.Count.ShouldEqual(2);
            }
        }

        [TestCase]
        public void DuplicateRegistrationDoesNotReplaceThePendingPlaceholderRequest()
        {
            using (WindowsFileSystemVirtualizerTester tester = new WindowsFileSystemVirtualizerTester(this.Repo, true))
            {
                this.HoldCheckoutLock();
                try
                {
                    tester.MockVirtualization.RequiredCallbacks.GetPlaceholderInfoCallback(1, "test.txt", 1, "reader").ShouldEqual(HResult.Pending);
                    tester.MockVirtualization.RequiredCallbacks.GetPlaceholderInfoCallback(1, "test.txt", 1, "reader").ShouldEqual(HResult.InternalError);
                    tester.MockVirtualization.CommandCompletions.Count.ShouldEqual(0);
                    tester.WindowsVirtualizer.PrepareToStop();
                    tester.MockVirtualization.WaitForCompletionStatus().ShouldEqual((HResult)HResultExtensions.HResultFromNtStatus.DeviceNotReady);
                    tester.MockVirtualization.CommandCompletions.Count.ShouldEqual(1);
                }
                finally
                {
                    this.Repo.Repository.GVFSLock.ReleaseLockHeldByExternalProcess(1234);
                }
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void PlaceholderLockWaitPreservesDisabledBehaviorAndResumesAfterRelease(bool enabled)
        {
            using (WindowsFileSystemVirtualizerTester tester = new WindowsFileSystemVirtualizerTester(this.Repo, enabled))
            {
                this.HoldCheckoutLock();
                try
                {
                    HResult result = tester.MockVirtualization.RequiredCallbacks.GetPlaceholderInfoCallback(1, "test.txt", 1, "reader");
                    result.ShouldEqual(enabled ? HResult.Pending : HResult.FileNotFound);
                    tester.MockVirtualization.CreatedPlaceholders.Count.ShouldEqual(0);
                }
                finally
                {
                    this.Repo.Repository.GVFSLock.ReleaseLockHeldByExternalProcess(1234).ShouldBeTrue();
                }

                if (enabled)
                {
                    tester.MockVirtualization.WaitForCompletionStatus().ShouldEqual(HResult.Ok);
                    tester.GitIndexProjection.PlaceholdersCreated.ShouldContain(path => path == "test.txt");
                    MockTracer tracer = (MockTracer)this.Repo.Context.Tracer;
                    SpinWait.SpinUntil(() => tracer.SnapshotRelatedEvents().Any(
                        entry => entry.Name == "PlaceholderLockWait" && (string)entry.Metadata["outcome"] == "Completed"), 10000).ShouldBeTrue();
                    EventMetadata metadata = new EventMetadata();
                    tester.WindowsVirtualizer.AddHeartbeatMetadata(metadata);
                    ((long)metadata["PlaceholderLockWaitSucceeded"]).ShouldEqual(1);
                    EventMetadata nextInterval = new EventMetadata();
                    tester.WindowsVirtualizer.AddHeartbeatMetadata(nextInterval);
                    ((long)nextInterval["PlaceholderLockWaitSucceeded"]).ShouldEqual(0);
                }
                else
                {
                    tester.BackgroundTaskRunner.Count.ShouldEqual(1);
                }
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void PlaceholderLockWaitExpiresWithTransientFailure(bool ownerExited)
        {
            using (WindowsFileSystemVirtualizerTester tester = new WindowsFileSystemVirtualizerTester(this.Repo, true))
            {
                this.HoldCheckoutLock();
                try
                {
                    long start = Environment.TickCount64;
                    tester.MockVirtualization.RequiredCallbacks.GetPlaceholderInfoCallback(1, "test.txt", 1, "reader").ShouldEqual(HResult.Pending);
                    if (ownerExited)
                    {
                        ((MockPlatform)GVFSPlatform.Instance).ActiveProcesses.Remove(1234);
                    }

                    tester.MockVirtualization.WaitForCompletionStatus().ShouldEqual(unchecked((HResult)0xD0000467));
                    (Environment.TickCount64 - start >= 1800).ShouldBeTrue();
                    tester.MockVirtualization.CreatedPlaceholders.Count.ShouldEqual(0);
                    tester.BackgroundTaskRunner.Count.ShouldEqual(0);
                    EventMetadata metadata = new EventMetadata();
                    tester.WindowsVirtualizer.AddHeartbeatMetadata(metadata);
                    ((long)metadata["PlaceholderLockWaitStarted"]).ShouldEqual(1);
                    ((long)metadata["PlaceholderLockWaitTimedOut"]).ShouldEqual(1);
                    EventMetadata nextInterval = new EventMetadata();
                    tester.WindowsVirtualizer.AddHeartbeatMetadata(nextInterval);
                    ((long)nextInterval["PlaceholderLockWaitStarted"]).ShouldEqual(0);
                    ((long)nextInterval["PlaceholderLockWaitTimedOut"]).ShouldEqual(0);

                    if (ownerExited)
                    {
                        this.Repo.Repository.GVFSLock.GetLockedGitCommand().ShouldEqual("git checkout-index --stdin");
                        this.Repo.Repository.GVFSLock.IsLockAvailableForExternalRequestor(out _).ShouldBeTrue();
                        tester.MockVirtualization.RequiredCallbacks.GetPlaceholderInfoCallback(2, "test.txt", 1, "reader").ShouldEqual(HResult.Pending);
                        tester.MockVirtualization.WaitForCompletionStatus().ShouldEqual(HResult.Ok);
                    }

                    // The single file/network worker remains available for an existing placeholder.
                    tester.InvokeGetFileDataCallback();
                    tester.MockVirtualization.WaitForCompletionStatus().ShouldEqual(HResult.Ok);
                }
                finally
                {
                    this.Repo.Repository.GVFSLock.ReleaseLockHeldByExternalProcess(1234);
                }
            }
        }

        [TestCase]
        public void PlaceholderLockWaitCancellationDoesNotCreateAfterRelease()
        {
            using (WindowsFileSystemVirtualizerTester tester = new WindowsFileSystemVirtualizerTester(this.Repo, true))
            {
                this.HoldCheckoutLock();
                tester.MockVirtualization.RequiredCallbacks.GetPlaceholderInfoCallback(1, "test.txt", 1, "reader").ShouldEqual(HResult.Pending);
                tester.MockVirtualization.OnCancelCommand(1);
                this.Repo.Repository.GVFSLock.ReleaseLockHeldByExternalProcess(1234).ShouldBeTrue();
                tester.WindowsVirtualizer.PrepareToStop();
                tester.MockVirtualization.CreatedPlaceholders.Count.ShouldEqual(0);
            }
        }

        [TestCase]
        public void PlaceholderLockWaitRegistersCancellationBeforeProjectionLookup()
        {
            using (WindowsFileSystemVirtualizerTester tester = new WindowsFileSystemVirtualizerTester(this.Repo, true))
            {
                tester.GitIndexProjection.BlockIsPathProjected(willWaitForRequest: true);
                Task cancel = Task.Factory.StartNew(
                    () =>
                    {
                        tester.GitIndexProjection.WaitForIsPathProjected();
                        tester.MockVirtualization.OnCancelCommand(1);
                        tester.GitIndexProjection.UnblockIsPathProjected();
                    },
                    TaskCreationOptions.LongRunning);
                tester.MockVirtualization.RequiredCallbacks.GetPlaceholderInfoCallback(1, "test.txt", 1, "reader").ShouldEqual(HResult.Pending);
                cancel.Wait(TimeSpan.FromSeconds(10)).ShouldBeTrue();
                tester.WindowsVirtualizer.PrepareToStop();
                tester.MockVirtualization.CreatedPlaceholders.Count.ShouldEqual(0);
                tester.MockVirtualization.CommandCompletions.Count.ShouldEqual(0);
            }
        }

        [TestCase]
        public void PlaceholderLockWaitStopsWithoutWaitingForGit()
        {
            using (WindowsFileSystemVirtualizerTester tester = new WindowsFileSystemVirtualizerTester(this.Repo, true))
            {
                this.HoldCheckoutLock();
                try
                {
                    tester.MockVirtualization.RequiredCallbacks.GetPlaceholderInfoCallback(1, "test.txt", 1, "reader").ShouldEqual(HResult.Pending);
                    tester.WindowsVirtualizer.PrepareToStop();
                    tester.MockVirtualization.WaitForCompletionStatus().ShouldEqual((HResult)HResultExtensions.HResultFromNtStatus.DeviceNotReady);
                    tester.MockVirtualization.CreatedPlaceholders.Count.ShouldEqual(0);
                    tester.MockVirtualization.RequiredCallbacks.GetPlaceholderInfoCallback(2, "test.txt", 1, "reader").ShouldEqual(HResult.Pending);
                    tester.MockVirtualization.WaitForCompletionStatus().ShouldEqual((HResult)HResultExtensions.HResultFromNtStatus.DeviceNotReady);
                    EventMetadata metadata = new EventMetadata();
                    tester.WindowsVirtualizer.AddHeartbeatMetadata(metadata);
                    ((long)metadata["PlaceholderLockWaitQueueFull"]).ShouldEqual(0);
                }
                finally
                {
                    this.Repo.Repository.GVFSLock.ReleaseLockHeldByExternalProcess(1234);
                }
            }
        }

        [TestCase]
        public void PlaceholderLockWaitRevalidatesOwnerAfterMetadataLookup()
        {
            using (WindowsFileSystemVirtualizerTester tester = new WindowsFileSystemVirtualizerTester(this.Repo, true))
            {
                tester.GitIndexProjection.BlockGetProjectedFileInfo(willWaitForRequest: true);
                this.HoldCheckoutLock();
                tester.MockVirtualization.RequiredCallbacks.GetPlaceholderInfoCallback(1, "test.txt", 1, "reader").ShouldEqual(HResult.Pending);
                this.Repo.Repository.GVFSLock.ReleaseLockHeldByExternalProcess(1234).ShouldBeTrue();
                tester.GitIndexProjection.WaitForGetProjectedFileInfo();
                this.HoldCheckoutLock();
                tester.GitIndexProjection.UnblockGetProjectedFileInfo();
                tester.MockVirtualization.WaitForCompletionStatus().ShouldEqual((HResult)HResultExtensions.HResultFromNtStatus.FileNotAvailable);
                tester.MockVirtualization.CreatedPlaceholders.Count.ShouldEqual(0);
                tester.GitIndexProjection.CreationAttempts.ShouldEqual(0);
                this.Repo.Repository.GVFSLock.ReleaseLockHeldByExternalProcess(1234).ShouldBeTrue();
            }
        }

        private void HoldCheckoutLock()
        {
            ((MockPlatform)GVFSPlatform.Instance).ActiveProcesses.Add(1234);
            this.Repo.Repository.GVFSLock.TryAcquireLockForExternalRequestor(
                new NamedPipeMessages.LockData(1234, false, false, "git checkout-index --stdin", string.Empty),
                out _).ShouldBeTrue();
        }

        [TestCase(false)]
        [TestCase(true)]
        public void PlaceholderLockWaitRetriesChangedProjectionBeforeCreation(bool slowMetadata)
        {
            using (WindowsFileSystemVirtualizerTester tester = new WindowsFileSystemVirtualizerTester(this.Repo, true))
            {
                tester.GitIndexProjection.BlockGetProjectedFileInfo(willWaitForRequest: true);
                this.HoldCheckoutLock();
                tester.MockVirtualization.RequiredCallbacks.GetPlaceholderInfoCallback(1, "test.txt", 1, "reader").ShouldEqual(HResult.Pending);
                this.Repo.Repository.GVFSLock.ReleaseLockHeldByExternalProcess(1234).ShouldBeTrue();
                try
                {
                    tester.GitIndexProjection.WaitForGetProjectedFileInfo();
                    if (slowMetadata)
                    {
                        Thread.Sleep(2200);
                    }

                    ++tester.GitIndexProjection.Generation;
                }
                finally
                {
                    tester.GitIndexProjection.UnblockGetProjectedFileInfo();
                }

                tester.MockVirtualization.WaitForCompletionStatus().ShouldEqual(HResult.Ok);
                tester.GitIndexProjection.CreationAttempts.ShouldEqual(2);
                tester.MockVirtualization.CommandCompletions.Count.ShouldEqual(1);
            }
        }

        [TestCase]
        public void PlaceholderLockWaitBoundsOutstandingRequestsAndCompletesEachOnce()
        {
            using (WindowsFileSystemVirtualizerTester tester = new WindowsFileSystemVirtualizerTester(this.Repo, true))
            {
                this.HoldCheckoutLock();
                try
                {
                    for (int id = 1; id <= 129; ++id)
                    {
                        tester.MockVirtualization.RequiredCallbacks.GetPlaceholderInfoCallback(id, "test.txt", 1, "reader").ShouldEqual(HResult.Pending);
                    }

                    tester.MockVirtualization.WaitForCompletionStatus().ShouldEqual((HResult)HResultExtensions.HResultFromNtStatus.FileNotAvailable);
                    tester.MockVirtualization.CommandCompletions.Single().CommandId.ShouldEqual(129);
                    EventMetadata metadata = new EventMetadata();
                    tester.WindowsVirtualizer.AddHeartbeatMetadata(metadata);
                    ((long)metadata["PlaceholderLockWaitQueueFull"]).ShouldEqual(1);
                    ((long)metadata["PlaceholderLockWaitStarted"]).ShouldEqual(128);
                    EventMetadata nextInterval = new EventMetadata();
                    tester.WindowsVirtualizer.AddHeartbeatMetadata(nextInterval);
                    ((long)nextInterval["PlaceholderLockWaitQueueFull"]).ShouldEqual(0);
                    tester.WindowsVirtualizer.PrepareToStop();
                    tester.MockVirtualization.CommandCompletions.Count.ShouldEqual(129);
                    tester.MockVirtualization.CommandCompletions.Select(entry => entry.CommandId).Distinct().Count().ShouldEqual(129);
                    tester.MockVirtualization.CreatedPlaceholders.Count.ShouldEqual(0);
                }
                finally
                {
                    this.Repo.Repository.GVFSLock.ReleaseLockHeldByExternalProcess(1234);
                }
            }
        }

        [TestCase]
        public void HResultToFSResultMapsHResults()
        {
            foreach (HResult result in Enum.GetValues(typeof(HResult)))
            {
                if (MappedHResults.ContainsKey(result))
                {
                    WindowsFileSystemVirtualizer.HResultToFSResult(result).ShouldEqual(MappedHResults[result]);
                }
                else
                {
                    WindowsFileSystemVirtualizer.HResultToFSResult(result).ShouldEqual(FSResult.IOError);
                }
            }
        }

        [TestCase]
        public void ClearNegativePathCache()
        {
            const uint InitialNegativePathCacheCount = 7;
            using (MockVirtualizationInstance mockVirtualization = new MockVirtualizationInstance())
            using (WindowsFileSystemVirtualizer virtualizer = new WindowsFileSystemVirtualizer(this.Repo.Context, this.Repo.GitObjects, mockVirtualization, numWorkThreads))
            {
                mockVirtualization.NegativePathCacheCount = InitialNegativePathCacheCount;

                uint totalEntryCount;
                virtualizer.ClearNegativePathCache(out totalEntryCount).ShouldEqual(new FileSystemResult(FSResult.Ok, (int)HResult.Ok));
                totalEntryCount.ShouldEqual(InitialNegativePathCacheCount);
            }
        }

        [TestCase]
        public void DeleteFile()
        {
            using (MockVirtualizationInstance mockVirtualization = new MockVirtualizationInstance())
            using (WindowsFileSystemVirtualizer virtualizer = new WindowsFileSystemVirtualizer(this.Repo.Context, this.Repo.GitObjects, mockVirtualization, numWorkThreads))
            {
                UpdateFailureReason failureReason = UpdateFailureReason.NoFailure;

                mockVirtualization.DeleteFileResult = HResult.Ok;
                mockVirtualization.DeleteFileUpdateFailureCause = UpdateFailureCause.NoFailure;
                virtualizer
                    .DeleteFile("test.txt", UpdatePlaceholderType.AllowReadOnly, out failureReason)
                    .ShouldEqual(new FileSystemResult(FSResult.Ok, (int)mockVirtualization.DeleteFileResult));
                failureReason.ShouldEqual((UpdateFailureReason)mockVirtualization.DeleteFileUpdateFailureCause);

                mockVirtualization.DeleteFileResult = HResult.FileNotFound;
                mockVirtualization.DeleteFileUpdateFailureCause = UpdateFailureCause.NoFailure;
                virtualizer
                    .DeleteFile("test.txt", UpdatePlaceholderType.AllowReadOnly, out failureReason)
                    .ShouldEqual(new FileSystemResult(FSResult.FileOrPathNotFound, (int)mockVirtualization.DeleteFileResult));
                failureReason.ShouldEqual((UpdateFailureReason)mockVirtualization.DeleteFileUpdateFailureCause);

                mockVirtualization.DeleteFileResult = HResult.VirtualizationInvalidOp;
                mockVirtualization.DeleteFileUpdateFailureCause = UpdateFailureCause.DirtyData;
                virtualizer
                    .DeleteFile("test.txt", UpdatePlaceholderType.AllowReadOnly, out failureReason)
                    .ShouldEqual(new FileSystemResult(FSResult.VirtualizationInvalidOperation, (int)mockVirtualization.DeleteFileResult));
                failureReason.ShouldEqual((UpdateFailureReason)mockVirtualization.DeleteFileUpdateFailureCause);
            }
        }

        [TestCase]
        public void DeleteFileReturnsIOErrorWhenVirtualizationInstanceThrows()
        {
            using (MockVirtualizationInstance mockVirtualization = new MockVirtualizationInstance())
            using (WindowsFileSystemVirtualizer virtualizer = new WindowsFileSystemVirtualizer(this.Repo.Context, this.Repo.GitObjects, mockVirtualization, numWorkThreads))
            {
                // A native ProjFS failure (for example a NullReferenceException surfaced from
                // projectedfslib.dll under memory pressure) must fail this single delete rather than
                // propagate out and crash the mount.
                mockVirtualization.DeleteFileException = new NullReferenceException("simulated native ProjFS failure");

                UpdateFailureReason failureReason = UpdateFailureReason.DirtyData;
                virtualizer
                    .DeleteFile("test.txt", UpdatePlaceholderType.AllowReadOnly, out failureReason)
                    .ShouldEqual(new FileSystemResult(FSResult.IOError, (int)HResult.InternalError));
                failureReason.ShouldEqual(UpdateFailureReason.NoFailure);
            }
        }

        [TestCase]
        public void OutboundProjFSOperationsReturnFailureWhenVirtualizationInstanceThrows()
        {
            using (MockVirtualizationInstance mockVirtualization = new MockVirtualizationInstance())
            using (WindowsFileSystemVirtualizer virtualizer = new WindowsFileSystemVirtualizer(this.Repo.Context, this.Repo.GitObjects, mockVirtualization, numWorkThreads))
            {
                // Every GVFS-initiated native ProjFS operation must fail the single call rather than
                // propagate a native exception (e.g. a NullReferenceException from projectedfslib.dll
                // under memory pressure) and crash the mount.
                mockVirtualization.NativeCallException = new NullReferenceException("simulated native ProjFS failure");

                virtualizer
                    .ClearNegativePathCache(out uint _)
                    .ShouldEqual(new FileSystemResult(FSResult.IOError, (int)HResult.InternalError));

                UpdateFailureReason failureReason = UpdateFailureReason.DirtyData;
                virtualizer
                    .UpdatePlaceholderIfNeeded(
                        "test.txt",
                        DateTime.Now,
                        DateTime.Now,
                        DateTime.Now,
                        DateTime.Now,
                        0,
                        15,
                        string.Empty,
                        UpdatePlaceholderType.AllowReadOnly,
                        out failureReason)
                    .ShouldEqual(new FileSystemResult(FSResult.IOError, (int)HResult.InternalError));
                failureReason.ShouldEqual(UpdateFailureReason.NoFailure);
            }
        }

        [TestCase]
        public void WritePlaceholderOperationsReturnFailureWhenVirtualizationInstanceThrows()
        {
            using (WindowsFileSystemVirtualizerTester tester = new WindowsFileSystemVirtualizerTester(this.Repo))
            {
                tester.MockVirtualization.NativeCallException = new NullReferenceException("simulated native ProjFS failure");

                tester.WindowsVirtualizer
                    .WritePlaceholderFile("test.txt", endOfFile: 15, sha: new string('0', 40))
                    .ShouldEqual(new FileSystemResult(FSResult.IOError, (int)HResult.InternalError));

                tester.WindowsVirtualizer
                    .WritePlaceholderDirectory("testDir")
                    .ShouldEqual(new FileSystemResult(FSResult.IOError, (int)HResult.InternalError));
            }
        }

        [TestCase]
        public void UpdatePlaceholderIfNeeded()
        {
            using (MockVirtualizationInstance mockVirtualization = new MockVirtualizationInstance())
            using (WindowsFileSystemVirtualizer virtualizer = new WindowsFileSystemVirtualizer(this.Repo.Context, this.Repo.GitObjects, mockVirtualization, numWorkThreads))
            {
                UpdateFailureReason failureReason = UpdateFailureReason.NoFailure;

                mockVirtualization.UpdateFileIfNeededResult = HResult.Ok;
                mockVirtualization.UpdateFileIfNeededFailureCase = UpdateFailureCause.NoFailure;
                virtualizer
                    .UpdatePlaceholderIfNeeded(
                        "test.txt",
                        DateTime.Now,
                        DateTime.Now,
                        DateTime.Now,
                        DateTime.Now,
                        0,
                        15,
                        string.Empty,
                        UpdatePlaceholderType.AllowReadOnly,
                        out failureReason)
                    .ShouldEqual(new FileSystemResult(FSResult.Ok, (int)mockVirtualization.UpdateFileIfNeededResult));
                failureReason.ShouldEqual((UpdateFailureReason)mockVirtualization.UpdateFileIfNeededFailureCase);

                mockVirtualization.UpdateFileIfNeededResult = HResult.FileNotFound;
                mockVirtualization.UpdateFileIfNeededFailureCase = UpdateFailureCause.NoFailure;
                virtualizer
                    .UpdatePlaceholderIfNeeded(
                        "test.txt",
                        DateTime.Now,
                        DateTime.Now,
                        DateTime.Now,
                        DateTime.Now,
                        0,
                        15,
                        string.Empty,
                        UpdatePlaceholderType.AllowReadOnly,
                        out failureReason)
                    .ShouldEqual(new FileSystemResult(FSResult.FileOrPathNotFound, (int)mockVirtualization.UpdateFileIfNeededResult));
                failureReason.ShouldEqual((UpdateFailureReason)mockVirtualization.UpdateFileIfNeededFailureCase);

                mockVirtualization.UpdateFileIfNeededResult = HResult.VirtualizationInvalidOp;
                mockVirtualization.UpdateFileIfNeededFailureCase = UpdateFailureCause.DirtyData;
                virtualizer
                    .UpdatePlaceholderIfNeeded(
                        "test.txt",
                        DateTime.Now,
                        DateTime.Now,
                        DateTime.Now,
                        DateTime.Now,
                        0,
                        15,
                        string.Empty,
                        UpdatePlaceholderType.AllowReadOnly,
                        out failureReason)
                    .ShouldEqual(new FileSystemResult(FSResult.VirtualizationInvalidOperation, (int)mockVirtualization.UpdateFileIfNeededResult));
                failureReason.ShouldEqual((UpdateFailureReason)mockVirtualization.UpdateFileIfNeededFailureCase);
            }
        }

        [TestCase]
        public void OnStartDirectoryEnumerationReturnsPendingWhenResultsNotInMemory()
        {
            using (WindowsFileSystemVirtualizerTester tester = new WindowsFileSystemVirtualizerTester(this.Repo))
            {
                Guid enumerationGuid = Guid.NewGuid();
                tester.GitIndexProjection.EnumerationInMemory = false;
                tester.MockVirtualization.RequiredCallbacks.StartDirectoryEnumerationCallback(1, enumerationGuid, "test", TriggeringProcessId, TriggeringProcessImageFileName).ShouldEqual(HResult.Pending);
                tester.MockVirtualization.WaitForCompletionStatus().ShouldEqual(HResult.Ok);
                tester.MockVirtualization.RequiredCallbacks.EndDirectoryEnumerationCallback(enumerationGuid).ShouldEqual(HResult.Ok);
            }
        }

        [TestCase]
        public void OnStartDirectoryEnumerationReturnsSuccessWhenResultsInMemory()
        {
            using (WindowsFileSystemVirtualizerTester tester = new WindowsFileSystemVirtualizerTester(this.Repo, new[] { "test" }))
            {
                Guid enumerationGuid = Guid.NewGuid();
                tester.GitIndexProjection.EnumerationInMemory = true;
                tester.MockVirtualization.RequiredCallbacks.StartDirectoryEnumerationCallback(1, enumerationGuid, "test", TriggeringProcessId, TriggeringProcessImageFileName).ShouldEqual(HResult.Ok);
                tester.MockVirtualization.RequiredCallbacks.EndDirectoryEnumerationCallback(enumerationGuid).ShouldEqual(HResult.Ok);
            }
        }

        [TestCase]
        public void HeartbeatMetadataReportsActiveEnumerationCount()
        {
            using (WindowsFileSystemVirtualizerTester tester = new WindowsFileSystemVirtualizerTester(this.Repo, new[] { "test" }))
            {
                tester.GitIndexProjection.EnumerationInMemory = true;

                tester.MockVirtualization.RequiredCallbacks.StartDirectoryEnumerationCallback(1, Guid.NewGuid(), "test", TriggeringProcessId, TriggeringProcessImageFileName).ShouldEqual(HResult.Ok);
                tester.MockVirtualization.RequiredCallbacks.StartDirectoryEnumerationCallback(2, Guid.NewGuid(), "test", TriggeringProcessId, TriggeringProcessImageFileName).ShouldEqual(HResult.Ok);

                EventMetadata metadata = new EventMetadata();
                tester.WindowsVirtualizer.AddHeartbeatMetadata(metadata);

                metadata.ContainsKey("ActiveEnumerationCount").ShouldBeTrue();
                ((int)metadata["ActiveEnumerationCount"]).ShouldEqual(2);
                metadata.ContainsKey("ActiveCommandCount").ShouldBeTrue();
            }
        }

        [TestCase]
        public void StaleEnumerationsAreNotEvictedWhenDisabled()
        {
            using (WindowsFileSystemVirtualizerTester tester = new WindowsFileSystemVirtualizerTester(this.Repo, new[] { "test" }))
            {
                tester.GitIndexProjection.EnumerationInMemory = true;

                // Eviction is disabled by default (gvfs.max-active-enumerations unset => 0). Even with
                // a zero stale timeout - which would make every enumeration eligible - nothing is evicted.
                tester.WindowsVirtualizer.MaxActiveEnumerationsForTest = 0;
                tester.WindowsVirtualizer.ActiveEnumerationStaleTimeoutForTest = TimeSpan.Zero;

                Guid firstId = Guid.NewGuid();
                Guid secondId = Guid.NewGuid();
                tester.MockVirtualization.RequiredCallbacks.StartDirectoryEnumerationCallback(1, firstId, "test", TriggeringProcessId, TriggeringProcessImageFileName).ShouldEqual(HResult.Ok);
                Thread.Sleep(20);
                tester.MockVirtualization.RequiredCallbacks.StartDirectoryEnumerationCallback(2, secondId, "test", TriggeringProcessId, TriggeringProcessImageFileName).ShouldEqual(HResult.Ok);

                tester.WindowsVirtualizer.ForceEnumerationEvictionSweepForTest();

                // Both enumerations are still present (a missing id would return InternalError).
                tester.MockVirtualization.RequiredCallbacks.EndDirectoryEnumerationCallback(firstId).ShouldEqual(HResult.Ok);
                tester.MockVirtualization.RequiredCallbacks.EndDirectoryEnumerationCallback(secondId).ShouldEqual(HResult.Ok);
            }
        }

        [TestCase]
        public void StaleEnumerationsAreEvictedWhenEnabledButLiveOnesAreKept()
        {
            using (WindowsFileSystemVirtualizerTester tester = new WindowsFileSystemVirtualizerTester(this.Repo, new[] { "test" }))
            {
                tester.GitIndexProjection.EnumerationInMemory = true;

                // Enable eviction above 1 live enumeration, treating anything idle longer than 20ms as stale.
                tester.WindowsVirtualizer.MaxActiveEnumerationsForTest = 1;
                tester.WindowsVirtualizer.ActiveEnumerationStaleTimeoutForTest = TimeSpan.FromMilliseconds(20);

                Guid staleId = Guid.NewGuid();
                tester.MockVirtualization.RequiredCallbacks.StartDirectoryEnumerationCallback(1, staleId, "test", TriggeringProcessId, TriggeringProcessImageFileName).ShouldEqual(HResult.Ok);

                // Let the first enumeration age well past the stale timeout before adding a fresh one.
                Thread.Sleep(200);

                Guid freshId = Guid.NewGuid();
                tester.MockVirtualization.RequiredCallbacks.StartDirectoryEnumerationCallback(2, freshId, "test", TriggeringProcessId, TriggeringProcessImageFileName).ShouldEqual(HResult.Ok);

                tester.WindowsVirtualizer.ForceEnumerationEvictionSweepForTest();

                // The stale enumeration was evicted: its End now fails to find it (fails loudly, does
                // not silently return partial results). The fresh enumeration is retained.
                tester.MockVirtualization.RequiredCallbacks.EndDirectoryEnumerationCallback(staleId).ShouldEqual(HResult.InternalError);
                tester.MockVirtualization.RequiredCallbacks.EndDirectoryEnumerationCallback(freshId).ShouldEqual(HResult.Ok);
            }
        }

        [TestCase]
        public void GetDirectoryEnumerationTagsMissReasonAndDeduplicates()
        {
            using (WindowsFileSystemVirtualizerTester tester = new WindowsFileSystemVirtualizerTester(this.Repo, new[] { "test" }))
            {
                tester.GitIndexProjection.EnumerationInMemory = true;

                tester.WindowsVirtualizer.MaxActiveEnumerationsForTest = 1;
                tester.WindowsVirtualizer.ActiveEnumerationStaleTimeoutForTest = TimeSpan.FromMilliseconds(20);

                Guid staleId = Guid.NewGuid();
                tester.MockVirtualization.RequiredCallbacks.StartDirectoryEnumerationCallback(1, staleId, "test", TriggeringProcessId, TriggeringProcessImageFileName).ShouldEqual(HResult.Ok);

                Thread.Sleep(200);

                Guid freshId = Guid.NewGuid();
                tester.MockVirtualization.RequiredCallbacks.StartDirectoryEnumerationCallback(2, freshId, "test", TriggeringProcessId, TriggeringProcessImageFileName).ShouldEqual(HResult.Ok);

                tester.WindowsVirtualizer.ForceEnumerationEvictionSweepForTest();

                MockTracer mockTracker = this.Repo.Context.Tracer as MockTracer;

                // A Get for the evicted enumeration is attributed to GVFS eviction (self-inflicted).
                // results is unused on the failure path, so null is safe.
                tester.MockVirtualization.RequiredCallbacks.GetDirectoryEnumerationCallback(3, staleId, string.Empty, false, null).ShouldEqual(HResult.InternalError);
                mockTracker.RelatedErrorEvents.Any(
                    e => e.Contains("Failed to find active enumeration ID") && e.Contains("\"EnumerationFailureReason\":\"Evicted\"")).ShouldBeTrue();

                // A Get for an ID GVFS never held is attributed to a never-seen delivery.
                Guid neverSeenId = Guid.NewGuid();
                tester.MockVirtualization.RequiredCallbacks.GetDirectoryEnumerationCallback(4, neverSeenId, string.Empty, false, null).ShouldEqual(HResult.InternalError);
                mockTracker.RelatedErrorEvents.Any(
                    e => e.Contains("Failed to find active enumeration ID") && e.Contains("\"EnumerationFailureReason\":\"NeverSeen\"")).ShouldBeTrue();

                // A Get that races/follows the End for the same enumeration is attributed to a benign
                // close/query race, not a never-seen delivery.
                Guid endedId = Guid.NewGuid();
                tester.MockVirtualization.RequiredCallbacks.StartDirectoryEnumerationCallback(5, endedId, "test", TriggeringProcessId, TriggeringProcessImageFileName).ShouldEqual(HResult.Ok);
                tester.MockVirtualization.RequiredCallbacks.EndDirectoryEnumerationCallback(endedId).ShouldEqual(HResult.Ok);
                tester.MockVirtualization.RequiredCallbacks.GetDirectoryEnumerationCallback(6, endedId, string.Empty, false, null).ShouldEqual(HResult.InternalError);
                mockTracker.RelatedErrorEvents.Any(
                    e => e.Contains("Failed to find active enumeration ID") && e.Contains("\"EnumerationFailureReason\":\"EndedRecently\"")).ShouldBeTrue();

                // Repeated Gets for the same missing ID are de-duplicated: the error is emitted once,
                // so a caller's retry loop cannot produce a telemetry storm.
                int errorsForNeverSeenId = mockTracker.RelatedErrorEvents.Count(e => e.Contains("\"EnumerationFailureReason\":\"NeverSeen\""));
                tester.MockVirtualization.RequiredCallbacks.GetDirectoryEnumerationCallback(7, neverSeenId, string.Empty, false, null).ShouldEqual(HResult.InternalError);
                tester.MockVirtualization.RequiredCallbacks.GetDirectoryEnumerationCallback(8, neverSeenId, string.Empty, false, null).ShouldEqual(HResult.InternalError);
                mockTracker.RelatedErrorEvents.Count(e => e.Contains("\"EnumerationFailureReason\":\"NeverSeen\"")).ShouldEqual(errorsForNeverSeenId);
            }
        }

        [TestCase]
        public void EndDirectoryEnumerationRecordsEndedBeforeRemovingFromActive()
        {
            using (WindowsFileSystemVirtualizerTester tester = new WindowsFileSystemVirtualizerTester(this.Repo, new[] { "test" }))
            {
                tester.GitIndexProjection.EnumerationInMemory = true;
                MockTracer mockTracker = this.Repo.Context.Tracer as MockTracer;

                Guid endedId = Guid.NewGuid();
                tester.MockVirtualization.RequiredCallbacks.StartDirectoryEnumerationCallback(1, endedId, "test", TriggeringProcessId, TriggeringProcessImageFileName).ShouldEqual(HResult.Ok);

                // Capture the state at the exact interleaving point a concurrent Get would observe:
                // after End records the ended ID but before it removes it from activeEnumerations. This
                // is the ordering the fix guarantees; a remove-before-record regression would fail it.
                bool activeAtHook = false;
                bool recentlyEndedAtHook = false;
                tester.WindowsVirtualizer.EnumerationEndBeforeRemoveHookForTest = () =>
                {
                    activeAtHook = tester.WindowsVirtualizer.ActiveEnumerationsContainsForTest(endedId);
                    recentlyEndedAtHook = tester.WindowsVirtualizer.EnumerationFailureTrackerForTest.ClassifyMiss(endedId) == EnumerationFailureReason.EndedRecently;
                };

                tester.MockVirtualization.RequiredCallbacks.EndDirectoryEnumerationCallback(endedId).ShouldEqual(HResult.Ok);

                // The end was recorded before the removal, and the entry was still live at that point,
                // so there is no window where the ID is absent from BOTH maps - a racing Get can never
                // be misclassified NeverSeen.
                recentlyEndedAtHook.ShouldBeTrue();
                activeAtHook.ShouldBeTrue();

                // After End completes the ID is out of the active set but still tracked as recently ended.
                tester.WindowsVirtualizer.ActiveEnumerationsContainsForTest(endedId).ShouldBeFalse();
                tester.WindowsVirtualizer.EnumerationFailureTrackerForTest.ClassifyMiss(endedId).ShouldEqual(EnumerationFailureReason.EndedRecently);
                mockTracker.RelatedErrorEvents.Any(e => e.Contains("Failed to find active enumeration ID")).ShouldBeFalse();
            }
        }

        [TestCase]
        public void GetDirectoryEnumerationPrefersEvictedWhenIdIsBothEvictedAndEnded()
        {
            using (WindowsFileSystemVirtualizerTester tester = new WindowsFileSystemVirtualizerTester(this.Repo, new[] { "test" }))
            {
                tester.GitIndexProjection.EnumerationInMemory = true;
                tester.WindowsVirtualizer.MaxActiveEnumerationsForTest = 1;
                tester.WindowsVirtualizer.ActiveEnumerationStaleTimeoutForTest = TimeSpan.FromMilliseconds(20);

                Guid staleId = Guid.NewGuid();
                tester.MockVirtualization.RequiredCallbacks.StartDirectoryEnumerationCallback(1, staleId, "test", TriggeringProcessId, TriggeringProcessImageFileName).ShouldEqual(HResult.Ok);

                Thread.Sleep(200);

                Guid freshId = Guid.NewGuid();
                tester.MockVirtualization.RequiredCallbacks.StartDirectoryEnumerationCallback(2, freshId, "test", TriggeringProcessId, TriggeringProcessImageFileName).ShouldEqual(HResult.Ok);

                // Evict staleId: it lands in the recently-evicted map and leaves the active set.
                tester.WindowsVirtualizer.ForceEnumerationEvictionSweepForTest();

                // A late End for the same ID also records it in the recently-ended map (the removal
                // itself fails because eviction already removed it), so the ID is now in BOTH maps.
                tester.MockVirtualization.RequiredCallbacks.EndDirectoryEnumerationCallback(staleId).ShouldEqual(HResult.InternalError);

                MockTracer mockTracker = this.Repo.Context.Tracer as MockTracer;

                // The classifier checks eviction first, so the more actionable self-inflicted cause wins.
                tester.MockVirtualization.RequiredCallbacks.GetDirectoryEnumerationCallback(3, staleId, string.Empty, false, null).ShouldEqual(HResult.InternalError);
                mockTracker.RelatedErrorEvents.Any(
                    e => e.Contains("Failed to find active enumeration ID") && e.Contains("\"EnumerationFailureReason\":\"Evicted\"")).ShouldBeTrue();
                mockTracker.RelatedErrorEvents.Any(
                    e => e.Contains("Failed to find active enumeration ID") && e.Contains("\"EnumerationFailureReason\":\"EndedRecently\"")).ShouldBeFalse();
            }
        }

        [TestCase]
        public void EndForNeverHeldIdDoesNotSkewLaterGetToEndedRecently()
        {
            using (WindowsFileSystemVirtualizerTester tester = new WindowsFileSystemVirtualizerTester(this.Repo, new[] { "test" }))
            {
                tester.GitIndexProjection.EnumerationInMemory = true;
                MockTracer mockTracker = this.Repo.Context.Tracer as MockTracer;

                // End arrives for an ID GVFS never held (no prior Start), so the removal finds nothing
                // and it was not evicted. The ended marker recorded before the removal must be undone.
                Guid neverHeldId = Guid.NewGuid();
                tester.MockVirtualization.RequiredCallbacks.EndDirectoryEnumerationCallback(neverHeldId).ShouldEqual(HResult.InternalError);

                // A later Get for that ID is therefore classified NeverSeen, not skewed to EndedRecently.
                tester.MockVirtualization.RequiredCallbacks.GetDirectoryEnumerationCallback(1, neverHeldId, string.Empty, false, null).ShouldEqual(HResult.InternalError);
                mockTracker.RelatedErrorEvents.Any(
                    e => e.Contains("Failed to find active enumeration ID") && e.Contains("\"EnumerationFailureReason\":\"NeverSeen\"")).ShouldBeTrue();
                mockTracker.RelatedErrorEvents.Any(
                    e => e.Contains("Failed to find active enumeration ID") && e.Contains("\"EnumerationFailureReason\":\"EndedRecently\"")).ShouldBeFalse();
            }
        }

        [TestCase]
        public void GetPlaceholderInformationHandlerPathNotProjected()
        {
            using (WindowsFileSystemVirtualizerTester tester = new WindowsFileSystemVirtualizerTester(this.Repo))
            {
                tester.MockVirtualization.RequiredCallbacks.GetPlaceholderInfoCallback(1, "doesNotExist", TriggeringProcessId, TriggeringProcessImageFileName).ShouldEqual(HResult.FileNotFound);
            }
        }

        [TestCase]
        public void GetPlaceholderInformationHandlerPathProjected()
        {
            using (WindowsFileSystemVirtualizerTester tester = new WindowsFileSystemVirtualizerTester(this.Repo))
            {
                tester.MockVirtualization.RequiredCallbacks.GetPlaceholderInfoCallback(1, "test.txt", TriggeringProcessId, TriggeringProcessImageFileName).ShouldEqual(HResult.Pending);
                tester.MockVirtualization.WaitForCompletionStatus().ShouldEqual(HResult.Ok);
                tester.MockVirtualization.CreatedPlaceholders.ShouldContain(entry => entry == "test.txt");
                tester.GitIndexProjection.PlaceholdersCreated.ShouldContain(entry => entry == "test.txt");
            }
        }

        [TestCase]
        public void GetPlaceholderInformationHandlerCancelledBeforeSchedulingAsync()
        {
            using (WindowsFileSystemVirtualizerTester tester = new WindowsFileSystemVirtualizerTester(this.Repo))
            {
                tester.GitIndexProjection.BlockIsPathProjected(willWaitForRequest: true);

                Task.Run(() =>
                {
                    // Wait for OnGetPlaceholderInformation to call IsPathProjected and then while it's blocked there
                    // call OnCancelCommand
                    tester.GitIndexProjection.WaitForIsPathProjected();
                    tester.MockVirtualization.OnCancelCommand(1);
                    tester.GitIndexProjection.UnblockIsPathProjected();
                });

                tester.MockVirtualization.RequiredCallbacks.GetPlaceholderInfoCallback(1, "test.txt", TriggeringProcessId, TriggeringProcessImageFileName).ShouldEqual(HResult.Pending);

                // Cancelling before GetPlaceholderInformation has registered the command results in placeholders being created
                tester.MockVirtualization.WaitForPlaceholderCreate();
                tester.GitIndexProjection.WaitForPlaceholderCreate();
                tester.MockVirtualization.CreatedPlaceholders.ShouldContain(entry => entry == "test.txt");
                tester.GitIndexProjection.PlaceholdersCreated.ShouldContain(entry => entry == "test.txt");
            }
        }

        [TestCase]
        public void GetPlaceholderInformationHandlerCancelledDuringAsyncCallback()
        {
            using (WindowsFileSystemVirtualizerTester tester = new WindowsFileSystemVirtualizerTester(this.Repo))
            {
                tester.GitIndexProjection.BlockGetProjectedFileInfo(willWaitForRequest: true);
                tester.MockVirtualization.RequiredCallbacks.GetPlaceholderInfoCallback(1, "test.txt", TriggeringProcessId, TriggeringProcessImageFileName).ShouldEqual(HResult.Pending);
                tester.GitIndexProjection.WaitForGetProjectedFileInfo();
                tester.MockVirtualization.OnCancelCommand(1);
                tester.GitIndexProjection.UnblockGetProjectedFileInfo();

                // Cancelling in the middle of GetPlaceholderInformation still allows it to create placeholders when the cancellation does not
                // interrupt network requests
                tester.MockVirtualization.WaitForPlaceholderCreate();
                tester.GitIndexProjection.WaitForPlaceholderCreate();
                tester.MockVirtualization.CreatedPlaceholders.ShouldContain(entry => entry == "test.txt");
                tester.GitIndexProjection.PlaceholdersCreated.ShouldContain(entry => entry == "test.txt");
            }
        }

        [TestCase]
        [Category(CategoryConstants.ExceptionExpected)]
        public void GetPlaceholderInformationHandlerCancelledDuringNetworkRequest()
        {
            using (WindowsFileSystemVirtualizerTester tester = new WindowsFileSystemVirtualizerTester(this.Repo))
            {
                MockTracer mockTracker = this.Repo.Context.Tracer as MockTracer;
                mockTracker.WaitRelatedEventName = "GetPlaceholderInformationAsyncHandler_GetProjectedFileInfo_Cancelled";
                tester.GitIndexProjection.ThrowOperationCanceledExceptionOnProjectionRequest = true;
                tester.MockVirtualization.RequiredCallbacks.GetPlaceholderInfoCallback(1, "test.txt", TriggeringProcessId, TriggeringProcessImageFileName).ShouldEqual(HResult.Pending);

                // Cancelling in the middle of GetPlaceholderInformation in the middle of a network request should not result in placeholder
                // getting created
                mockTracker.WaitForRelatedEvent();
                tester.MockVirtualization.CreatedPlaceholders.ShouldNotContain(entry => entry == "test.txt");
                tester.GitIndexProjection.PlaceholdersCreated.ShouldNotContain(entry => entry == "test.txt");
            }
        }

        [TestCase]
        public void OnGetFileStreamReturnsInternalErrorWhenOffsetNonZero()
        {
            using (WindowsFileSystemVirtualizerTester tester = new WindowsFileSystemVirtualizerTester(this.Repo))
            {
                tester.InvokeGetFileDataCallback(expectedResult: HResult.InternalError, byteOffset: 10);
            }
        }

        [TestCase]
        public void OnGetFileStreamReturnsInternalErrorWhenPlaceholderVersionDoesNotMatchExpected()
        {
            using (WindowsFileSystemVirtualizerTester tester = new WindowsFileSystemVirtualizerTester(this.Repo))
            {
                byte[] epochId = new byte[] { FileSystemVirtualizer.PlaceholderVersion + 1 };
                tester.InvokeGetFileDataCallback(expectedResult: HResult.InternalError, providerId: epochId);
            }
        }

        [TestCase]
        public void MoveFileIntoDotGitDirectory()
        {
            using (WindowsFileSystemVirtualizerTester tester = new WindowsFileSystemVirtualizerTester(this.Repo))
            {
                NotificationType notificationType = NotificationType.UseExistingMask;
                tester.MockVirtualization.OnNotifyFileRenamed(
                        "test.txt",
                        Path.Combine(".git", "test.txt"),
                        isDirectory: false,
                        triggeringProcessId: TriggeringProcessId,
                        triggeringProcessImageFileName: TriggeringProcessImageFileName,
                        notificationMask: out notificationType);
                notificationType.ShouldEqual(NotificationType.UseExistingMask);
                tester.FileSystemCallbacks.OnIndexFileChangeCallCount.ShouldEqual(0);
                tester.FileSystemCallbacks.OnLogsHeadChangeCallCount.ShouldEqual(0);
                tester.FileSystemCallbacks.OnFileRenamedCallCount.ShouldEqual(1);
                tester.FileSystemCallbacks.OnFolderRenamedCallCount.ShouldEqual(0);
                tester.FileSystemCallbacks.ResetCalls();

                // We don't expect something to rename something from outside the .gitdir to the .git\index, but this
                // verifies that we behave as expected in case that happens
                tester.MockVirtualization.OnNotifyFileRenamed(
                        "test.txt",
                        Path.Combine(".git", "index"),
                        isDirectory: false,
                        triggeringProcessId: TriggeringProcessId,
                        triggeringProcessImageFileName: TriggeringProcessImageFileName,
                        notificationMask: out notificationType);
                notificationType.ShouldEqual(NotificationType.UseExistingMask);
                tester.FileSystemCallbacks.OnIndexFileChangeCallCount.ShouldEqual(1);
                tester.FileSystemCallbacks.OnLogsHeadChangeCallCount.ShouldEqual(0);
                tester.FileSystemCallbacks.OnFileRenamedCallCount.ShouldEqual(1);
                tester.FileSystemCallbacks.OnFolderRenamedCallCount.ShouldEqual(0);
                tester.FileSystemCallbacks.ResetCalls();

                // We don't expect something to rename something from outside the .gitdir to the .git\logs\HEAD, but this
                // verifies that we behave as expected in case that happens
                tester.MockVirtualization.OnNotifyFileRenamed(
                        "test.txt",
                        Path.Combine(".git", "logs\\HEAD"),
                        isDirectory: false,
                        triggeringProcessId: TriggeringProcessId,
                        triggeringProcessImageFileName: TriggeringProcessImageFileName,
                        notificationMask: out notificationType);
                notificationType.ShouldEqual(NotificationType.UseExistingMask);
                tester.FileSystemCallbacks.OnIndexFileChangeCallCount.ShouldEqual(0);
                tester.FileSystemCallbacks.OnLogsHeadChangeCallCount.ShouldEqual(1);
                tester.FileSystemCallbacks.OnFileRenamedCallCount.ShouldEqual(1);
                tester.FileSystemCallbacks.OnFolderRenamedCallCount.ShouldEqual(0);
                tester.FileSystemCallbacks.ResetCalls();
            }
        }

        [TestCase]
        public void MoveFileFromDotGitToSrc()
        {
            using (WindowsFileSystemVirtualizerTester tester = new WindowsFileSystemVirtualizerTester(this.Repo))
            {
                NotificationType notificationType = NotificationType.UseExistingMask;
                tester.MockVirtualization.OnNotifyFileRenamed(
                        Path.Combine(".git", "test.txt"),
                        "test2.txt",
                        isDirectory: false,
                        triggeringProcessId: TriggeringProcessId,
                        triggeringProcessImageFileName: TriggeringProcessImageFileName,
                        notificationMask: out notificationType);
                notificationType.ShouldEqual(NotificationType.UseExistingMask);
                tester.FileSystemCallbacks.OnIndexFileChangeCallCount.ShouldEqual(0);
                tester.FileSystemCallbacks.OnLogsHeadChangeCallCount.ShouldEqual(0);
                tester.FileSystemCallbacks.OnFileRenamedCallCount.ShouldEqual(1);
                tester.FileSystemCallbacks.OnFolderRenamedCallCount.ShouldEqual(0);
            }
        }

        [TestCase]
        public void MoveFile()
        {
            using (WindowsFileSystemVirtualizerTester tester = new WindowsFileSystemVirtualizerTester(this.Repo))
            {
                NotificationType notificationType = NotificationType.UseExistingMask;
                tester.MockVirtualization.OnNotifyFileRenamed(
                    "test.txt",
                    "test2.txt",
                    isDirectory: false,
                    triggeringProcessId: TriggeringProcessId,
                    triggeringProcessImageFileName: TriggeringProcessImageFileName,
                    notificationMask: out notificationType);
                notificationType.ShouldEqual(NotificationType.UseExistingMask);
                tester.FileSystemCallbacks.OnIndexFileChangeCallCount.ShouldEqual(0);
                tester.FileSystemCallbacks.OnLogsHeadChangeCallCount.ShouldEqual(0);
                tester.FileSystemCallbacks.OnFileRenamedCallCount.ShouldEqual(1);
                tester.FileSystemCallbacks.OnFolderRenamedCallCount.ShouldEqual(0);
                tester.FileSystemCallbacks.ResetCalls();

                tester.MockVirtualization.OnNotifyFileRenamed(
                    "test_folder_src",
                    "test_folder_dst",
                    isDirectory: true,
                    triggeringProcessId: TriggeringProcessId,
                    triggeringProcessImageFileName: TriggeringProcessImageFileName,
                    notificationMask: out notificationType);
                notificationType.ShouldEqual(NotificationType.UseExistingMask);
                tester.FileSystemCallbacks.OnIndexFileChangeCallCount.ShouldEqual(0);
                tester.FileSystemCallbacks.OnLogsHeadChangeCallCount.ShouldEqual(0);
                tester.FileSystemCallbacks.OnFileRenamedCallCount.ShouldEqual(0);
                tester.FileSystemCallbacks.OnFolderRenamedCallCount.ShouldEqual(1);
            }
        }

        [TestCase]
        public void OnGetFileStreamReturnsPendingAndCompletesWithSuccessWhenNoFailures()
        {
            using (WindowsFileSystemVirtualizerTester tester = new WindowsFileSystemVirtualizerTester(this.Repo))
            {
                tester.MockVirtualization.WriteFileReturnResult = HResult.Ok;

                tester.InvokeGetFileDataCallback(expectedResult: HResult.Pending);

                tester.MockVirtualization.WaitForCompletionStatus().ShouldEqual(HResult.Ok);
            }
        }

        [TestCase]
        [Category(CategoryConstants.ExceptionExpected)]
        public void OnGetFileStreamHandlesTryCopyBlobContentStreamThrowingOperationCanceled()
        {
            using (WindowsFileSystemVirtualizerTester tester = new WindowsFileSystemVirtualizerTester(this.Repo))
            {
                MockTracer mockTracker = this.Repo.Context.Tracer as MockTracer;
                mockTracker.WaitRelatedEventName = "GetFileStreamHandlerAsyncHandler_OperationCancelled";
                MockGVFSGitObjects mockGVFSGitObjects = this.Repo.GitObjects as MockGVFSGitObjects;
                mockGVFSGitObjects.CancelTryCopyBlobContentStream = true;

                tester.InvokeGetFileDataCallback(expectedResult: HResult.Pending);

                mockTracker.WaitForRelatedEvent();
            }
        }

        [TestCase]
        [Category(CategoryConstants.ExceptionExpected)]
        public void OnGetFileStreamHandlesCancellationDuringWriteAction()
        {
            using (WindowsFileSystemVirtualizerTester tester = new WindowsFileSystemVirtualizerTester(this.Repo))
            {
                MockTracer mockTracker = this.Repo.Context.Tracer as MockTracer;
                mockTracker.WaitRelatedEventName = "GetFileStreamHandlerAsyncHandler_OperationCancelled";

                tester.MockVirtualization.BlockCreateWriteBuffer(willWaitForRequest: true);
                tester.InvokeGetFileDataCallback(expectedResult: HResult.Pending);

                tester.MockVirtualization.WaitForCreateWriteBuffer();
                tester.MockVirtualization.OnCancelCommand(1);
                tester.MockVirtualization.UnblockCreateWriteBuffer();
                mockTracker.WaitForRelatedEvent();
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void OnGetFileStreamCompletesFailureWithoutChangingAggregationEligibility(bool aggregate)
        {
            using (WindowsFileSystemVirtualizerTester tester = new WindowsFileSystemVirtualizerTester(this.Repo))
            {
                MockGVFSGitObjects objects = (MockGVFSGitObjects)this.Repo.GitObjects;
                objects.RejectHydrationWithOpenCircuit = aggregate;
                objects.FailHydrationWithoutAggregation = !aggregate;
                tester.InvokeGetFileDataCallback(expectedResult: HResult.Pending);
                Assert.That(tester.MockVirtualization.WaitForCompletionStatus(), Is.EqualTo((HResult)HResultExtensions.HResultFromNtStatus.FileNotAvailable));

                MockTracer tracer = (MockTracer)this.Repo.Context.Tracer;
                if (aggregate)
                {
                    Assert.That(tracer.RelatedErrorEvents.Single(), Does.Contain("\"FailureCategory\":\"CircuitBreakerOpen\""));
                    Assert.That(tracer.RelatedErrorEvents.Single(), Does.Contain("\"Operation\":\"GetFileStreamHandlerAsyncHandler\""));
                    Assert.That(tracer.RelatedErrorEvents.Single(), Does.Contain("\"AggregationResult\":\"Failed\""));
                }
                else
                {
                    Assert.That(tracer.RelatedErrorEvents.Single(), Does.Not.Contain("TelemetryAggregation"));
                }
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        [Category(CategoryConstants.ExceptionExpected)]
        public void MissingSizeErrorsRemainFailuresAndUseTypedAggregation(bool enumeration)
        {
            using (WindowsFileSystemVirtualizerTester tester = new WindowsFileSystemVirtualizerTester(this.Repo))
            {
                tester.GitIndexProjection.ThrowSizesUnavailableExceptionOnProjectionRequest = true;
                HResult result = enumeration
                    ? tester.MockVirtualization.RequiredCallbacks.StartDirectoryEnumerationCallback(1, Guid.NewGuid(), "test", TriggeringProcessId, TriggeringProcessImageFileName)
                    : tester.MockVirtualization.RequiredCallbacks.GetPlaceholderInfoCallback(1, "test.txt", TriggeringProcessId, TriggeringProcessImageFileName);
                Assert.That(result, Is.EqualTo(HResult.Pending));
                Assert.That(tester.MockVirtualization.WaitForCompletionStatus(), Is.EqualTo((HResult)HResultExtensions.HResultFromNtStatus.FileNotAvailable));

                MockTracer tracer = (MockTracer)this.Repo.Context.Tracer;
                Assert.That(tracer.RelatedErrorEvents.Single(), Does.Contain("\"FailureCategory\":\"SizesUnavailable\""));
                Assert.That(tester.MockVirtualization.CreatedPlaceholders, Is.Empty);
            }
        }

        [TestCase]
        public void OnGetFileStreamHandlesWriteFailure()
        {
            using (WindowsFileSystemVirtualizerTester tester = new WindowsFileSystemVirtualizerTester(this.Repo))
            {
                tester.MockVirtualization.WriteFileReturnResult = HResult.InternalError;
                tester.InvokeGetFileDataCallback(expectedResult: HResult.Pending);

                HResult result = tester.MockVirtualization.WaitForCompletionStatus();
                result.ShouldEqual(tester.MockVirtualization.WriteFileReturnResult);

                // The failure is tagged as a ProjFS write failure (a cause outside gvfs.exe's
                // control) so telemetry can bucket it apart from actionable hydration failures.
                MockTracer mockTracker = this.Repo.Context.Tracer as MockTracer;
                mockTracker.RelatedErrorEvents.Any(
                    e => e.Contains("\"BlobHydrationFailureCategory\":\"ProjFSWriteFailed\"")).ShouldBeTrue();
            }
        }

        [TestCase]
        [Category(CategoryConstants.ExceptionExpected)]
        public void OnGetFileStreamTagsSizeMismatch()
        {
            using (WindowsFileSystemVirtualizerTester tester = new WindowsFileSystemVirtualizerTester(this.Repo))
            {
                // The blob length served (FileLength) differs from the length ProjFS requested
                // (DefaultFileLength), so hydration fails with a size mismatch (actionable cause).
                MockGVFSGitObjects mockGVFSGitObjects = this.Repo.GitObjects as MockGVFSGitObjects;
                mockGVFSGitObjects.FileLength = MockGVFSGitObjects.DefaultFileLength - 1;

                tester.InvokeGetFileDataCallback(expectedResult: HResult.Pending);
                tester.MockVirtualization.WaitForCompletionStatus();

                MockTracer mockTracker = this.Repo.Context.Tracer as MockTracer;
                mockTracker.RelatedErrorEvents.Any(
                    e => e.Contains("\"BlobHydrationFailureCategory\":\"SizeMismatch\"")).ShouldBeTrue();
            }
        }

        [TestCase]
        [Category(CategoryConstants.ExceptionExpected)]
        public void OnGetFileStreamTagsUnexpectedException()
        {
            using (WindowsFileSystemVirtualizerTester tester = new WindowsFileSystemVirtualizerTester(this.Repo))
            {
                // A non-cancellation, non-GetFileStreamException failure hits the generic catch and
                // is tagged Unexpected so it can be triaged separately from known causes.
                MockGVFSGitObjects mockGVFSGitObjects = this.Repo.GitObjects as MockGVFSGitObjects;
                mockGVFSGitObjects.ThrowOnTryCopyBlobContentStream = true;

                tester.InvokeGetFileDataCallback(expectedResult: HResult.Pending);
                tester.MockVirtualization.WaitForCompletionStatus();

                MockTracer mockTracker = this.Repo.Context.Tracer as MockTracer;
                mockTracker.RelatedErrorEvents.Any(
                    e => e.Contains("\"BlobHydrationFailureCategory\":\"Unexpected\"")).ShouldBeTrue();
            }
        }

        [TestCase]
        [Category(CategoryConstants.ExceptionExpected)]
        public void OnGetFileStreamTagsLocalIO()
        {
            using (WindowsFileSystemVirtualizerTester tester = new WindowsFileSystemVirtualizerTester(this.Repo))
            {
                // Reading the blob content throws IOException while copying to the ProjFS buffer,
                // so hydration fails with the LocalIO cause (outside gvfs.exe's control).
                MockGVFSGitObjects mockGVFSGitObjects = this.Repo.GitObjects as MockGVFSGitObjects;
                mockGVFSGitObjects.ThrowIOExceptionDuringCopy = true;

                tester.InvokeGetFileDataCallback(expectedResult: HResult.Pending);
                tester.MockVirtualization.WaitForCompletionStatus();

                MockTracer mockTracker = this.Repo.Context.Tracer as MockTracer;
                mockTracker.RelatedErrorEvents.Any(
                    e => e.Contains("\"BlobHydrationFailureCategory\":\"LocalIO\"")).ShouldBeTrue();
            }
        }

        [TestCase]
        [Category(CategoryConstants.ExceptionExpected)]
        public void OnGetFileStreamHandlesHResultHandleResult()
        {
            using (WindowsFileSystemVirtualizerTester tester = new WindowsFileSystemVirtualizerTester(this.Repo))
            {
                tester.MockVirtualization.WriteFileReturnResult = HResult.Handle;
                tester.InvokeGetFileDataCallback(expectedResult: HResult.Pending);

                HResult result = tester.MockVirtualization.WaitForCompletionStatus();
                result.ShouldEqual(tester.MockVirtualization.WriteFileReturnResult);
                MockTracer mockTracker = this.Repo.Context.Tracer as MockTracer;
                mockTracker.RelatedErrorEvents.ShouldBeEmpty();
            }
        }

        [TestCase]
        public void OnStartDirectoryEnumerationCleansUpEnumerationWhenNativeCompletionFails()
        {
            using (WindowsFileSystemVirtualizerTester tester = new WindowsFileSystemVirtualizerTester(this.Repo))
            {
                MockTracer mockTracker = this.Repo.Context.Tracer as MockTracer;
                mockTracker.WaitRelatedEventName = "StartDirectoryEnumerationAsyncHandler_CommandNotCompleted";

                // Simulate the native ProjFS completion faulting under memory pressure. TryInvokeProjFS
                // must swallow the exception so the mount survives, and because ProjFS never accepted the
                // completion no EndDirectoryEnumeration callback will arrive. The enumeration registered by
                // the async handler must therefore be removed here rather than leaked.
                tester.MockVirtualization.CompleteCommandException = new NullReferenceException("simulated native ProjFS failure");

                Guid enumerationGuid = Guid.NewGuid();
                tester.GitIndexProjection.EnumerationInMemory = false;
                tester.MockVirtualization.RequiredCallbacks.StartDirectoryEnumerationCallback(1, enumerationGuid, "test", TriggeringProcessId, TriggeringProcessImageFileName).ShouldEqual(HResult.Pending);

                // Deterministically wait for the async handler to reach its cleanup path (the enumeration is
                // removed immediately before this event is emitted). If the native failure were reported as a
                // successful completion, this event would never fire.
                mockTracker.WaitForRelatedEvent();

                // The enumeration must already have been removed, so End cannot find it.
                tester.MockVirtualization.RequiredCallbacks.EndDirectoryEnumerationCallback(enumerationGuid).ShouldEqual(HResult.InternalError);
            }
        }
    }
}
