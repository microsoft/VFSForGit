using GVFS.FunctionalTests.Properties;
using GVFS.FunctionalTests.Tools;
using NUnit.Framework;

namespace GVFS.FunctionalTests.Tests.GitCommands
{
    [TestFixtureSource(typeof(GitRepoTests), nameof(GitRepoTests.ValidateWorkingTree))]
    [Category(Categories.GitCommands)]
    public class UpdateRefTests : GitRepoTests
    {
        public UpdateRefTests(Settings.ValidateWorkingTreeMode validateWorkingTree)
            : base(enlistmentPerTest: true, validateWorkingTree: validateWorkingTree)
        {
        }

        [TestCase]
        public void UpdateRefModifiesHead()
        {
            this.ValidateGitCommand("status");
            this.ValidateGitCommand("update-ref HEAD f1bce402a7a980a8320f3f235cf8c8fdade4b17a");
        }

        [TestCase]
        public void UpdateRefModifiesHeadThenResets()
        {
            this.ValidateGitCommand("status");
            this.ValidateGitCommand("update-ref HEAD f1bce402a7a980a8320f3f235cf8c8fdade4b17a");
            this.ValidateGitCommand("reset HEAD");
        }

        public override void TearDownForTest()
        {
            if (FileSystemHelpers.CaseSensitiveFileSystem)
            {
                this.TestValidationAndCleanup();
            }
            else
            {
                // On case-insensitive filesystems, we
                // need to ignore case changes in this test because the update-ref will have
                // folder names that only changed the case and when checking the folder structure
                // it will create partial folders with that case and will not get updated to the
                // previous case when the reset --hard running in the tear down step
                this.TestValidationAndCleanup(ignoreCase: true);
            }
        }
    }
}
