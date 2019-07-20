using GVFS.FunctionalTests.Should;
using GVFS.FunctionalTests.Tools;
using GVFS.Tests.Should;
using NUnit.Framework;
using System;
using System.IO;

namespace GVFS.FunctionalTests.Tests.EnlistmentPerTestCase
{
    [TestFixture]
    [Category(Categories.ExtraCoverage)]
    [Category(Categories.MacOnly)]
    public class MacDiskLayoutUpgradeTests : DiskLayoutUpgradeTests
    {
        public const int CurrentDiskLayoutMajorVersion = 19;
        public const int CurrentDiskLayoutMinorVersion = 0;

        public override int GetCurrentDiskLayoutMajorVersion() => CurrentDiskLayoutMajorVersion;
        public override int GetCurrentDiskLayoutMinorVersion() => CurrentDiskLayoutMinorVersion;

        [TestCase]
        public void MountUpgradesPlaceholderListDatabaseToSQLite()
        {
            this.Enlistment.UnmountGVFS();

            this.fileSystem.DeleteFile(Path.Combine(this.Enlistment.DotGVFSRoot, TestConstants.Databases.VFSForGit));
            this.WriteOldPlaceholderListDatabase();

            GVFSHelpers.SaveDiskLayoutVersion(this.Enlistment.DotGVFSRoot, "18", "0");

            this.Enlistment.MountGVFS();
            this.Enlistment.UnmountGVFS();

            // Validate the placeholders are in the SQLite placeholder database now
            string placeholderDatabasePath = Path.Combine(this.Enlistment.DotGVFSRoot, TestConstants.Databases.VFSForGit);
            placeholderDatabasePath.ShouldBeAFile(this.fileSystem);
            string[] lines = GVFSHelpers.GetAllSQLitePlaceholdersAsString(placeholderDatabasePath).Split(new char[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            lines.Length.ShouldEqual(10);
            this.PlaceholderDatabaseShouldIncludeCommonLines(lines);
            lines.ShouldContain(x => x == this.PartialFolderPlaceholderString("GVFS", "GVFS.Tests", "Properties"));

            this.ValidatePersistedVersionMatchesCurrentVersion();
        }
    }
}
