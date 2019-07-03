using GVFS.Tests.Should;
using NUnit.Framework;
using NUnit.Framework.Internal;
using System;
using System.IO;

namespace GVFS.FunctionalTests.Tests.EnlistmentPerFixture
{
    [TestFixture]
    public class HealthTests : TestsWithEnlistmentPerFixture
    {
        [TestCase, Order(0)]
        public void AfterCloningRepoIsPerfectlyHealthy()
        {
            // .gitignore is always a placeholder on creation
            // .gitconfig is always a modified path in functional tests since it is written at run time
            string[] healthOutputLines = this.GetHealthOutputLines();

            healthOutputLines[0].ShouldEqual("Health of directory: ");
            healthOutputLines[1].ShouldEqual("Total files in HEAD commit:           1,211 | 100%");
            healthOutputLines[2].ShouldEqual("Files managed by VFS for Git (fast):      1 |   0%");
            healthOutputLines[3].ShouldEqual("Files managed by git (slow):              1 |   0%");
            healthOutputLines[4].ShouldEqual("Total hydration percentage:                     0%");

            healthOutputLines[6].ShouldEqual("   0% | GVFS                   ");
            healthOutputLines[7].ShouldEqual("   0% | GVFlt_BugRegressionTest");
            healthOutputLines[8].ShouldEqual("   0% | GVFlt_DeleteFileTest   ");
            healthOutputLines[9].ShouldEqual("   0% | GVFlt_DeleteFolderTest ");
            healthOutputLines[10].ShouldEqual("   0% | GVFlt_EnumTest         ");

            healthOutputLines[11].ShouldEqual("Repository status: OK");
        }

        [TestCase, Order(1)]
        public void PlaceholdersChangeHealthScores()
        {
            // Hydrate all files in the Scripts/ directory as placeholders
            // This creates 6 placeholders, 5 files along with the Scripts/ directory
            this.HydratePlaceholder(Path.Combine(this.Enlistment.RepoRoot, "Scripts/CreateCommonAssemblyVersion.bat"));
            this.HydratePlaceholder(Path.Combine(this.Enlistment.RepoRoot, "Scripts/CreateCommonCliAssemblyVersion.bat"));
            this.HydratePlaceholder(Path.Combine(this.Enlistment.RepoRoot, "Scripts/CreateCommonVersionHeader.bat"));
            this.HydratePlaceholder(Path.Combine(this.Enlistment.RepoRoot, "Scripts/RunFunctionalTests.bat"));
            this.HydratePlaceholder(Path.Combine(this.Enlistment.RepoRoot, "Scripts/RunUnitTests.bat"));

            string[] healthOutputLines = this.GetHealthOutputLines();

            healthOutputLines[0].ShouldEqual("Health of directory: ");
            healthOutputLines[1].ShouldEqual("Total files in HEAD commit:           1,211 | 100%");
            healthOutputLines[2].ShouldEqual("Files managed by VFS for Git (fast):      7 |   1%");
            healthOutputLines[3].ShouldEqual("Files managed by git (slow):              1 |   0%");
            healthOutputLines[4].ShouldEqual("Total hydration percentage:                     1%");

            healthOutputLines[6].ShouldEqual(" 100% | Scripts                ");
            healthOutputLines[7].ShouldEqual("   0% | GVFS                   ");
            healthOutputLines[8].ShouldEqual("   0% | GVFlt_BugRegressionTest");
            healthOutputLines[9].ShouldEqual("   0% | GVFlt_DeleteFileTest   ");
            healthOutputLines[10].ShouldEqual("   0% | GVFlt_DeleteFolderTest ");

            healthOutputLines[11].ShouldEqual("Repository status: OK");
        }

        [TestCase, Order(2)]
        public void ModifiedPathsChangeHealthScores()
        {
            // Hydrate all files in GVFlt_FileOperationTest as modified paths
            // This creates 2 modified paths and one placeholder
            this.HydrateFullFile(Path.Combine(this.Enlistment.RepoRoot, "GVFlt_FileOperationTest/DeleteExistingFile.txt"));
            this.HydrateFullFile(Path.Combine(this.Enlistment.RepoRoot, "GVFlt_FileOperationTest/WriteAndVerify.txt"));

            string[] healthOutputLines = this.GetHealthOutputLines();

            healthOutputLines[0].ShouldEqual("Health of directory: ");
            healthOutputLines[1].ShouldEqual("Total files in HEAD commit:           1,211 | 100%");
            healthOutputLines[2].ShouldEqual("Files managed by VFS for Git (fast):      8 |   1%");
            healthOutputLines[3].ShouldEqual("Files managed by git (slow):              3 |   0%");
            healthOutputLines[4].ShouldEqual("Total hydration percentage:                     1%");

            healthOutputLines[6].ShouldEqual(" 100% | GVFlt_FileOperationTest");
            healthOutputLines[7].ShouldEqual(" 100% | Scripts                ");
            healthOutputLines[8].ShouldEqual("   0% | GVFS                   ");
            healthOutputLines[9].ShouldEqual("   0% | GVFlt_BugRegressionTest");
            healthOutputLines[10].ShouldEqual("   0% | GVFlt_DeleteFileTest   ");

            healthOutputLines[11].ShouldEqual("Repository status: OK");
        }

        [TestCase, Order(3)]
        public void TurnPlaceholdersIntoModifiedPaths()
        {
            // Hydrate the files in Scripts/ from placeholders to modified paths
            this.HydrateFullFile(Path.Combine(this.Enlistment.RepoRoot, "Scripts/CreateCommonAssemblyVersion.bat"));
            this.HydrateFullFile(Path.Combine(this.Enlistment.RepoRoot, "Scripts/CreateCommonCliAssemblyVersion.bat"));
            this.HydrateFullFile(Path.Combine(this.Enlistment.RepoRoot, "Scripts/CreateCommonVersionHeader.bat"));
            this.HydrateFullFile(Path.Combine(this.Enlistment.RepoRoot, "Scripts/RunFunctionalTests.bat"));
            this.HydrateFullFile(Path.Combine(this.Enlistment.RepoRoot, "Scripts/RunUnitTests.bat"));

            string[] healthOutputLines = this.GetHealthOutputLines();

            healthOutputLines[0].ShouldEqual("Health of directory: ");
            healthOutputLines[1].ShouldEqual("Total files in HEAD commit:           1,211 | 100%");
            healthOutputLines[2].ShouldEqual("Files managed by VFS for Git (fast):      3 |   0%");
            healthOutputLines[3].ShouldEqual("Files managed by git (slow):              8 |   1%");
            healthOutputLines[4].ShouldEqual("Total hydration percentage:                     1%");

            healthOutputLines[6].ShouldEqual(" 100% | GVFlt_FileOperationTest");
            healthOutputLines[7].ShouldEqual(" 100% | Scripts                ");
            healthOutputLines[8].ShouldEqual("   0% | GVFS                   ");
            healthOutputLines[9].ShouldEqual("   0% | GVFlt_BugRegressionTest");
            healthOutputLines[10].ShouldEqual("   0% | GVFlt_DeleteFileTest   ");

            healthOutputLines[11].ShouldEqual("Repository status: OK");
        }

        [TestCase, Order(4)]
        public void FilterIntoDirectory()
        {
            string[] healthOutputLines = this.GetHealthOutputLines("Scripts/");

            healthOutputLines[0].ShouldEqual("Health of directory: Scripts/");
            healthOutputLines[1].ShouldEqual("Total files in HEAD commit:           5 | 100%");
            healthOutputLines[2].ShouldEqual("Files managed by VFS for Git (fast):  0 |   0%");
            healthOutputLines[3].ShouldEqual("Files managed by git (slow):          5 | 100%");
            healthOutputLines[4].ShouldEqual("Total hydration percentage:               100%");

            healthOutputLines[6].ShouldEqual("Repository status: Highly Hydrated");
        }

        private void HydratePlaceholder(string filePath)
        {
            File.ReadAllText(filePath);
        }

        private void HydrateFullFile(string filePath)
        {
            File.OpenWrite(filePath).Close();
        }

        private string[] GetHealthOutputLines(string directory = null)
        {
            return this.Enlistment.Health(directory).Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
        }
    }
}
