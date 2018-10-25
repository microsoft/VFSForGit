using GVFS.FunctionalTests.Should;
using GVFS.FunctionalTests.Tools;
using NUnit.Framework;

namespace GVFS.FunctionalTests.Tests.GitCommands
{
    [TestFixtureSource(typeof(GitRepoTests), GitRepoTests.ValidateWorkingTree)]
    [Category(Categories.GitCommands)]
    [Category(Categories.MacTODO.M3)]
    public class HashObjectTests : GitRepoTests
    {
        public HashObjectTests(ValidateWorkingTreeOptions validateWorkingTree) 
            : base(enlistmentPerTest: false, validateWorkingTree: validateWorkingTree)
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
