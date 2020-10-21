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
        protected static readonly string PlaceholderListDatabaseContent = $@"A .gitignore{GVFSHelpers.PlaceholderFieldDelimiter}E9630E4CF715315FC90D4AEC98E16A7398F8BF64
A Readme.md{GVFSHelpers.PlaceholderFieldDelimiter}583F1A56DB7CC884D54534C5D9C56B93A1E00A2B
A Scripts{GVFSHelpers.PlaceholderFieldDelimiter}{TestConstants.PartialFolderPlaceholderDatabaseValue}
A Scripts{Path.DirectorySeparatorChar}RunUnitTests.bat{GVFSHelpers.PlaceholderFieldDelimiter}0112E0DD6FC64BF57C4735F4D7D6E018C0F34B6D
A GVFS{GVFSHelpers.PlaceholderFieldDelimiter}{TestConstants.PartialFolderPlaceholderDatabaseValue}
A GVFS{Path.DirectorySeparatorChar}GVFS.Common{GVFSHelpers.PlaceholderFieldDelimiter}{TestConstants.PartialFolderPlaceholderDatabaseValue}
A GVFS{Path.DirectorySeparatorChar}GVFS.Common{Path.DirectorySeparatorChar}Git{GVFSHelpers.PlaceholderFieldDelimiter}{TestConstants.PartialFolderPlaceholderDatabaseValue}
A GVFS{Path.DirectorySeparatorChar}GVFS.Common{Path.DirectorySeparatorChar}Git{Path.DirectorySeparatorChar}GitRefs.cs{GVFSHelpers.PlaceholderFieldDelimiter}37595A9C6C7E00A8AFDE306765896770F2508927
A GVFS{Path.DirectorySeparatorChar}GVFS.Tests{GVFSHelpers.PlaceholderFieldDelimiter}{TestConstants.PartialFolderPlaceholderDatabaseValue}
A GVFS{Path.DirectorySeparatorChar}GVFS.Tests{Path.DirectorySeparatorChar}Properties{GVFSHelpers.PlaceholderFieldDelimiter}{TestConstants.PartialFolderPlaceholderDatabaseValue}
A GVFS{Path.DirectorySeparatorChar}GVFS.Tests{Path.DirectorySeparatorChar}Properties{Path.DirectorySeparatorChar}AssemblyInfo.cs{GVFSHelpers.PlaceholderFieldDelimiter}5911485CFE87E880F64B300BA5A289498622DBC1
D GVFS{Path.DirectorySeparatorChar}GVFS.Tests{Path.DirectorySeparatorChar}Properties{Path.DirectorySeparatorChar}AssemblyInfo.cs
";

        protected FileSystemRunner fileSystem = new SystemIORunner();

        private const string PlaceholderTableFilePathType = "0";
        private const string PlaceholderTablePartialFolderPathType = "1";

        public abstract int GetCurrentDiskLayoutMajorVersion();
        public abstract int GetCurrentDiskLayoutMinorVersion();

        protected void PlaceholderDatabaseShouldIncludeCommonLines(string[] placeholderLines)
        {
            placeholderLines.ShouldContain(x => x.Contains(this.FilePlaceholderString("Readme.md")));
            placeholderLines.ShouldContain(x => x.Contains(this.FilePlaceholderString("Scripts", "RunUnitTests.bat")));
            placeholderLines.ShouldContain(x => x.Contains(this.FilePlaceholderString("GVFS", "GVFS.Common", "Git", "GitRefs.cs")));
            placeholderLines.ShouldContain(x => x.Contains(this.FilePlaceholderString(".gitignore")));
            placeholderLines.ShouldContain(x => x == this.PartialFolderPlaceholderString("Scripts"));
            placeholderLines.ShouldContain(x => x == this.PartialFolderPlaceholderString("GVFS"));
            placeholderLines.ShouldContain(x => x == this.PartialFolderPlaceholderString("GVFS", "GVFS.Common"));
            placeholderLines.ShouldContain(x => x == this.PartialFolderPlaceholderString("GVFS", "GVFS.Common", "Git"));
            placeholderLines.ShouldContain(x => x == this.PartialFolderPlaceholderString("GVFS", "GVFS.Tests"));
        }

        protected void WriteOldPlaceholderListDatabase()
        {
            this.fileSystem.WriteAllText(Path.Combine(this.Enlistment.DotGVFSRoot, GVFSHelpers.PlaceholderListFile), PlaceholderListDatabaseContent);
        }

        protected void PerformIOBeforePlaceholderDatabaseUpgradeTest()
        {
            // Create some placeholder data
            this.fileSystem.ReadAllText(Path.Combine(this.Enlistment.RepoRoot, "Readme.md"));
            this.fileSystem.ReadAllText(Path.Combine(this.Enlistment.RepoRoot, "Scripts", "RunUnitTests.bat"));
            this.fileSystem.ReadAllText(Path.Combine(this.Enlistment.RepoRoot, "GVFS", "GVFS.Common", "Git", "GitRefs.cs"));

            // Create a full folder
            this.fileSystem.CreateDirectory(Path.Combine(this.Enlistment.RepoRoot, "GVFS", "FullFolder"));
            this.fileSystem.WriteAllText(Path.Combine(this.Enlistment.RepoRoot, "GVFS", "FullFolder", "test.txt"), "Test contents");

            // Create a tombstone
            this.fileSystem.DeleteDirectory(Path.Combine(this.Enlistment.RepoRoot, "GVFS", "GVFS.Tests", "Properties"));

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

        protected string FilePlaceholderString(params string[] pathParts)
        {
            return $"{Path.Combine(pathParts)}{GVFSHelpers.PlaceholderFieldDelimiter}{PlaceholderTableFilePathType}{GVFSHelpers.PlaceholderFieldDelimiter}";
        }

        protected string PartialFolderPlaceholderString(params string[] pathParts)
        {
            return $"{Path.Combine(pathParts)}{GVFSHelpers.PlaceholderFieldDelimiter}{PlaceholderTablePartialFolderPathType}{GVFSHelpers.PlaceholderFieldDelimiter}{TestConstants.PartialFolderPlaceholderDatabaseValue}{GVFSHelpers.PlaceholderFieldDelimiter}";
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
