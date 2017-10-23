using GVFS.FunctionalTests.Tools;
using GVFS.Tests.Should;
using NUnit.Framework;
using System.IO;
using System.Linq;

namespace GVFS.FunctionalTests.Tests.EnlistmentPerTestCase
{
    [TestFixture]
    public class RepairTests : TestsWithEnlistmentPerTestCase
    {
        [TestCase]
        public void FixesCorruptHeadSha()
        {
            this.Enlistment.UnmountGVFS();

            string headFilePath = Path.Combine(this.Enlistment.RepoRoot, ".git", "HEAD");
            File.WriteAllText(headFilePath, "0000");

            this.Enlistment.TryMountGVFS().ShouldEqual(false, "GVFS shouldn't mount when HEAD is corrupt");

            this.Enlistment.Repair();

            this.Enlistment.MountGVFS();
        }

        [TestCase]
        public void FixesCorruptHeadSymRef()
        {
            this.Enlistment.UnmountGVFS();

            string headFilePath = Path.Combine(this.Enlistment.RepoRoot, ".git", "HEAD");
            File.WriteAllText(headFilePath, "ref: refs");

            this.Enlistment.TryMountGVFS().ShouldEqual(false, "GVFS shouldn't mount when HEAD is corrupt");

            this.Enlistment.Repair();

            this.Enlistment.MountGVFS();
        }

        [TestCase]
        public void FixesCorruptBlobSizesDatabase()
        {
            this.Enlistment.UnmountGVFS();
            
            // Most other files in an ESENT folder can be corrupted without blocking GVFS mount. ESENT just recreates them.
            string blobSizesDbPath = Path.Combine(this.Enlistment.DotGVFSRoot, "BlobSizes", "PersistentDictionary.edb");
            File.WriteAllText(blobSizesDbPath, "0000");

            this.Enlistment.TryMountGVFS().ShouldEqual(false, "GVFS shouldn't mount when blob size db is corrupt");

            this.Enlistment.Repair();

            this.Enlistment.MountGVFS();
        }

        [TestCase]
        public void FixesMissingGitIndex()
        {
            this.Enlistment.UnmountGVFS();

            string gitIndexPath = Path.Combine(this.Enlistment.RepoRoot, ".git", "index");
            File.Delete(gitIndexPath);

            this.Enlistment.TryMountGVFS().ShouldEqual(false, "GVFS shouldn't mount when git index is missing");

            this.Enlistment.Repair();

            this.Enlistment.MountGVFS();
        }

        [TestCase]
        public void FixesGitIndexCorruptedWithBadData()
        {
            this.Enlistment.UnmountGVFS();

            string gitIndexPath = Path.Combine(this.Enlistment.RepoRoot, ".git", "index");
            File.WriteAllText(gitIndexPath, "BAD_INDEX");

            string output;
            this.Enlistment.TryMountGVFS(out output).ShouldEqual(false, "GVFS shouldn't mount when index is corrupt");
            output.ShouldContain("Index validation failed");

            this.Enlistment.Repair();

            this.Enlistment.MountGVFS();
        }

        [TestCase]
        public void FixesGitIndexContainingAllNulls()
        {
            this.Enlistment.UnmountGVFS();

            string gitIndexPath = Path.Combine(this.Enlistment.RepoRoot, ".git", "index");

            // Set the contents of the index file to gitIndexPath NULL
            FileInfo indexFileInfo = new FileInfo(gitIndexPath);
            File.WriteAllBytes(gitIndexPath, Enumerable.Repeat<byte>(0, (int)indexFileInfo.Length).ToArray());

            string output;
            this.Enlistment.TryMountGVFS(out output).ShouldEqual(false, "GVFS shouldn't mount when index is corrupt");
            output.ShouldContain("Index validation failed");

            this.Enlistment.Repair();

            this.Enlistment.MountGVFS();
        }

        [TestCase]
        public void FixesGitIndexCorruptedByTruncation()
        {
            this.Enlistment.UnmountGVFS();

            string gitIndexPath = Path.Combine(this.Enlistment.RepoRoot, ".git", "index");

            // Truncate the contents of the index
            FileInfo indexFileInfo = new FileInfo(gitIndexPath);
            using (FileStream indexStream = new FileStream(gitIndexPath, FileMode.Open))
            {
                // 20 will truncate the file in the middle of the first entry in the index
                indexStream.SetLength(20);
            }

            string output;
            this.Enlistment.TryMountGVFS(out output).ShouldEqual(false, "GVFS shouldn't mount when index is corrupt");
            output.ShouldContain("Index validation failed");

            this.Enlistment.Repair();

            this.Enlistment.MountGVFS();
        }

        [TestCase]
        public void FixesCorruptGitConfig()
        {
            this.Enlistment.UnmountGVFS();

            string gitIndexPath = Path.Combine(this.Enlistment.RepoRoot, ".git", "config");
            File.WriteAllText(gitIndexPath, "[cor");

            this.Enlistment.TryMountGVFS().ShouldEqual(false, "GVFS shouldn't mount when git config is missing");

            this.Enlistment.Repair();

            ProcessResult result = GitProcess.InvokeProcess(this.Enlistment.RepoRoot, "remote add origin " + this.Enlistment.RepoUrl);
            result.ExitCode.ShouldEqual(0, result.Errors);
            
            this.Enlistment.MountGVFS();
        }
    }
}
