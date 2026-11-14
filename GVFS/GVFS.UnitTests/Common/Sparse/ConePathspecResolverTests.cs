using System.Collections.Generic;
using GVFS.Common.Sparse;
using GVFS.Tests.Should;
using NUnit.Framework;

namespace GVFS.UnitTests.Common.Sparse
{
    [TestFixture]
    public class ConePathspecResolverTests
    {
        private const string Root = "C:/repo/src";

        [TestCase]
        public void RelativeFileResolvesAgainstBaseDirectory()
        {
            Resolve("C:/repo/src/A", "b.txt").ShouldMatchInOrder("A/b.txt");
        }

        [TestCase]
        public void RelativeNestedFileKeepsFullPath()
        {
            Resolve("C:/repo/src/A", "B/c.txt").ShouldMatchInOrder("A/B/c.txt");
        }

        [TestCase]
        public void TrailingSlashMarksRecursiveFolder()
        {
            Resolve("C:/repo/src/A", "sub/").ShouldMatchInOrder("A/sub/");
        }

        [TestCase]
        public void FileAtRepoRootResolvesToBareName()
        {
            Resolve(Root, "root.txt").ShouldMatchInOrder("root.txt");
        }

        [TestCase]
        public void DotSegmentsAreCollapsed()
        {
            Resolve("C:/repo/src/A", "./B/./c.txt").ShouldMatchInOrder("A/B/c.txt");
        }

        [TestCase]
        public void DotDotClimbsToSibling()
        {
            Resolve("C:/repo/src/A", "../D/d.txt").ShouldMatchInOrder("D/d.txt");
        }

        [TestCase]
        public void DotDotThatEscapesRootIsSkipped()
        {
            // From C:/repo/src/A, ../.. is C:/repo, which is above the worktree root.
            Resolve("C:/repo/src/A", "../../outside.txt").ShouldBeEmpty();
        }

        [TestCase]
        public void BackslashesAreNormalized()
        {
            Resolve("C:\\repo\\src\\A", "B\\c.txt").ShouldMatchInOrder("A/B/c.txt");
        }

        [TestCase]
        public void AbsolutePathUnderRootIsMadeRelative()
        {
            Resolve("C:/repo/src", "C:/repo/src/X/y.txt").ShouldMatchInOrder("X/y.txt");
        }

        [TestCase]
        public void AbsolutePathOutsideRootIsSkipped()
        {
            Resolve("C:/repo/src", "C:/other/z.txt").ShouldBeEmpty();
        }

        [TestCase]
        public void RootPrefixMatchIsCaseInsensitiveButSuffixCasingIsKept()
        {
            Resolve("C:/repo/src", "C:/REPO/SRC/Keep/Case.txt").ShouldMatchInOrder("Keep/Case.txt");
        }

        [TestCase]
        public void PathspecEqualToRootIsSkipped()
        {
            Resolve("C:/repo/src", "C:/repo/src").ShouldBeEmpty();
        }

        [TestCase]
        public void MagicPathspecIsSkipped()
        {
            Resolve("C:/repo/src", ":(glob)**/*.txt").ShouldBeEmpty();
            Resolve("C:/repo/src", ":/top.txt").ShouldBeEmpty();
        }

        [TestCase]
        public void DuplicateResultsAreDeduplicated()
        {
            Resolve("C:/repo/src/A", "b.txt", "./b.txt").ShouldMatchInOrder("A/b.txt");
        }

        [TestCase]
        public void EmptyAndNullPathspecsAreSkipped()
        {
            Resolve("C:/repo/src", string.Empty, null, "keep.txt").ShouldMatchInOrder("keep.txt");
        }

        [TestCase]
        public void NullPathspecEnumerableYieldsEmpty()
        {
            ConePathspecResolver.ResolveToGitPaths(Root, Root, null).ShouldBeEmpty();
        }

        [TestCase]
        public void FileAndFolderShapesAreDistinctEntries()
        {
            Resolve("C:/repo/src", "A", "A/").ShouldMatchInOrder("A", "A/");
        }

        [TestCase]
        public void WildcardInFileNameIsKept()
        {
            Resolve("C:/repo/src/A", "*.cs", "B/f?le.txt").ShouldMatchInOrder("A/*.cs", "A/B/f?le.txt");
        }

        [TestCase]
        public void WildcardInDirectorySegmentIsSkipped()
        {
            Resolve("C:/repo/src", "*/a.txt", "A/[bc]/d.txt", "A/B*/", "**/x.cs", "keep/a.txt")
                .ShouldMatchInOrder("keep/a.txt");
        }

        [TestCase("C:/repo/src", null, "C:/repo/src")]
        [TestCase("C:/repo/src", "", "C:/repo/src")]
        [TestCase("C:/repo/src", "A/B/", "C:/repo/src/A/B")]
        [TestCase("C:\\REPO\\src\\", "A\\B\\", "C:/repo/src/A/B")]
        [TestCase("C:/repo/src/A", "B/", "C:/repo/src/A")]
        public void PathspecBaseDirectoryAppliesPrefixOnlyAtWorktreeRoot(string hookCurrentDirectory, string gitPrefix, string expected)
        {
            string actual = ConePathspecResolver.GetPathspecBaseDirectory(hookCurrentDirectory, Root, gitPrefix);
            actual.Replace('\\', '/').TrimEnd('/').ShouldEqual(expected);
        }

        [TestCase]
        public void PrefixedBaseDirectoryResolvesRelativePathspecs()
        {
            string baseDirectory = ConePathspecResolver.GetPathspecBaseDirectory(Root, Root, "A/B/");
            ConePathspecResolver.ResolveToGitPaths(baseDirectory, Root, new[] { "c.txt", "../d/" })
                .ShouldMatchInOrder("A/B/c.txt", "A/d/");
        }

        [TestCase("../outside/")]
        [TestCase("A/../../outside/")]
        [TestCase("C:/Windows/")]
        [TestCase("D:relative/")]
        [TestCase("/rooted/")]
        [TestCase("A//B/")]
        public void PrefixThatCanLeaveTheWorktreeIsIgnored(string gitPrefix)
        {
            ConePathspecResolver.GetPathspecBaseDirectory(Root, Root, gitPrefix).ShouldEqual(Root);
        }

        [TestCase("D:dir/file.txt")]
        [TestCase("A/stream.txt:alternate")]
        [TestCase("A/line\nbreak.txt")]
        [TestCase("A/frame\u0003end.txt")]
        public void PathThatIsNotAValidGitPathIsSkipped(string pathspec)
        {
            Resolve("C:/repo/src", pathspec, "keep.txt").ShouldMatchInOrder("keep.txt");
        }

        private static IReadOnlyList<string> Resolve(string baseDirectory, params string[] pathspecs)
        {
            return ConePathspecResolver.ResolveToGitPaths(baseDirectory, Root, pathspecs);
        }
    }
}
