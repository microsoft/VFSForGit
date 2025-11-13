using GVFS.Common;
using GVFS.Common.Git;
using GVFS.Tests.Should;
using GVFS.UnitTests.Mock.Common;
using GVFS.UnitTests.Mock.FileSystem;
using GVFS.UnitTests.Mock.Git;
using NUnit.Framework;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace GVFS.UnitTests.Common
{
    [TestFixture]
    public class EnlistmentHydrationSummaryTests
    {
        private MockFileSystem fileSystem;
        private MockGitProcess gitProcess;
        private GVFSContext context;
        private string gitParentPath;
        private string gvfsMetadataPath;
        private MockDirectory enlistmentDirectory;

        private const string HeadTreeId = "0123456789012345678901234567890123456789";
        private const int HeadPathCount = 42;

        public static IEnumerable<(string CachePrecontents, string ExpectedCachePostContents)> HeadTreeCountCacheContents
        {
            get
            {
                yield return (null, $"{HeadTreeId}\n{HeadPathCount}");
                yield return ($"{HeadTreeId}\n{HeadPathCount}", $"{HeadTreeId}\n{HeadPathCount}");
                yield return ($"{HeadTreeId}\n{HeadPathCount - 1}", $"{HeadTreeId}\n{HeadPathCount - 1}");
                yield return ($"{HeadTreeId.Replace("1", "a")}\n{HeadPathCount - 1}", $"{HeadTreeId}\n{HeadPathCount}");
                yield return ($"{HeadTreeId}\nabc", $"{HeadTreeId}\n{HeadPathCount}");
                yield return ($"{HeadTreeId}\nabc", $"{HeadTreeId}\n{HeadPathCount}");
                yield return ($"\n", $"{HeadTreeId}\n{HeadPathCount}");
                yield return ($"\nabc", $"{HeadTreeId}\n{HeadPathCount}");
            }
        }

        [SetUp]
        public void Setup()
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
        }

        [TestCaseSource("HeadTreeCountCacheContents")]
        public void HeadTreeCountCacheTests((string CachePrecontents, string ExpectedCachePostContents) args)
        {
            string totalPathCountPath = Path.Combine(this.gvfsMetadataPath, GVFSConstants.DotGVFS.GitStatusCache.TreeCount);
            if (args.CachePrecontents != null)
            {
                this.enlistmentDirectory.CreateFile(totalPathCountPath, args.CachePrecontents, createDirectories: true);
            }

            this.gitProcess.SetExpectedCommandResult("git show -s --format=%T HEAD",
                () => new GitProcess.Result(HeadTreeId, "", 0));
            this.gitProcess.SetExpectedCommandResult("ls-tree -r -d HEAD",
                () => new GitProcess.Result(
                    string.Join("\n", Enumerable.Range(0, HeadPathCount)
                                        .Select(x => x.ToString())),
                    "", 0));

            Assert.AreEqual(
                args.CachePrecontents != null,
                this.fileSystem.FileExists(totalPathCountPath));

            int result = EnlistmentHydrationSummary.GetHeadTreeCount(this.context.Enlistment, this.context.FileSystem);

            this.fileSystem.FileExists(totalPathCountPath).ShouldBeTrue();
            var postContents = this.fileSystem.ReadAllText(totalPathCountPath);
            Assert.AreEqual(
                args.ExpectedCachePostContents,
                postContents);
            Assert.AreEqual(postContents.Split('\n')[1], result.ToString());
        }
    }
}
