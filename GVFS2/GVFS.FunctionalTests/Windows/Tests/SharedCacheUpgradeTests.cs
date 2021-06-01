using GVFS.FunctionalTests.FileSystemRunners;
using GVFS.FunctionalTests.Should;
using GVFS.FunctionalTests.Tests.MultiEnlistmentTests;
using GVFS.FunctionalTests.Tools;
using GVFS.FunctionalTests.Windows.Tests;
using GVFS.FunctionalTests.Windows.Tools;
using GVFS.Tests.Should;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.IO;

namespace GVFS.FunctionalTests.Windows.Windows.Tests
{
    [TestFixture]
    [Category(Categories.ExtraCoverage)]
    [Category(Categories.WindowsOnly)]
    public class SharedCacheUpgradeTests : TestsWithMultiEnlistment
    {
        private string localCachePath;
        private string localCacheParentPath;

        private FileSystemRunner fileSystem;

        public SharedCacheUpgradeTests()
        {
            this.fileSystem = new SystemIORunner();
        }

        [SetUp]
        public void SetCacheLocation()
        {
            this.localCacheParentPath = Path.Combine(Properties.Settings.Default.EnlistmentRoot, "..", Guid.NewGuid().ToString("N"));
            this.localCachePath = Path.Combine(this.localCacheParentPath, ".customGVFSCache");
        }

        [TestCase]
        public void MountUpgradesLocalSizesToSharedCache()
        {
            GVFSFunctionalTestEnlistment enlistment = this.CloneAndMountEnlistment();
            enlistment.UnmountGVFS();

            string localCacheRoot = GVFSHelpers.GetPersistedLocalCacheRoot(enlistment.DotGVFSRoot);
            string gitObjectsRoot = GVFSHelpers.GetPersistedGitObjectsRoot(enlistment.DotGVFSRoot);

            // Delete the existing repo metadata
            string versionJsonPath = Path.Combine(enlistment.DotGVFSRoot, GVFSHelpers.RepoMetadataName);
            versionJsonPath.ShouldBeAFile(this.fileSystem);
            this.fileSystem.DeleteFile(versionJsonPath);

            // Since there isn't a sparse-checkout file that is used anymore one needs to be added
            // in order to test the old upgrades that might have needed it
            string sparseCheckoutPath = Path.Combine(enlistment.RepoRoot, TestConstants.DotGit.Info.SparseCheckoutPath);
            this.fileSystem.WriteAllText(sparseCheckoutPath, "/.gitattributes\r\n");

            // "13.0" was the last version before blob sizes were moved out of Esent
            string metadataPath = Path.Combine(enlistment.DotGVFSRoot, GVFSHelpers.RepoMetadataName);
            this.fileSystem.CreateEmptyFile(metadataPath);
            GVFSHelpers.SaveDiskLayoutVersion(enlistment.DotGVFSRoot, "13", "0");
            GVFSHelpers.SaveLocalCacheRoot(enlistment.DotGVFSRoot, localCacheRoot);
            GVFSHelpers.SaveGitObjectsRoot(enlistment.DotGVFSRoot, gitObjectsRoot);

            // Create a legacy PersistedDictionary sizes database
            List<KeyValuePair<string, long>> entries = new List<KeyValuePair<string, long>>()
            {
                new KeyValuePair<string, long>(new string('0', 40), 1),
                new KeyValuePair<string, long>(new string('1', 40), 2),
                new KeyValuePair<string, long>(new string('2', 40), 4),
                new KeyValuePair<string, long>(new string('3', 40), 8),
            };

            ESENTDatabase.CreateEsentBlobSizesDatabase(enlistment.DotGVFSRoot, entries);

            enlistment.MountGVFS();

            string majorVersion;
            string minorVersion;
            GVFSHelpers.GetPersistedDiskLayoutVersion(enlistment.DotGVFSRoot, out majorVersion, out minorVersion);

            majorVersion
                .ShouldBeAnInt("Disk layout version should always be an int")
                .ShouldEqual(WindowsDiskLayoutUpgradeTests.CurrentDiskLayoutMajorVersion, "Disk layout version should be upgraded to the latest");

            minorVersion
                .ShouldBeAnInt("Disk layout version should always be an int")
                .ShouldEqual(WindowsDiskLayoutUpgradeTests.CurrentDiskLayoutMinorVersion, "Disk layout version should be upgraded to the latest");

            string newBlobSizesRoot = Path.Combine(Path.GetDirectoryName(gitObjectsRoot), WindowsDiskLayoutUpgradeTests.BlobSizesCacheName);
            GVFSHelpers.GetPersistedBlobSizesRoot(enlistment.DotGVFSRoot)
                .ShouldEqual(newBlobSizesRoot);

            string blobSizesDbPath = Path.Combine(newBlobSizesRoot, WindowsDiskLayoutUpgradeTests.BlobSizesDBFileName);
            newBlobSizesRoot.ShouldBeADirectory(this.fileSystem);
            blobSizesDbPath.ShouldBeAFile(this.fileSystem);

            foreach (KeyValuePair<string, long> entry in entries)
            {
                GVFSHelpers.SQLiteBlobSizesDatabaseHasEntry(blobSizesDbPath, entry.Key, entry.Value);
            }

            // Upgrade a second repo, and make sure all sizes from both upgrades are in the shared database

            GVFSFunctionalTestEnlistment enlistment2 = this.CloneAndMountEnlistment();
            enlistment2.UnmountGVFS();

            // Delete the existing repo metadata
            versionJsonPath = Path.Combine(enlistment2.DotGVFSRoot, GVFSHelpers.RepoMetadataName);
            versionJsonPath.ShouldBeAFile(this.fileSystem);
            this.fileSystem.DeleteFile(versionJsonPath);

            // Since there isn't a sparse-checkout file that is used anymore one needs to be added
            // in order to test the old upgrades that might have needed it
            string sparseCheckoutPath2 = Path.Combine(enlistment2.RepoRoot, TestConstants.DotGit.Info.SparseCheckoutPath);
            this.fileSystem.WriteAllText(sparseCheckoutPath2, "/.gitattributes\r\n");

            // "13.0" was the last version before blob sizes were moved out of Esent
            metadataPath = Path.Combine(enlistment2.DotGVFSRoot, GVFSHelpers.RepoMetadataName);
            this.fileSystem.CreateEmptyFile(metadataPath);
            GVFSHelpers.SaveDiskLayoutVersion(enlistment2.DotGVFSRoot, "13", "0");
            GVFSHelpers.SaveLocalCacheRoot(enlistment2.DotGVFSRoot, localCacheRoot);
            GVFSHelpers.SaveGitObjectsRoot(enlistment2.DotGVFSRoot, gitObjectsRoot);

            // Create a legacy PersistedDictionary sizes database
            List<KeyValuePair<string, long>> additionalEntries = new List<KeyValuePair<string, long>>()
            {
                new KeyValuePair<string, long>(new string('4', 40), 16),
                new KeyValuePair<string, long>(new string('5', 40), 32),
                new KeyValuePair<string, long>(new string('6', 40), 64),
            };

            ESENTDatabase.CreateEsentBlobSizesDatabase(enlistment2.DotGVFSRoot, additionalEntries);

            enlistment2.MountGVFS();

            foreach (KeyValuePair<string, long> entry in entries)
            {
                GVFSHelpers.SQLiteBlobSizesDatabaseHasEntry(blobSizesDbPath, entry.Key, entry.Value);
            }

            foreach (KeyValuePair<string, long> entry in additionalEntries)
            {
                GVFSHelpers.SQLiteBlobSizesDatabaseHasEntry(blobSizesDbPath, entry.Key, entry.Value);
            }
        }

        private GVFSFunctionalTestEnlistment CloneAndMountEnlistment(string branch = null)
        {
            return this.CreateNewEnlistment(this.localCachePath, branch);
        }
    }
}
