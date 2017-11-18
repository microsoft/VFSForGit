using RGFS.FunctionalTests.Should;
using NUnit.Framework;

namespace RGFS.FunctionalTests.Tests.GitCommands
{
    [TestFixture]
    public class RmTests : GitRepoTests
    {
        public RmTests() : base(enlistmentPerTest: false)
        {
        }

        [TestCase]
        public void CanReadFileAfterGitRmDryRun()
        {
            this.ValidateGitCommand("status");

            // Validate that Readme.md is not on disk at all
            string fileName = "Readme.md";

            this.Enlistment.UnmountRGFS();
            this.Enlistment.GetVirtualPathTo(fileName).ShouldNotExistOnDisk(this.FileSystem);
            this.Enlistment.MountRGFS();
                    
            this.ValidateGitCommand("rm --dry-run " + fileName);
            this.FileContentsShouldMatch(fileName);
        }
    }
}
