using GVFS.Common;
using GVFS.Common.Git;
using GVFS.Common.NamedPipes;
using GVFS.Tests.Should;
using GVFS.UnitTests.Category;
using GVFS.UnitTests.Mock.Common;
using GVFS.UnitTests.Mock.FileSystem;
using GVFS.UnitTests.Mock.Git;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using GVFS.Common.Tracing;

namespace GVFS.UnitTests.Common
{
    [TestFixture]
    public class GitStatusCacheTests
    {
        private static NamedPipeMessages.LockData statusCommandLockData = new NamedPipeMessages.LockData(123, false, false, "git status", "123");

        private MockFileSystem fileSystem;
        private MockGitProcess gitProcess;
        private GVFSContext context;
        private string gitParentPath;
        private string gvfsMetadataPath;
        private MockDirectory enlistmentDirectory;

        public static IEnumerable<Exception> ExceptionsThrownByCreateDirectory
        {
            get
            {
                yield return new IOException("Error creating directory");
                yield return new UnauthorizedAccessException("Error creating directory");
            }
        }

        [SetUp]
        public void SetUp()
        {
            MockTracer tracer = new MockTracer();

            string enlistmentRoot = Path.Combine("mock:", "GVFS", "UnitTests", "Repo");
            string statusCachePath = Path.Combine("mock:", "GVFS", "UnitTests", "Repo", GVFSPlatform.Instance.Constants.DotGVFSRoot, "gitStatusCache");

            this.gitProcess = new MockGitProcess();
            this.gitProcess.SetExpectedCommandResult($"--no-optional-locks status \"--serialize={statusCachePath}", () => new GitProcess.Result(string.Empty, string.Empty, 0), true);
            MockGVFSEnlistment enlistment = new MockGVFSEnlistment(enlistmentRoot, "fake://repoUrl", "fake://gitBinPath", this.gitProcess);
            enlistment.InitializeCachePathsFromKey("fake:\\gvfsSharedCache", "fakeCacheKey");

            this.gitParentPath = enlistment.WorkingDirectoryBackingRoot;
            this.gvfsMetadataPath = enlistment.DotGVFSRoot;

            this.enlistmentDirectory = new MockDirectory(
                enlistmentRoot,
                new MockDirectory[]
                {
                    new MockDirectory(this.gitParentPath, folders: null, files: null),
                },
                null);

            this.enlistmentDirectory.CreateFile(Path.Combine(this.gitParentPath, ".git", "config"), ".git config Contents", createDirectories: true);
            this.enlistmentDirectory.CreateFile(Path.Combine(this.gitParentPath, ".git", "HEAD"), ".git HEAD Contents", createDirectories: true);
            this.enlistmentDirectory.CreateFile(Path.Combine(this.gitParentPath, ".git", "logs", "HEAD"), "HEAD Contents", createDirectories: true);
            this.enlistmentDirectory.CreateFile(Path.Combine(this.gitParentPath, ".git", "info", "always_exclude"), "always_exclude Contents", createDirectories: true);
            this.enlistmentDirectory.CreateDirectory(Path.Combine(this.gitParentPath, ".git", "objects", "pack"));

            this.fileSystem = new MockFileSystem(this.enlistmentDirectory);
            this.fileSystem.AllowMoveFile = true;
            this.fileSystem.DeleteNonExistentFileThrowsException = false;

            this.context = new GVFSContext(
                tracer,
                this.fileSystem,
                new MockGitRepo(tracer, enlistment, this.fileSystem),
                enlistment);
            GitStatusCache.TEST_EnableHydrationSummaryOverride = false;
        }

        [TearDown]
        public void TearDown()
        {
            this.fileSystem = null;
            this.gitProcess = null;
            this.context = null;
            this.gitParentPath = null;
            this.gvfsMetadataPath = null;
            this.enlistmentDirectory = null;
            GitStatusCache.TEST_EnableHydrationSummaryOverride = null;
        }

        [TestCase]
        public void MissingHydrationProviderExplainsTheCacheMiss()
        {
            using (GitStatusCache cache = new GitStatusCache(this.context, TimeSpan.Zero))
            {
                cache.UpdateHydrationSummary();
                Assert.IsNull(cache.GetCachedHydrationSummary(out NamedPipeMessages.HydrationStatus.CacheInfo info));
                Assert.AreEqual(NamedPipeMessages.HydrationStatus.CacheState.ProviderUnavailable, info.State);
                Assert.IsNull(info.CalculationId);
                Assert.AreEqual(-1, info.AgeMs);
            }
        }

        [TestCase]
        public void DisabledHydrationFeatureExplainsTheCacheMiss()
        {
            using (GitStatusCache cache = new GitStatusCache(this.context, TimeSpan.Zero))
            {
                cache.SetProjectedFolderCountProvider(() => throw new AssertionException("A disabled feature must not count folders."));
                cache.UpdateHydrationSummary();
                Assert.IsNull(cache.GetCachedHydrationSummary(out NamedPipeMessages.HydrationStatus.CacheInfo info));
                Assert.AreEqual(NamedPipeMessages.HydrationStatus.CacheState.FeatureDisabled, info.State);
                Assert.IsFalse(info.CountsComplete);
            }
        }

        [TestCase]
        public void HydrationSnapshotPublishesCountsIdentityAgeAndInvalidationTogether()
        {
            this.AddHydrationIndex();
            using (DiagnosticStatusCache cache = new DiagnosticStatusCache(this.context))
            {
                cache.Calculate = diagnostics => this.CalculateHydration(diagnostics);
                cache.UpdateHydrationSummary();
                EnlistmentHydrationSummary summary = cache.GetCachedHydrationSummary(out NamedPipeMessages.HydrationStatus.CacheInfo info);
                Assert.IsNotNull(summary);
                Assert.AreEqual(summary.Diagnostics.CalculationId, info.CalculationId);
                Assert.AreEqual(NamedPipeMessages.HydrationStatus.CacheState.Available, info.State);
                Assert.IsTrue(info.CountsComplete);
                Assert.That(info.AgeMs, Is.GreaterThanOrEqualTo(0));
                Assert.IsFalse(info.Invalidated);
                cache.Invalidate();
                Assert.AreSame(summary, cache.GetCachedHydrationSummary(out info));
                Assert.IsTrue(info.Invalidated);
                cache.Enabled = false;
                cache.UpdateHydrationSummary();
                Assert.AreSame(summary, cache.GetCachedHydrationSummary(out NamedPipeMessages.HydrationStatus.CacheInfo skipped));
                Assert.AreEqual(NamedPipeMessages.HydrationStatus.CacheState.FeatureDisabled, skipped.State);
                Assert.AreEqual(info.CalculationId, skipped.CalculationId);
                Assert.That(skipped.AgeMs, Is.GreaterThanOrEqualTo(info.AgeMs));
                cache.Enabled = true;
                cache.Breaker.Disabled = true;
                cache.UpdateHydrationSummary();
                Assert.AreSame(summary, cache.GetCachedHydrationSummary(out info));
                Assert.AreEqual(NamedPipeMessages.HydrationStatus.CacheState.CircuitBreakerDisabled, info.State);
            }
        }

        [TestCase("InvalidCounts", false)]
        [TestCase("IncompleteData", true)]
        [TestCase("Cancelled", false)]
        [TestCase("Failed", false)]
        [Category(CategoryConstants.ExceptionExpected)]
        public void HydrationRefreshReportsEachCalculationState(string state, bool available)
        {
            this.AddHydrationIndex();
            using (DiagnosticStatusCache cache = new DiagnosticStatusCache(this.context))
            using (CancellationTokenSource cancelled = new CancellationTokenSource())
            {
                cancelled.Cancel();
                cache.Calculate = diagnostics => this.CalculateHydration(diagnostics);
                cache.UpdateHydrationSummary();
                Assert.IsNotNull(cache.GetCachedHydrationSummary(out _));
                cache.Calculate = diagnostics => this.CalculateHydration(diagnostics,
                    failModified: state == "IncompleteData",
                    folderCount: state == "InvalidCounts" ? -1 : 20,
                    throwFolder: state == "Failed",
                    cancellationToken: state == "Cancelled" ? cancelled.Token : default);
                cache.UpdateHydrationSummary();
                EnlistmentHydrationSummary summary = cache.GetCachedHydrationSummary(out NamedPipeMessages.HydrationStatus.CacheInfo info);
                Assert.AreEqual(available, summary != null);
                Assert.AreEqual(state, info.State.ToString());
                Assert.IsNotNull(info.CalculationId);
                Assert.IsFalse(info.CountsComplete && state != "InvalidCounts");
                Assert.AreEqual(state == "Failed" ? 1 : 0, cache.Breaker.Failures);
            }
        }

        [TestCase]
        [Category(CategoryConstants.ExceptionExpected)]
        public void ConfigurationFailurePreservesSummaryAndDoesNotTripBreaker()
        {
            this.AddHydrationIndex();
            using (DiagnosticStatusCache cache = new DiagnosticStatusCache(this.context))
            {
                cache.Calculate = diagnostics => this.CalculateHydration(diagnostics);
                cache.UpdateHydrationSummary();
                EnlistmentHydrationSummary previous = cache.GetCachedHydrationSummary(out NamedPipeMessages.HydrationStatus.CacheInfo previousInfo);
                cache.ThrowConfig = true;
                Assert.Throws<IOException>(() => cache.UpdateHydrationSummary());
                Assert.AreSame(previous, cache.GetCachedHydrationSummary(out NamedPipeMessages.HydrationStatus.CacheInfo info));
                Assert.AreEqual(previousInfo.CalculationId, info.CalculationId);
                Assert.AreEqual(NamedPipeMessages.HydrationStatus.CacheState.Available, info.State);
                Assert.AreEqual(0, cache.Breaker.Failures);
            }
        }

        [TestCase]
        [Category(CategoryConstants.ExceptionExpected)]
        public void UnexpectedCacheUpdateFailureHasExplicitStateAndTripsBreakerOnce()
        {
            using (DiagnosticStatusCache cache = new DiagnosticStatusCache(this.context))
            {
                cache.Calculate = diagnostics => throw new IOException("mock:\\private\\cache");
                cache.UpdateHydrationSummary();
                Assert.IsNull(cache.GetCachedHydrationSummary(out NamedPipeMessages.HydrationStatus.CacheInfo info));
                Assert.AreEqual(NamedPipeMessages.HydrationStatus.CacheState.Failed, info.State);
                Assert.AreEqual(1, cache.Breaker.Failures);
            }
        }

        [TestCase]
        [Category(CategoryConstants.ExceptionExpected)]
        public void ForeignCancellationDoesNotIncrementTheBreaker()
        {
            this.AddHydrationIndex();
            using (DiagnosticStatusCache cache = new DiagnosticStatusCache(this.context))
            {
                cache.Calculate = diagnostics => EnlistmentHydrationSummary.CreateSummary(this.context.Enlistment, this.fileSystem, this.context.Tracer,
                    () => throw new OperationCanceledException(), new EmptyHydrationPaths(), diagnostics: diagnostics);
                cache.UpdateHydrationSummary();
                Assert.IsNull(cache.GetCachedHydrationSummary(out NamedPipeMessages.HydrationStatus.CacheInfo info));
                Assert.AreEqual(NamedPipeMessages.HydrationStatus.CacheState.Cancelled, info.State);
                Assert.AreEqual(0, cache.Breaker.Failures);
            }
        }

        [TestCase]
        public void HeartbeatCountersResetAndCurrentStateIsAPointSample()
        {
            using (DiagnosticStatusCache cache = new DiagnosticStatusCache(this.context))
            {
                cache.Initialize();
                cache.GetCachedHydrationSummary(out _);
                cache.GetCachedHydrationSummary(out _);
                cache.Enabled = false;
                cache.UpdateHydrationSummary();
                EventMetadata metadata = new EventMetadata();
                Assert.IsTrue(cache.WriteTelemetryandReset(metadata));
                Assert.AreEqual(2L, metadata["HydrationStatus.RequestCount"]);
                Assert.AreEqual(2L, metadata["HydrationStatus.UnavailableCount"]);
                Assert.AreEqual(NamedPipeMessages.HydrationStatus.CacheState.FeatureDisabled, metadata["HydrationStatus.CurrentCacheState"]);
                EventMetadata reset = new EventMetadata();
                cache.WriteTelemetryandReset(reset);
                Assert.IsFalse(reset.ContainsKey("HydrationStatus.RequestCount"));
            }
        }

        private void AddHydrationIndex()
        {
            byte[] bytes = new byte[12];
            System.Text.Encoding.ASCII.GetBytes("DIRC").CopyTo(bytes, 0);
            bytes[7] = 4;
            bytes[11] = 100;
            MockFile file = new MockFile(this.context.Enlistment.GitIndexPath, bytes);
            this.enlistmentDirectory.FindDirectory(Path.Combine(this.gitParentPath, ".git")).Files.Add(file.FullName, file);
        }

        private EnlistmentHydrationSummary CalculateHydration(HydrationSummaryDiagnostics diagnostics, bool failModified = false,
            int folderCount = 20, bool throwFolder = false, CancellationToken cancellationToken = default)
        {
            return EnlistmentHydrationSummary.CreateSummary(this.context.Enlistment, this.fileSystem, this.context.Tracer,
                () => throwFolder ? throw new IOException("mock:\\private\\folder") : folderCount,
                new EmptyHydrationPaths { FailModified = failModified }, cancellationToken, diagnostics);
        }

        private sealed class EmptyHydrationPaths : EnlistmentPathData
        {
            public bool FailModified { get; set; }

            public override void LoadPlaceholdersFromDatabase(GVFSEnlistment enlistment, HydrationSummaryDiagnostics diagnostics = null)
            {
            }

            public override void LoadModifiedPaths(GVFSEnlistment enlistment, ITracer tracer, HydrationSummaryDiagnostics diagnostics = null)
            {
                diagnostics.ModifiedPathsState = this.FailModified ? HydrationDataState.Failed : HydrationDataState.Complete;
            }
        }

        private sealed class DiagnosticStatusCache : GitStatusCache
        {
            public DiagnosticStatusCache(GVFSContext context) : base(context, TimeSpan.Zero)
            {
                this.SetProjectedFolderCountProvider(() => 20);
            }

            public Func<HydrationSummaryDiagnostics, EnlistmentHydrationSummary> Calculate { get; set; }
            public bool Enabled { get; set; } = true;
            public bool ThrowConfig { get; set; }
            public MockHydrationBreaker Breaker { get; } = new MockHydrationBreaker();

            internal override bool GetHydrationSummaryEnabled()
            {
                return this.ThrowConfig ? throw new IOException("mock:\\config") : this.Enabled;
            }

            internal override HydrationStatusCircuitBreaker CreateHydrationCircuitBreaker()
            {
                return this.Breaker;
            }

            internal override EnlistmentHydrationSummary CreateHydrationSummary(HydrationSummaryDiagnostics diagnostics)
            {
                return this.Calculate(diagnostics);
            }
        }

        private sealed class MockHydrationBreaker : HydrationStatusCircuitBreaker
        {
            public MockHydrationBreaker() : base("mock:\\gvfs", NullTracer.Instance)
            {
            }

            public bool Disabled { get; set; }
            public int Failures { get; private set; }

            public override bool IsDisabled()
            {
                return this.Disabled;
            }

            public override void RecordFailure()
            {
                this.Failures++;
            }
        }

        [TestCase]
        public void CanInvalidateCleanCache()
        {
            this.enlistmentDirectory.CreateFile(Path.Combine(this.gvfsMetadataPath, GVFSConstants.DotGVFS.GitStatusCache.CachePath), "Git status cache contents", createDirectories: true);
            using (GitStatusCache statusCache = new GitStatusCache(this.context, TimeSpan.Zero))
            {
                statusCache.Initialize();
                statusCache.IsCacheReadyAndUpToDate().ShouldBeFalse();

                // Refresh the cache to put it into the clean state.
                statusCache.RefreshAndWait();

                bool result = statusCache.IsReadyForExternalAcquireLockRequests(statusCommandLockData, out _);

                result.ShouldBeTrue();
                statusCache.IsCacheReadyAndUpToDate().ShouldBeTrue();

                // Invalidate the cache, and make sure that it transistions into
                // the dirty state, and that commands are still allowed through.
                statusCache.Invalidate();
                statusCache.IsCacheReadyAndUpToDate().ShouldBeFalse();

                result = statusCache.IsReadyForExternalAcquireLockRequests(statusCommandLockData, out _);
                result.ShouldBeTrue();

                // After checking if we are ready for external lock requests, cache should still be dirty
                statusCache.IsCacheReadyAndUpToDate().ShouldBeFalse();

                statusCache.Shutdown();
            }
        }

        [TestCase]
        [Category(CategoryConstants.ExceptionExpected)]
        public void CacheFileErrorShouldBlock()
        {
            this.fileSystem.DeleteFileThrowsException = true;
            this.enlistmentDirectory.CreateFile(Path.Combine(this.gvfsMetadataPath, GVFSConstants.DotGVFS.GitStatusCache.CachePath), "Git status cache contents", createDirectories: true);

            using (GitStatusCache statusCache = new GitStatusCache(this.context, TimeSpan.Zero))
            {
                statusCache.Initialize();

                statusCache.IsCacheReadyAndUpToDate().ShouldBeFalse();

                bool isReady = statusCache.IsReadyForExternalAcquireLockRequests(statusCommandLockData, out _);
                isReady.ShouldBeFalse();

                statusCache.IsCacheReadyAndUpToDate().ShouldBeFalse();

                statusCache.Shutdown();
            }
        }

        [TestCase]
        public void CanRefreshCache()
        {
            this.enlistmentDirectory.CreateFile(Path.Combine(this.gvfsMetadataPath, GVFSConstants.DotGVFS.GitStatusCache.CachePath), "Git status cache contents", createDirectories: true);
            using (GitStatusCache statusCache = new GitStatusCache(this.context, TimeSpan.Zero))
            {
                statusCache.Initialize();

                statusCache.IsCacheReadyAndUpToDate().ShouldBeFalse();

                string message;
                bool result = statusCache.IsReadyForExternalAcquireLockRequests(statusCommandLockData, out message);
                result.ShouldBeTrue();

                statusCache.RefreshAndWait();

                result = statusCache.IsReadyForExternalAcquireLockRequests(statusCommandLockData, out message);
                result.ShouldBeTrue();

                statusCache.IsCacheReadyAndUpToDate().ShouldBeTrue();

                statusCache.Shutdown();
            }
        }

        [TestCaseSource("ExceptionsThrownByCreateDirectory")]
        [Category(CategoryConstants.ExceptionExpected)]
        public void HandlesExceptionsCreatingDirectory(Exception exceptionToThrow)
        {
            this.enlistmentDirectory.CreateFile(Path.Combine(this.gvfsMetadataPath, GVFSConstants.DotGVFS.GitStatusCache.CachePath), "Git status cache contents", createDirectories: true);
            this.fileSystem.ExceptionThrownByCreateDirectory = exceptionToThrow;
            using (GitStatusCache statusCache = new GitStatusCache(this.context, TimeSpan.Zero))
            {
                statusCache.Initialize();

                statusCache.IsCacheReadyAndUpToDate().ShouldBeFalse();

                string message;
                bool result = statusCache.IsReadyForExternalAcquireLockRequests(statusCommandLockData, out message);
                result.ShouldBeTrue();

                statusCache.RefreshAndWait();

                result = statusCache.IsReadyForExternalAcquireLockRequests(statusCommandLockData, out message);
                result.ShouldBeTrue();

                statusCache.IsCacheReadyAndUpToDate().ShouldBeFalse();

                statusCache.Shutdown();
            }
        }
    }
}
