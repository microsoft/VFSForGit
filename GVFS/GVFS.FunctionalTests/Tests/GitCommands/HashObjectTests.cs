using GVFS.FunctionalTests.Should;
using GVFS.FunctionalTests.Tools;
using NUnit.Framework;
using System.IO;

namespace GVFS.FunctionalTests.Tests.GitCommands
{
    [TestFixture]
    [Category(Categories.GitCommands)]
    public class HashObjectTests : GitRepoTests
    {
        public HashObjectTests() 
            : base(enlistmentPerTest: false, validateWorkingTree: ValidateWorkingTreeOptions.DoNotValidateWorkingTree)
        {
        }

        [TestCase]
        public void CanReadFileAfterHashObject()
        {
            this.ValidateGitCommand("status");

            // Validate that Scripts\RunUnitTests.bad is not on disk at all
            string fileName = Path.Combine("Scripts", "RunUnitTests.bat");

            this.Enlistment.UnmountGVFS();
            this.Enlistment.GetVirtualPathTo(fileName).ShouldNotExistOnDisk(this.FileSystem);
            this.Enlistment.MountGVFS();

            // TODO 1087312: Fix 'git hash-oject' so that it works for files that aren't on disk yet
            GitHelpers.InvokeGitAgainstGVFSRepo(
                this.Enlistment.RepoRoot,
                "hash-object " + fileName.Replace("\\", "/"));

            this.FileContentsShouldMatch(fileName);
        }
    }
}
