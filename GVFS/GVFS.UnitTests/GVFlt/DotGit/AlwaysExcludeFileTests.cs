using GVFS.GVFlt.DotGit;
using GVFS.Tests.Should;
using GVFS.UnitTests.Virtual;
using NUnit.Framework;
using System.Collections.Generic;
using System.IO;

namespace GVFS.UnitTests.GVFlt.DotGit
{
    [TestFixture]
    public class AlwaysExcludeFileTests : TestsWithCommonRepo
    {
        [TestCase]
        public void HasDefaultEntriesAfterLoad()
        {
            string alwaysExcludeFilePath = Path.Combine(this.Repo.GitParentPath, GVFS.Common.GVFSConstants.DotGit.Info.AlwaysExcludeName);
            AlwaysExcludeFile alwaysExcludeFile = new AlwaysExcludeFile(this.Repo.Context, alwaysExcludeFilePath);
            this.Repo.Context.FileSystem.FileExists(alwaysExcludeFilePath).ShouldEqual(false);
            alwaysExcludeFile.LoadOrCreate();
            this.Repo.Context.FileSystem.FileExists(alwaysExcludeFilePath).ShouldEqual(true);

            List<string> expectedContents = new List<string>() { "*" };
            this.CheckFileContents(alwaysExcludeFilePath, expectedContents);
        }

        [TestCase]
        public void WritesOnFolderChange()
        {
            string alwaysExcludeFilePath = Path.Combine(this.Repo.GitParentPath, GVFS.Common.GVFSConstants.DotGit.Info.AlwaysExcludeName);
            AlwaysExcludeFile alwaysExcludeFile = new AlwaysExcludeFile(this.Repo.Context, alwaysExcludeFilePath);
            this.Repo.Context.FileSystem.FileExists(alwaysExcludeFilePath).ShouldEqual(false);
            alwaysExcludeFile.LoadOrCreate();
            this.Repo.Context.FileSystem.FileExists(alwaysExcludeFilePath).ShouldEqual(true);

            alwaysExcludeFile.AddEntriesForFileOrFolder("A\\B\\C", isFolder: true);
            alwaysExcludeFile.AddEntriesForFileOrFolder("A\\D\\E", isFolder: true);
            alwaysExcludeFile.AddEntriesForFileOrFolder("A\\C\\E.txt", isFolder: false);

            List<string> expectedContents = new List<string>() { "*", "!/A", "!/A/B", "!/A/B/C", "!/A/B/C/*", "!/A/D", "!/A/D/E", "!/A/D/E/*", "!/A/C", "!/A/C/*" };
            this.CheckFileContents(alwaysExcludeFilePath, expectedContents);
        }

        [TestCase]

        public void DoesNotWriteDuplicateFolderEntries()
        {
            string alwaysExcludeFilePath = Path.Combine(this.Repo.GitParentPath, GVFS.Common.GVFSConstants.DotGit.Info.AlwaysExcludeName);
            AlwaysExcludeFile alwaysExcludeFile = new AlwaysExcludeFile(this.Repo.Context, alwaysExcludeFilePath);
            this.Repo.Context.FileSystem.FileExists(alwaysExcludeFilePath).ShouldEqual(false);
            alwaysExcludeFile.LoadOrCreate();
            this.Repo.Context.FileSystem.FileExists(alwaysExcludeFilePath).ShouldEqual(true);

            alwaysExcludeFile.AddEntriesForFileOrFolder("A\\B", isFolder: true);
            alwaysExcludeFile.AddEntriesForFileOrFolder("a\\b", isFolder: true);
            alwaysExcludeFile.AddEntriesForFileOrFolder("a\\b.txt", isFolder: false);
            alwaysExcludeFile.AddEntriesForFileOrFolder("a\\b\\c.txt", isFolder: false);
            alwaysExcludeFile.AddEntriesForFileOrFolder("A\\D", isFolder: true);
            alwaysExcludeFile.AddEntriesForFileOrFolder("A\\d", isFolder: true);
            alwaysExcludeFile.AddEntriesForFileOrFolder("a\\f", isFolder: true);
            alwaysExcludeFile.AddEntriesForFileOrFolder("a\\F", isFolder: true);

            List<string> expectedContents = new List<string>() { "*", "!/A", "!/A/B", "!/A/B/*", "!/a/*", "!/A/D", "!/A/D/*", "!/a/f", "!/a/f/*" };
            this.CheckFileContents(alwaysExcludeFilePath, expectedContents);
        }

        [TestCase]
        public void WritesAfterLoad()
        {
            string alwaysExcludeFilePath = Path.Combine(this.Repo.GitParentPath, GVFS.Common.GVFSConstants.DotGit.Info.AlwaysExcludeName);
            AlwaysExcludeFile alwaysExcludeFile = new AlwaysExcludeFile(this.Repo.Context, alwaysExcludeFilePath);
            this.Repo.Context.FileSystem.FileExists(alwaysExcludeFilePath).ShouldEqual(false);
            alwaysExcludeFile.LoadOrCreate();
            this.Repo.Context.FileSystem.FileExists(alwaysExcludeFilePath).ShouldEqual(true);

            alwaysExcludeFile.AddEntriesForFileOrFolder("A\\B", isFolder: true);
            alwaysExcludeFile.AddEntriesForFileOrFolder("A\\D", isFolder: true);

            List<string> expectedContents = new List<string>() { "*", "!/A", "!/A/B", "!/A/B/*", "!/A/D", "!/A/D/*" };
            this.CheckFileContents(alwaysExcludeFilePath, expectedContents);

            alwaysExcludeFile = new AlwaysExcludeFile(this.Repo.Context, alwaysExcludeFilePath);
            alwaysExcludeFile.LoadOrCreate();
            alwaysExcludeFile.AddEntriesForFileOrFolder("a\\f", isFolder: true);

            expectedContents = new List<string>() { "*", "!/A", "!/A/B", "!/A/B/*", "!/A/D", "!/A/D/*", "!/a/f", "!/a/f/*" };
            this.CheckFileContents(alwaysExcludeFilePath, expectedContents);
        }

        private void CheckFileContents(string sparseCheckoutFilePath, List<string> expectedContents)
        {
            IEnumerator<string> expectedLines = expectedContents.GetEnumerator();
            expectedLines.MoveNext().ShouldEqual(true);
            foreach (string fileLine in this.Repo.Context.FileSystem.ReadLines(sparseCheckoutFilePath))
            {
                expectedLines.Current.ShouldEqual(fileLine);
                expectedLines.MoveNext();
            }
        }
    }
}
