using GVFS.Common;
using NUnit.Framework;
using System.IO;

namespace GVFS.UnitTests.Common
{
    [TestFixture]
    public class WorktreeNestedPathTests
    {
        // Basic containment
        [TestCase(@"C:\repo\src\subfolder",      @"C:\repo\src",    true,  Description = "Child path is inside directory")]
        [TestCase(@"C:\repo\src",                 @"C:\repo\src",    true,  Description = "Equal path is inside directory")]
        [TestCase(@"C:\repo\src\a\b\c\d",         @"C:\repo\src",    true,  Description = "Deeply nested path is inside")]
        [TestCase(@"C:\repo\src.worktrees\wt1",   @"C:\repo\src",    false, Description = "Path with prefix overlap is outside")]
        [TestCase(@"C:\repo\src2",                @"C:\repo\src",    false, Description = "Sibling path is outside")]

        // Path traversal normalization
        [TestCase(@"C:\repo\src\..\..\..\evil",   @"C:\repo\src",    false, Description = "Traversal escaping directory is outside")]
        [TestCase(@"C:\repo\src\..",              @"C:\repo\src",    false, Description = "Traversal to parent is outside")]
        [TestCase(@"C:\repo\src\..\other",        @"C:\repo\src",    false, Description = "Traversal to sibling is outside")]
        [TestCase(@"C:\repo\src\sub\..\other",    @"C:\repo\src",    true,  Description = "Traversal staying inside directory")]
        [TestCase(@"C:\repo\src\.\subfolder",     @"C:\repo\src",    true,  Description = "Dot segment resolves to same path")]
        [TestCase(@"C:\repo\src\subfolder",       @"C:\repo\.\src",  true,  Description = "Dot segment in directory")]

        // Trailing separators
        [TestCase(@"C:\repo\src\subfolder",       @"C:\repo\src\",   true,  Description = "Trailing slash on directory")]
        [TestCase(@"C:\repo\src\subfolder\",      @"C:\repo\src",    true,  Description = "Trailing slash on path")]

        // Case sensitivity
        [TestCase(@"C:\Repo\SRC\subfolder",       @"C:\repo\src",    true,  Description = "Case-insensitive child path")]
        [TestCase(@"C:\REPO\SRC",                 @"C:\repo\src",    true,  Description = "Case-insensitive equal path")]
        [TestCase(@"c:\repo\src\subfolder",       @"C:\REPO\SRC",    true,  Description = "Lower drive letter vs upper")]
        [TestCase(@"C:\Repo\Src2",                @"C:\repo\src",    false, Description = "Case-insensitive sibling is outside")]

        // Mixed forward and backward slashes
        [TestCase(@"C:\repo\src/subfolder",       @"C:\repo\src",    true,  Description = "Forward slash in child path")]
        [TestCase("C:/repo/src/subfolder",        @"C:\repo\src",    true,  Description = "All forward slashes in path")]
        [TestCase(@"C:\repo\src\subfolder",       "C:/repo/src",     true,  Description = "All forward slashes in directory")]
        [TestCase("C:/repo/src",                  "C:/repo/src",     true,  Description = "Both paths with forward slashes")]
        [TestCase("C:/repo/src/../other",         @"C:\repo\src",    false, Description = "Forward slashes with traversal")]
        public void IsPathInsideDirectory(string path, string directory, bool expected)
        {
            Assert.AreEqual(expected, GVFSEnlistment.IsPathInsideDirectory(path, directory));
        }

        private string testDir;

        [SetUp]
        public void SetUp()
        {
            this.testDir = Path.Combine(Path.GetTempPath(), "WorktreeNestedPathTests_" + Path.GetRandomFileName());
            Directory.CreateDirectory(this.testDir);
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(this.testDir))
            {
                Directory.Delete(this.testDir, recursive: true);
            }
        }

        [TestCase]
        public void GetKnownWorktreePathsReturnsEmptyWhenNoWorktreesDir()
        {
            string[] paths = GVFSEnlistment.GetKnownWorktreePaths(this.testDir);
            Assert.AreEqual(0, paths.Length);
        }

        [TestCase]
        public void GetKnownWorktreePathsReturnsEmptyWhenWorktreesDirIsEmpty()
        {
            Directory.CreateDirectory(Path.Combine(this.testDir, "worktrees"));

            string[] paths = GVFSEnlistment.GetKnownWorktreePaths(this.testDir);
            Assert.AreEqual(0, paths.Length);
        }

        [TestCase]
        public void GetKnownWorktreePathsReadsGitdirFiles()
        {
            string wt1Dir = Path.Combine(this.testDir, "worktrees", "wt1");
            string wt2Dir = Path.Combine(this.testDir, "worktrees", "wt2");
            Directory.CreateDirectory(wt1Dir);
            Directory.CreateDirectory(wt2Dir);

            File.WriteAllText(Path.Combine(wt1Dir, "gitdir"), @"C:\worktrees\wt1\.git" + "\n");
            File.WriteAllText(Path.Combine(wt2Dir, "gitdir"), @"C:\worktrees\wt2\.git" + "\n");

            string[] paths = GVFSEnlistment.GetKnownWorktreePaths(this.testDir);
            Assert.AreEqual(2, paths.Length);
            Assert.That(paths, Has.Member(@"C:\worktrees\wt1"));
            Assert.That(paths, Has.Member(@"C:\worktrees\wt2"));
        }

        [TestCase]
        public void GetKnownWorktreePathsSkipsEntriesWithoutGitdirFile()
        {
            string wt1Dir = Path.Combine(this.testDir, "worktrees", "wt1");
            string wt2Dir = Path.Combine(this.testDir, "worktrees", "wt2");
            Directory.CreateDirectory(wt1Dir);
            Directory.CreateDirectory(wt2Dir);

            File.WriteAllText(Path.Combine(wt1Dir, "gitdir"), @"C:\worktrees\wt1\.git" + "\n");
            // wt2 has no gitdir file

            string[] paths = GVFSEnlistment.GetKnownWorktreePaths(this.testDir);
            Assert.AreEqual(1, paths.Length);
            Assert.AreEqual(@"C:\worktrees\wt1", paths[0]);
        }

        [TestCase]
        public void GetKnownWorktreePathsNormalizesForwardSlashes()
        {
            string wtDir = Path.Combine(this.testDir, "worktrees", "wt1");
            Directory.CreateDirectory(wtDir);

            File.WriteAllText(Path.Combine(wtDir, "gitdir"), "C:/worktrees/wt1/.git\n");

            string[] paths = GVFSEnlistment.GetKnownWorktreePaths(this.testDir);
            Assert.AreEqual(1, paths.Length);
            Assert.AreEqual(@"C:\worktrees\wt1", paths[0]);
        }
    }
}
