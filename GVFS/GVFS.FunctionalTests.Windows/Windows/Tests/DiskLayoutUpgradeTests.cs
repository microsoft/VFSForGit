using GVFS.FunctionalTests.FileSystemRunners;
using GVFS.FunctionalTests.Should;
using GVFS.FunctionalTests.Tests.EnlistmentPerTestCase;
using GVFS.FunctionalTests.Tools;
using GVFS.FunctionalTests.Windows.Tools;
using GVFS.Tests.Should;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace GVFS.FunctionalTests.Windows.Tests
{
    [TestFixture]
    [Category(Categories.FullSuiteOnly)]
    [Category(Categories.WindowsOnly)]
    public class DiskLayoutUpgradeTests : TestsWithEnlistmentPerTestCase
    {
        public const int CurrentDiskLayoutMajorVersion = 17;
        public const int CurrentDiskLayoutMinorVersion = 0;

        public const string BlobSizesCacheName = "blobSizes";
        public const string BlobSizesDBFileName = "BlobSizes.sql";

        private const string DatabasesFolderName = "databases";

        private FileSystemRunner fileSystem = new SystemIORunner();

        [SetUp]
        public override void CreateEnlistment()
        {
            base.CreateEnlistment();

            // Since there isn't a sparse-checkout file that is used anymore one needs to be added
            // in order to test the old upgrades that might have needed it
            string sparseCheckoutPath = Path.Combine(this.Enlistment.RepoRoot, TestConstants.DotGit.Info.SparseCheckoutPath);
            this.fileSystem.WriteAllText(sparseCheckoutPath, "/.gitattributes\r\n");
        }

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

            ESENTDatabase.CreateEsentBackgroundOpsDatabase(this.Enlistment.DotGVFSRoot);
            ESENTDatabase.CreateEsentPlaceholderDatabase(this.Enlistment.DotGVFSRoot);

            // Nine is the last version with ESENT BackgroundOps and Placeholders DBs
            GVFSHelpers.SaveDiskLayoutVersion(this.Enlistment.DotGVFSRoot, "9", "0");
            this.Enlistment.MountGVFS();

            this.ValidatePersistedVersionMatchesCurrentVersion();

            flatBackgroundPath.ShouldBeAFile(this.fileSystem);
            flatPlaceholdersPath.ShouldBeAFile(this.fileSystem);
        }

        [TestCase]
        public void MountUpgradesFromPriorToPlaceholderCreationsBlockedForGit()
        {
            this.Enlistment.UnmountGVFS();

            GVFSHelpers.SaveDiskLayoutVersion(this.Enlistment.DotGVFSRoot, "10", "0");

            this.Enlistment.MountGVFS();

            this.ValidatePersistedVersionMatchesCurrentVersion();
        }

        [TestCase]
        public void MountFailsToUpgradeFromEsentVersion6ToJsonRepoMetadata()
        {
            this.Enlistment.UnmountGVFS();

            // Delete the existing repo metadata
            string versionJsonPath = Path.Combine(this.Enlistment.DotGVFSRoot, GVFSHelpers.RepoMetadataName);
            versionJsonPath.ShouldBeAFile(this.fileSystem);
            this.fileSystem.DeleteFile(versionJsonPath);

            ESENTDatabase.SaveDiskLayoutVersionAsEsentDatabase(this.Enlistment.DotGVFSRoot, "6");
            string esentDatabasePath = Path.Combine(this.Enlistment.DotGVFSRoot, ESENTDatabase.EsentRepoMetadataFolder);
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
            GVFSHelpers.SaveDiskLayoutVersion(this.Enlistment.DotGVFSRoot, "11", "0");

            // Create the legacy cache location: <root>\.gvfs\gitObjectCache
            string legacyGitObjectsCachePath = Path.Combine(this.Enlistment.DotGVFSRoot, "gitObjectCache");
            this.fileSystem.CreateDirectory(legacyGitObjectsCachePath);

            this.Enlistment.MountGVFS();

            this.ValidatePersistedVersionMatchesCurrentVersion();

            GVFSHelpers.GetPersistedLocalCacheRoot(this.Enlistment.DotGVFSRoot)
                .ShouldEqual(string.Empty, "LocalCacheRoot should be an empty string when upgrading from a version prior to 12");

            GVFSHelpers.GetPersistedGitObjectsRoot(this.Enlistment.DotGVFSRoot)
                .ShouldEqual(legacyGitObjectsCachePath);
        }

        [TestCase]
        public void MountSucceedsIfMinorVersionHasAdvancedButNotMajorVersion()
        {
            // Advance the minor version, mount should still work
            this.Enlistment.UnmountGVFS();
            GVFSHelpers.SaveDiskLayoutVersion(
                this.Enlistment.DotGVFSRoot,
                CurrentDiskLayoutMajorVersion.ToString(),
                (CurrentDiskLayoutMinorVersion + 1).ToString());
            this.Enlistment.TryMountGVFS().ShouldBeTrue("Mount should succeed because only the minor version advanced");

            // Advance the major version, mount should fail
            this.Enlistment.UnmountGVFS();
            GVFSHelpers.SaveDiskLayoutVersion(
                this.Enlistment.DotGVFSRoot,
                (CurrentDiskLayoutMajorVersion + 1).ToString(),
                CurrentDiskLayoutMinorVersion.ToString());
            this.Enlistment.TryMountGVFS().ShouldBeFalse("Mount should fail because the major version has advanced");
        }

        [TestCase]
        public void MountWritesFolderPlaceholdersToPlaceholderDatabase()
        {
            this.PerformIOBeforePlaceholderDatabaseUpgradeTest();

            this.Enlistment.UnmountGVFS();

            // Delete the existing folder placeholder data
            string placeholderDatabasePath = Path.Combine(this.Enlistment.DotGVFSRoot, GVFSHelpers.PlaceholderListFile);
            string[] lines = this.GetPlaceholderDatabaseLinesBeforeUpgrade(placeholderDatabasePath);

            // Placeholder database file should only have file placeholders
            this.fileSystem.WriteAllText(
                placeholderDatabasePath, 
                string.Join(Environment.NewLine, lines.Where(x => !x.EndsWith(TestConstants.PartialFolderPlaceholderDatabaseValue))) + Environment.NewLine);

            GVFSHelpers.SaveDiskLayoutVersion(this.Enlistment.DotGVFSRoot, "12", "1");

            this.Enlistment.MountGVFS();
            this.Enlistment.UnmountGVFS();

            // Validate the folder placeholders are in the placeholder database now
            this.GetPlaceholderDatabaseLinesAfterUpgradeFrom12_1(placeholderDatabasePath);

            this.ValidatePersistedVersionMatchesCurrentVersion();
        }

        [TestCase]
        public void MountUpdatesAllZeroShaFolderPlaceholderEntriesToPartialFolderSpecialValue()
        {
            this.PerformIOBeforePlaceholderDatabaseUpgradeTest();

            this.Enlistment.UnmountGVFS();

            // Delete the existing folder placeholder data
            string placeholderDatabasePath = Path.Combine(this.Enlistment.DotGVFSRoot, GVFSHelpers.PlaceholderListFile);
            string[] lines = this.GetPlaceholderDatabaseLinesBeforeUpgrade(placeholderDatabasePath);

            // Update the placeholder file so that folders have an all zero SHA
            this.fileSystem.WriteAllText(
                placeholderDatabasePath, 
                string.Join(
                    Environment.NewLine, 
                    lines.Select(x => x.Replace(TestConstants.PartialFolderPlaceholderDatabaseValue, TestConstants.AllZeroSha))) + Environment.NewLine);

            GVFSHelpers.SaveDiskLayoutVersion(this.Enlistment.DotGVFSRoot, "16", "0");

            this.Enlistment.MountGVFS();
            this.Enlistment.UnmountGVFS();

            // Validate the folder placeholders in the database have PartialFolderPlaceholderDatabaseValue values
            this.GetPlaceholderDatabaseLinesAfterUpgradeFrom16(placeholderDatabasePath);

            this.ValidatePersistedVersionMatchesCurrentVersion();
        }

        [TestCase]
        public void MountUpgradesPreSharedCacheLocalSizes()
        {
            this.Enlistment.UnmountGVFS();

            // Delete the existing repo metadata
            string versionJsonPath = Path.Combine(this.Enlistment.DotGVFSRoot, GVFSHelpers.RepoMetadataName);
            versionJsonPath.ShouldBeAFile(this.fileSystem);
            this.fileSystem.DeleteFile(versionJsonPath);

            // "11" was the last version before the introduction of a volume wide GVFS cache
            string metadataPath = Path.Combine(this.Enlistment.DotGVFSRoot, GVFSHelpers.RepoMetadataName);
            this.fileSystem.CreateEmptyFile(metadataPath);
            GVFSHelpers.SaveDiskLayoutVersion(this.Enlistment.DotGVFSRoot, "11", "0");

            // Create the legacy cache location: <root>\.gvfs\gitObjectCache
            string legacyGitObjectsCachePath = Path.Combine(this.Enlistment.DotGVFSRoot, "gitObjectCache");
            this.fileSystem.CreateDirectory(legacyGitObjectsCachePath);

            // Create a legacy PersistedDictionary sizes database
            List<KeyValuePair<string, long>> entries = new List<KeyValuePair<string, long>>()
            {
                new KeyValuePair<string, long>(new string('0', 40), 1),
                new KeyValuePair<string, long>(new string('1', 40), 2),
                new KeyValuePair<string, long>(new string('2', 40), 4),
                new KeyValuePair<string, long>(new string('3', 40), 8),
            };

            ESENTDatabase.CreateEsentBlobSizesDatabase(this.Enlistment.DotGVFSRoot, entries);

            this.Enlistment.MountGVFS();

            this.ValidatePersistedVersionMatchesCurrentVersion();

            GVFSHelpers.GetPersistedLocalCacheRoot(this.Enlistment.DotGVFSRoot)
                .ShouldEqual(string.Empty, "LocalCacheRoot should be an empty string when upgrading from a version prior to 12");

            GVFSHelpers.GetPersistedGitObjectsRoot(this.Enlistment.DotGVFSRoot)
                .ShouldEqual(legacyGitObjectsCachePath);

            string newBlobSizesRoot = Path.Combine(this.Enlistment.DotGVFSRoot, DatabasesFolderName, BlobSizesCacheName);
            GVFSHelpers.GetPersistedBlobSizesRoot(this.Enlistment.DotGVFSRoot)
                .ShouldEqual(newBlobSizesRoot);

            string blobSizesDbPath = Path.Combine(newBlobSizesRoot, BlobSizesDBFileName);
            newBlobSizesRoot.ShouldBeADirectory(this.fileSystem);
            blobSizesDbPath.ShouldBeAFile(this.fileSystem);

            foreach (KeyValuePair<string, long> entry in entries)
            {
                GVFSHelpers.SQLiteBlobSizesDatabaseHasEntry(blobSizesDbPath, entry.Key, entry.Value);
            }
        }

        [TestCase]
        public void MountCreatesModifiedPathsDatabase()
        {
            this.Enlistment.UnmountGVFS();
            GVFSHelpers.SaveDiskLayoutVersion(this.Enlistment.DotGVFSRoot, "14", "0");

            // Delete the existing modified paths database to make sure mount creates it.
            string modifiedPathsDatabasePath = Path.Combine(this.Enlistment.DotGVFSRoot, TestConstants.Databases.ModifiedPaths);
            this.fileSystem.DeleteFile(modifiedPathsDatabasePath);

            // Overwrite the sparse-checkout with entries to test
            string sparseCheckoutPath = Path.Combine(this.Enlistment.RepoRoot, TestConstants.DotGit.Info.SparseCheckoutPath);
            string sparseCheckoutContent = @"/.gitattributes
/developer/me/
/developer/me/JLANGE9._prerazzle
/developer/me/StateSwitch.Save
/tools/x86/remote.exe
/tools/x86/runelevated.exe
/tools/amd64/remote.exe
/tools/amd64/runelevated.exe
/tools/perllib/MS/TraceLogging.dll
/tools/managed/v2.0/midldd.CheckedInExe
/tools/managed/v4.0/sdapi.dll
/tools/managed/v2.0/midlpars.dll
/tools/managed/v2.0/RPCDataSupport.dll
";
            this.fileSystem.WriteAllText(sparseCheckoutPath, sparseCheckoutContent);

            // Overwrite the always_exclude file with entries to test
            string alwaysExcludePath = Path.Combine(this.Enlistment.RepoRoot, TestConstants.DotGit.Info.AlwaysExcludePath);
            string alwaysExcludeContent = @"*
!/developer
!/developer/*
!/developer/me
!/developer/me/*
!/tools
!/tools/x86
!/tools/x86/*
!/tools/amd64
!/tools/amd64/*
!/tools/perllib/
!/tools/perllib/MS/
!/tools/perllib/MS/Somefile.txt
!/tools/managed/
!/tools/managed/v2.0/
!/tools/managed/v2.0/MidlStaticAnalysis.dll
!/tools/managed/v2.0/RPCDataSupport.dll
";
            this.fileSystem.WriteAllText(alwaysExcludePath, alwaysExcludeContent);

            this.Enlistment.MountGVFS();
            this.Enlistment.UnmountGVFS();

            string[] expectedModifiedPaths =
                {
                    "A .gitattributes",
                    "A developer/me/",
                    "A tools/x86/remote.exe",
                    "A tools/x86/runelevated.exe",
                    "A tools/amd64/remote.exe",
                    "A tools/amd64/runelevated.exe",
                    "A tools/perllib/MS/TraceLogging.dll",
                    "A tools/managed/v2.0/midldd.CheckedInExe",
                    "A tools/managed/v4.0/sdapi.dll",
                    "A tools/managed/v2.0/midlpars.dll",
                    "A tools/managed/v2.0/RPCDataSupport.dll",
                    "A tools/managed/v2.0/MidlStaticAnalysis.dll",
                    "A tools/perllib/MS/Somefile.txt",
                };

            modifiedPathsDatabasePath.ShouldBeAFile(this.fileSystem);
            this.fileSystem.ReadAllText(modifiedPathsDatabasePath)
                .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .OrderBy(x => x)
                .ShouldMatchInOrder(expectedModifiedPaths.OrderBy(x => x));

            this.ValidatePersistedVersionMatchesCurrentVersion();
        }

        private void PerformIOBeforePlaceholderDatabaseUpgradeTest()
        {
            // Create some placeholder data
            this.fileSystem.ReadAllText(Path.Combine(this.Enlistment.RepoRoot, "Readme.md"));
            this.fileSystem.ReadAllText(Path.Combine(this.Enlistment.RepoRoot, "Scripts\\RunUnitTests.bat"));
            this.fileSystem.ReadAllText(Path.Combine(this.Enlistment.RepoRoot, "GVFS\\GVFS.Common\\Git\\GitRefs.cs"));

            // Create a full folder
            this.fileSystem.CreateDirectory(Path.Combine(this.Enlistment.RepoRoot, "GVFS\\FullFolder"));
            this.fileSystem.WriteAllText(Path.Combine(this.Enlistment.RepoRoot, "GVFS\\FullFolder\\test.txt"), "Test contents");

            // Create a tombstone
            this.fileSystem.DeleteDirectory(Path.Combine(this.Enlistment.RepoRoot, "GVFS\\GVFS.Tests\\Properties"));

            string junctionTarget = Path.Combine(this.Enlistment.EnlistmentRoot, "DirJunction");
            string symLinkTarget = Path.Combine(this.Enlistment.EnlistmentRoot, "DirSymLink");
            Directory.CreateDirectory(junctionTarget);
            Directory.CreateDirectory(symLinkTarget);

            string junctionLink = Path.Combine(this.Enlistment.RepoRoot, "DirJunction");
            string symLink = Path.Combine(this.Enlistment.RepoRoot, "DirLink");
            ProcessHelper.Run("CMD.exe", "/C mklink /J " + junctionLink + " " + junctionTarget);
            ProcessHelper.Run("CMD.exe", "/C mklink /D " + symLink + " " + symLinkTarget);

            string target = Path.Combine(this.Enlistment.EnlistmentRoot, "GVFS", "GVFS", "GVFS.UnitTests");
            string link = Path.Combine(this.Enlistment.RepoRoot, "UnitTests");
            ProcessHelper.Run("CMD.exe", "/C mklink /J " + link + " " + target);
            target = Path.Combine(this.Enlistment.EnlistmentRoot, "GVFS", "GVFS", "GVFS.Installer");
            link = Path.Combine(this.Enlistment.RepoRoot, "Installer");
            ProcessHelper.Run("CMD.exe", "/C mklink /D " + link + " " + target);
        }

        private void PlaceholderDatabaseShouldIncludeCommonLinesForUpgradeTestIO(string[] placeholderLines)
        {
            placeholderLines.ShouldContain(x => x.Contains("A Readme.md"));
            placeholderLines.ShouldContain(x => x.Contains("A Scripts\\RunUnitTests.bat"));
            placeholderLines.ShouldContain(x => x.Contains("A GVFS\\GVFS.Common\\Git\\GitRefs.cs"));
            placeholderLines.ShouldContain(x => x.Contains("A .gitignore"));
            placeholderLines.ShouldContain(x => x == "A Scripts\0" + TestConstants.PartialFolderPlaceholderDatabaseValue);
            placeholderLines.ShouldContain(x => x == "A GVFS\0" + TestConstants.PartialFolderPlaceholderDatabaseValue);
            placeholderLines.ShouldContain(x => x == "A GVFS\\GVFS.Common\0" + TestConstants.PartialFolderPlaceholderDatabaseValue);
            placeholderLines.ShouldContain(x => x == "A GVFS\\GVFS.Common\\Git\0" + TestConstants.PartialFolderPlaceholderDatabaseValue);
            placeholderLines.ShouldContain(x => x == "A GVFS\\GVFS.Tests\0" + TestConstants.PartialFolderPlaceholderDatabaseValue);
        }

        private string[] GetPlaceholderDatabaseLinesBeforeUpgrade(string placeholderDatabasePath)
        {
            placeholderDatabasePath.ShouldBeAFile(this.fileSystem);
            string[] lines = this.fileSystem.ReadAllText(placeholderDatabasePath).Split(new char[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            lines.Length.ShouldEqual(12);
            this.PlaceholderDatabaseShouldIncludeCommonLinesForUpgradeTestIO(lines);
            lines.ShouldContain(x => x.Contains("A GVFS\\GVFS.Tests\\Properties\\AssemblyInfo.cs"));
            lines.ShouldContain(x => x == "D GVFS\\GVFS.Tests\\Properties\\AssemblyInfo.cs");
            lines.ShouldContain(x => x == "A GVFS\\GVFS.Tests\\Properties\0" + TestConstants.PartialFolderPlaceholderDatabaseValue);
            return lines;
        }

        private string[] GetPlaceholderDatabaseLinesAfterUpgradeFrom12_1(string placeholderDatabasePath)
        {
            placeholderDatabasePath.ShouldBeAFile(this.fileSystem);
            string[] lines = this.fileSystem.ReadAllText(placeholderDatabasePath).Split(new char[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            lines.Length.ShouldEqual(9);
            this.PlaceholderDatabaseShouldIncludeCommonLinesForUpgradeTestIO(lines);
            return lines;
        }

        private string[] GetPlaceholderDatabaseLinesAfterUpgradeFrom16(string placeholderDatabasePath)
        {
            placeholderDatabasePath.ShouldBeAFile(this.fileSystem);
            string[] lines = this.fileSystem.ReadAllText(placeholderDatabasePath).Split(new char[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            lines.Length.ShouldEqual(10);
            this.PlaceholderDatabaseShouldIncludeCommonLinesForUpgradeTestIO(lines);
            lines.ShouldContain(x => x == "A GVFS\\GVFS.Tests\\Properties\0" + TestConstants.PartialFolderPlaceholderDatabaseValue);
            return lines;
        }

        private void RunEsentRepoMetadataUpgradeTest(string sourceVersion)
        {
            this.Enlistment.UnmountGVFS();

            // Delete the existing repo metadata
            string versionJsonPath = Path.Combine(this.Enlistment.DotGVFSRoot, GVFSHelpers.RepoMetadataName);
            versionJsonPath.ShouldBeAFile(this.fileSystem);
            this.fileSystem.DeleteFile(versionJsonPath);

            ESENTDatabase.SaveDiskLayoutVersionAsEsentDatabase(this.Enlistment.DotGVFSRoot, sourceVersion);
            string esentDatabasePath = Path.Combine(this.Enlistment.DotGVFSRoot, ESENTDatabase.EsentRepoMetadataFolder);
            esentDatabasePath.ShouldBeADirectory(this.fileSystem);

            // We should be able to mount, and there should no longer be any Esent Repo Metadata
            this.Enlistment.MountGVFS();
            esentDatabasePath.ShouldNotExistOnDisk(this.fileSystem);
            versionJsonPath.ShouldBeAFile(this.fileSystem);

            this.ValidatePersistedVersionMatchesCurrentVersion();

            GVFSHelpers.GetPersistedLocalCacheRoot(this.Enlistment.DotGVFSRoot)
                .ShouldEqual(string.Empty, "LocalCacheRoot should be an empty string when upgrading from a version prior to 12");

            // We're starting with fresh enlisments, and so the legacy cache location: <root>\.gvfs\gitObjectCache should not be on disk
            Path.Combine(this.Enlistment.DotGVFSRoot, @".gvfs\gitObjectCache").ShouldNotExistOnDisk(this.fileSystem);

            // The upgrader should set GitObjectsRoot to src\.git\objects (because the legacy cache location is not on disk)
            GVFSHelpers.GetPersistedGitObjectsRoot(this.Enlistment.DotGVFSRoot)
                .ShouldNotBeNull("GitObjectsRoot should not be null")
                .ShouldEqual(Path.Combine(this.Enlistment.RepoRoot, @".git\objects"));
        }

        private void ValidatePersistedVersionMatchesCurrentVersion()
        {
            string majorVersion;
            string minorVersion;
            GVFSHelpers.GetPersistedDiskLayoutVersion(this.Enlistment.DotGVFSRoot, out majorVersion, out minorVersion);

            majorVersion
                .ShouldBeAnInt("Disk layout version should always be an int")
                .ShouldEqual(CurrentDiskLayoutMajorVersion, "Disk layout version should be upgraded to the latest");

            minorVersion
                .ShouldBeAnInt("Disk layout version should always be an int")
                .ShouldEqual(CurrentDiskLayoutMinorVersion, "Disk layout version should be upgraded to the latest");
        }
    }
}
