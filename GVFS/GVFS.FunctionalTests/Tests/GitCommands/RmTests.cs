using GVFS.FunctionalTests.Should;
using NUnit.Framework;
using System.IO;

namespace GVFS.FunctionalTests.Tests.GitCommands
{
    [TestFixture]
    public class RmTests : GitRepoTests
    {
        public RmTests()
            : base(enlistmentPerTest: false, validateWorkingTree: false)
        {
        }

        [TestCase]
        public void CanReadFileAfterGitRmDryRun()
        {
            this.ValidateGitCommand("status");

            // Validate that Scripts\RunUnitTests.bad is not on disk at all
            string fileName = Path.Combine("Scripts", "RunUnitTests.bat");

            this.Enlistment.UnmountGVFS();
            this.Enlistment.GetVirtualPathTo(fileName).ShouldNotExistOnDisk(this.FileSystem);
            this.Enlistment.MountGVFS();
                    
            this.ValidateGitCommand("rm --dry-run " + fileName.Replace("\\", "/"));
            this.FileContentsShouldMatch(fileName);
        }
    }
}
