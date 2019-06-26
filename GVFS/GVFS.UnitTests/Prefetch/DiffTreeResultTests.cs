using GVFS.Common.Git;
using GVFS.Tests.Should;
using GVFS.UnitTests.Category;
using NUnit.Framework;
using System;
using System.IO;

namespace GVFS.UnitTests.Prefetch
{
    [TestFixture]
    public class DiffTreeResultTests
    {
        private const string TestSha1 = "0ee459db639f34c3064f56845acbc7df0d528e81";
        private const string Test2Sha1 = "2052fbe2ce5b081db3e3b9ffdebe9b0258d14cce";
        private const string EmptySha1 = "0000000000000000000000000000000000000000";

        private const string TestTreePath1 = "Test/GVFS";
        private const string TestTreePath2 = "Test/directory with blob and spaces";
        private const string TestBlobPath1 = "Test/file with spaces.txt";
        private const string TestBlobPath2 = "Test/file with tree and spaces.txt";

        private static readonly string MissingColonLineFromDiffTree = $"040000 040000 {TestSha1} {Test2Sha1} M\t{TestTreePath1}";
        private static readonly string TooManyFieldsLineFromDiffTree = $":040000 040000 {TestSha1} {Test2Sha1} M BadData\t{TestTreePath1}";
        private static readonly string NotEnoughFieldsLineFromDiffTree = $":040000 040000 {TestSha1} {Test2Sha1}\t{TestTreePath1}";
        private static readonly string TwoPathLineFromDiffTree = $":040000 040000 {TestSha1} {Test2Sha1} M\t{TestTreePath1}\t{TestBlobPath1}";
        private static readonly string ModifyTreeLineFromDiffTree = $":040000 040000 {TestSha1} {Test2Sha1} M\t{TestTreePath1}";
        private static readonly string DeleteTreeLineFromDiffTree = $":040000 000000 {TestSha1} {EmptySha1} D\t{TestTreePath1}";
        private static readonly string AddTreeLineFromDiffTree = $":000000 040000 {EmptySha1} {Test2Sha1} A\t{TestTreePath1}";
        private static readonly string ModifyBlobLineFromDiffTree = $":100644 100644 {TestSha1} {Test2Sha1} M\t{TestBlobPath1}";
        private static readonly string DeleteBlobLineFromDiffTree = $":100755 000000 {TestSha1} {EmptySha1} D\t{TestBlobPath1}";
        private static readonly string DeleteBlobLineFromDiffTree2 = $":100644 000000 {TestSha1} {EmptySha1} D\t{TestBlobPath1}";
        private static readonly string AddBlobLineFromDiffTree = $":000000 100644 {EmptySha1} {Test2Sha1} A\t{TestBlobPath1}";

        private static readonly string BlobLineFromLsTree = $"100644 blob {TestSha1}\t{TestTreePath1}";
        private static readonly string BlobLineWithTreePathFromLsTree = $"100644 blob {TestSha1}\t{TestBlobPath2}";
        private static readonly string TreeLineFromLsTree = $"040000 tree {TestSha1}\t{TestTreePath1}";
        private static readonly string TreeLineWithBlobPathFromLsTree = $"040000 tree {TestSha1}\t{TestTreePath2}";
        private static readonly string InvalidLineFromLsTree = $"040000 bad {TestSha1}\t{TestTreePath1}";
        private static readonly string SymLinkLineFromLsTree = $"120000 blob {TestSha1}\t{TestTreePath1}";

        [TestCase]
        [Category(CategoryConstants.ExceptionExpected)]
        public void ParseFromDiffTreeLine_NullLine()
        {
            Assert.Throws<ArgumentException>(() => DiffTreeResult.ParseFromDiffTreeLine(null));
        }

        [TestCase]
        [Category(CategoryConstants.ExceptionExpected)]
        public void ParseFromDiffTreeLine_EmptyLine()
        {
            Assert.Throws<ArgumentException>(() => DiffTreeResult.ParseFromDiffTreeLine(string.Empty));
        }

        [TestCase]
        [Category(CategoryConstants.ExceptionExpected)]
        public void ParseFromDiffTreeLine_EmptyRepo()
        {
            DiffTreeResult expected = new DiffTreeResult()
            {
                Operation = DiffTreeResult.Operations.Modify,
                SourceIsDirectory = true,
                TargetIsDirectory = true,
                TargetPath = TestTreePath1.Replace('/', Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar,
                SourceSha = TestSha1,
                TargetSha = Test2Sha1
            };

            DiffTreeResult result = DiffTreeResult.ParseFromDiffTreeLine(ModifyTreeLineFromDiffTree);
            this.ValidateDiffTreeResult(expected, result);
        }

        [TestCase]
        [Category(CategoryConstants.ExceptionExpected)]
        public void ParseFromLsTreeLine_NullLine()
        {
            Assert.Throws<ArgumentException>(() => DiffTreeResult.ParseFromLsTreeLine(null));
        }

        [TestCase]
        [Category(CategoryConstants.ExceptionExpected)]
        public void ParseFromLsTreeLine_EmptyLine()
        {
            Assert.Throws<ArgumentException>(() => DiffTreeResult.ParseFromLsTreeLine(string.Empty));
        }

        [TestCase]
        public void ParseFromLsTreeLine_EmptyRepoRoot()
        {
            DiffTreeResult expected = new DiffTreeResult()
            {
                Operation = DiffTreeResult.Operations.Add,
                SourceIsDirectory = false,
                TargetIsDirectory = false,
                TargetPath = TestTreePath1.Replace('/', Path.DirectorySeparatorChar),
                SourceSha = null,
                TargetSha = TestSha1
            };

            DiffTreeResult result = DiffTreeResult.ParseFromLsTreeLine(BlobLineFromLsTree);
            this.ValidateDiffTreeResult(expected, result);
        }

        [TestCase]
        public void ParseFromLsTreeLine_BlobLine()
        {
            DiffTreeResult expected = new DiffTreeResult()
            {
                Operation = DiffTreeResult.Operations.Add,
                SourceIsDirectory = false,
                TargetIsDirectory = false,
                TargetPath = TestTreePath1.Replace('/', Path.DirectorySeparatorChar),
                SourceSha = null,
                TargetSha = TestSha1
            };

            DiffTreeResult result = DiffTreeResult.ParseFromLsTreeLine(BlobLineFromLsTree);
            this.ValidateDiffTreeResult(expected, result);
        }

        [TestCase]
        public void ParseFromLsTreeLine_TreeLine()
        {
            DiffTreeResult expected = new DiffTreeResult()
            {
                Operation = DiffTreeResult.Operations.Add,
                SourceIsDirectory = false,
                TargetIsDirectory = true,
                TargetPath = CreateTreePath(TestTreePath1),
                SourceSha = null,
                TargetSha = null
            };

            DiffTreeResult result = DiffTreeResult.ParseFromLsTreeLine(TreeLineFromLsTree);
            this.ValidateDiffTreeResult(expected, result);
        }

        [TestCase]
        public void ParseFromLsTreeLine_InvalidLine()
        {
            DiffTreeResult.ParseFromLsTreeLine(InvalidLineFromLsTree).ShouldBeNull();
        }

        [TestCase]
        [Category(CategoryConstants.ExceptionExpected)]
        public void ParseFromDiffTreeLine_NoColonLine()
        {
            Assert.Throws<ArgumentException>(() => DiffTreeResult.ParseFromDiffTreeLine(MissingColonLineFromDiffTree));
        }

        [TestCase]
        [Category(CategoryConstants.ExceptionExpected)]
        public void ParseFromDiffTreeLine_TooManyFieldsLine()
        {
            Assert.Throws<ArgumentException>(() => DiffTreeResult.ParseFromDiffTreeLine(TooManyFieldsLineFromDiffTree));
        }

        [TestCase]
        [Category(CategoryConstants.ExceptionExpected)]
        public void ParseFromDiffTreeLine_NotEnoughFieldsLine()
        {
            Assert.Throws<ArgumentException>(() => DiffTreeResult.ParseFromDiffTreeLine(NotEnoughFieldsLineFromDiffTree));
        }

        [TestCase]

        [Category(CategoryConstants.ExceptionExpected)]
        public void ParseFromDiffTreeLine_TwoPathLine()
        {
            Assert.Throws<ArgumentException>(() => DiffTreeResult.ParseFromDiffTreeLine(TwoPathLineFromDiffTree));
        }

        [TestCase]
        public void ParseFromDiffTreeLine_ModifyTreeLine()
        {
            DiffTreeResult expected = new DiffTreeResult()
            {
                Operation = DiffTreeResult.Operations.Modify,
                SourceIsDirectory = true,
                TargetIsDirectory = true,
                TargetPath = CreateTreePath(TestTreePath1),
                SourceSha = TestSha1,
                TargetSha = Test2Sha1
            };

            DiffTreeResult result = DiffTreeResult.ParseFromDiffTreeLine(ModifyTreeLineFromDiffTree);
            this.ValidateDiffTreeResult(expected, result);
        }

        [TestCase]
        public void ParseFromDiffTreeLine_DeleteTreeLine()
        {
            DiffTreeResult expected = new DiffTreeResult()
            {
                Operation = DiffTreeResult.Operations.Delete,
                SourceIsDirectory = true,
                TargetIsDirectory = false,
                TargetPath = CreateTreePath(TestTreePath1),
                SourceSha = TestSha1,
                TargetSha = EmptySha1
            };

            DiffTreeResult result = DiffTreeResult.ParseFromDiffTreeLine(DeleteTreeLineFromDiffTree);
            this.ValidateDiffTreeResult(expected, result);
        }

        [TestCase]
        public void ParseFromDiffTreeLine_AddTreeLine()
        {
            DiffTreeResult expected = new DiffTreeResult()
            {
                Operation = DiffTreeResult.Operations.Add,
                SourceIsDirectory = false,
                TargetIsDirectory = true,
                TargetPath = CreateTreePath(TestTreePath1),
                SourceSha = EmptySha1,
                TargetSha = Test2Sha1
            };

            DiffTreeResult result = DiffTreeResult.ParseFromDiffTreeLine(AddTreeLineFromDiffTree);
            this.ValidateDiffTreeResult(expected, result);
        }

        [TestCase]
        public void ParseFromDiffTreeLine_AddBlobLine()
        {
            DiffTreeResult expected = new DiffTreeResult()
            {
                Operation = DiffTreeResult.Operations.Add,
                SourceIsDirectory = false,
                TargetIsDirectory = false,
                TargetPath = TestBlobPath1.Replace('/', Path.DirectorySeparatorChar),
                SourceSha = EmptySha1,
                TargetSha = Test2Sha1
            };

            DiffTreeResult result = DiffTreeResult.ParseFromDiffTreeLine(AddBlobLineFromDiffTree);
            this.ValidateDiffTreeResult(expected, result);
        }

        [TestCase]
        public void ParseFromDiffTreeLine_DeleteBlobLine()
        {
            DiffTreeResult expected = new DiffTreeResult()
            {
                Operation = DiffTreeResult.Operations.Delete,
                SourceIsDirectory = false,
                TargetIsDirectory = false,
                TargetPath = TestBlobPath1.Replace('/', Path.DirectorySeparatorChar),
                SourceSha = TestSha1,
                TargetSha = EmptySha1
            };

            DiffTreeResult result = DiffTreeResult.ParseFromDiffTreeLine(DeleteBlobLineFromDiffTree);
            this.ValidateDiffTreeResult(expected, result);
        }

        [TestCase]
        public void ParseFromDiffTreeLine_DeleteBlobLine2()
        {
            DiffTreeResult expected = new DiffTreeResult()
            {
                Operation = DiffTreeResult.Operations.Delete,
                SourceIsDirectory = false,
                TargetIsDirectory = false,
                TargetPath = TestBlobPath1.Replace('/', Path.DirectorySeparatorChar),
                SourceSha = TestSha1,
                TargetSha = EmptySha1
            };

            DiffTreeResult result = DiffTreeResult.ParseFromDiffTreeLine(DeleteBlobLineFromDiffTree2);
            this.ValidateDiffTreeResult(expected, result);
        }

        [TestCase]
        public void ParseFromDiffTreeLine_ModifyBlobLine()
        {
            DiffTreeResult expected = new DiffTreeResult()
            {
                Operation = DiffTreeResult.Operations.Modify,
                SourceIsDirectory = false,
                TargetIsDirectory = false,
                TargetPath = TestBlobPath1.Replace('/', Path.DirectorySeparatorChar),
                SourceSha = TestSha1,
                TargetSha = Test2Sha1
            };

            DiffTreeResult result = DiffTreeResult.ParseFromDiffTreeLine(ModifyBlobLineFromDiffTree);
            this.ValidateDiffTreeResult(expected, result);
        }

        [TestCase]
        public void ParseFromLsTreeLine_SymLinkLine()
        {
            DiffTreeResult expected = new DiffTreeResult()
            {
                Operation = DiffTreeResult.Operations.Add,
                SourceIsDirectory = false,
                TargetIsDirectory = false,
                TargetIsSymLink = true,
                TargetPath = TestTreePath1.Replace('/', Path.DirectorySeparatorChar),
                SourceSha = null,
                TargetSha = TestSha1
            };

            DiffTreeResult result = DiffTreeResult.ParseFromLsTreeLine(SymLinkLineFromLsTree);
            this.ValidateDiffTreeResult(expected, result);
        }

        [TestCase]
        public void ParseFromDiffTreeLine_TreeLineWithBlobPath()
        {
            DiffTreeResult expected = new DiffTreeResult()
            {
                Operation = DiffTreeResult.Operations.Add,
                SourceIsDirectory = false,
                TargetIsDirectory = true,
                TargetPath = CreateTreePath(TestTreePath2),
                SourceSha = null,
                TargetSha = null
            };

            DiffTreeResult result = DiffTreeResult.ParseFromLsTreeLine(TreeLineWithBlobPathFromLsTree);
            this.ValidateDiffTreeResult(expected, result);
        }

        [TestCase]
        public void ParseFromDiffTreeLine_BlobLineWithTreePath()
        {
            DiffTreeResult expected = new DiffTreeResult()
            {
                Operation = DiffTreeResult.Operations.Add,
                SourceIsDirectory = false,
                TargetIsDirectory = false,
                TargetPath = TestBlobPath2.Replace('/', Path.DirectorySeparatorChar),
                SourceSha = null,
                TargetSha = TestSha1
            };

            DiffTreeResult result = DiffTreeResult.ParseFromLsTreeLine(BlobLineWithTreePathFromLsTree);
            this.ValidateDiffTreeResult(expected, result);
        }

        [TestCase("040000 tree 73b881d52b607b0f3e9e620d36f556d3d233a11d\tGVFS", DiffTreeResult.TreeMarker, true)]
        [TestCase("040000 tree 73b881d52b607b0f3e9e620d36f556d3d233a11d\tGVFS", DiffTreeResult.BlobMarker, false)]
        [TestCase("100644 blob 44c5f5cba4b29d31c2ad06eed51ea02af76c27c0\tReadme.md", DiffTreeResult.BlobMarker, true)]
        [TestCase("100755 blob 196142fbb753c0a3c7c6690323db7aa0a11f41ec\tScripts / BuildGVFSForMac.sh", DiffTreeResult.BlobMarker, true)]
        [TestCase("100755 blob 196142fbb753c0a3c7c6690323db7aa0a11f41ec\tScripts / BuildGVFSForMac.sh", DiffTreeResult.BlobMarker, true)]
        [TestCase("100755 blob 196142fbb753c0a3c7c6690323db7aa0a11f41ec\tScripts / tree file.txt", DiffTreeResult.TreeMarker, false)]
        [TestCase("100755 ", DiffTreeResult.TreeMarker, false)]
        public void TestGetIndexOfTypeMarker(string line, string typeMarker, bool expectedResult)
        {
            DiffTreeResult.IsLsTreeLineOfType(line, typeMarker).ShouldEqual(expectedResult);
        }

        private static string CreateTreePath(string testPath)
        {
            return testPath.Replace('/', Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        }

        private void ValidateDiffTreeResult(DiffTreeResult expected, DiffTreeResult actual)
        {
            actual.Operation.ShouldEqual(expected.Operation, $"{nameof(DiffTreeResult)}.{nameof(actual.Operation)}");
            actual.SourceIsDirectory.ShouldEqual(expected.SourceIsDirectory, $"{nameof(DiffTreeResult)}.{nameof(actual.SourceIsDirectory)}");
            actual.TargetIsDirectory.ShouldEqual(expected.TargetIsDirectory, $"{nameof(DiffTreeResult)}.{nameof(actual.TargetIsDirectory)}");
            actual.TargetPath.ShouldEqual(expected.TargetPath, $"{nameof(DiffTreeResult)}.{nameof(actual.TargetPath)}");
            actual.SourceSha.ShouldEqual(expected.SourceSha, $"{nameof(DiffTreeResult)}.{nameof(actual.SourceSha)}");
            actual.TargetSha.ShouldEqual(expected.TargetSha, $"{nameof(DiffTreeResult)}.{nameof(actual.TargetSha)}");
        }
    }
}
