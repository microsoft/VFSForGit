using GVFS.Common.Git;
using GVFS.Common.Prefetch.Git;
using GVFS.Tests;
using GVFS.Tests.Should;
using GVFS.UnitTests.Category;
using GVFS.UnitTests.Mock.Common;
using GVFS.UnitTests.Mock.Git;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

namespace GVFS.UnitTests.Prefetch
{
    [TestFixtureSource(typeof(DataSources), nameof(DataSources.AllBools))]
    public class DiffHelperTests
    {
        public DiffHelperTests(bool symLinkSupport)
        {
            this.IncludeSymLinks = symLinkSupport;
        }

        public bool IncludeSymLinks { get; set; }

        // Make two commits. The first should look like this:
        // recursiveDelete
        // recursiveDelete/subfolder
        // recursiveDelete/subfolder/childFile.txt
        // fileToBecomeFolder
        // fileToDelete.txt
        // fileToEdit.txt
        // fileToRename.txt
        // fileToRenameEdit.txt
        // folderToBeFile
        // folderToBeFile/childFile.txt
        // folderToDelete
        // folderToDelete/childFile.txt
        // folderToEdit
        // folderToEdit/childFile.txt
        // folderToRename
        // folderToRename/childFile.txt
        // symLinkToBeCreated.txt
        //
        // The second should follow the action indicated by the file/folder name:
        // eg. recursiveDelete should run "rmdir /s/q recursiveDelete"
        // eg. folderToBeFile should be deleted and replaced with a file of the same name
        // Note that each childFile.txt should have unique contents, but is only a placeholder to force git to add a folder.
        //
        // Then to generate the diffs, run:
        // git diff-tree -r -t Head~1 Head > forward.txt
        // git diff-tree -r -t Head Head ~1 > backward.txt
        [TestCase]
        public void CanParseDiffForwards()
        {
            MockTracer tracer = new MockTracer();
            DiffHelper diffForwards = new DiffHelper(tracer, new MockGVFSEnlistment(), new List<string>(), new List<string>(), includeSymLinks: this.IncludeSymLinks);
            diffForwards.ParseDiffFile(GetDataPath("forward.txt"));

            // File added, file edited, file renamed, folder => file, edit-rename file, SymLink added (if applicable)
            // Children of: Add folder, Renamed folder, edited folder, file => folder
            diffForwards.RequiredBlobs.Count.ShouldEqual(diffForwards.ShouldIncludeSymLinks ? 10 : 9);

            diffForwards.FileAddOperations.ContainsKey("3bd509d373734a9f9685d6a73ba73324f72931e3").ShouldEqual(diffForwards.ShouldIncludeSymLinks);

            // File deleted, folder deleted, file > folder, edit-rename
            diffForwards.FileDeleteOperations.Count.ShouldEqual(4);

            // Includes children of: Recursive delete folder, deleted folder, renamed folder, and folder => file
            diffForwards.TotalFileDeletes.ShouldEqual(8);

            // Folder created, folder edited, folder deleted, folder renamed (add + delete),
            // folder => file, file => folder, recursive delete (top-level only)
            diffForwards.DirectoryOperations.Count.ShouldEqual(8);

            // Should also include the deleted folder of recursive delete
            diffForwards.TotalDirectoryOperations.ShouldEqual(9);
        }

        // Parses Diff B => A
        [TestCase]
        public void CanParseBackwardsDiff()
        {
            MockTracer tracer = new MockTracer();
            DiffHelper diffBackwards = new DiffHelper(tracer, new Mock.Common.MockGVFSEnlistment(), new List<string>(), new List<string>(), includeSymLinks: this.IncludeSymLinks);
            diffBackwards.ParseDiffFile(GetDataPath("backward.txt"));

            // File > folder, deleted file, edited file, renamed file, rename-edit file
            // Children of file > folder, renamed folder, deleted folder, recursive delete file, edited folder
            diffBackwards.RequiredBlobs.Count.ShouldEqual(10);

            // File added, folder > file, moved folder, added folder
            diffBackwards.FileDeleteOperations.Count.ShouldEqual(6);

            // Also includes, the children of: Folder added, folder renamed, file => folder
            diffBackwards.TotalFileDeletes.ShouldEqual(9);

            // Folder created, folder edited, folder deleted, folder renamed (add + delete),
            // folder => file, file => folder, recursive delete (include subfolder)
            diffBackwards.TotalDirectoryOperations.ShouldEqual(9);
        }

        // Delete a folder with two sub folders each with a single file
        // Readd it with a different casing and same contents
        [TestCase]
        [Category(CategoryConstants.CaseInsensitiveFileSystemOnly)]
        public void ParsesCaseChangesAsAdds()
        {
            MockTracer tracer = new MockTracer();
            DiffHelper diffBackwards = new DiffHelper(tracer, new Mock.Common.MockGVFSEnlistment(), new List<string>(), new List<string>(), includeSymLinks: this.IncludeSymLinks);
            diffBackwards.ParseDiffFile(GetDataPath("caseChange.txt"));

            diffBackwards.RequiredBlobs.Count.ShouldEqual(2);
            diffBackwards.FileAddOperations.Sum(list => list.Value.Count).ShouldEqual(2);

            // File deletes inside case-renamed directories are filtered out by FlushStagedQueues
            diffBackwards.FileDeleteOperations.Count.ShouldEqual(0);

            // File deletes are now staged (not suppressed) so FlushStagedQueues can filter them properly
            diffBackwards.TotalFileDeletes.ShouldEqual(2);

            diffBackwards.DirectoryOperations.ShouldNotContain(entry => entry.Operation == DiffTreeResult.Operations.Delete);

            // Only the top-level directory rename is enqueued; children are filtered because
            // the parent rename moves them automatically
            diffBackwards.DirectoryOperations.Count.ShouldEqual(1);

            // The enqueued directory operation should carry the old-cased path for the rename
            DiffTreeResult dirOp = diffBackwards.DirectoryOperations.First();
            dirOp.SourcePath.ShouldNotBeNull();
            Assert.AreEqual("GVFLT_MultiThreadTest" + Path.DirectorySeparatorChar, dirOp.SourcePath);
            Assert.AreEqual("GVFlt_MultiThreadTest" + Path.DirectorySeparatorChar, dirOp.TargetPath);

            diffBackwards.TotalDirectoryOperations.ShouldEqual(3);
        }

        // Mirror of ParsesCaseChangesAsAdds for the opposite emit order: the Add
        // lines appear in the diff before the Delete lines. This happens when the
        // new (target) casing sorts byte-wise before the old (source) casing, e.g.
        // "GVFlt_*" -> "GVFLT_*" (uppercase 'L' < lowercase 'l').
        //
        // The parser must produce the same staged state regardless of which
        // ordering the diff-tree output uses.
        [TestCase]
        [Category(CategoryConstants.CaseInsensitiveFileSystemOnly)]
        public void ParsesCaseChangesWhenAddPrecedesDelete()
        {
            MockTracer tracer = new MockTracer();
            DiffHelper diffBackwards = new DiffHelper(tracer, new Mock.Common.MockGVFSEnlistment(), new List<string>(), new List<string>(), includeSymLinks: this.IncludeSymLinks);
            diffBackwards.ParseDiffFile(GetDataPath("caseChangeAddFirst.txt"));

            diffBackwards.RequiredBlobs.Count.ShouldEqual(2);
            diffBackwards.FileAddOperations.Sum(list => list.Value.Count).ShouldEqual(2);

            // File deletes inside case-renamed directories are filtered out by FlushStagedQueues
            diffBackwards.FileDeleteOperations.Count.ShouldEqual(0);

            // File deletes are staged (not suppressed) so FlushStagedQueues can filter them properly
            diffBackwards.TotalFileDeletes.ShouldEqual(2);

            diffBackwards.DirectoryOperations.ShouldNotContain(entry => entry.Operation == DiffTreeResult.Operations.Delete);

            // Only the top-level directory rename is enqueued; children are filtered because
            // the parent rename moves them automatically
            diffBackwards.DirectoryOperations.Count.ShouldEqual(1);

            // The enqueued directory operation should carry the old-cased path for the rename
            // even though the Add was staged first and the Delete arrived second.
            DiffTreeResult dirOp = diffBackwards.DirectoryOperations.First();
            dirOp.SourcePath.ShouldNotBeNull();
            Assert.AreEqual("GVFlt_MultiThreadTest" + Path.DirectorySeparatorChar, dirOp.SourcePath);
            Assert.AreEqual("GVFLT_MultiThreadTest" + Path.DirectorySeparatorChar, dirOp.TargetPath);

            diffBackwards.TotalDirectoryOperations.ShouldEqual(3);
        }

        // File-level case rename ("foo.txt" -> "FOO.txt") with no directory case
        // changes. The fixture emits a Delete for the old casing followed by an
        // Add for the new casing; DiffHelper should stage both — the delete
        // removes the old-cased file from disk, the add writes the new-cased
        // file — so FlushStagedQueues hands both to the checkout stage.
        [TestCase]
        [Category(CategoryConstants.CaseInsensitiveFileSystemOnly)]
        public void ParsesFileOnlyCaseRename()
        {
            MockTracer tracer = new MockTracer();
            DiffHelper diff = new DiffHelper(tracer, new Mock.Common.MockGVFSEnlistment(), new List<string>(), new List<string>(), includeSymLinks: this.IncludeSymLinks);
            diff.ParseDiffFile(GetDataPath("fileCaseChange.txt"));

            diff.RequiredBlobs.Count.ShouldEqual(1);
            diff.FileAddOperations.Sum(list => list.Value.Count).ShouldEqual(1);
            diff.FileDeleteOperations.Count.ShouldEqual(1);
            diff.TotalFileDeletes.ShouldEqual(1);
            diff.TotalDirectoryOperations.ShouldEqual(0);
            diff.DirectoryOperations.Count.ShouldEqual(0);

            // The delete keeps the old casing; the add carries the new casing.
            string deletedPath = diff.FileDeleteOperations.ToArray()[0];
            Assert.AreEqual("foo.txt", deletedPath);

            string addedPath = diff.FileAddOperations.First().Value.First().Path;
            Assert.AreEqual("FOO.txt", addedPath);
        }

        // Nested case rename: both an outer directory ("Outer" -> "outer") and
        // an inner directory inside it ("Outer/Inner" -> "outer/inner") change
        // casing in the same diff. Only the outermost rename should be enqueued
        // for the checkout stage — the inner rename's parent path is in
        // directoriesReplacedByCaseRename, so FlushStagedQueues suppresses it
        // (the outer rename moves the whole subtree on disk and Windows
        // preserves child casing through the move). The file inside the inner
        // directory is similarly suppressed at the file-delete stage.
        [TestCase]
        [Category(CategoryConstants.CaseInsensitiveFileSystemOnly)]
        public void ParsesNestedCaseChanges()
        {
            MockTracer tracer = new MockTracer();
            DiffHelper diff = new DiffHelper(tracer, new Mock.Common.MockGVFSEnlistment(), new List<string>(), new List<string>(), includeSymLinks: this.IncludeSymLinks);
            diff.ParseDiffFile(GetDataPath("nestedCaseChange.txt"));

            diff.RequiredBlobs.Count.ShouldEqual(1);
            diff.FileAddOperations.Sum(list => list.Value.Count).ShouldEqual(1);

            // File delete inside the case-renamed parent is filtered out.
            diff.FileDeleteOperations.Count.ShouldEqual(0);
            diff.TotalFileDeletes.ShouldEqual(1);

            // Two directory case-renames were collapsed into Adds in the
            // staging dictionary; only the outermost survives the parent-path
            // filter in FlushStagedQueues.
            diff.TotalDirectoryOperations.ShouldEqual(2);
            diff.DirectoryOperations.Count.ShouldEqual(1);
            diff.DirectoryOperations.ShouldNotContain(entry => entry.Operation == DiffTreeResult.Operations.Delete);

            DiffTreeResult outerOp = diff.DirectoryOperations.First();
            outerOp.SourcePath.ShouldNotBeNull();
            Assert.AreEqual("Outer" + Path.DirectorySeparatorChar, outerOp.SourcePath);
            Assert.AreEqual("outer" + Path.DirectorySeparatorChar, outerOp.TargetPath);
        }

        // Delete a folder with two sub folders each with a single file
        // Readd it with a different casing and same contents
        [TestCase]
        [Category(CategoryConstants.CaseSensitiveFileSystemOnly)]
        public void ParsesCaseChangesAsRenames()
        {
            MockTracer tracer = new MockTracer();
            DiffHelper diffBackwards = new DiffHelper(tracer, new Mock.Common.MockGVFSEnlistment(), new List<string>(), new List<string>(), includeSymLinks: this.IncludeSymLinks);
            diffBackwards.ParseDiffFile(GetDataPath("caseChange.txt"));

            diffBackwards.RequiredBlobs.Count.ShouldEqual(2);
            diffBackwards.FileAddOperations.Sum(list => list.Value.Count).ShouldEqual(2);

            diffBackwards.FileDeleteOperations.Count.ShouldEqual(0);
            diffBackwards.TotalFileDeletes.ShouldEqual(2);

            diffBackwards.DirectoryOperations.ShouldContain(entry => entry.Operation == DiffTreeResult.Operations.Add);
            diffBackwards.DirectoryOperations.ShouldContain(entry => entry.Operation == DiffTreeResult.Operations.Delete);
            diffBackwards.TotalDirectoryOperations.ShouldEqual(6);
        }

        [TestCase]
        public void DiffHelperThrowsOnReuse()
        {
            MockTracer tracer = new MockTracer();
            DiffHelper diff = new DiffHelper(tracer, new Mock.Common.MockGVFSEnlistment(), new List<string>(), new List<string>(), includeSymLinks: this.IncludeSymLinks);
            diff.ParseDiffFile(GetDataPath("forward.txt"));

            Assert.Throws<InvalidOperationException>(() => diff.ParseDiffFile(GetDataPath("forward.txt")));
        }

        [TestCase]
        public void DetectsFailuresInDiffTree()
        {
            MockTracer tracer = new MockTracer();
            MockGitProcess gitProcess = new MockGitProcess();
            gitProcess.SetExpectedCommandResult("diff-tree -r -t sha1 sha2", () => new GitProcess.Result(string.Empty, string.Empty, 1));

            DiffHelper diffBackwards = new DiffHelper(tracer, new Mock.Common.MockGVFSEnlistment(), gitProcess, new List<string>(), new List<string>(), includeSymLinks: this.IncludeSymLinks);
            diffBackwards.PerformDiff("sha1", "sha2");
            diffBackwards.HasFailures.ShouldEqual(true);
        }

        [TestCase]
        public void DetectsFailuresInLsTree()
        {
            MockTracer tracer = new MockTracer();
            MockGitProcess gitProcess = new MockGitProcess();
            gitProcess.SetExpectedCommandResult("ls-tree -r -t sha1", () => new GitProcess.Result(string.Empty, string.Empty, 1));

            DiffHelper diffBackwards = new DiffHelper(tracer, new Mock.Common.MockGVFSEnlistment(), gitProcess, new List<string>(), new List<string>(), includeSymLinks: this.IncludeSymLinks);
            diffBackwards.PerformDiff(null, "sha1");
            diffBackwards.HasFailures.ShouldEqual(true);
        }

        private static string GetDataPath(string fileName)
        {
            string workingDirectory = Path.GetDirectoryName(Environment.ProcessPath);
            return Path.Combine(workingDirectory, "Data", fileName);
        }
    }
}
