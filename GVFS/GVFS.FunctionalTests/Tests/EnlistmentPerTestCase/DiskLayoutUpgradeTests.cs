using GVFS.FunctionalTests.FileSystemRunners;
using GVFS.FunctionalTests.Should;
using GVFS.FunctionalTests.Tools;
using GVFS.Tests.Should;
using NUnit.Framework;
using System.IO;

namespace GVFS.FunctionalTests.Tests.EnlistmentPerTestCase
{
    [TestFixture]
    public class DiskLayoutUpgradeTests : TestsWithEnlistmentPerTestCase
    {
        private const int CurrentDiskLayoutVersion = 12;
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
            this.Enlistment.UnmountGVFS();

            // Delete the existing background ops data
            string flatBackgroundPath = Path.Combine(this.Enlistment.DotGVFSRoot, GVFSHelpers.BackgroundOpsFile);
            flatBackgroundPath.ShouldBeAFile(this.fileSystem);
            this.fileSystem.DeleteFile(flatBackgroundPath);
            
            // Delete the existing placeholder data
            string flatPlaceholdersPath = Path.Combine(this.Enlistment.DotGVFSRoot, GVFSHelpers.PlaceholderListFile);
            flatPlaceholdersPath.ShouldBeAFile(this.fileSystem);
            this.fileSystem.DeleteFile(flatPlaceholdersPath);

            GVFSHelpers.CreateEsentBackgroundOpsDatabase(this.Enlistment.DotGVFSRoot);
            GVFSHelpers.CreateEsentPlaceholderDatabase(this.Enlistment.DotGVFSRoot);

            // Nine is the last version with ESENT BackgroundOps and Placeholders DBs
            GVFSHelpers.SaveDiskLayoutVersion(this.Enlistment.DotGVFSRoot, "9");
            this.Enlistment.MountGVFS();

            GVFSHelpers.GetPersistedDiskLayoutVersion(this.Enlistment.DotGVFSRoot)
                .ShouldBeAnInt("Disk layout version should always be an int")
                .ShouldEqual(CurrentDiskLayoutVersion);

            flatBackgroundPath.ShouldBeAFile(this.fileSystem);
            flatPlaceholdersPath.ShouldBeAFile(this.fileSystem);
        }

        [TestCase]
        public void MountUpgradesFromPriorToPlaceholderCreationsBlockedForGit()
        {
            this.Enlistment.UnmountGVFS();

            GVFSHelpers.SaveDiskLayoutVersion(this.Enlistment.DotGVFSRoot, "10");

            this.Enlistment.MountGVFS();

            GVFSHelpers.GetPersistedDiskLayoutVersion(this.Enlistment.DotGVFSRoot)
                .ShouldBeAnInt("Disk layout version should always be an int")
                .ShouldEqual(CurrentDiskLayoutVersion);
        }

        [TestCase]
        public void MountFailsToUpgradeFromEsentVersion6ToJsonRepoMetadata()
        {
            this.Enlistment.UnmountGVFS();

            // Delete the existing repo metadata
            string versionJsonPath = Path.Combine(this.Enlistment.DotGVFSRoot, GVFSHelpers.RepoMetadataName);
            versionJsonPath.ShouldBeAFile(this.fileSystem);
            this.fileSystem.DeleteFile(versionJsonPath);

            GVFSHelpers.SaveDiskLayoutVersionAsEsentDatabase(this.Enlistment.DotGVFSRoot, "6");
            string esentDatabasePath = Path.Combine(this.Enlistment.DotGVFSRoot, GVFSHelpers.EsentRepoMetadataFolder);
            esentDatabasePath.ShouldBeADirectory(this.fileSystem);

            this.Enlistment.TryMountGVFS().ShouldEqual(false, "Should not be able to upgrade from version 6");
            
            esentDatabasePath.ShouldBeADirectory(this.fileSystem);
        }

        [TestCase]
        public void MountSetsGitObjectsRootToLegacyDotGVFSCache()
        {
            this.Enlistment.UnmountGVFS();

            // Delete the existing repo metadata
            string versionJsonPath = Path.Combine(this.Enlistment.DotGVFSRoot, GVFSHelpers.RepoMetadataName);
            versionJsonPath.ShouldBeAFile(this.fileSystem);
            this.fileSystem.DeleteFile(versionJsonPath);

            // "11" was the last version before the introduction of a volume wide GVFS cache
            string metadataPath = Path.Combine(this.Enlistment.DotGVFSRoot, GVFSHelpers.RepoMetadataName);
            this.fileSystem.CreateEmptyFile(metadataPath);
            GVFSHelpers.SaveDiskLayoutVersion(this.Enlistment.DotGVFSRoot, "11");

            // Create the legacy cache location: <root>\.gvfs\gitObjectCache
            string legacyGitObjectsCachePath = Path.Combine(this.Enlistment.DotGVFSRoot, "gitObjectCache");
            this.fileSystem.CreateDirectory(legacyGitObjectsCachePath);

            this.Enlistment.MountGVFS();

            GVFSHelpers.GetPersistedDiskLayoutVersion(this.Enlistment.DotGVFSRoot)
                .ShouldBeAnInt("Disk layout version should always be an int")
                .ShouldEqual(CurrentDiskLayoutVersion, "Disk layout version should be upgraded to the latest");

            GVFSHelpers.GetPersistedLocalCacheRoot(this.Enlistment.DotGVFSRoot)
                .ShouldEqual(string.Empty, "LocalCacheRoot should be an empty string when upgrading from a version prior to 12");

            GVFSHelpers.GetPersistedGitObjectsRoot(this.Enlistment.DotGVFSRoot)
                .ShouldEqual(legacyGitObjectsCachePath);
        }

        private void RunEsentRepoMetadataUpgradeTest(string sourceVersion)
        {
            this.Enlistment.UnmountGVFS();

            // Delete the existing repo metadata
            string versionJsonPath = Path.Combine(this.Enlistment.DotGVFSRoot, GVFSHelpers.RepoMetadataName);
            versionJsonPath.ShouldBeAFile(this.fileSystem);
            this.fileSystem.DeleteFile(versionJsonPath);

            GVFSHelpers.SaveDiskLayoutVersionAsEsentDatabase(this.Enlistment.DotGVFSRoot, sourceVersion);
            string esentDatabasePath = Path.Combine(this.Enlistment.DotGVFSRoot, GVFSHelpers.EsentRepoMetadataFolder);
            esentDatabasePath.ShouldBeADirectory(this.fileSystem);

            // We should be able to mount, and there should no longer be any Esent Repo Metadata
            this.Enlistment.MountGVFS();
            esentDatabasePath.ShouldNotExistOnDisk(this.fileSystem);
            versionJsonPath.ShouldBeAFile(this.fileSystem);

            GVFSHelpers.GetPersistedDiskLayoutVersion(this.Enlistment.DotGVFSRoot)
                .ShouldBeAnInt("Disk layout version should always be an int")
                .ShouldEqual(CurrentDiskLayoutVersion, "Disk layout version should be upgraded to the latest");

            GVFSHelpers.GetPersistedLocalCacheRoot(this.Enlistment.DotGVFSRoot)
                .ShouldEqual(string.Empty, "LocalCacheRoot should be an empty string when upgrading from a version prior to 12");

            // We're starting with fresh enlisments, and so the legacy cache location: <root>\.gvfs\gitObjectCache should not be on disk
            Path.Combine(this.Enlistment.DotGVFSRoot, @".gvfs\gitObjectCache").ShouldNotExistOnDisk(this.fileSystem);

            // The upgrader should set GitObjectsRoot to src\.git\objects (because the legacy cache location is not on disk)
            GVFSHelpers.GetPersistedGitObjectsRoot(this.Enlistment.DotGVFSRoot)
                .ShouldNotBeNull("GitObjectsRoot should not be null")
                .ShouldEqual(Path.Combine(this.Enlistment.RepoRoot, @".git\objects"));
        }
    }
}
