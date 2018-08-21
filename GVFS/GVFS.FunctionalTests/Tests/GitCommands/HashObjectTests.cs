using GVFS.FunctionalTests.Should;
using GVFS.FunctionalTests.Tools;
using NUnit.Framework;

namespace GVFS.FunctionalTests.Tests.GitCommands
{
    [TestFixture]
    [Category(Categories.Mac.M3)]
    public class HashObjectTests : GitRepoTests
    {
        public HashObjectTests() : base(enlistmentPerTest: false)
        {
        }

        [TestCase]
        public void CanReadFileAfterHashObject()
        {
            this.ValidateGitCommand("status");

            // Validate that Readme.md is not on disk at all
            string fileName = "Readme.md";

            this.Enlistment.UnmountGVFS();
            this.Enlistment.GetVirtualPathTo(fileName).ShouldNotExistOnDisk(this.FileSystem);
            this.Enlistment.MountGVFS();

            // TODO 1087312: Fix 'git hash-oject' so that it works for files that aren't on disk yet
            GitHelpers.InvokeGitAgainstGVFSRepo(
                this.Enlistment.RepoRoot,
                "hash-object " + fileName);

            this.FileContentsShouldMatch(fileName);
        }
    }
}
