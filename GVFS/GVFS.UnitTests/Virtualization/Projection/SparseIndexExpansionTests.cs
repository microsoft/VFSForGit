using GVFS.Common;
using GVFS.Common.Database;
using GVFS.Common.FileSystem;
using GVFS.Common.Git;
using GVFS.Common.Tracing;
using GVFS.Tests.Should;
using GVFS.UnitTests.Category;
using GVFS.UnitTests.Mock.Common;
using GVFS.UnitTests.Mock.FileSystem;
using GVFS.UnitTests.Mock.Git;
using GVFS.Virtualization.Background;
using GVFS.Virtualization.Projection;
using Moq;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using static GVFS.Virtualization.Projection.GitIndexProjection;
using static GVFS.Virtualization.Projection.GitIndexProjection.GitIndexParser;
using Entry = GVFS.UnitTests.Mock.Git.GitIndexV4Writer.Entry;

namespace GVFS.UnitTests.Virtualization.Projection
{
    /// <summary>
    /// Tests for the expansion of the sparse-directory entries of a sparse index (git index.sparse),
    /// when gvfs.auto-sparse-index is true. Each test builds a repository of trees in memory, and
    /// writes the full index and a sparse index for the same files. The projection of the sparse
    /// index must be the same as the projection of the full index: every folder, every file, every
    /// SHA, and every file mode.
    /// </summary>
    [TestFixture]
    public class SparseIndexExpansionTests
    {
        private const uint RegularMode = 0x81A4;    // 100644
        private const uint ExecutableMode = 0x81ED; // 100755
        private const uint SymLinkMode = 0xA000;    // 120000
        private const uint GitLinkMode = 0xE000;    // 160000
        private const uint TreeMode = 0x4000;       // 040000

        private string testRoot;
        private List<IDisposable> disposables;

        public static IEnumerable<TestCaseData> EquivalentRepositories()
        {
            foreach (bool supportsFileMode in new[] { true, false })
            {
                yield return new TestCaseData(
                    supportsFileMode,
                    new[] { "a/x/b.txt", "a/x/deep/er/c.txt", "a/x/deep/d.txt", "a/y.txt", "b.txt", "z/1.txt" },
                    new string[0],
                    new[] { "a/x", "z" })
                    .SetName($"NestedSparseDirectoryNextToInConeFiles(supportsFileMode={supportsFileMode})");

                yield return new TestCaseData(
                    supportsFileMode,
                    new[] { "big/a/1.txt", "big/a/b/2.txt", "big/a/b/c/3.txt", "big/a/b/c/d/4.txt", "big/e/5.txt", "big/6.txt" },
                    new string[0],
                    new[] { "big" })
                    .SetName($"DeeplyNestedTrees(supportsFileMode={supportsFileMode})");

                yield return new TestCaseData(
                    supportsFileMode,
                    new[] { "p/a.b", "p/a/1.txt", "p/a-", "p/a0", "p/A/2.txt", "q/a.b", "q/a/1.txt", "q/a-", "q/a0", "r.txt" },
                    new string[0],
                    new[] { "p" })
                    .SetName($"TreeOrderDiffersFromProjectionOrder(supportsFileMode={supportsFileMode})");

                yield return new TestCaseData(
                    supportsFileMode,
                    new[] { "c/Foo/Sub/1.txt", "c/foo/sub/2.txt", "c/foo/3.txt", "c/FOO.txt", "d.txt" },
                    new string[0],
                    new[] { "c" })
                    .SetName($"FolderNamesThatDifferOnlyInCase(supportsFileMode={supportsFileMode})");

                yield return new TestCaseData(
                    supportsFileMode,
                    new[] { "e/file.txt", "e/inner/f.txt" },
                    new[] { "e/empty", "e/inner/empty", "onlyempty/a", "onlyempty/b/c" },
                    new[] { "e", "onlyempty" })
                    .SetName($"EmptyTrees(supportsFileMode={supportsFileMode})");

                yield return new TestCaseData(
                    supportsFileMode,
                    new[] { "m/1.txt", "n/2.txt", "o/3.txt", "o/p/4.txt" },
                    new string[0],
                    new[] { "m", "n", "o/p" })
                    .SetName($"AdjacentSparseDirectories(supportsFileMode={supportsFileMode})");
            }
        }

        [SetUp]
        public void SetUp()
        {
            if (GVFSPlatform.Instance == null)
            {
                GVFSPlatform.Register(new MockPlatform());
            }

            RepoMetadata.Shutdown();
            this.disposables = new List<IDisposable>();
            this.testRoot = Path.Combine(Path.GetTempPath(), "SparseIndexExpansionTests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(this.testRoot);
        }

        [TearDown]
        public void TearDown()
        {
            // GVFSPlatform.Instance is shared by every fixture, so restore its default.
            SetSupportsFileMode(true);
            RepoMetadata.Shutdown();
            this.disposables.ForEach(disposable => disposable.Dispose());

            if (Directory.Exists(this.testRoot))
            {
                Directory.Delete(this.testRoot, recursive: true);
            }
        }

        [TestCaseSource(nameof(EquivalentRepositories))]
        public void SparseIndexProjectsTheSameAsFullIndex(bool supportsFileMode, string[] files, string[] emptyTrees, string[] sparseDirectories)
        {
            SetSupportsFileMode(supportsFileMode);
            TestRepository repository = new TestRepository();
            foreach (string file in files)
            {
                repository.AddFile(file);
            }

            foreach (string emptyTree in emptyTrees)
            {
                repository.AddEmptyTree(emptyTree);
            }

            this.ShouldProjectTheSame(repository, sparseDirectories);
        }

        [TestCase(true)]
        [TestCase(false)]
        public void SparseIndexProjectsFileModesTheSameAsFullIndex(bool supportsFileMode)
        {
            SetSupportsFileMode(supportsFileMode);
            TestRepository repository = new TestRepository();
            repository.AddFile("tools/run.sh", ExecutableMode);
            repository.AddFile("tools/link", SymLinkMode);
            repository.AddFile("tools/module", GitLinkMode);
            repository.AddFile("tools/plain.txt", RegularMode);
            repository.AddFile("tools/nested/run2.sh", ExecutableMode);
            repository.AddFile("top.txt", RegularMode);

            List<string> projection = this.ShouldProjectTheSame(repository, "tools");

            if (supportsFileMode)
            {
                projection.ShouldContain(line => line.StartsWith("tools/run.sh=") && line.EndsWith(" Regular 755"));
                projection.ShouldContain(line => line.StartsWith("tools/link=") && line.EndsWith(" SymLink 0"));
                projection.ShouldContain(line => line.StartsWith("tools/module=") && line.EndsWith(" GitLink 0"));
                projection.ShouldContain(line => line.StartsWith("tools/nested/run2.sh=") && line.EndsWith(" Regular 755"));
            }
        }

        [TestCase]
        public void SparseIndexIncludesFoldersTheSameAsFullIndex()
        {
            // The gvfs sparse folders (ISparseCollection) mark folders as included or excluded
            SetSupportsFileMode(false);
            TestRepository repository = new TestRepository();
            foreach (string file in new[] { "a/b/c/1.txt", "a/b/c/d/e/7.txt", "a/b/d/2.txt", "a/e/3.txt", "a/4.txt", "f/g/5.txt", "h/6.txt", "h/i/8.txt" })
            {
                repository.AddFile(file);
            }

            // "a/b/c" is a recursive sparse folder with folders under it. "h" is a recursive sparse
            // folder above the sparse directory "h/i".
            Mock<ISparseCollection> sparseCollection = new Mock<ISparseCollection>(MockBehavior.Strict);
            sparseCollection
                .Setup(x => x.GetAll())
                .Returns(new HashSet<string>(StringComparer.OrdinalIgnoreCase) { Path.Combine("a", "b", "c"), "h" });

            this.ShouldProjectTheSame(repository, new[] { "a", "f" }, sparseCollection: sparseCollection.Object);
            this.ShouldProjectTheSame(repository, new[] { "a/b", "a/e", "f/g", "h/i" }, sparseCollection: sparseCollection.Object);
        }

        [TestCase]
        public void ModifiedPathsUnderSparseDirectoryAreProjectedAsInFullIndex()
        {
            // For a full index, the virtual file system hook reports the modified paths, and git
            // clears the skip-worktree bit of each index entry that they match. The full index does
            // not project those entries. Matches ignore case.
            SetSupportsFileMode(false);
            TestRepository repository = new TestRepository();
            foreach (string file in new[] { "s/keep.txt", "s/edited.txt", "s/dir/1.txt", "s/dir/sub/2.txt", "s/only/3.txt", "s/other/4.txt", "t/5.txt", "u/6.txt", "p/x/7.txt", "p/y.txt" })
            {
                repository.AddFile(file);
            }

            // "p/" is a modified folder above the sparse directory "p/x"
            ModifiedPathsDatabase modifiedPaths = this.CreateModifiedPaths(
                "S/EDITED.txt",
                "s/dir/",
                "s/only/3.txt",
                "t/",
                "p/",
                "new-folder/",
                "s/other/not-in-tree.txt");

            string[] nonSkipWorktree = { "s/edited.txt", "s/dir/1.txt", "s/dir/sub/2.txt", "s/only/3.txt", "t/5.txt", "p/x/7.txt", "p/y.txt" };
            List<string> full = this.BuildProjection(repository.CreateFullIndex(nonSkipWorktree), repository, modifiedPaths: null);
            List<string> sparse = this.BuildProjection(repository.CreateSparseIndex(new[] { "s", "t", "u", "p/x" }, "p/y.txt"), repository, modifiedPaths);

            sparse.ShouldMatchInOrder(full);
            sparse.ShouldContain(line => line.StartsWith("s/keep.txt="));
            sparse.ShouldNotContain(line => line.StartsWith("s/edited.txt=") || line.StartsWith("s/dir/") || line.StartsWith("s/only/") || line.StartsWith("t/") || line.StartsWith("p/"));
        }

        [TestCase]
        [Category(CategoryConstants.ExceptionExpected)]
        public void FeatureFlagEnablesExpansionThroughRepository()
        {
            SetSupportsFileMode(false);
            TestRepository repository = new TestRepository();
            repository.AddFile("a/b/c.txt");
            repository.AddFile("d.txt");
            string indexPath = Path.Combine(this.testRoot, "index");
            File.WriteAllBytes(indexPath, repository.CreateSparseIndex("a"));

            MockTracer tracer = new MockTracer();
            MockGVFSEnlistment enlistment = new MockGVFSEnlistment(this.testRoot, "mock://repoUrl", Path.Combine("mock:", "git"), gitProcess: null);
            ConfiguredGitRepo gitRepo = new ConfiguredGitRepo(tracer, enlistment, repository.Store);
            gitRepo.Config[GVFSConstants.GitConfig.AutoSparseIndex] = true;
            GVFSContext context = new GVFSContext(tracer, new PhysicalFileSystem(), gitRepo, enlistment);

            using (GitIndexProjection projection = new GitIndexProjection(context, null, null, null, null, null, null, null))
            {
                projection.BuildProjectionFromPath(tracer, indexPath);
                DumpProjection(projection).ShouldMatchInOrder(
                    "a/",
                    "a/b/",
                    "a/b/c.txt=" + TestRepository.HashOf("blob a/b/c.txt"),
                    "d.txt=" + TestRepository.HashOf("blob d.txt"));
            }

            gitRepo.Config[GVFSConstants.GitConfig.AutoSparseIndex] = false;
            using (GitIndexProjection projection = new GitIndexProjection(context, null, null, null, null, null, null, null))
            {
                Assert.Throws<SparseIndexNotSupportedException>(() => projection.BuildProjectionFromPath(tracer, indexPath));
            }
        }

        [TestCase]
        public void FullIndexWithExpansionEnabledReadsNoTrees()
        {
            SetSupportsFileMode(false);
            TestRepository repository = new TestRepository();
            repository.AddFile("a/b.txt");
            repository.AddFile("c.txt");

            List<string> withoutExpansion = this.BuildProjection(repository.CreateFullIndex(), treeReader: null);
            List<string> withExpansion = this.BuildProjection(repository.CreateFullIndex(), repository);

            withExpansion.ShouldMatchInOrder(withoutExpansion);
            repository.Store.ReadCount.ShouldEqual(0);
        }

        [TestCase]
        [Category(CategoryConstants.ExceptionExpected)]
        public void SparseIndexWithExpansionDisabledStillFails()
        {
            SetSupportsFileMode(false);
            TestRepository repository = new TestRepository();
            repository.AddFile("a/b.txt");

            SparseIndexNotSupportedException error = Assert.Throws<SparseIndexNotSupportedException>(
                () => this.BuildProjection(repository.CreateSparseIndex("a"), treeReader: null));
            error.Message.ShouldContain("sparse-directory entry 'a/'", "1. If the enlistment is mounted, run 'gvfs unmount'.");
        }

        [TestCase]
        [Category(CategoryConstants.ExceptionExpected)]
        public void FileNamesThatDifferOnlyInCaseFailAsInFullIndex()
        {
            SetSupportsFileMode(false);
            TestRepository repository = new TestRepository();
            repository.AddFile("c/A.txt");
            repository.AddFile("c/a.txt");

            Exception fullError = Assert.Catch<Exception>(() => this.BuildProjection(repository.CreateFullIndex(), repository));
            Exception sparseError = Assert.Catch<Exception>(() => this.BuildProjection(repository.CreateSparseIndex("c"), repository));
            sparseError.GetType().ShouldEqual(fullError.GetType());
            sparseError.Message.ShouldEqual(fullError.Message);
        }

        [TestCase]
        public void MissingTreeIsDownloadedOnce()
        {
            SetSupportsFileMode(false);
            TestRepository repository = new TestRepository();
            repository.AddFile("a/b/c.txt");
            repository.AddFile("a/d.txt");
            string subtree = repository.GetTreeSha("a/b");
            repository.Store.MissingTrees.Add(subtree);
            repository.Store.DownloadableTrees.Add(subtree);

            List<string> full = this.BuildProjection(repository.CreateFullIndex(), treeReader: null);
            List<string> sparse = this.BuildProjection(repository.CreateSparseIndex("a"), repository);

            sparse.ShouldMatchInOrder(full);
            repository.Store.Downloads.ShouldMatchInOrder(subtree);
        }

        [TestCase(TreeEnumerationResult.MissingTree, false)]
        [TestCase(TreeEnumerationResult.MissingTree, true)]
        [TestCase(TreeEnumerationResult.CorruptTree, false)]
        [TestCase(TreeEnumerationResult.NotTree, false)]
        [TestCase(TreeEnumerationResult.RepositoryUnavailable, false)]
        [Category(CategoryConstants.ExceptionExpected)]
        public void UnreadableTreeFailsTheBuild(TreeEnumerationResult readResult, bool downloadSucceedsButTreeStaysMissing)
        {
            SetSupportsFileMode(false);
            TestRepository repository = new TestRepository();
            repository.AddFile("a/b/c.txt");
            repository.AddFile("a/d.txt");
            string subtree = repository.GetTreeSha("a/b");
            repository.Store.FailingTrees[subtree] = readResult;
            if (downloadSucceedsButTreeStaysMissing)
            {
                repository.Store.DownloadSucceedsWithoutEffect.Add(subtree);
            }

            SparseIndexExpansionException error = Assert.Throws<SparseIndexExpansionException>(
                () => this.BuildProjection(repository.CreateSparseIndex("a"), repository));
            error.Message.ShouldContain(
                "Cannot read tree " + subtree + " for the folder 'a/b' in the sparse index (" + readResult + ")",
                "run 'git -c core.hookspath= config gvfs.auto-sparse-index false'");

            // Only a missing tree can become readable after a download
            repository.Store.Downloads.Count.ShouldEqual(readResult == TreeEnumerationResult.MissingTree ? 1 : 0);
        }

        [TestCase("..")]
        [TestCase(".")]
        [TestCase(".GIT")]
        [TestCase(".git.")]
        [TestCase(".Git ")]
        [TestCase("GIT~1")]
        [TestCase("a\\b")]
        [TestCase("a/b")]
        [TestCase("a\0b")]
        [TestCase("")]
        [Category(CategoryConstants.ExceptionExpected)]
        public void InvalidTreeEntryNameFailsTheBuild(string name)
        {
            SetSupportsFileMode(false);
            TestRepository repository = new TestRepository();
            string blob = TestRepository.HashOf("blob");
            string badTree = repository.Store.AddTree(new TreeEntry(name, RegularMode, blob));
            byte[] index = GitIndexV4Writer.Create(new Entry("bad/", GitIndexV4Writer.SparseDirectoryMode, badTree));

            SparseIndexExpansionException error = Assert.Throws<SparseIndexExpansionException>(() => this.BuildProjection(index, repository));
            error.Message.ShouldContain("The folder 'bad' in the sparse index has a tree entry with the name", "Git cannot add this name to an index.");
        }

        [TestCase("a{0}b")]
        [TestCase("{")]
        [Category(CategoryConstants.ExceptionExpected)]
        public void TreeEntryNameWithBracesIsReportedLiterally(string folderName)
        {
            // The name reaches the message without string.Format, so braces do not throw FormatException
            SetSupportsFileMode(false);
            TestRepository repository = new TestRepository();
            string badTree = repository.Store.AddTree(new TreeEntry("..", RegularMode, TestRepository.HashOf("blob")));
            string parent = repository.Store.AddTree(new TreeEntry(folderName, TreeMode, badTree));
            byte[] index = GitIndexV4Writer.Create(new Entry("top/", GitIndexV4Writer.SparseDirectoryMode, parent));

            SparseIndexExpansionException error = Assert.Throws<SparseIndexExpansionException>(() => this.BuildProjection(index, repository));
            error.Message.ShouldContain("The folder 'top/" + folderName + "' in the sparse index");
        }

        [TestCase(0x81B6u)] // 100666
        [TestCase(0xA1A4u)] // 120644
        [TestCase(0x6000u)] // unknown type
        [Category(CategoryConstants.ExceptionExpected)]
        public void InvalidFileModeFailsTheBuildWhenThePlatformRecordsModes(uint mode)
        {
            SetSupportsFileMode(true);
            TestRepository repository = new TestRepository();
            string badTree = repository.Store.AddTree(new TreeEntry("file", mode, TestRepository.HashOf("blob")));
            byte[] index = GitIndexV4Writer.Create(new Entry("bad/", GitIndexV4Writer.SparseDirectoryMode, badTree));

            SparseIndexExpansionException error = Assert.Throws<SparseIndexExpansionException>(() => this.BuildProjection(index, repository));
            error.Message.ShouldContain("The file 'bad/file' in the folder 'bad' of the sparse index has the invalid type");
        }

        [TestCase]
        [Category(CategoryConstants.ExceptionExpected)]
        public void PathLongerThanTheIndexLimitFailsTheBuild()
        {
            // Also a tree that is deeper than the projection can hold. The walk does not recurse,
            // so the depth cannot exhaust the stack.
            SetSupportsFileMode(false);
            TestRepository repository = new TestRepository();
            string tree = repository.Store.AddTree(new TreeEntry("file.txt", RegularMode, TestRepository.HashOf("blob")));
            for (int i = 0; i < SparseDirectoryExpander.MaxPathLength; i++)
            {
                tree = repository.Store.AddTree(new TreeEntry("d", TreeMode, tree));
            }

            byte[] index = GitIndexV4Writer.Create(new Entry("deep/", GitIndexV4Writer.SparseDirectoryMode, tree));

            SparseIndexExpansionException error = Assert.Throws<SparseIndexExpansionException>(() => this.BuildProjection(index, repository));
            error.Message.ShouldContain("has a path that is longer than " + SparseDirectoryExpander.MaxPathLength + " bytes");
        }

        [TestCase]
        public void LongNamesAndWideTreesProjectTheSameAsFullIndexWithSmallPools()
        {
            // A sparse index has few entries, so the string pools start small. A name that is
            // longer than one pool expansion must not overrun the pool.
            SetSupportsFileMode(false);
            TestRepository repository = new TestRepository();
            for (int i = 0; i < 150; i++)
            {
                repository.AddFile("wide/" + i.ToString("D3") + new string('x', 200) + ".txt");
            }

            repository.AddFile("top.txt");

            List<string> full = this.BuildProjection(repository.CreateFullIndex(), treeReader: null);
            ResetStringPools(indexEntryCount: 1);
            List<string> sparse = this.BuildProjection(repository.CreateSparseIndex("wide"), repository);

            sparse.ShouldMatchInOrder(full);
            sparse.Count.ShouldEqual(152);
        }

        [TestCase]
        public void RebuildUsesTheCurrentModifiedPaths()
        {
            SetSupportsFileMode(false);
            TestRepository repository = new TestRepository();
            repository.AddFile("s/a.txt");
            repository.AddFile("s/b.txt");
            string indexPath = Path.Combine(this.testRoot, "index");
            File.WriteAllBytes(indexPath, repository.CreateSparseIndex("s"));
            ModifiedPathsDatabase modifiedPaths = this.CreateModifiedPaths();

            GitIndexProjection projection = this.CreateProjection(new Mock<IPlaceholderCollection>(MockBehavior.Strict).Object, modifiedPaths, sparseCollection: null);
            try
            {
                projection.EnableSparseIndexExpansionForTests(repository.Store);
                projection.BuildProjectionFromPath(new MockTracer(), indexPath);
                DumpProjection(projection).ShouldContain(line => line.StartsWith("s/a.txt="));

                modifiedPaths.TryAdd("s/a.txt", isFolder: false, out bool _).ShouldBeTrue();
                projection.BuildProjectionFromPath(new MockTracer(), indexPath);
                DumpProjection(projection).ShouldNotContain(line => line.StartsWith("s/a.txt="));
                DumpProjection(projection).ShouldContain(line => line.StartsWith("s/b.txt="));
            }
            finally
            {
                projection.Dispose();
            }
        }

        [TestCase]
        public void SparseDirectoryWithoutSkipWorktreeIsNotProjected()
        {
            // As a full index does not project an entry without the skip-worktree bit
            SetSupportsFileMode(false);
            TestRepository repository = new TestRepository();
            repository.AddFile("gone/a.txt");
            repository.AddFile("kept.txt");
            byte[] index = GitIndexV4Writer.Create(
                new Entry("gone/", GitIndexV4Writer.SparseDirectoryMode, repository.GetTreeSha("gone")) { SkipWorktree = false },
                new Entry("kept.txt", GitIndexV4Writer.RegularFileMode, TestRepository.HashOf("blob kept.txt")));

            this.BuildProjection(index, repository).ShouldMatchInOrder("kept.txt=" + TestRepository.HashOf("blob kept.txt"));
            repository.Store.ReadCount.ShouldEqual(0);
        }

        [TestCase]
        [Category(CategoryConstants.ExceptionExpected)]
        public void InitializeFailsAndInvalidatesProjectionWhenATreeCannotBeRead()
        {
            SetSupportsFileMode(false);
            TestRepository repository = new TestRepository();
            repository.AddFile("a/b.txt");
            MockGVFSEnlistment enlistment = this.CreateEnlistmentWithMetadata();
            File.WriteAllBytes(enlistment.GitIndexPath, repository.CreateSparseIndex("a"));
            repository.Store.FailingTrees[repository.GetTreeSha("a")] = TreeEnumerationResult.MissingTree;

            GitIndexProjection projection = this.CreateProjection(new Mock<IPlaceholderCollection>(MockBehavior.Strict).Object, modifiedPaths: null, sparseCollection: null, RepoMetadata.Instance);
            try
            {
                projection.EnableSparseIndexExpansionForTests(repository.Store);
                Assert.Throws<SparseIndexExpansionException>(() => projection.Initialize(backgroundFileSystemTaskRunner: null));
                RepoMetadata.Instance.GetProjectionInvalid().ShouldBeTrue();
            }
            finally
            {
                projection.Dispose();
            }
        }

        [TestCase(true)]
        [TestCase(false)]
        public void ProjectedFileCountIsReportedOnlyForASparseIndex(bool sparse)
        {
            SetSupportsFileMode(false);
            TestRepository repository = new TestRepository();
            repository.AddFile("a/b.txt");
            repository.AddFile("a/c/d.txt");
            repository.AddFile("e.txt");
            string indexPath = Path.Combine(this.testRoot, "index");
            File.WriteAllBytes(indexPath, sparse ? repository.CreateSparseIndex("a") : repository.CreateFullIndex());

            GitIndexProjection projection = this.CreateProjection(new Mock<IPlaceholderCollection>(MockBehavior.Strict).Object, modifiedPaths: null, sparseCollection: null);
            try
            {
                projection.EnableSparseIndexExpansionForTests(repository.Store);
                projection.BuildProjectionFromPath(new MockTracer(), indexPath);
                projection.GetProjectedFileCountIfSparseIndex().ShouldEqual(sparse ? 3 : -1);
            }
            finally
            {
                projection.Dispose();
            }
        }

        [TestCase("src", true)]
        [TestCase("src//x/", false)]
        [TestCase("/src/", false)]
        [TestCase("src/", false, (int)MergeStage.Theirs)]
        [Category(CategoryConstants.ExceptionExpected)]
        public void MalformedSparseDirectoryEntryFailsValidation(string path, bool supportsFileMode, int mergeStage = 0)
        {
            SetSupportsFileMode(supportsFileMode);
            byte[] index = GitIndexV4Writer.Create(new Entry(path, GitIndexV4Writer.SparseDirectoryMode) { MergeState = (MergeStage)mergeStage });

            using (MemoryStream stream = new MemoryStream(index))
            {
                InvalidDataException error = Assert.Throws<InvalidDataException>(
                    () => GitIndexParser.ValidateIndex(new MockTracer(), stream, GitIndexParser.SparseDirectoryHandling.Accept));
                error.Message.ShouldContain("The sparse-directory entry '" + path + "'");
            }
        }

        [TestCase]
        [Category(CategoryConstants.ExceptionExpected)]
        public void SparseDirectoryPathWithFileModeFailsValidation()
        {
            SetSupportsFileMode(true);
            byte[] index = GitIndexV4Writer.Create(new Entry("src/", GitIndexV4Writer.RegularFileMode));

            using (MemoryStream stream = new MemoryStream(index))
            {
                InvalidDataException error = Assert.Throws<InvalidDataException>(
                    () => GitIndexParser.ValidateIndex(new MockTracer(), stream, GitIndexParser.SparseDirectoryHandling.Accept));
                error.Message.ShouldContain("does not have the directory mode");
            }
        }


        [TestCase]
        [Category(CategoryConstants.ExceptionExpected)]
        public void RepairValidationAcceptsSparseIndexWithoutReadingTrees()
        {
            SetSupportsFileMode(false);
            string indexPath = Path.Combine(this.testRoot, "index");
            File.WriteAllBytes(indexPath, GitIndexV4Writer.Create(new Entry("README.md"), new Entry("src/", GitIndexV4Writer.SparseDirectoryMode)));

            GitIndexProjection projection = this.CreateProjection(placeholders: null, modifiedPaths: null, sparseCollection: null);
            try
            {
                projection.BuildProjectionFromPath(new MockTracer(), indexPath, acceptSparseDirectories: true);

                // The setting applies to one call only
                Assert.Throws<SparseIndexNotSupportedException>(() => projection.BuildProjectionFromPath(new MockTracer(), indexPath));
            }
            finally
            {
                projection.Dispose();
            }
        }

        [TestCase]
        public void PlaceholderScanTreatsFilesInSparseDirectoryTreeAsInIndex()
        {
            // A full index has an entry, with the skip-worktree bit, for each file in the tree of a
            // collapsed folder. The placeholder scan must not move their placeholders to the
            // modified paths. A placeholder for a file that is not in the tree (for example after
            // 'git reset --mixed') is not in the index, so it moves to the modified paths and the
            // file stays on disk.
            SetSupportsFileMode(false);
            TestRepository repository = new TestRepository();
            repository.AddFile("src/deep/file.txt");
            repository.AddFile("src/Foo/1.txt");
            repository.AddFile("src/foo/2.txt");
            repository.AddFile("unreadable/x.txt");
            repository.Store.FailingTrees[repository.GetTreeSha("unreadable")] = TreeEnumerationResult.CorruptTree;
            ModifiedPathsDatabase modifiedPaths = this.CreateModifiedPaths();

            string inTree = Path.Combine("src", "DEEP", "File.txt");
            string inSecondCaseFolder = Path.Combine("src", "FOO", "2.txt");
            string notInTree = Path.Combine("src", "deep", "stale.txt");
            string folderNotFile = Path.Combine("src", "deep");
            string underUnreadableTree = Path.Combine("unreadable", "x.txt");
            string underNotSkipWorktree = Path.Combine("gone", "file.txt");
            string notInIndex = Path.Combine("other", "file.txt");
            string similarPrefix = Path.Combine("src2", "file.txt");

            Mock<IPlaceholderCollection> placeholders = new Mock<IPlaceholderCollection>(MockBehavior.Strict);
            placeholders
                .Setup(x => x.GetAllFilePaths())
                .Returns(new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                {
                    inTree, inSecondCaseFolder, notInTree, folderNotFile, underUnreadableTree, underNotSkipWorktree, notInIndex, similarPrefix, "README.md"
                });
            foreach (string removed in new[] { notInTree, folderNotFile, underUnreadableTree, underNotSkipWorktree, notInIndex, similarPrefix })
            {
                placeholders.Setup(x => x.Remove(removed));
            }

            byte[] index = GitIndexV4Writer.Create(
                new Entry("README.md"),
                new Entry("gone/", GitIndexV4Writer.SparseDirectoryMode) { SkipWorktree = false },
                new Entry("src/", GitIndexV4Writer.SparseDirectoryMode, repository.GetTreeSha("src")),
                new Entry("unreadable/", GitIndexV4Writer.SparseDirectoryMode, repository.GetTreeSha("unreadable")));

            GitIndexProjection projection = this.CreateProjection(placeholders.Object, modifiedPaths, sparseCollection: null);
            try
            {
                projection.EnableSparseIndexExpansionForTests(repository.Store);
                GitIndexParser parser = new GitIndexParser(projection);
                using (MemoryStream stream = new MemoryStream(index))
                {
                    parser.AddMissingModifiedFilesAndRemoveThemFromPlaceholderList(new MockTracer(), stream).ShouldEqual(FileSystemTaskResult.Success);
                }

                placeholders.VerifyAll();
                placeholders.Verify(x => x.Remove(It.IsAny<string>()), Times.Exactly(6));
                modifiedPaths.Contains("gone", isFolder: true).ShouldBeTrue();
                modifiedPaths.Contains("src/deep/stale.txt", isFolder: false).ShouldBeTrue();
                modifiedPaths.Contains("unreadable/x.txt", isFolder: false).ShouldBeTrue();
                modifiedPaths.Contains("other/file.txt", isFolder: false).ShouldBeTrue();
                modifiedPaths.Contains("src2/file.txt", isFolder: false).ShouldBeTrue();
                modifiedPaths.Contains("src/deep/file.txt", isFolder: false).ShouldBeFalse();
                modifiedPaths.Contains("src/foo/2.txt", isFolder: false).ShouldBeFalse();
            }
            finally
            {
                projection.Dispose();
            }
        }

        private static void SetSupportsFileMode(bool value)
        {
            ((MockPlatformFileSystem)GVFSPlatform.Instance.FileSystem).SupportsFileMode = value;
        }

        private static void ResetStringPools(uint indexEntryCount)
        {
            LazyUTF8String.ResetPool(new MockTracer(), indexEntryCount);
        }

        /// <summary>
        /// Lists every folder and file of the projection, depth first in projection order. A
        /// folder line shows whether the folder is included. A file line shows the SHA, and the
        /// file type and mode when the platform supports file modes.
        /// </summary>
        private static List<string> DumpProjection(GitIndexProjection projection)
        {
            FieldInfo rootField = typeof(GitIndexProjection).GetField("rootFolderData", BindingFlags.Instance | BindingFlags.NonPublic);
            FolderData root = (FolderData)rootField.GetValue(projection);
            List<string> lines = new List<string>();
            DumpFolder(projection, root, string.Empty, lines);
            return lines;
        }

        private static void DumpFolder(GitIndexProjection projection, FolderData folder, string folderPath, List<string> lines)
        {
            for (int i = 0; i < folder.ChildEntries.Count; i++)
            {
                FolderEntryData child = folder.ChildEntries[i];
                string childPath = folderPath + child.Name.GetString();
                if (child.IsFolder)
                {
                    FolderData childFolder = (FolderData)child;
                    lines.Add(childPath + "/" + (childFolder.IsIncluded ? string.Empty : " excluded"));
                    DumpFolder(projection, childFolder, childPath + "/", lines);
                }
                else
                {
                    string line = childPath + "=" + ((FileData)child).ConvertShaToString().ToLowerInvariant();
                    if (GVFSPlatform.Instance.FileSystem.SupportsFileMode)
                    {
                        projection.GetFileTypeAndMode(childPath, out FileType fileType, out ushort fileMode);
                        line += " " + fileType + " " + Convert.ToString(fileMode, 8);
                    }

                    lines.Add(line);
                }
            }
        }

        private List<string> ShouldProjectTheSame(TestRepository repository, params string[] sparseDirectories)
        {
            return this.ShouldProjectTheSame(repository, sparseDirectories, sparseCollection: null);
        }

        private List<string> ShouldProjectTheSame(TestRepository repository, string[] sparseDirectories, ISparseCollection sparseCollection)
        {
            List<string> full = this.BuildProjection(repository.CreateFullIndex(), treeReader: null, sparseCollection: sparseCollection);
            List<string> sparse = this.BuildProjection(repository.CreateSparseIndex(sparseDirectories), repository, sparseCollection: sparseCollection);

            full.Count.ShouldBeAtLeast(1);
            sparse.ShouldMatchInOrder(full);
            return sparse;
        }

        private List<string> BuildProjection(byte[] index, TestRepository treeReader, ModifiedPathsDatabase modifiedPaths = null, ISparseCollection sparseCollection = null)
        {
            string indexPath = Path.Combine(this.testRoot, "index");
            File.WriteAllBytes(indexPath, index);

            GitIndexProjection projection = this.CreateProjection(new Mock<IPlaceholderCollection>(MockBehavior.Strict).Object, modifiedPaths, sparseCollection);
            try
            {
                if (treeReader != null)
                {
                    projection.EnableSparseIndexExpansionForTests(treeReader.Store);
                }

                projection.BuildProjectionFromPath(new MockTracer(), indexPath);
                return DumpProjection(projection);
            }
            finally
            {
                projection.Dispose();
            }
        }

        private ModifiedPathsDatabase CreateModifiedPaths(params string[] paths)
        {
            string dataPath = Path.Combine(this.testRoot, "ModifiedPaths_" + Guid.NewGuid().ToString("N") + ".dat");
            ModifiedPathsDatabase.TryLoadOrCreate(new MockTracer(), dataPath, new PhysicalFileSystem(), out ModifiedPathsDatabase modifiedPaths, out string error).ShouldBeTrue(error);
            this.disposables.Add(modifiedPaths);
            foreach (string path in paths)
            {
                bool isFolder = path.EndsWith("/");
                modifiedPaths.TryAdd(isFolder ? path.TrimEnd('/') : path, isFolder, out bool _).ShouldBeTrue();
            }

            return modifiedPaths;
        }

        private GitIndexProjection CreateProjection(IPlaceholderCollection placeholders, ModifiedPathsDatabase modifiedPaths, ISparseCollection sparseCollection, RepoMetadata repoMetadata = null)
        {
            MockTracer tracer = new MockTracer();
            MockGVFSEnlistment enlistment = this.CreateEnlistment();
            GVFSContext context = new GVFSContext(tracer, new PhysicalFileSystem(), new MockGitRepo(tracer, enlistment, fileSystem: null), enlistment);
            return new GitIndexProjection(
                context,
                gitObjects: null,
                blobSizes: null,
                repoMetadata: repoMetadata,
                fileSystemVirtualizer: null,
                placeholderDatabase: placeholders,
                sparseCollection: sparseCollection,
                modifiedPaths: modifiedPaths);
        }

        private MockGVFSEnlistment CreateEnlistment()
        {
            return new MockGVFSEnlistment(this.testRoot, "mock://repoUrl", Path.Combine("mock:", "git"), gitProcess: null);
        }

        private MockGVFSEnlistment CreateEnlistmentWithMetadata()
        {
            MockGVFSEnlistment enlistment = this.CreateEnlistment();
            Directory.CreateDirectory(Path.GetDirectoryName(enlistment.GitIndexPath));
            Directory.CreateDirectory(enlistment.DotGVFSRoot);
            RepoMetadata.TryInitialize(new MockTracer(), new OverwritingFileSystem(), enlistment.DotGVFSRoot, out string error).ShouldBeTrue(error);
            RepoMetadata.Instance.SetProjectionInvalid(false);
            return enlistment;
        }

        private class OverwritingFileSystem : PhysicalFileSystem
        {
            // RepoMetadata flushes through MoveAndOverwriteFile, which MockPlatform does not support
            public override void MoveAndOverwriteFile(string sourceFileName, string destinationFilename)
            {
                File.Move(sourceFileName, destinationFilename, overwrite: true);
            }
        }

        private class ConfiguredGitRepo : MockGitRepo
        {
            private readonly IProjectionTreeReader trees;

            public ConfiguredGitRepo(ITracer tracer, Enlistment enlistment, IProjectionTreeReader trees)
                : base(tracer, enlistment, fileSystem: null)
            {
                this.trees = trees;
            }

            public Dictionary<string, bool> Config { get; } = new Dictionary<string, bool>();

            public override bool GetConfigBoolOrDefault(string name, bool defaultValue)
            {
                return this.Config.TryGetValue(name, out bool value) ? value : defaultValue;
            }

            public override TreeEnumerationResult TryEnumerateTree(byte[] treeId, TreeEntryVisitor visitor)
            {
                return this.trees.TryReadTree(treeId, visitor);
            }
        }

        private class TreeEntry
        {
            public TreeEntry(string name, uint mode, string sha)
            {
                this.Name = name;
                this.Mode = mode;
                this.Sha = sha;
            }

            public string Name { get; }
            public uint Mode { get; }
            public string Sha { get; }

            public bool IsTree => this.Mode == TreeMode;

            // Git sorts the entries of a tree as if the name of a tree ends in '/'
            public string SortKey => this.IsTree ? this.Name + "/" : this.Name;
        }

        /// <summary>
        /// An object store with trees only, that the expander reads through <see cref="IProjectionTreeReader"/>.
        /// </summary>
        private class FakeTreeStore : IProjectionTreeReader
        {
            private readonly Dictionary<string, List<TreeEntry>> trees = new Dictionary<string, List<TreeEntry>>(StringComparer.OrdinalIgnoreCase);

            public HashSet<string> MissingTrees { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            public HashSet<string> DownloadableTrees { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // Trees whose read always gives this result
            public Dictionary<string, TreeEnumerationResult> FailingTrees { get; } = new Dictionary<string, TreeEnumerationResult>(StringComparer.OrdinalIgnoreCase);

            // Trees whose download reports success, but does not make the tree readable
            public HashSet<string> DownloadSucceedsWithoutEffect { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            public List<string> Downloads { get; } = new List<string>();
            public int ReadCount { get; private set; }

            public string AddTree(params TreeEntry[] entries)
            {
                List<TreeEntry> sorted = entries.OrderBy(entry => entry.SortKey, StringComparer.Ordinal).ToList();
                string sha = TestRepository.HashOf("tree " + string.Join("|", sorted.Select(entry => $"{entry.Mode:X} {entry.Name} {entry.Sha}")));
                this.trees[sha] = sorted;
                return sha;
            }

            public TreeEnumerationResult TryReadTree(byte[] treeId, TreeEntryVisitor visitor)
            {
                this.ReadCount++;
                string sha = SHA1Util.HexStringFromBytes(treeId);
                if (this.FailingTrees.TryGetValue(sha, out TreeEnumerationResult failure))
                {
                    return failure;
                }

                if (this.MissingTrees.Contains(sha) || !this.trees.TryGetValue(sha, out List<TreeEntry> entries))
                {
                    return TreeEnumerationResult.MissingTree;
                }

                foreach (TreeEntry entry in entries)
                {
                    visitor(Encoding.UTF8.GetBytes(entry.Name), SHA1Util.BytesFromHexString(entry.Sha), entry.Mode);
                }

                return TreeEnumerationResult.Success;
            }

            public bool TryDownloadTree(string treeSha)
            {
                this.Downloads.Add(treeSha);
                if (this.DownloadSucceedsWithoutEffect.Contains(treeSha))
                {
                    return true;
                }

                if (this.DownloadableTrees.Remove(treeSha))
                {
                    this.MissingTrees.Remove(treeSha);
                    return true;
                }

                return false;
            }
        }

        /// <summary>
        /// A set of files and empty trees. It builds the trees in a <see cref="FakeTreeStore"/>, and
        /// writes a full index or a sparse index for the files.
        /// </summary>
        private class TestRepository
        {
            private readonly SortedDictionary<string, TreeEntry> files = new SortedDictionary<string, TreeEntry>(StringComparer.Ordinal);
            private readonly HashSet<string> emptyTrees = new HashSet<string>(StringComparer.Ordinal);
            private Dictionary<string, string> treeShas;

            public FakeTreeStore Store { get; } = new FakeTreeStore();

            public static string HashOf(string value)
            {
                using (SHA1 sha1 = SHA1.Create())
                {
                    return SHA1Util.HexStringFromBytes(sha1.ComputeHash(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
                }
            }

            public void AddFile(string path, uint mode = RegularMode)
            {
                this.files.Add(path, new TreeEntry(path, mode, HashOf("blob " + path)));
                this.treeShas = null;
            }

            public void AddEmptyTree(string path)
            {
                this.emptyTrees.Add(path);
                this.treeShas = null;
            }

            public string GetTreeSha(string folderPath)
            {
                return this.BuildTrees()[folderPath];
            }

            public byte[] CreateFullIndex(params string[] notSkipWorktree)
            {
                return GitIndexV4Writer.Create(this.files.Values
                    .Select(file => new Entry(file.Name, (ushort)file.Mode, file.Sha) { SkipWorktree = !notSkipWorktree.Contains(file.Name) })
                    .ToArray());
            }

            public byte[] CreateSparseIndex(params string[] sparseDirectories)
            {
                return this.CreateSparseIndex(sparseDirectories, new string[0]);
            }

            public byte[] CreateSparseIndex(string[] sparseDirectories, params string[] notSkipWorktree)
            {
                Dictionary<string, string> trees = this.BuildTrees();
                List<Entry> entries = this.files.Values
                    .Where(file => !sparseDirectories.Any(folder => file.Name.StartsWith(folder + "/", StringComparison.Ordinal)))
                    .Select(file => new Entry(file.Name, (ushort)file.Mode, file.Sha) { SkipWorktree = !notSkipWorktree.Contains(file.Name) })
                    .ToList();
                entries.AddRange(sparseDirectories.Select(folder => new Entry(folder + "/", GitIndexV4Writer.SparseDirectoryMode, trees[folder])));
                return GitIndexV4Writer.Create(entries.OrderBy(entry => entry.Path, StringComparer.Ordinal).ToArray());
            }

            /// <summary>
            /// Adds a tree for every folder to the store, and returns the tree SHA of each folder path.
            /// </summary>
            private Dictionary<string, string> BuildTrees()
            {
                if (this.treeShas != null)
                {
                    return this.treeShas;
                }

                Dictionary<string, List<TreeEntry>> folderEntries = new Dictionary<string, List<TreeEntry>>(StringComparer.Ordinal) { { string.Empty, new List<TreeEntry>() } };
                foreach (string folder in this.files.Keys.Select(GetParent).Concat(this.emptyTrees).ToList())
                {
                    for (string current = folder; current.Length > 0; current = GetParent(current))
                    {
                        if (!folderEntries.ContainsKey(current))
                        {
                            folderEntries.Add(current, new List<TreeEntry>());
                        }
                    }
                }

                foreach (TreeEntry file in this.files.Values)
                {
                    folderEntries[GetParent(file.Name)].Add(new TreeEntry(GetName(file.Name), file.Mode, file.Sha));
                }

                this.treeShas = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (string folder in folderEntries.Keys.OrderByDescending(path => path.Count(c => c == '/')).ThenByDescending(path => path.Length))
                {
                    if (folder.Length == 0)
                    {
                        continue;
                    }

                    string sha = this.Store.AddTree(folderEntries[folder].ToArray());
                    this.treeShas.Add(folder, sha);
                    folderEntries[GetParent(folder)].Add(new TreeEntry(GetName(folder), TreeMode, sha));
                }

                return this.treeShas;
            }

            private static string GetParent(string path)
            {
                int separator = path.LastIndexOf('/');
                return separator < 0 ? string.Empty : path.Substring(0, separator);
            }

            private static string GetName(string path)
            {
                return path.Substring(path.LastIndexOf('/') + 1);
            }
        }
    }
}
