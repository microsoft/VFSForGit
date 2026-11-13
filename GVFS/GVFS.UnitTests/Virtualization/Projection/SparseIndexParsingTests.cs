using GVFS.Common;
using GVFS.Common.Database;
using GVFS.Common.FileSystem;
using GVFS.RepairJobs;
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
using System.Threading;
using static GVFS.Virtualization.Projection.GitIndexProjection;
using Entry = GVFS.UnitTests.Mock.Git.GitIndexV4Writer.Entry;

namespace GVFS.UnitTests.Virtualization.Projection
{
    /// <summary>
    /// Tests for how the projection parser handles a sparse index (git index.sparse). A sparse
    /// index stores a collapsed subtree as one sparse-directory entry, with mode 040000 and a
    /// path that ends in '/'. The parser must reject such an entry with an actionable message.
    /// A full index must parse as before.
    /// </summary>
    [TestFixture]
    public class SparseIndexParsingTests
    {
        private const ushort RegularFileMode = GitIndexV4Writer.RegularFileMode;
        private const ushort SparseDirectoryMode = GitIndexV4Writer.SparseDirectoryMode;

        private const string DisableSparseIndexCommand = "git -c core.hookspath= config --worktree index.sparse false";
        private const string ExpandIndexCommand = "git -c core.virtualfilesystem= -c core.hookspath= -c index.sparse=false update-index --force-write-index";

        private string testRoot;

        [SetUp]
        public void SetUp()
        {
            if (GVFSPlatform.Instance == null)
            {
                GVFSPlatform.Register(new MockPlatform());
            }

            RepoMetadata.Shutdown();
            this.testRoot = Path.Combine(Path.GetTempPath(), "SparseIndexParsingTests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(this.testRoot);
        }

        [TearDown]
        public void TearDown()
        {
            // GVFSPlatform.Instance is shared by every fixture, so restore its default.
            SetSupportsFileMode(true);
            RepoMetadata.Shutdown();

            if (Directory.Exists(this.testRoot))
            {
                Directory.Delete(this.testRoot, recursive: true);
            }
        }

        [TestCase]
        public void FileTypeAndModeRecognizesSparseDirectoryMode()
        {
            FileTypeAndMode typeAndMode = new FileTypeAndMode(SparseDirectoryMode);
            typeAndMode.Type.ShouldEqual(FileType.Directory);
            typeAndMode.Mode.ShouldEqual((ushort)0);
        }

        [TestCase(true)]
        [TestCase(false)]
        [Category(CategoryConstants.ExceptionExpected)]
        public void ValidateIndexRejectsSparseDirectoryEntryWithRecoverySteps(bool supportsFileMode)
        {
            SetSupportsFileMode(supportsFileMode);
            byte[] index = GitIndexV4Writer.Create(
                new Entry("README.md"),
                new Entry("src/", SparseDirectoryMode));

            SparseIndexNotSupportedException error = Assert.Throws<SparseIndexNotSupportedException>(() => ValidateIndex(index));

            error.Message.ShouldContain(
                "sparse-directory entry 'src/'",
                "1. If the enlistment is mounted, run 'gvfs unmount'.",
                "2. In the 'src' folder of the enlistment, run '" + DisableSparseIndexCommand + "'.",
                "3. In the 'src' folder of the enlistment, run '" + ExpandIndexCommand + "'.",
                "4. Delete '" + Path.Combine(GVFSPlatform.Instance.Constants.DotGVFSRoot, ProjectionIndexBackupName) + "' in the enlistment root.",
                "5. Run 'gvfs mount'.");
        }

        [TestCase]
        [Category(CategoryConstants.ExceptionExpected)]
        public void ValidateIndexEscapesControlCharactersInEntryPath()
        {
            byte[] index = GitIndexV4Writer.Create(new Entry("src\n2. Run 'evil'.\u001b[2K\u202e/", SparseDirectoryMode));

            SparseIndexNotSupportedException error = Assert.Throws<SparseIndexNotSupportedException>(() => ValidateIndex(index));

            error.Message.ShouldContain(@"sparse-directory entry 'src\u000A2. Run 'evil'.\u001B[2K\u202E/'");
            error.Message.ShouldNotContain(ignoreCase: false, "\n2. Run 'evil'", "\u001b", "\u202e");
        }

        [TestCase]
        [Category(CategoryConstants.ExceptionExpected)]
        public void ValidateIndexRejectsDirectoryModeEntryWithoutTrailingSlash()
        {
            SetSupportsFileMode(true);
            byte[] index = GitIndexV4Writer.Create(new Entry("src", SparseDirectoryMode));

            SparseIndexNotSupportedException error = Assert.Throws<SparseIndexNotSupportedException>(() => ValidateIndex(index));

            error.Message.ShouldContain("sparse-directory entry 'src'");
        }

        [TestCase]
        [Category(CategoryConstants.ExceptionExpected)]
        public void ValidateIndexRejectsDirectoryModeWithPermissionBits()
        {
            SetSupportsFileMode(true);
            byte[] index = GitIndexV4Writer.Create(new Entry("src/", (ushort)(SparseDirectoryMode | 0x1A4)));

            InvalidDataException error = Assert.Throws<InvalidDataException>(() => ValidateIndex(index));

            error.Message.ShouldContain("Invalid file mode 644 found for sparse directory");
        }

        [TestCase(true)]
        [TestCase(false)]
        [Category(CategoryConstants.ExceptionExpected)]
        public void CountIndexFoldersRejectsSparseDirectoryEntry(bool supportsFileMode)
        {
            SetSupportsFileMode(supportsFileMode);
            byte[] index = GitIndexV4Writer.Create(
                new Entry("README.md"),
                new Entry("src/", SparseDirectoryMode));

            using (MemoryStream stream = new MemoryStream(index))
            {
                Assert.Throws<SparseIndexNotSupportedException>(() => GitIndexProjection.CountIndexFolders(new MockTracer(), stream));
            }
        }

        [TestCase(true)]
        [TestCase(false)]
        public void FullIndexProjectsAsBefore(bool supportsFileMode)
        {
            SetSupportsFileMode(supportsFileMode);
            string indexPath = Path.Combine(this.testRoot, "index");
            File.WriteAllBytes(
                indexPath,
                GitIndexV4Writer.Create(
                    new Entry("a/b/c.txt", RegularFileMode, ShaOf(1)),
                    new Entry("a/b/run.sh", GitIndexV4Writer.ExecutableFileMode, ShaOf(2)),
                    new Entry("a/d.txt", RegularFileMode, ShaOf(3)),
                    new Entry("a/modified.txt", RegularFileMode, ShaOf(4)) { SkipWorktree = false },
                    new Entry("z.txt", RegularFileMode, ShaOf(5))));

            GitIndexProjection projection = this.CreateProjection(new Mock<IPlaceholderCollection>(MockBehavior.Strict).Object);
            try
            {
                projection.BuildProjectionFromPath(new MockTracer(), indexPath);

                ProjectedItems(projection, string.Empty).ShouldMatchInOrder("a/", "z.txt=" + ShaOf(5));
                ProjectedItems(projection, "a").ShouldMatchInOrder("b/", "d.txt=" + ShaOf(3));
                ProjectedItems(projection, Path.Combine("a", "b")).ShouldMatchInOrder("c.txt=" + ShaOf(1), "run.sh=" + ShaOf(2));

                if (supportsFileMode)
                {
                    projection.GetFileTypeAndMode("a/b/run.sh", out FileType fileType, out ushort fileMode);
                    fileType.ShouldEqual(FileType.Regular);
                    fileMode.ShouldEqual(FileMode755);
                }
            }
            finally
            {
                projection.Dispose();
            }
        }

        [TestCase(true)]
        [TestCase(false)]
        [Category(CategoryConstants.ExceptionExpected)]
        public void BuildProjectionRejectsSparseDirectoryEntryWithEnlistmentPaths(bool supportsFileMode)
        {
            SetSupportsFileMode(supportsFileMode);
            string indexPath = Path.Combine(this.testRoot, "index");
            File.WriteAllBytes(
                indexPath,
                GitIndexV4Writer.Create(
                    new Entry("README.md"),
                    new Entry("src/", SparseDirectoryMode)));

            GitIndexProjection projection = this.CreateProjection(new Mock<IPlaceholderCollection>(MockBehavior.Strict).Object);
            try
            {
                SparseIndexNotSupportedException error = Assert.Throws<SparseIndexNotSupportedException>(
                    () => projection.BuildProjectionFromPath(new MockTracer(), indexPath));

                MockGVFSEnlistment enlistment = this.CreateEnlistment();
                error.Message.ShouldContain(
                    "sparse-directory entry 'src/'",
                    "2. In '" + enlistment.WorkingDirectoryRoot + "', run '" + DisableSparseIndexCommand + "'.",
                    "3. In '" + enlistment.WorkingDirectoryRoot + "', run '" + ExpandIndexCommand + "'.",
                    "4. Delete '" + Path.Combine(enlistment.DotGVFSRoot, ProjectionIndexBackupName) + "'.");
            }
            finally
            {
                projection.Dispose();
            }
        }

        [TestCase]
        [Category(CategoryConstants.ExceptionExpected)]
        public void InitializeRejectsSparseProjectionBackupAndInvalidatesProjection()
        {
            // The persisted projection backup (GVFS_projection) can hold a sparse index even
            // when .git\index is full. Initialize parses the backup when the projection is valid.
            SetSupportsFileMode(false);
            MockGVFSEnlistment enlistment = this.CreateEnlistmentWithMetadata();
            byte[] fullIndex = GitIndexV4Writer.Create(new Entry("README.md"), new Entry("src/file.txt"));
            string backupPath = Path.Combine(enlistment.DotGVFSRoot, ProjectionIndexBackupName);
            File.WriteAllBytes(enlistment.GitIndexPath, fullIndex);
            File.WriteAllBytes(backupPath, GitIndexV4Writer.Create(new Entry("README.md"), new Entry("src/", SparseDirectoryMode)));

            GitIndexProjection projection = this.CreateProjection(new Mock<IPlaceholderCollection>(MockBehavior.Strict).Object);
            try
            {
                SparseIndexNotSupportedException exception = Assert.Throws<SparseIndexNotSupportedException>(
                    () => projection.Initialize(backgroundFileSystemTaskRunner: null));
                exception.Message.ShouldContain("sparse-directory entry 'src/'", "4. Delete '" + backupPath + "'.");

                // The next mount must rebuild the projection from .git\index, not from the backup.
                RepoMetadata.Instance.GetProjectionInvalid().ShouldBeTrue();
                File.ReadAllBytes(enlistment.GitIndexPath).SequenceEqual(fullIndex).ShouldBeTrue();
            }
            finally
            {
                projection.Dispose();
            }
        }

        [TestCase(true)]
        [TestCase(false)]
        [Category(CategoryConstants.ExceptionExpected)]
        public void PlaceholderScanRejectsSparseDirectoryEntryBeforePlaceholderChanges(bool supportsFileMode)
        {
            SetSupportsFileMode(supportsFileMode);

            // A strict mock fails the test if the parser removes any placeholder.
            Mock<IPlaceholderCollection> placeholders = new Mock<IPlaceholderCollection>(MockBehavior.Strict);
            placeholders
                .Setup(x => x.GetAllFilePaths())
                .Returns(new HashSet<string>(StringComparer.OrdinalIgnoreCase) { Path.Combine("src", "file.txt") });

            byte[] index = GitIndexV4Writer.Create(new Entry("README.md"), new Entry("src/", SparseDirectoryMode));

            GitIndexProjection projection = this.CreateProjection(placeholders.Object);
            try
            {
                GitIndexParser parser = new GitIndexParser(projection);
                using (MemoryStream stream = new MemoryStream(index))
                {
                    SparseIndexNotSupportedException error = Assert.Throws<SparseIndexNotSupportedException>(
                        () => parser.AddMissingModifiedFilesAndRemoveThemFromPlaceholderList(new MockTracer(), stream));
                    error.Message.ShouldContain("sparse-directory entry 'src/'");
                }

                placeholders.VerifyAll();
            }
            finally
            {
                projection.Dispose();
            }
        }

        [TestCase]
        public void AddMissingModifiedFilesFailsAndInvalidatesProjectionForSparseIndex()
        {
            // A mounted projection, built from a full index. Git then writes a sparse index.
            SetSupportsFileMode(false);
            MockGVFSEnlistment enlistment = this.CreateEnlistmentWithMetadata();
            File.WriteAllBytes(enlistment.GitIndexPath, GitIndexV4Writer.Create(new Entry("README.md"), new Entry("src/file.txt")));

            Mock<IPlaceholderCollection> placeholders = new Mock<IPlaceholderCollection>(MockBehavior.Strict);
            placeholders
                .Setup(x => x.GetAllFilePaths())
                .Returns(new HashSet<string>(StringComparer.OrdinalIgnoreCase) { Path.Combine("src", "file.txt") });

            GitIndexProjection projection = this.CreateProjection(placeholders.Object);
            try
            {
                projection.Initialize(backgroundFileSystemTaskRunner: null);
                RepoMetadata.Instance.GetProjectionInvalid().ShouldBeFalse();

                File.WriteAllBytes(enlistment.GitIndexPath, GitIndexV4Writer.Create(new Entry("README.md"), new Entry("src/", SparseDirectoryMode)));
                projection.InvalidateModifiedFiles();
                projection.OpenIndexForRead().ShouldEqual(FileSystemTaskResult.Success);
                try
                {
                    projection.AddMissingModifiedFiles().ShouldEqual(FileSystemTaskResult.FatalError);
                }
                finally
                {
                    projection.CloseIndex();
                }

                // The next mount must parse .git\index, so that it reports the recovery steps.
                RepoMetadata.Instance.GetProjectionInvalid().ShouldBeTrue();
                placeholders.VerifyAll();
            }
            finally
            {
                projection.Shutdown();
                projection.Dispose();
            }
        }

        [TestCase]
        public void RepairDoesNotReplaceSparseIndex()
        {
            // Repair replaces an index that it cannot parse with an index built from HEAD. A
            // sparse index is valid and can hold staged changes, so repair must not replace it.
            MockGVFSEnlistment enlistment = this.CreateEnlistment();
            Directory.CreateDirectory(Path.GetDirectoryName(enlistment.GitIndexPath));
            File.WriteAllBytes(enlistment.GitIndexPath, GitIndexV4Writer.Create(new Entry("README.md"), new Entry("src/", SparseDirectoryMode)));

            GitIndexRepairJob repairJob = new GitIndexRepairJob(new MockTracer(), TextWriter.Null, enlistment);
            List<string> messages = new List<string>();

            repairJob.HasIssue(messages).ShouldEqual(RepairJob.IssueType.CantFix);
            string.Join(Environment.NewLine, messages).ShouldContain("sparse-directory entry 'src/'", ExpandIndexCommand);
        }

        private static void SetSupportsFileMode(bool value)
        {
            ((MockPlatformFileSystem)GVFSPlatform.Instance.FileSystem).SupportsFileMode = value;
        }

        private static void ValidateIndex(byte[] index)
        {
            using (MemoryStream stream = new MemoryStream(index))
            {
                GitIndexParser.ValidateIndex(new MockTracer(), stream);
            }
        }

        private static List<string> ProjectedItems(GitIndexProjection projection, string folderPath)
        {
            return projection
                .GetProjectedItems(CancellationToken.None, blobSizesConnection: null, folderPath: folderPath)
                .Select(item => item.IsFolder ? item.Name + "/" : item.Name + "=" + item.Sha.ToString().ToLowerInvariant())
                .ToList();
        }

        private static string ShaOf(byte value)
        {
            return string.Concat(Enumerable.Repeat(value.ToString("x2"), 20));
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

        private GitIndexProjection CreateProjection(IPlaceholderCollection placeholders)
        {
            MockTracer tracer = new MockTracer();
            MockGVFSEnlistment enlistment = this.CreateEnlistment();
            GVFSContext context = new GVFSContext(tracer, new PhysicalFileSystem(), new MockGitRepo(tracer, enlistment, fileSystem: null), enlistment);
            return new GitIndexProjection(
                context,
                gitObjects: null,
                blobSizes: null,
                repoMetadata: RepoMetadata.Instance,
                fileSystemVirtualizer: null,
                placeholderDatabase: placeholders,
                sparseCollection: null,
                modifiedPaths: null);
        }

        private class OverwritingFileSystem : PhysicalFileSystem
        {
            // RepoMetadata flushes through MoveAndOverwriteFile, which MockPlatform does not support.
            public override void MoveAndOverwriteFile(string sourceFileName, string destinationFilename)
            {
                File.Move(sourceFileName, destinationFilename, overwrite: true);
            }
        }
    }
}
