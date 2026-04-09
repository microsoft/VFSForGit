using GVFS.FunctionalTests.Properties;
using GVFS.FunctionalTests.Should;
using GVFS.FunctionalTests.Tools;
using NUnit.Framework;
using System.IO;

namespace GVFS.FunctionalTests.Tests.GitCommands
{
    [TestFixture]
    public class RmTests : GitRepoTests
    {
        public RmTests()
            : base(enlistmentPerTest: false, validateWorkingTree: Settings.ValidateWorkingTreeMode.None)
        {
        }

        [TestCase]
        public void CanReadFileAfterGitRmDryRun()
        {
            this.ValidateGitCommand("status");

            // Validate that Scripts\RunUnitTests.bad is not on disk at all
            string filePath = Path.Combine("Scripts", "RunUnitTests.bat");

            this.Enlistment.UnmountGVFS();
            this.Enlistment.GetVirtualPathTo(filePath).ShouldNotExistOnDisk(this.FileSystem);
            this.Enlistment.MountGVFS();

            this.ValidateGitCommand("rm --dry-run " + GitHelpers.ConvertPathToGitFormat(filePath));
            this.FileContentsShouldMatch(filePath);
        }
    }
}
