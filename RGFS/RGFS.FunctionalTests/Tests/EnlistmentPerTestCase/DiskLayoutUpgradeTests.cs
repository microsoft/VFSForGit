using RGFS.FunctionalTests.FileSystemRunners;
using RGFS.FunctionalTests.Should;
using RGFS.FunctionalTests.Tools;
using RGFS.Tests.Should;
using NUnit.Framework;
using System.IO;

namespace RGFS.FunctionalTests.Tests.EnlistmentPerTestCase
{
    [TestFixture]
    public class DiskLayoutUpgradeTests : TestsWithEnlistmentPerTestCase
    {
        private const int CurrentDiskLayoutVersion = 11;
        private FileSystemRunner fileSystem = new SystemIORunner();

        [TestCase]
        public void MountUpgradesFromVersion7()
        {
            // Seven to eight is a just a version change (non-breaking), but preserves ESENT RepoMetadata
            this.RunEsentRepoMetadataUpgradeTest("7");
        }

        [TestCase]
        public void MountUpgradesFromEsentToJsonRepoMetadata()
        {
            // Eight is the last version with ESENT RepoMetadata DB
            this.RunEsentRepoMetadataUpgradeTest("8");
        }

        [TestCase]
        public void MountUpgradesFromEsentDatabasesToFlatDatabases()
        {
            this.Enlistment.UnmountRGFS();

            // Delete the existing background ops data
            string flatBackgroundPath = Path.Combine(this.Enlistment.DotRGFSRoot, RGFSHelpers.BackgroundOpsFile);
            flatBackgroundPath.ShouldBeAFile(this.fileSystem);
            this.fileSystem.DeleteFile(flatBackgroundPath);
            
            // Delete the existing placeholder data
            string flatPlaceholdersPath = Path.Combine(this.Enlistment.DotRGFSRoot, RGFSHelpers.PlaceholderListFile);
            flatPlaceholdersPath.ShouldBeAFile(this.fileSystem);
            this.fileSystem.DeleteFile(flatPlaceholdersPath);

            RGFSHelpers.CreateEsentBackgroundOpsDatabase(this.Enlistment.DotRGFSRoot);
            RGFSHelpers.CreateEsentPlaceholderDatabase(this.Enlistment.DotRGFSRoot);

            // Nine is the last version with ESENT BackgroundOps and Placeholders DBs
            RGFSHelpers.SaveDiskLayoutVersion(this.Enlistment.DotRGFSRoot, "9");
            this.Enlistment.MountRGFS();

            RGFSHelpers.GetPersistedDiskLayoutVersion(this.Enlistment.DotRGFSRoot)
                .ShouldBeAnInt("Disk layout version should always be an int")
                .ShouldEqual(CurrentDiskLayoutVersion);

            flatBackgroundPath.ShouldBeAFile(this.fileSystem);
            flatPlaceholdersPath.ShouldBeAFile(this.fileSystem);
        }

        [TestCase]
        public void MountUpgradesFromPriorToPlaceholderCreationsBlockedForGit()
        {
            this.Enlistment.UnmountRGFS();

            RGFSHelpers.SaveDiskLayoutVersion(this.Enlistment.DotRGFSRoot, "10");

            this.Enlistment.MountRGFS();

            RGFSHelpers.GetPersistedDiskLayoutVersion(this.Enlistment.DotRGFSRoot)
                .ShouldBeAnInt("Disk layout version should always be an int")
                .ShouldEqual(CurrentDiskLayoutVersion);
        }

        [TestCase]
        public void MountFailsToUpgradeFromEsentVersion6ToJsonRepoMetadata()
        {
            this.Enlistment.UnmountRGFS();

            // Delete the existing repo metadata
            string versionJsonPath = Path.Combine(this.Enlistment.DotRGFSRoot, RGFSHelpers.RepoMetadataName);
            versionJsonPath.ShouldBeAFile(this.fileSystem);
            this.fileSystem.DeleteFile(versionJsonPath);

            RGFSHelpers.SaveDiskLayoutVersionAsEsentDatabase(this.Enlistment.DotRGFSRoot, "6");
            string esentDatabasePath = Path.Combine(this.Enlistment.DotRGFSRoot, RGFSHelpers.EsentRepoMetadataFolder);
            esentDatabasePath.ShouldBeADirectory(this.fileSystem);

            this.Enlistment.TryMountRGFS().ShouldEqual(false, "Should not be able to upgrade from version 6");
            
            esentDatabasePath.ShouldBeADirectory(this.fileSystem);
        }

        private void RunEsentRepoMetadataUpgradeTest(string sourceVersion)
        {
            this.Enlistment.UnmountRGFS();

            // Delete the existing repo metadata
            string versionJsonPath = Path.Combine(this.Enlistment.DotRGFSRoot, RGFSHelpers.RepoMetadataName);
            versionJsonPath.ShouldBeAFile(this.fileSystem);
            this.fileSystem.DeleteFile(versionJsonPath);

            RGFSHelpers.SaveDiskLayoutVersionAsEsentDatabase(this.Enlistment.DotRGFSRoot, sourceVersion);
            string esentDatabasePath = Path.Combine(this.Enlistment.DotRGFSRoot, RGFSHelpers.EsentRepoMetadataFolder);
            esentDatabasePath.ShouldBeADirectory(this.fileSystem);

            // We should be able to mount, and there should no longer be any Esent Repo Metadata
            this.Enlistment.MountRGFS();
            esentDatabasePath.ShouldNotExistOnDisk(this.fileSystem);
            versionJsonPath.ShouldBeAFile(this.fileSystem);

            RGFSHelpers.GetPersistedDiskLayoutVersion(this.Enlistment.DotRGFSRoot)
                .ShouldBeAnInt("Disk layout version should always be an int")
                .ShouldEqual(CurrentDiskLayoutVersion, "Disk layout version should be upgraded to the latest");
        }
    }
}
