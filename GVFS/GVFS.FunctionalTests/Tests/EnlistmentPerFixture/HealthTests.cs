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
        [TestCase]
        public void AfterCloningRepoIsPerfectlyHealthy()
        {
            string[] healthOutputLines = this.Enlistment.Health().Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
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

        [TestCase]
        public void PlaceholdersChangeHealthScores()
        {
            this.HydratePlaceholder(Path.Combine(this.Enlistment.RepoRoot, "Scripts/SetupDevService.bat"));
            this.HydratePlaceholder(Path.Combine(this.Enlistment.RepoRoot, "Scripts/StartDevService.bat"));
            this.HydratePlaceholder(Path.Combine(this.Enlistment.RepoRoot, "Scripts/StopDevService.bat"));

            string healthInfo = this.Enlistment.Health();
        }

        /*
        [TestCase]
        public void ModifiedPathsChangeHealthScores()
        {

        }

        [TestCase]
        directoryfiltering
        */

        private void HydratePlaceholder(string filePath)
        {
            File.ReadAllText(filePath);
        }

        private void HydrateFullFile(string filePath)
        {
            File.OpenWrite(filePath).Close();
        }
    }
}
