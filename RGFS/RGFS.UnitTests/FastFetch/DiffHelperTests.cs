using FastFetch.Git;
using RGFS.Common.Git;
using RGFS.Tests.Should;
using RGFS.UnitTests.Mock.Common;
using RGFS.UnitTests.Mock.FileSystem;
using RGFS.UnitTests.Mock.Git;
using NUnit.Framework;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

namespace RGFS.UnitTests.FastFetch
{
    [TestFixture]
    public class DiffHelperTests
    {
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
            DiffHelper diffForwards = new DiffHelper(tracer, new MockEnlistment(), new List<string>(), new List<string>());
            diffForwards.ParseDiffFile(GetDataPath("forward.txt"), "xx:\\fakeRepo");

            // File added, file edited, file renamed, folder => file, edit-rename file
            // Children of: Add folder, Renamed folder, edited folder, file => folder
            diffForwards.RequiredBlobs.Count.ShouldEqual(9);

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
            DiffHelper diffBackwards = new DiffHelper(tracer, new MockEnlistment(), new List<string>(), new List<string>());
            diffBackwards.ParseDiffFile(GetDataPath("backward.txt"), "xx:\\fakeRepo");

            // File > folder, deleted file, edited file, renamed file, rename-edit file
            // Children of file > folder, renamed folder, deleted folder, recursive delete file, edited folder
            diffBackwards.RequiredBlobs.Count.ShouldEqual(10);

            // File added, folder > file, moved folder, added folder
            diffBackwards.FileDeleteOperations.Count.ShouldEqual(4);

            // Also includes, the children of: Folder added, folder renamed, file => folder
            diffBackwards.TotalFileDeletes.ShouldEqual(7);

            // Folder created, folder edited, folder deleted, folder renamed (add + delete), 
            // folder => file, file => folder, recursive delete (include subfolder)
            diffBackwards.TotalDirectoryOperations.ShouldEqual(9);
        }

        // Delete a folder with two sub folders each with a single file
        // Readd it with a different casing and same contents
        [TestCase]
        public void ParsesCaseChangesAsAdds()
        {
            MockTracer tracer = new MockTracer();
            DiffHelper diffBackwards = new DiffHelper(tracer, new MockEnlistment(), new List<string>(), new List<string>());
            diffBackwards.ParseDiffFile(GetDataPath("caseChange.txt"), "xx:\\fakeRepo");
            
            diffBackwards.RequiredBlobs.Count.ShouldEqual(2);
            diffBackwards.FileAddOperations.Sum(list => list.Value.Count).ShouldEqual(2);

            diffBackwards.FileDeleteOperations.Count.ShouldEqual(0);
            diffBackwards.TotalFileDeletes.ShouldEqual(0);
            
            diffBackwards.DirectoryOperations.ShouldNotContain(entry => entry.Operation == DiffTreeResult.Operations.Delete);
            diffBackwards.TotalDirectoryOperations.ShouldEqual(3);
        }

        [TestCase]
        public void DetectsFailuresInDiffTree()
        {
            MockTracer tracer = new MockTracer();
            MockGitProcess gitProcess = new MockGitProcess(new ConfigurableFileSystem());
            gitProcess.SetExpectedCommandResult("diff-tree -r -t sha1 sha2", () => new GitProcess.Result(string.Empty, string.Empty, 1));

            DiffHelper diffBackwards = new DiffHelper(tracer, new MockEnlistment(), gitProcess, new List<string>(), new List<string>());
            diffBackwards.PerformDiff("sha1", "sha2");
            diffBackwards.HasFailures.ShouldEqual(true);
        }

        [TestCase]
        public void DetectsFailuresInLsTree()
        {
            MockTracer tracer = new MockTracer();
            MockGitProcess gitProcess = new MockGitProcess(new ConfigurableFileSystem());
            gitProcess.SetExpectedCommandResult("ls-tree -r -t sha1", () => new GitProcess.Result(string.Empty, string.Empty, 1));

            DiffHelper diffBackwards = new DiffHelper(tracer, new MockEnlistment(), gitProcess, new List<string>(), new List<string>());
            diffBackwards.PerformDiff(null, "sha1");
            diffBackwards.HasFailures.ShouldEqual(true);
        }

        private static string GetDataPath(string fileName)
        {
            string workingDirectory = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            return Path.Combine(workingDirectory, "Data", fileName);
        }
    }
}