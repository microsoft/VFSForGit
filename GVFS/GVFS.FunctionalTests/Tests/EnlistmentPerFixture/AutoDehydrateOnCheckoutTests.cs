using GVFS.FunctionalTests.FileSystemRunners;
using GVFS.FunctionalTests.Should;
using GVFS.FunctionalTests.Tools;
using GVFS.Tests.Should;
using NUnit.Framework;
using System.IO;

namespace GVFS.FunctionalTests.Tests.EnlistmentPerFixture
{
    /// <summary>
    /// Single critical-path functional test for auto-dehydrate on checkout.
    /// Verifies that when a hydration threshold is configured and exceeded,
    /// a branch-switching checkout triggers automatic dehydration.
    /// Threshold logic is covered in unit tests.
    /// </summary>
    [TestFixture]
    [Category(Categories.ExtraCoverage)]
    public class AutoDehydrateOnCheckoutTests : TestsWithEnlistmentPerFixture
    {
        private const string BranchA = "FunctionalTests/20201014";
        private const string BranchB = "FunctionalTests/20201014_CheckoutTests2";
        private FileSystemRunner fileSystem;

        public AutoDehydrateOnCheckoutTests()
            : base(forcePerRepoObjectCache: true)
        {
            this.fileSystem = new SystemIORunner();
        }

        [OneTimeSetUp]
        public override void CreateEnlistment()
        {
            base.CreateEnlistment();

            // Fetch BranchB with explicit refspec to create a remote tracking ref
            GitHelpers.InvokeGitAgainstGVFSRepo(
                this.Enlistment.RepoRoot,
                $"fetch origin refs/heads/{BranchB}:refs/remotes/origin/{BranchB}");
        }

        [TearDown]
        public void TearDown()
        {
            string backupFolder = Path.Combine(this.Enlistment.EnlistmentRoot, "dehydrate_backup");
            if (this.fileSystem.DirectoryExists(backupFolder))
            {
                this.fileSystem.DeleteDirectory(backupFolder);
            }

            GitHelpers.InvokeGitAgainstGVFSRepo(
                this.Enlistment.RepoRoot,
                "config --unset gvfs.auto-dehydrate-modified-percent");

            if (!this.Enlistment.IsMounted())
            {
                this.Enlistment.MountGVFS();
            }
        }

        [TestCase]
        public void CheckoutDehydratesWhenThresholdExceeded()
        {
            // Set a low modified threshold so any hydration triggers dehydrate
            GitHelpers.InvokeGitAgainstGVFSRepo(
                this.Enlistment.RepoRoot,
                "config gvfs.auto-dehydrate-modified-percent 0");

            // Hydrate files by reading, then delete+restore to push into modified paths
            this.HydrateAndModifyFiles();

            // Warm the hydration cache by running git status (triggers async
            // cache rebuild in mount), then wait for it to populate
            GitHelpers.InvokeGitAgainstGVFSRepo(this.Enlistment.RepoRoot, "status");
            System.Threading.Thread.Sleep(5000);

            ProcessResult result = GitHelpers.InvokeGitAgainstGVFSRepo(
                this.Enlistment.RepoRoot,
                "checkout " + BranchB);
            result.ExitCode.ShouldEqual(0, "checkout failed: " + result.Errors);
            string allOutput = result.Output + result.Errors;
            allOutput.ShouldContain("Dehydrating before checkout");
        }

        private void HydrateAndModifyFiles()
        {
            string[] filesToModify = new[]
            {
                "Readme.md",
                "GVFS/GVFS.sln",
                "GVFS/GVFS/Program.cs",
            };

            foreach (string file in filesToModify)
            {
                string path = this.Enlistment.GetVirtualPathTo(file);
                if (File.Exists(path))
                {
                    File.ReadAllText(path);
                    File.Delete(path);
                }
            }

            GitHelpers.InvokeGitAgainstGVFSRepo(
                this.Enlistment.RepoRoot,
                "checkout -- Readme.md GVFS/GVFS.sln GVFS/GVFS/Program.cs");
        }
    }
}
