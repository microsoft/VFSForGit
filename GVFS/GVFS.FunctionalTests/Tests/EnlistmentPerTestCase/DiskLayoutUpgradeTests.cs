using GVFS.FunctionalTests.FileSystemRunners;
using GVFS.FunctionalTests.Should;
using GVFS.FunctionalTests.Tools;
using GVFS.Tests.Should;
using System;
using System.IO;

namespace GVFS.FunctionalTests.Tests.EnlistmentPerTestCase
{
    public abstract class DiskLayoutUpgradeTests : TestsWithEnlistmentPerTestCase
    {
        protected const string PlaceholderListDatabaseDelimiter = "\0";

        protected static readonly string PlaceholderListDatabaseContent = $@"A .gitignore{PlaceholderListDatabaseDelimiter}E9630E4CF715315FC90D4AEC98E16A7398F8BF64
A Readme.md{PlaceholderListDatabaseDelimiter}583F1A56DB7CC884D54534C5D9C56B93A1E00A2B
A Scripts{PlaceholderListDatabaseDelimiter}{TestConstants.PartialFolderPlaceholderDatabaseValue}
A Scripts\RunUnitTests.bat{PlaceholderListDatabaseDelimiter}0112E0DD6FC64BF57C4735F4D7D6E018C0F34B6D
A GVFS{PlaceholderListDatabaseDelimiter}{TestConstants.PartialFolderPlaceholderDatabaseValue}
A GVFS\GVFS.Common{PlaceholderListDatabaseDelimiter}{TestConstants.PartialFolderPlaceholderDatabaseValue}
A GVFS\GVFS.Common\Git{PlaceholderListDatabaseDelimiter}{TestConstants.PartialFolderPlaceholderDatabaseValue}
A GVFS\GVFS.Common\Git\GitRefs.cs{PlaceholderListDatabaseDelimiter}37595A9C6C7E00A8AFDE306765896770F2508927
A GVFS\GVFS.Tests{PlaceholderListDatabaseDelimiter}{TestConstants.PartialFolderPlaceholderDatabaseValue}
A GVFS\GVFS.Tests\Properties{PlaceholderListDatabaseDelimiter}{TestConstants.PartialFolderPlaceholderDatabaseValue}
A GVFS\GVFS.Tests\Properties\AssemblyInfo.cs{PlaceholderListDatabaseDelimiter}5911485CFE87E880F64B300BA5A289498622DBC1
D GVFS\GVFS.Tests\Properties\AssemblyInfo.cs
";

        protected FileSystemRunner fileSystem = new SystemIORunner();

        public abstract int GetCurrentDiskLayoutMajorVersion();
        public abstract int GetCurrentDiskLayoutMinorVersion();

        protected void PlaceholderDatabaseShouldIncludeCommonLines(string[] placeholderLines)
        {
            placeholderLines.ShouldContain(x => x.Contains(this.FilePlaceholderString("Readme.md")));
            placeholderLines.ShouldContain(x => x.Contains(this.FilePlaceholderString("Scripts\\RunUnitTests.bat")));
            placeholderLines.ShouldContain(x => x.Contains(this.FilePlaceholderString("GVFS\\GVFS.Common\\Git\\GitRefs.cs")));
            placeholderLines.ShouldContain(x => x.Contains(this.FilePlaceholderString(".gitignore")));
            placeholderLines.ShouldContain(x => x == this.PartialFolderPlaceholderString("Scripts"));
            placeholderLines.ShouldContain(x => x == this.PartialFolderPlaceholderString("GVFS"));
            placeholderLines.ShouldContain(x => x == this.PartialFolderPlaceholderString("GVFS\\GVFS.Common"));
            placeholderLines.ShouldContain(x => x == this.PartialFolderPlaceholderString("GVFS\\GVFS.Common\\Git"));
            placeholderLines.ShouldContain(x => x == this.PartialFolderPlaceholderString("GVFS\\GVFS.Tests"));
        }

        protected void WriteOldPlaceholderListDatabase()
        {
            this.fileSystem.WriteAllText(Path.Combine(this.Enlistment.DotGVFSRoot, GVFSHelpers.PlaceholderListFile), PlaceholderListDatabaseContent);
        }

        protected void PerformIOBeforePlaceholderDatabaseUpgradeTest()
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

        protected void PlaceholderDatabaseShouldIncludeCommonLinesForUpgradeTestIO(string[] placeholderLines)
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

        protected string[] GetPlaceholderDatabaseLinesBeforeUpgrade(string placeholderDatabasePath)
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

        protected string FilePlaceholderString(string path)
        {
            return $"{path}{GVFSHelpers.PlaceholderFieldDelimiter}0{GVFSHelpers.PlaceholderFieldDelimiter}";
        }

        protected string PartialFolderPlaceholderString(string path)
        {
            return $"{path}{GVFSHelpers.PlaceholderFieldDelimiter}1{GVFSHelpers.PlaceholderFieldDelimiter}";
        }

        protected void ValidatePersistedVersionMatchesCurrentVersion()
        {
            string majorVersion;
            string minorVersion;
            GVFSHelpers.GetPersistedDiskLayoutVersion(this.Enlistment.DotGVFSRoot, out majorVersion, out minorVersion);

            majorVersion
                .ShouldBeAnInt("Disk layout version should always be an int")
                .ShouldEqual(this.GetCurrentDiskLayoutMajorVersion(), "Disk layout version should be upgraded to the latest");

            minorVersion
                .ShouldBeAnInt("Disk layout version should always be an int")
                .ShouldEqual(this.GetCurrentDiskLayoutMinorVersion(), "Disk layout version should be upgraded to the latest");
        }
    }
}
