using GVFS.Common.Git;
using GVFS.Tests.Should;
using GVFS.UnitTests.Category;
using GVFS.UnitTests.Mock.Common;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace GVFS.UnitTests.FastFetch
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
            DiffHelper diffForwards = new DiffHelper(tracer, null, null, new List<string>());
            diffForwards.ParseDiffFile(this.GetDataPath("forward.txt"), "xx:\\fakeRepo");

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
            DiffHelper diffBackwards = new DiffHelper(tracer, null, null, new List<string>());
            diffBackwards.ParseDiffFile(this.GetDataPath("backward.txt"), "xx:\\fakeRepo");

            // File > folder, deleted file, edited file, renamed file, rename-edit file
            // Children of file > folder, renamed folder, deleted folder, recursive delete file, edited folder
            diffBackwards.RequiredBlobs.Count.ShouldEqual(10);

            // File added, folder > file, moved folder, added folder
            diffBackwards.FileDeleteOperations.Count.ShouldEqual(4);

            // Also includes, the children of: Folder added, folder renamed, file => folder
            diffBackwards.TotalFileDeletes.ShouldEqual(7);

            // Folder created, folder edited, folder deleted, folder renamed (add + delete), 
            // folder => file, file => folder, recursive delete (include subfolder)
            diffBackwards.DirectoryOperations.Count.ShouldEqual(9);

            // Should match count above since there were no recursive adds to become recursive deletes
            diffBackwards.TotalDirectoryOperations.ShouldEqual(9);
        }

        private string GetDataPath(string fileName)
        {
            string workingDirectory = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            return Path.Combine(workingDirectory, "Data", fileName);
        }
    }
}