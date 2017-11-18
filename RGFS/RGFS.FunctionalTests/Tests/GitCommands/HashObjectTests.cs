using RGFS.FunctionalTests.Should;
using RGFS.FunctionalTests.Tools;
using NUnit.Framework;

namespace RGFS.FunctionalTests.Tests.GitCommands
{
    [TestFixture]
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

            this.Enlistment.UnmountRGFS();
            this.Enlistment.GetVirtualPathTo(fileName).ShouldNotExistOnDisk(this.FileSystem);
            this.Enlistment.MountRGFS();

            // TODO 1087312: Fix 'git hash-oject' so that it works for files that aren't on disk yet
            GitHelpers.InvokeGitAgainstRGFSRepo(
                this.Enlistment.RepoRoot,
                "hash-object " + fileName);

            this.FileContentsShouldMatch(fileName);
        }
    }
}
