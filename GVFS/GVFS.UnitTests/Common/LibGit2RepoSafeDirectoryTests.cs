using GVFS.Common.Git;
using GVFS.Common.Tracing;
using GVFS.Tests.Should;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.IO;

namespace GVFS.UnitTests.Common
{
    [TestFixture]
    public class LibGit2RepoSafeDirectoryTests
    {
        // ───────────────────────────────────────────────
        //  Layer 1 – NormalizePathForSafeDirectoryComparison (pure string tests)
        // ───────────────────────────────────────────────

        [TestCase(@"C:\Repos\Foo", "C:/REPOS/FOO")]
        [TestCase(@"c:\repos\foo", "C:/REPOS/FOO")]
        [TestCase("c:/repos/foo", "C:/REPOS/FOO")]
        [TestCase("C:/Repos/Foo/", "C:/REPOS/FOO")]
        [TestCase(@"C:\Repos\Foo\", "C:/REPOS/FOO")]
        [TestCase("C:/Repos/Foo///", "C:/REPOS/FOO")]
        [TestCase(@"C:\Repos/Mixed\Path", "C:/REPOS/MIXED/PATH")]
        [TestCase("already/normalized", "ALREADY/NORMALIZED")]
        public void NormalizePathForSafeDirectoryComparison_ProducesExpectedResult(string input, string expected)
        {
            LibGit2Repo.NormalizePathForSafeDirectoryComparison(input).ShouldEqual(expected);
        }

        [TestCase(null)]
        [TestCase("")]
        public void NormalizePathForSafeDirectoryComparison_HandlesNullAndEmpty(string input)
        {
            LibGit2Repo.NormalizePathForSafeDirectoryComparison(input).ShouldEqual(input);
        }

        [TestCase(@"C:\Repos\Foo", "c:/repos/foo")]
        [TestCase(@"C:\Repos\Foo", @"c:\Repos\Foo")]
        [TestCase("C:/Repos/Foo/", @"c:\repos\foo")]
        public void NormalizePathForSafeDirectoryComparison_CaseInsensitiveMatch(string a, string b)
        {
            LibGit2Repo.NormalizePathForSafeDirectoryComparison(a).ShouldEqual(LibGit2Repo.NormalizePathForSafeDirectoryComparison(b));
        }

        // ───────────────────────────────────────────────
        //  Layer 2 – Constructor control-flow tests via mock
        //  Tests go through the public LibGit2Repo(ITracer, string)
        //  constructor, which is the real entry point.
        // ───────────────────────────────────────────────

        [TestCase]
        public void Constructor_OwnershipError_WithMatchingConfigEntry_OpensSuccessfully()
        {
            // First Open() fails with ownership error, config has a case-variant match,
            // second Open() with the configured path succeeds → constructor completes.
            string requestedPath = @"C:\Repos\MyProject";
            string configuredPath = @"c:\repos\myproject";

            using (MockSafeDirectoryRepo repo = MockSafeDirectoryRepo.Create(
                requestedPath,
                safeDirectoryEntries: new[] { configuredPath },
                openableRepos: new HashSet<string>(StringComparer.Ordinal) { configuredPath }))
            {
                // Constructor completed without throwing — the workaround succeeded.
                repo.OpenedPaths.ShouldContain(p => p == configuredPath);
            }
        }

        [TestCase]
        public void Constructor_OwnershipError_NoMatchingConfigEntry_Throws()
        {
            // Open() fails with ownership error, config has no matching entry → throws.
            string requestedPath = @"C:\Repos\MyProject";

            Assert.Throws<InvalidDataException>(() =>
            {
                MockSafeDirectoryRepo.Create(
                    requestedPath,
                    safeDirectoryEntries: new[] { @"D:\Other\Repo" },
                    openableRepos: new HashSet<string>(StringComparer.Ordinal));
            });
        }

        [TestCase]
        public void Constructor_OwnershipError_MatchButOpenFails_Throws()
        {
            // Open() fails with ownership error, config entry matches but
            // the retry also fails → throws.
            string requestedPath = @"C:\Repos\MyProject";
            string configuredPath = @"c:\repos\myproject";

            Assert.Throws<InvalidDataException>(() =>
            {
                MockSafeDirectoryRepo.Create(
                    requestedPath,
                    safeDirectoryEntries: new[] { configuredPath },
                    openableRepos: new HashSet<string>(StringComparer.Ordinal));
            });
        }

        [TestCase]
        public void Constructor_OwnershipError_EmptyConfig_Throws()
        {
            string requestedPath = @"C:\Repos\MyProject";

            Assert.Throws<InvalidDataException>(() =>
            {
                MockSafeDirectoryRepo.Create(
                    requestedPath,
                    safeDirectoryEntries: Array.Empty<string>(),
                    openableRepos: new HashSet<string>(StringComparer.Ordinal));
            });
        }

        [TestCase]
        public void Constructor_OwnershipError_MultipleEntries_PicksCorrectMatch()
        {
            // Config has several entries; only one is a case-variant match.
            string requestedPath = @"C:\Repos\Target";
            string correctConfigEntry = @"c:/repos/target";

            using (MockSafeDirectoryRepo repo = MockSafeDirectoryRepo.Create(
                requestedPath,
                safeDirectoryEntries: new[]
                {
                    @"D:\Other\Repo",
                    correctConfigEntry,
                    @"E:\Unrelated\Path",
                },
                openableRepos: new HashSet<string>(StringComparer.Ordinal)
                {
                    correctConfigEntry,
                }))
            {
                repo.OpenedPaths.ShouldContain(p => p == correctConfigEntry);
            }
        }

        [TestCase]
        public void Constructor_NonOwnershipError_Throws()
        {
            // Open() fails with a different error (not ownership) → throws
            // without attempting safe.directory workaround.
            string requestedPath = @"C:\Repos\MyProject";

            Assert.Throws<InvalidDataException>(() =>
            {
                MockSafeDirectoryRepo.Create(
                    requestedPath,
                    safeDirectoryEntries: new[] { requestedPath },
                    openableRepos: new HashSet<string>(StringComparer.Ordinal),
                    nativeError: "repository not found");
            });

            MockSafeDirectoryRepo.LastCreatedInstance
                .SafeDirectoryCheckAttempted
                .ShouldBeFalse("Safe.directory workaround should not be attempted for non-ownership errors");
        }

        [TestCase]
        public void Constructor_OpenSucceedsFirstTime_NoWorkaround()
        {
            // Open() succeeds immediately → no safe.directory logic triggered.
            string requestedPath = @"C:\Repos\MyProject";

            using (MockSafeDirectoryRepo repo = MockSafeDirectoryRepo.Create(
                requestedPath,
                safeDirectoryEntries: Array.Empty<string>(),
                openableRepos: new HashSet<string>(StringComparer.Ordinal) { requestedPath }))
            {
                // Only one Open call (the initial one), no retry.
                repo.OpenedPaths.Count.ShouldEqual(1);
                repo.OpenedPaths.ShouldContain(p => p == requestedPath);
            }
        }

        /// <summary>
        /// Mock that intercepts all native P/Invoke calls so the public
        /// constructor can be exercised without touching libgit2.
        /// Uses thread-static config to work around virtual-call-from-
        /// constructor ordering (base ctor runs before derived fields init).
        /// </summary>
        private class MockSafeDirectoryRepo : LibGit2Repo
        {
            [ThreadStatic]
            private static MockConfig pendingConfig;

            [ThreadStatic]
            private static MockSafeDirectoryRepo lastCreatedInstance;

            private string[] safeDirectoryEntries;
            private HashSet<string> openableRepos;
            private string nativeError;

            public List<string> OpenedPaths { get; } = new List<string>();
            public bool SafeDirectoryCheckAttempted { get; private set; }

            /// <summary>
            /// Returns the most recently constructed instance on the current
            /// thread, even if the constructor threw an exception.
            /// </summary>
            public static MockSafeDirectoryRepo LastCreatedInstance => lastCreatedInstance;

            private MockSafeDirectoryRepo(ITracer tracer, string repoPath)
                : base(tracer, repoPath)
            {
                // Fields already populated from pendingConfig by the time
                // virtual methods are called from base ctor.
            }

            public static MockSafeDirectoryRepo Create(
                string repoPath,
                string[] safeDirectoryEntries,
                HashSet<string> openableRepos,
                string nativeError = "repository path '/some/path' is not owned by current user")
            {
                pendingConfig = new MockConfig
                {
                    SafeDirectoryEntries = safeDirectoryEntries,
                    OpenableRepos = openableRepos,
                    NativeError = nativeError,
                };

                try
                {
                    return new MockSafeDirectoryRepo(NullTracer.Instance, repoPath);
                }
                finally
                {
                    pendingConfig = null;
                }
            }

            protected override void InitNative()
            {
                // Grab config from thread-static before base ctor proceeds.
                this.safeDirectoryEntries = pendingConfig.SafeDirectoryEntries;
                this.openableRepos = pendingConfig.OpenableRepos;
                this.nativeError = pendingConfig.NativeError;
                lastCreatedInstance = this;
            }

            protected override void ShutdownNative()
            {
            }

            protected override string GetLastNativeError()
            {
                return this.nativeError;
            }

            protected override void GetSafeDirectoryConfigEntries(MultiVarConfigCallback callback)
            {
                this.SafeDirectoryCheckAttempted = true;
                foreach (string entry in this.safeDirectoryEntries)
                {
                    callback(entry);
                }
            }

            protected override Native.ResultCode TryOpenRepo(string path, out IntPtr repoHandle)
            {
                this.OpenedPaths.Add(path);
                repoHandle = IntPtr.Zero;
                return this.openableRepos.Contains(path)
                    ? Native.ResultCode.Success
                    : Native.ResultCode.Failure;
            }

            protected override void Dispose(bool disposing)
            {
            }

            private class MockConfig
            {
                public string[] SafeDirectoryEntries;
                public HashSet<string> OpenableRepos;
                public string NativeError;
            }
        }
    }
}
