using RGFS.FunctionalTests.Tools;
using RGFS.Tests.Should;
using NUnit.Framework;
using System.IO;
using System.Linq;

namespace RGFS.FunctionalTests.Tests.EnlistmentPerTestCase
{
    [TestFixture]
    public class RepairTests : TestsWithEnlistmentPerTestCase
    {
        [TestCase]
        public void FixesCorruptHeadSha()
        {
            this.Enlistment.UnmountRGFS();

            string headFilePath = Path.Combine(this.Enlistment.RepoRoot, ".git", "HEAD");
            File.WriteAllText(headFilePath, "0000");

            this.Enlistment.TryMountRGFS().ShouldEqual(false, "RGFS shouldn't mount when HEAD is corrupt");

            this.Enlistment.Repair();

            this.Enlistment.MountRGFS();
        }

        [TestCase]
        public void FixesCorruptHeadSymRef()
        {
            this.Enlistment.UnmountRGFS();

            string headFilePath = Path.Combine(this.Enlistment.RepoRoot, ".git", "HEAD");
            File.WriteAllText(headFilePath, "ref: refs");

            this.Enlistment.TryMountRGFS().ShouldEqual(false, "RGFS shouldn't mount when HEAD is corrupt");

            this.Enlistment.Repair();

            this.Enlistment.MountRGFS();
        }

        [TestCase]
        public void FixesCorruptBlobSizesDatabase()
        {
            this.Enlistment.UnmountRGFS();
            
            // Most other files in an ESENT folder can be corrupted without blocking RGFS mount. ESENT just recreates them.
            string blobSizesDbPath = Path.Combine(this.Enlistment.DotRGFSRoot, "BlobSizes", "PersistentDictionary.edb");
            File.WriteAllText(blobSizesDbPath, "0000");

            this.Enlistment.TryMountRGFS().ShouldEqual(false, "RGFS shouldn't mount when blob size db is corrupt");

            this.Enlistment.Repair();

            this.Enlistment.MountRGFS();
        }

        [TestCase]
        public void FixesMissingGitIndex()
        {
            this.Enlistment.UnmountRGFS();

            string gitIndexPath = Path.Combine(this.Enlistment.RepoRoot, ".git", "index");
            File.Delete(gitIndexPath);

            this.Enlistment.TryMountRGFS().ShouldEqual(false, "RGFS shouldn't mount when git index is missing");

            this.Enlistment.Repair();

            this.Enlistment.MountRGFS();
        }

        [TestCase]
        public void FixesGitIndexCorruptedWithBadData()
        {
            this.Enlistment.UnmountRGFS();

            string gitIndexPath = Path.Combine(this.Enlistment.RepoRoot, ".git", "index");
            File.WriteAllText(gitIndexPath, "BAD_INDEX");

            string output;
            this.Enlistment.TryMountRGFS(out output).ShouldEqual(false, "RGFS shouldn't mount when index is corrupt");
            output.ShouldContain("Index validation failed");

            this.Enlistment.Repair();

            this.Enlistment.MountRGFS();
        }

        [TestCase]
        public void FixesGitIndexContainingAllNulls()
        {
            this.Enlistment.UnmountRGFS();

            string gitIndexPath = Path.Combine(this.Enlistment.RepoRoot, ".git", "index");

            // Set the contents of the index file to gitIndexPath NULL
            FileInfo indexFileInfo = new FileInfo(gitIndexPath);
            File.WriteAllBytes(gitIndexPath, Enumerable.Repeat<byte>(0, (int)indexFileInfo.Length).ToArray());

            string output;
            this.Enlistment.TryMountRGFS(out output).ShouldEqual(false, "RGFS shouldn't mount when index is corrupt");
            output.ShouldContain("Index validation failed");

            this.Enlistment.Repair();

            this.Enlistment.MountRGFS();
        }

        [TestCase]
        public void FixesGitIndexCorruptedByTruncation()
        {
            this.Enlistment.UnmountRGFS();

            string gitIndexPath = Path.Combine(this.Enlistment.RepoRoot, ".git", "index");

            // Truncate the contents of the index
            FileInfo indexFileInfo = new FileInfo(gitIndexPath);
            using (FileStream indexStream = new FileStream(gitIndexPath, FileMode.Open))
            {
                // 20 will truncate the file in the middle of the first entry in the index
                indexStream.SetLength(20);
            }

            string output;
            this.Enlistment.TryMountRGFS(out output).ShouldEqual(false, "RGFS shouldn't mount when index is corrupt");
            output.ShouldContain("Index validation failed");

            this.Enlistment.Repair();

            this.Enlistment.MountRGFS();
        }

        [TestCase]
        public void FixesCorruptGitConfig()
        {
            this.Enlistment.UnmountRGFS();

            string gitIndexPath = Path.Combine(this.Enlistment.RepoRoot, ".git", "config");
            File.WriteAllText(gitIndexPath, "[cor");

            this.Enlistment.TryMountRGFS().ShouldEqual(false, "RGFS shouldn't mount when git config is missing");

            this.Enlistment.Repair();

            ProcessResult result = GitProcess.InvokeProcess(this.Enlistment.RepoRoot, "remote add origin " + this.Enlistment.RepoUrl);
            result.ExitCode.ShouldEqual(0, result.Errors);
            
            this.Enlistment.MountRGFS();
        }
    }
}
