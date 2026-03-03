using GVFS.FunctionalTests.Properties;
using NUnit.Framework;

namespace GVFS.FunctionalTests.Tests.GitCommands
{
    /// <summary>
    /// This class is used to reproduce corruption scenarios in the GVFS virtual projection.
    /// </summary>
    [Category(Categories.GitCommands)]
    [TestFixtureSource(typeof(GitRepoTests), nameof(GitRepoTests.ValidateWorkingTree))]
    public class CorruptionReproTests : GitRepoTests
    {
        public CorruptionReproTests(Settings.ValidateWorkingTreeMode validateWorkingTree)
           : base(enlistmentPerTest: true, validateWorkingTree: validateWorkingTree)
        {
        }

        /// <summary>
        /// Reproduction of a reported issue:
        /// Restoring a file after its parent directory was deleted fails with
        /// "fatal: could not unlink 'path\to\': Directory not empty"
        /// </summary>
        [TestCase]
        public void RestoreAfterDeleteNesteredDirectory()
        {
            // Delete a directory with nested subdirectories and files.
            this.ValidateNonGitCommand("cmd.exe", "/c \"rmdir /s /q GVFlt_DeleteFileTest\"");

            // Restore the working directory.
            this.ValidateGitCommand("restore .");

            this.FilesShouldMatchCheckoutOfSourceBranch();
        }
    }
}