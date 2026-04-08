using GVFS.FunctionalTests.Should;
using GVFS.FunctionalTests.Tests.EnlistmentPerTestCase;
using GVFS.FunctionalTests.Tools;
using GVFS.Tests.Should;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace GVFS.FunctionalTests.Windows.Tests
{
    [TestFixture]
    [Category(Categories.ExtraCoverage)]
    public class WindowsDiskLayoutUpgradeTests : DiskLayoutUpgradeTests
    {
        public const int CurrentDiskLayoutMajorVersion = 19;
        public const int CurrentDiskLayoutMinorVersion = 0;

        public const string BlobSizesCacheName = "blobSizes";
        public const string BlobSizesDBFileName = "BlobSizes.sql";

        private const string DatabasesFolderName = "databases";

        public override int GetCurrentDiskLayoutMajorVersion() => CurrentDiskLayoutMajorVersion;
        public override int GetCurrentDiskLayoutMinorVersion() => CurrentDiskLayoutMinorVersion;

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
        public void MountUpgradesFromMinimumSupportedVersion()
        {
            this.Enlistment.UnmountGVFS();

            GVFSHelpers.SaveDiskLayoutVersion(this.Enlistment.DotGVFSRoot, "14", "0");

            this.Enlistment.MountGVFS();

            this.ValidatePersistedVersionMatchesCurrentVersion();
        }

        [TestCase]
        public void MountWritesFolderPlaceholdersToPlaceholderDatabase()
        {
            this.PerformIOBeforePlaceholderDatabaseUpgradeTest();

            this.Enlistment.UnmountGVFS();

            this.fileSystem.DeleteFile(Path.Combine(this.Enlistment.DotGVFSRoot, TestConstants.Databases.VFSForGit));
            this.WriteOldPlaceholderListDatabase();

            // Get the existing folder placeholder data
            string placeholderDatabasePath = Path.Combine(this.Enlistment.DotGVFSRoot, GVFSHelpers.PlaceholderListFile);
            string[] lines = this.GetPlaceholderDatabaseLinesBeforeUpgrade(placeholderDatabasePath);

            // Placeholder database file should only have file placeholders
            this.fileSystem.WriteAllText(
                placeholderDatabasePath,
                string.Join(Environment.NewLine, lines.Where(x => !x.EndsWith(TestConstants.PartialFolderPlaceholderDatabaseValue))) + Environment.NewLine);

            GVFSHelpers.SaveDiskLayoutVersion(this.Enlistment.DotGVFSRoot, "15", "0");

            this.Enlistment.MountGVFS();
            this.Enlistment.UnmountGVFS();

            // Validate the folder placeholders are in the placeholder database now
            this.GetPlaceholderDatabaseLinesAfterUpgradeFrom12_1(Path.Combine(this.Enlistment.DotGVFSRoot, TestConstants.Databases.VFSForGit));

            this.ValidatePersistedVersionMatchesCurrentVersion();
        }

        [TestCase]
        public void MountUpdatesAllZeroShaFolderPlaceholderEntriesToPartialFolderSpecialValue()
        {
            this.PerformIOBeforePlaceholderDatabaseUpgradeTest();

            this.Enlistment.UnmountGVFS();
            this.WriteOldPlaceholderListDatabase();

            // Get the existing folder placeholder data
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
            this.GetPlaceholderDatabaseLinesAfterUpgradeFrom16(Path.Combine(this.Enlistment.DotGVFSRoot, TestConstants.Databases.VFSForGit));

            this.ValidatePersistedVersionMatchesCurrentVersion();
        }

        [TestCase]
        public void MountCreatesModifiedPathsDatabase()
        {
            this.Enlistment.UnmountGVFS();
            GVFSHelpers.SaveDiskLayoutVersion(this.Enlistment.DotGVFSRoot, "15", "0");

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
            string[] lines = GVFSHelpers.GetAllSQLitePlaceholdersAsString(placeholderDatabasePath).Split(new char[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            lines.Length.ShouldEqual(9);
            this.PlaceholderDatabaseShouldIncludeCommonLines(lines);
            return lines;
        }

        private string[] GetPlaceholderDatabaseLinesAfterUpgradeFrom16(string placeholderDatabasePath)
        {
            placeholderDatabasePath.ShouldBeAFile(this.fileSystem);
            string[] lines = GVFSHelpers.GetAllSQLitePlaceholdersAsString(placeholderDatabasePath).Split(new char[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            lines.Length.ShouldEqual(10);
            this.PlaceholderDatabaseShouldIncludeCommonLines(lines);
            lines.ShouldContain(x => x == this.PartialFolderPlaceholderString("GVFS", "GVFS.Tests", "Properties"));
            return lines;
        }
    }
}
