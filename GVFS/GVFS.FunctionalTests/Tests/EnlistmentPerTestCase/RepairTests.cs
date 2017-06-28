using GVFS.Tests.Should;
using NUnit.Framework;
using System.IO;

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
            File.WriteAllText(headFilePath, "000");

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
            File.WriteAllText(blobSizesDbPath, "000");

            this.Enlistment.TryMountGVFS().ShouldEqual(false, "GVFS shouldn't mount when blob size db is corrupt");

            this.Enlistment.Repair();

            this.Enlistment.MountGVFS();
        }
    }
}
