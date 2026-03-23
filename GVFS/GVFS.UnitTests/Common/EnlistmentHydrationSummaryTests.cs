using GVFS.Common;
using GVFS.Common.Git;
using GVFS.Common.Tracing;
using GVFS.UnitTests.Mock.Common;
using GVFS.UnitTests.Mock.FileSystem;
using GVFS.UnitTests.Mock.Git;
using GVFS.Virtualization.Projection;
using NUnit.Framework;
using System;
using System.IO;
using System.Text;
using System.Threading;
using static GVFS.Virtualization.Projection.GitIndexProjection.GitIndexParser;

namespace GVFS.UnitTests.Common
{
    [TestFixture]
    public class EnlistmentHydrationSummaryTests
    {
        [TestCase]
        public void CountIndexFolders_FlatDirectories()
        {
            int count = CountFoldersInIndex(new[] { "src/file1.cs", "test/file2.cs" });
            Assert.AreEqual(2, count); // "src", "test"
        }

        [TestCase]
        public void CountIndexFolders_NestedDirectories()
        {
            int count = CountFoldersInIndex(new[] { "a/b/c/file1.cs", "a/b/file2.cs", "x/file3.cs" });
            Assert.AreEqual(4, count); // "a", "a/b", "a/b/c", "x"
        }

        [TestCase]
        public void CountIndexFolders_RootFilesOnly()
        {
            int count = CountFoldersInIndex(new[] { "README.md", ".gitignore" });
            Assert.AreEqual(0, count);
        }

        [TestCase]
        public void CountIndexFolders_EmptyIndex()
        {
            int count = CountFoldersInIndex(new string[0]);
            Assert.AreEqual(0, count);
        }

        [TestCase]
        public void CountIndexFolders_DeepNesting()
        {
            int count = CountFoldersInIndex(new[] { "a/b/c/d/e/file.txt" });
            Assert.AreEqual(5, count); // "a", "a/b", "a/b/c", "a/b/c/d", "a/b/c/d/e"
        }

        [TestCase]
        public void CountIndexFolders_ExcludesNonSkipWorktree()
        {
            // Entries without skip-worktree and with NoConflicts merge state are not
            // projected, so their directories should not be counted.
            IndexEntryInfo[] entries = new[]
            {
                new IndexEntryInfo("src/file1.cs", skipWorktree: true),
                new IndexEntryInfo("vendor/lib/file2.cs", skipWorktree: false),
            };

            int count = CountFoldersInIndex(entries);
            Assert.AreEqual(1, count); // only "src"
        }

        [TestCase]
        public void CountIndexFolders_ExcludesCommonAncestor()
        {
            // CommonAncestor entries are excluded even when skip-worktree is set.
            IndexEntryInfo[] entries = new[]
            {
                new IndexEntryInfo("src/file1.cs", skipWorktree: true),
                new IndexEntryInfo("conflict/file2.cs", skipWorktree: true, mergeState: MergeStage.CommonAncestor),
            };

            int count = CountFoldersInIndex(entries);
            Assert.AreEqual(1, count); // only "src"
        }

        [TestCase]
        public void CountIndexFolders_IncludesYoursMergeState()
        {
            // Yours merge-state entries are projected even without skip-worktree.
            IndexEntryInfo[] entries = new[]
            {
                new IndexEntryInfo("src/file1.cs", skipWorktree: true),
                new IndexEntryInfo("merge/file2.cs", skipWorktree: false, mergeState: MergeStage.Yours),
            };

            int count = CountFoldersInIndex(entries);
            Assert.AreEqual(2, count); // "src" and "merge"
        }

        private static int CountFoldersInIndex(string[] paths)
        {
            byte[] indexBytes = CreateV4Index(paths);
            using (MemoryStream stream = new MemoryStream(indexBytes))
            {
                return GitIndexProjection.CountIndexFolders(new MockTracer(), stream);
            }
        }

        private static int CountFoldersInIndex(IndexEntryInfo[] entries)
        {
            byte[] indexBytes = CreateV4Index(entries);
            using (MemoryStream stream = new MemoryStream(indexBytes))
            {
                return GitIndexProjection.CountIndexFolders(new MockTracer(), stream);
            }
        }

        /// <summary>
        /// Create a minimal git index v4 binary matching the format GitIndexGenerator produces.
        /// Uses prefix-compression for paths (v4 format).
        /// </summary>
        private static byte[] CreateV4Index(string[] paths)
        {
            IndexEntryInfo[] entries = new IndexEntryInfo[paths.Length];
            for (int i = 0; i < paths.Length; i++)
            {
                entries[i] = new IndexEntryInfo(paths[i], skipWorktree: true);
            }

            return CreateV4Index(entries);
        }

        private static byte[] CreateV4Index(IndexEntryInfo[] entries)
        {
            // Stat entry header matching GitIndexGenerator.EntryHeader:
            // 40 bytes with file mode 0x81A4 (regular file, 644) at offset 24-27
            byte[] entryHeader = new byte[40];
            entryHeader[26] = 0x81;
            entryHeader[27] = 0xA4;

            using (MemoryStream ms = new MemoryStream())
            using (BinaryWriter bw = new BinaryWriter(ms))
            {
                // Header
                bw.Write(new byte[] { (byte)'D', (byte)'I', (byte)'R', (byte)'C' });
                WriteBigEndian32(bw, 4); // version 4
                WriteBigEndian32(bw, (uint)entries.Length);

                string previousPath = string.Empty;
                foreach (IndexEntryInfo entry in entries)
                {
                    // 40-byte stat entry header with valid file mode
                    bw.Write(entryHeader);
                    // 20 bytes SHA-1 (zeros)
                    bw.Write(new byte[20]);
                    // Flags: path length in low 12 bits, merge state in bits 12-13, extended bit 14
                    byte[] pathBytes = Encoding.UTF8.GetBytes(entry.Path);
                    ushort flags = (ushort)(Math.Min(pathBytes.Length, 0xFFF) | 0x4000 | ((ushort)entry.MergeState << 12));
                    WriteBigEndian16(bw, flags);
                    // Extended flags: skip-worktree bit
                    ushort extendedFlags = entry.SkipWorktree ? (ushort)0x4000 : (ushort)0;
                    WriteBigEndian16(bw, extendedFlags);

                    // V4 prefix compression: compute common prefix with previous path
                    int commonLen = 0;
                    int maxCommon = Math.Min(previousPath.Length, entry.Path.Length);
                    while (commonLen < maxCommon && previousPath[commonLen] == entry.Path[commonLen])
                    {
                        commonLen++;
                    }

                    int replaceLen = previousPath.Length - commonLen;
                    string suffix = entry.Path.Substring(commonLen);

                    // Write replace length as varint
                    WriteVarint(bw, replaceLen);
                    // Write suffix + null terminator
                    bw.Write(Encoding.UTF8.GetBytes(suffix));
                    bw.Write((byte)0);

                    previousPath = entry.Path;
                }

                return ms.ToArray();
            }
        }

        private struct IndexEntryInfo
        {
            public string Path;
            public bool SkipWorktree;
            public MergeStage MergeState;

            public IndexEntryInfo(string path, bool skipWorktree, MergeStage mergeState = MergeStage.NoConflicts)
            {
                this.Path = path;
                this.SkipWorktree = skipWorktree;
                this.MergeState = mergeState;
            }
        }

        private static void WriteBigEndian32(BinaryWriter bw, uint value)
        {
            bw.Write((byte)((value >> 24) & 0xFF));
            bw.Write((byte)((value >> 16) & 0xFF));
            bw.Write((byte)((value >> 8) & 0xFF));
            bw.Write((byte)(value & 0xFF));
        }

        private static void WriteBigEndian16(BinaryWriter bw, ushort value)
        {
            bw.Write((byte)((value >> 8) & 0xFF));
            bw.Write((byte)(value & 0xFF));
        }

        private static void WriteVarint(BinaryWriter bw, int value)
        {
            // Git index v4 varint encoding (same as ReadReplaceLength in GitIndexParser)
            if (value < 0x80)
            {
                bw.Write((byte)value);
                return;
            }

            byte[] bytes = new byte[5];
            int pos = 4;
            bytes[pos] = (byte)(value & 0x7F);
            value = (value >> 7) - 1;
            while (value >= 0)
            {
                pos--;
                bytes[pos] = (byte)(0x80 | (value & 0x7F));
                value = (value >> 7) - 1;
            }

            bw.Write(bytes, pos, 5 - pos);
        }
    }

    /// <summary>
    /// Tests for EnlistmentHydrationSummary that require the full mock filesystem/context.
    /// </summary>
    [TestFixture]
    public class EnlistmentHydrationSummaryContextTests
    {
        private MockFileSystem fileSystem;
        private MockTracer tracer;
        private GVFSContext context;
        private string gitParentPath;
        private MockDirectory enlistmentDirectory;

        [SetUp]
        public void Setup()
        {
            this.tracer = new MockTracer();

            string enlistmentRoot = Path.Combine("mock:", "GVFS", "UnitTests", "Repo");
            string statusCachePath = Path.Combine("mock:", "GVFS", "UnitTests", "Repo", GVFSPlatform.Instance.Constants.DotGVFSRoot, "gitStatusCache");

            MockGitProcess gitProcess = new MockGitProcess();
            gitProcess.SetExpectedCommandResult($"--no-optional-locks status \"--serialize={statusCachePath}", () => new GitProcess.Result(string.Empty, string.Empty, 0), true);
            MockGVFSEnlistment enlistment = new MockGVFSEnlistment(enlistmentRoot, "fake://repoUrl", "fake://gitBinPath", gitProcess);
            enlistment.InitializeCachePathsFromKey("fake:\\gvfsSharedCache", "fakeCacheKey");

            this.gitParentPath = enlistment.WorkingDirectoryBackingRoot;

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
                this.tracer,
                this.fileSystem,
                new MockGitRepo(this.tracer, enlistment, this.fileSystem),
                enlistment);
        }

        [TestCase]
        public void GetIndexFileCount_IndexTooSmall_ReturnsNegativeOne()
        {
            string indexPath = Path.Combine(this.gitParentPath, ".git", "index");
            this.enlistmentDirectory.CreateFile(indexPath, "short", createDirectories: true);

            int result = EnlistmentHydrationSummary.GetIndexFileCount(
                this.context.Enlistment, this.context.FileSystem);

            Assert.AreEqual(-1, result);
        }

        [TestCase]
        public void CreateSummary_CancelledToken_ReturnsInvalidSummary()
        {
            // Set up a valid index file so CreateSummary gets past GetIndexFileCount
            // before hitting the first cancellation check.
            string indexPath = Path.Combine(this.gitParentPath, ".git", "index");
            byte[] indexBytes = new byte[12];
            indexBytes[11] = 100; // file count = 100 (big-endian)
            MockFile indexFile = new MockFile(indexPath, indexBytes);
            MockDirectory gitDir = this.enlistmentDirectory.FindDirectory(Path.Combine(this.gitParentPath, ".git"));
            gitDir.Files.Add(indexFile.FullName, indexFile);

            CancellationTokenSource cts = new CancellationTokenSource();
            cts.Cancel();

            Func<int> dummyProvider = () => 0;
            EnlistmentHydrationSummary result = EnlistmentHydrationSummary.CreateSummary(
                this.context.Enlistment, this.context.FileSystem, this.context.Tracer, dummyProvider, cts.Token);

            Assert.IsFalse(result.IsValid);
            Assert.IsNull(result.Error);
        }
    }
}
