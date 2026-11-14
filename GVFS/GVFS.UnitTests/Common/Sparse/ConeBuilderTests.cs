using System;
using System.Linq;
using GVFS.Common.Sparse;
using GVFS.Tests.Should;
using GVFS.UnitTests.Category;
using NUnit.Framework;

namespace GVFS.UnitTests.Common.Sparse
{
    [TestFixture]
    public class ConeBuilderTests
    {
        // The worked examples use this tree:
        //   /
        //     root.txt
        //     .gitattributes
        //     A/
        //       a.txt
        //       .gitattributes
        //       B/
        //         b.txt
        //         C/
        //           c.txt
        //       D/
        //         d.txt
        private const string Header = "/*\n!/*/\n";

        [TestCase]
        public void EmptyModifiedSetProducesHeaderOnly()
        {
            SerializeCone().ShouldEqual(Header);
        }

        [TestCase]
        [Category(CategoryConstants.ExceptionExpected)]
        public void NullModifiedPathsThrows()
        {
            Assert.Throws<ArgumentNullException>(() => ConeBuilder.BuildFromModifiedPaths(null, ignoreCase: false));
        }

        [TestCase]
        public void OneRootFileProducesHeaderOnly()
        {
            SerializeCone("root.txt").ShouldEqual(Header);
        }

        [TestCase]
        public void RootGitAttributesProducesNoExtraPattern()
        {
            SerializeCone(".gitattributes").ShouldEqual(Header);
        }

        [TestCase]
        public void OneDeepFileProducesParentOnlyChain()
        {
            SerializeCone("A/B/b.txt").ShouldEqual(
                Header +
                "/A/\n!/A/*/\n" +
                "/A/B/\n!/A/B/*/\n");
        }

        [TestCase]
        public void DeeplyNestedFileProducesFullParentOnlyChain()
        {
            SerializeCone("A/B/C/c.txt").ShouldEqual(
                Header +
                "/A/\n!/A/*/\n" +
                "/A/B/\n!/A/B/*/\n" +
                "/A/B/C/\n!/A/B/C/*/\n");
        }

        [TestCase]
        public void TwoFilesInAdjacentDirectories()
        {
            SerializeCone("A/B/b.txt", "A/D/d.txt").ShouldEqual(
                Header +
                "/A/\n!/A/*/\n" +
                "/A/B/\n!/A/B/*/\n" +
                "/A/D/\n!/A/D/*/\n");
        }

        [TestCase]
        public void OneDeepFileAndOneDeeperFile()
        {
            SerializeCone("A/B/b.txt", "A/B/C/c.txt").ShouldEqual(
                Header +
                "/A/\n!/A/*/\n" +
                "/A/B/\n!/A/B/*/\n" +
                "/A/B/C/\n!/A/B/C/*/\n");
        }

        [TestCase]
        public void ModifiedFolderMapsToRecursive()
        {
            SerializeCone("A/B/").ShouldEqual(
                Header +
                "/A/\n!/A/*/\n" +
                "/A/B/\n");
        }

        [TestCase]
        public void FolderPlusSiblingFile()
        {
            SerializeCone("A/B/", "A/D/d.txt").ShouldEqual(
                Header +
                "/A/\n!/A/*/\n" +
                "/A/D/\n!/A/D/*/\n" +
                "/A/B/\n");
        }

        [TestCase]
        public void TwoFilesInSameDirectoryProduceOneParentPair()
        {
            ConePatternSet cone = Build("A/B/b.txt", "A/B/b2.txt");

            cone.RecursiveDirectories.ShouldBeEmpty();
            cone.ParentOnlyDirectories.ShouldMatchInOrder("A", "A/B");
        }

        [TestCase]
        public void SingleModifiedFileNeverProducesRecursivePattern()
        {
            ConePatternSet cone = Build("A/B/b.txt");

            // An edited file must not pull its subtree into the index, so it must not
            // create a recursive pattern.
            cone.RecursiveDirectories.ShouldBeEmpty();
            cone.ParentOnlyDirectories.ShouldMatchInOrder("A", "A/B");
        }

        [TestCase]
        public void FileInsideModifiedFolderNeedsNoParentPair()
        {
            // A/B/ (folder) is recursive. The file A/B/b.txt below it needs no parent-only
            // pattern, because the recursive pattern already covers it.
            ConePatternSet cone = Build("A/B/b.txt", "A/B/");

            cone.RecursiveDirectories.ShouldMatchInOrder("A/B");
            cone.ParentOnlyDirectories.ShouldMatchInOrder("A");

            SerializeCone("A/B/b.txt", "A/B/").ShouldEqual(
                Header +
                "/A/\n!/A/*/\n" +
                "/A/B/\n");
        }

        [TestCase]
        public void RecursiveAncestorRemovesChildRecursiveEntries()
        {
            ConePatternSet cone = Build("A/", "A/B/");

            cone.RecursiveDirectories.ShouldMatchInOrder("A");
            cone.ParentOnlyDirectories.ShouldBeEmpty();

            SerializeCone("A/", "A/B/").ShouldEqual(Header + "/A/\n");
        }

        [TestCase]
        public void ParentOnlyDirectoriesBelowRecursiveVanish()
        {
            // The file's parent-only chain (A, A/B) is entirely inside the recursive A/,
            // so nothing parent-only survives.
            ConePatternSet cone = Build("A/B/x.txt", "A/");

            cone.RecursiveDirectories.ShouldMatchInOrder("A");
            cone.ParentOnlyDirectories.ShouldBeEmpty();
        }

        [TestCase]
        public void ParentOnlyAncestorOfRecursiveIsKept()
        {
            // A is an ancestor of the recursive A/B, not a descendant, so it stays
            // parent-only: a recursive directory does not include its ancestors' direct files.
            ConePatternSet cone = Build("A/B/");

            cone.RecursiveDirectories.ShouldMatchInOrder("A/B");
            cone.ParentOnlyDirectories.ShouldMatchInOrder("A");
        }

        [TestCase]
        public void SiblingRecursiveFoldersAreBothKept()
        {
            ConePatternSet cone = Build("A/B/", "A/D/");

            cone.RecursiveDirectories.ShouldMatchInOrder("A/B", "A/D");
            cone.ParentOnlyDirectories.ShouldMatchInOrder("A");
        }

        [TestCase]
        public void SimilarPrefixIsNotTreatedAsAncestor()
        {
            // "AB" starts with "A" but is not below it, so the recursive A/ must not remove it.
            ConePatternSet cone = Build("A/", "AB/x.txt");

            cone.RecursiveDirectories.ShouldMatchInOrder("A");
            cone.ParentOnlyDirectories.ShouldMatchInOrder("AB");
        }

        [TestCase]
        public void NullAndEmptyEntriesAreSkipped()
        {
            ConePatternSet cone = Build(null, string.Empty, "/", "A/B/b.txt");

            cone.RecursiveDirectories.ShouldBeEmpty();
            cone.ParentOnlyDirectories.ShouldMatchInOrder("A", "A/B");
        }

        [TestCase]
        public void PathsAreNormalizedBeforeMapping()
        {
            // Backslashes, a leading slash, and duplicate separators must all give the
            // same cone as the canonical git-form input.
            string canonical = SerializeCone("A/B/b.txt");

            SerializeCone(@"A\B\b.txt").ShouldEqual(canonical);
            SerializeCone("/A/B/b.txt").ShouldEqual(canonical);
            SerializeCone("A//B/b.txt").ShouldEqual(canonical);
            SerializeCone(@"A\B\").ShouldEqual(SerializeCone("A/B/"));
        }

        [TestCase]
        public void OutputIsSortedOrdinally()
        {
            // Ordinal sort places all uppercase letters before lowercase, as git's
            // strcmp-based writer does.
            ConePatternSet cone = Build("a/one.txt", "B/two.txt");

            cone.ParentOnlyDirectories.ShouldMatchInOrder("B", "a");
        }

        [TestCase]
        public void GlobSpecialCharactersInPathAreEscaped()
        {
            SerializeCone("A/B[1]/x.txt", "weird*dir/f.txt", "q?dir/f.txt").ShouldEqual(
                Header +
                "/A/\n!/A/*/\n" +
                "/A/B\\[1]/\n!/A/B\\[1]/*/\n" +
                "/q\\?dir/\n!/q\\?dir/*/\n" +
                "/weird\\*dir/\n!/weird\\*dir/*/\n");
        }

        [TestCase]
        public void CaseVariantDirectoriesStaySeparateWhenCaseMatters()
        {
            ConePatternSet cone = Build("src/x.cs", "Src/y.cs");

            cone.ParentOnlyDirectories.ShouldMatchInOrder("Src", "src");
        }

        [TestCase]
        public void CaseVariantDirectoriesMergeWhenIgnoringCase()
        {
            // Under core.ignoreCase, git treats "Src" and "src" as one pattern. If the file
            // names both, git reports a repeated pattern and turns off cone matching.
            ConePatternSet cone = BuildIgnoringCase("src/x.cs", "Src/y.cs", "SRC/Deep/z.cs");

            cone.RecursiveDirectories.ShouldBeEmpty();
            cone.ParentOnlyDirectories.ShouldMatchInOrder("SRC", "SRC/Deep");
        }

        [TestCase]
        public void CaseVariantMergeKeepsOrdinallySmallestSpellingForAnyInputOrder()
        {
            string[] paths = new[] { "src/x.cs", "lower/UPPER/f.txt", "Src/y.cs", "Lower/upper/Sub/" };

            ConePatternSet forward = BuildIgnoringCase(paths);
            ConePatternSet reversed = BuildIgnoringCase(paths.Reverse().ToArray());

            forward.ParentOnlyDirectories.ShouldMatchInOrder("Lower", "Lower/upper", "Src");
            forward.RecursiveDirectories.ShouldMatchInOrder("Lower/upper/Sub");
            reversed.ParentOnlyDirectories.ShouldMatchInOrder(forward.ParentOnlyDirectories);
            reversed.RecursiveDirectories.ShouldMatchInOrder(forward.RecursiveDirectories);
        }

        [TestCase]
        public void RecursiveDirectoryCoversCaseVariantParentsWhenIgnoringCase()
        {
            // "A/B" and "A/B/C" are the recursive "a/b" or below it when case is ignored.
            // The recursive directory also takes the ordinally smallest spelling, "A/B".
            ConePatternSet cone = BuildIgnoringCase("a/b/", "A/B/C/f.txt");

            cone.IgnoreCase.ShouldBeTrue();
            cone.RecursiveDirectories.ShouldMatchInOrder("A/B");
            cone.ParentOnlyDirectories.ShouldMatchInOrder("A");
        }

        [TestCase]
        public void NonAsciiCaseVariantsStaySeparateWhenIgnoringCase()
        {
            // Git folds only ASCII letters, so it treats "\u00C4" and "\u00E4" as different
            // directories. Merging them would leave the files of one directory out of the cone.
            ConePatternSet cone = BuildIgnoringCase("\u00C4/x.txt", "\u00E4/y.txt");

            cone.ParentOnlyDirectories.ShouldMatchInOrder("\u00C4", "\u00E4");
        }

        [TestCase]
        [Category(CategoryConstants.ExceptionExpected)]
        public void PathWithLineBreakInADirectoryFailsTheBuild()
        {
            // Serialized as is, this path would add a "!src" line to the file. Git would then
            // turn off cone matching and leave the modified file src/main.c out of the cone.
            Assert.Throws<ArgumentException>(
                () => ConeBuilder.BuildFromModifiedPaths(new[] { "src/main.c", "z\n!src\nb/f.txt" }, ignoreCase: false));
        }

        [TestCase]
        [Category(CategoryConstants.ExceptionExpected)]
        public void DotDotDirectoryFailsTheBuildEvenBelowARecursiveDirectory()
        {
            // "A/D/../C" looks like a descendant of the recursive "A/D". If the builder dropped
            // it as covered, "A/C/f.txt" would be outside the cone.
            Assert.Throws<ArgumentException>(
                () => ConeBuilder.BuildFromModifiedPaths(new[] { "A/D/", "A/D/../C/f.txt" }, ignoreCase: false));
        }

        private static ConePatternSet Build(params string[] modifiedPaths)
        {
            return ConeBuilder.BuildFromModifiedPaths(modifiedPaths, ignoreCase: false);
        }

        private static ConePatternSet BuildIgnoringCase(params string[] modifiedPaths)
        {
            return ConeBuilder.BuildFromModifiedPaths(modifiedPaths, ignoreCase: true);
        }

        private static string SerializeCone(params string[] modifiedPaths)
        {
            return ConeFileWriter.Serialize(Build(modifiedPaths));
        }
    }
}
