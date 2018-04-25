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
        public void WritesParentFoldersWithoutDuplicates()
        {
            string alwaysExcludeFilePath = Path.Combine(this.Repo.GitParentPath, GVFS.Common.GVFSConstants.DotGit.Info.AlwaysExcludeName);
            AlwaysExcludeFile alwaysExcludeFile = new AlwaysExcludeFile(this.Repo.Context, alwaysExcludeFilePath);
            this.Repo.Context.FileSystem.FileExists(alwaysExcludeFilePath).ShouldEqual(false);
            alwaysExcludeFile.LoadOrCreate();
            this.Repo.Context.FileSystem.FileExists(alwaysExcludeFilePath).ShouldEqual(true);

            alwaysExcludeFile.AddEntriesForPath("a\\1.txt");
            alwaysExcludeFile.AddEntriesForPath("a\\2.txt");
            alwaysExcludeFile.AddEntriesForPath("a\\3.txt");
            alwaysExcludeFile.AddEntriesForPath("a\\b\\1.txt");
            alwaysExcludeFile.AddEntriesForPath("c\\1.txt");

            List<string> expectedContents = new List<string>() { "*", "!/a/", "!/a/1.txt", "!/a/2.txt", "!/a/3.txt", "!/a/b/", "!/a/b/1.txt", "!/c/", "!/c/1.txt" };
            this.CheckFileContents(alwaysExcludeFilePath, expectedContents);
        }

        [TestCase]
        public void HandlesCaseCorrectly()
        {
            string alwaysExcludeFilePath = Path.Combine(this.Repo.GitParentPath, GVFS.Common.GVFSConstants.DotGit.Info.AlwaysExcludeName);
            AlwaysExcludeFile alwaysExcludeFile = new AlwaysExcludeFile(this.Repo.Context, alwaysExcludeFilePath);
            this.Repo.Context.FileSystem.FileExists(alwaysExcludeFilePath).ShouldEqual(false);
            alwaysExcludeFile.LoadOrCreate();
            this.Repo.Context.FileSystem.FileExists(alwaysExcludeFilePath).ShouldEqual(true);

            alwaysExcludeFile.AddEntriesForPath("a\\1.txt");
            alwaysExcludeFile.AddEntriesForPath("A\\2.txt");
            alwaysExcludeFile.AddEntriesForPath("a\\b\\1.txt");
            alwaysExcludeFile.AddEntriesForPath("a\\B\\2.txt");
            alwaysExcludeFile.AddEntriesForPath("A\\b\\3.txt");
            alwaysExcludeFile.AddEntriesForPath("A\\B\\4.txt");

            List<string> expectedContents = new List<string>() { "*", "!/a/", "!/a/1.txt", "!/A/2.txt", "!/a/b/", "!/a/b/1.txt", "!/a/B/2.txt", "!/A/b/3.txt", "!/A/B/4.txt" };
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

            alwaysExcludeFile.AddEntriesForPath("a\\1.txt");
            alwaysExcludeFile.AddEntriesForPath("a\\2.txt");

            List<string> expectedContents = new List<string>() { "*", "!/a/", "!/a/1.txt", "!/a/2.txt" };
            this.CheckFileContents(alwaysExcludeFilePath, expectedContents);

            alwaysExcludeFile = new AlwaysExcludeFile(this.Repo.Context, alwaysExcludeFilePath);
            alwaysExcludeFile.LoadOrCreate();
            alwaysExcludeFile.AddEntriesForPath("a\\3.txt");

            expectedContents = new List<string>() { "*", "!/a/", "!/a/1.txt", "!/a/2.txt", "!/a/3.txt" };
            this.CheckFileContents(alwaysExcludeFilePath, expectedContents);
        }

        [TestCase]
        public void RemovesEntries()
        {
            string alwaysExcludeFilePath = Path.Combine(this.Repo.GitParentPath, GVFS.Common.GVFSConstants.DotGit.Info.AlwaysExcludeName);
            AlwaysExcludeFile alwaysExcludeFile = new AlwaysExcludeFile(this.Repo.Context, alwaysExcludeFilePath);
            this.Repo.Context.FileSystem.FileExists(alwaysExcludeFilePath).ShouldEqual(false);
            alwaysExcludeFile.LoadOrCreate();
            this.Repo.Context.FileSystem.FileExists(alwaysExcludeFilePath).ShouldEqual(true);

            alwaysExcludeFile.AddEntriesForPath("a\\1.txt");
            alwaysExcludeFile.AddEntriesForPath("a\\2.txt");
            alwaysExcludeFile.RemoveEntriesForFile("a\\1.txt");
            alwaysExcludeFile.FlushAndClose();

            List<string> expectedContents = new List<string>() { "*", "!/a/", "!/a/2.txt" };
            this.CheckFileContents(alwaysExcludeFilePath, expectedContents);
        }

        [TestCase]
        public void RemovesEntriesWithDifferentCase()
        {
            string alwaysExcludeFilePath = Path.Combine(this.Repo.GitParentPath, GVFS.Common.GVFSConstants.DotGit.Info.AlwaysExcludeName);
            AlwaysExcludeFile alwaysExcludeFile = new AlwaysExcludeFile(this.Repo.Context, alwaysExcludeFilePath);
            this.Repo.Context.FileSystem.FileExists(alwaysExcludeFilePath).ShouldEqual(false);
            alwaysExcludeFile.LoadOrCreate();
            this.Repo.Context.FileSystem.FileExists(alwaysExcludeFilePath).ShouldEqual(true);

            alwaysExcludeFile.AddEntriesForPath("a\\x.txt");
            alwaysExcludeFile.AddEntriesForPath("A\\y.txt");
            alwaysExcludeFile.AddEntriesForPath("a\\Z.txt");
            alwaysExcludeFile.RemoveEntriesForFile("a\\y.txt");
            alwaysExcludeFile.RemoveEntriesForFile("a\\z.txt");
            alwaysExcludeFile.FlushAndClose();

            List<string> expectedContents = new List<string>() { "*", "!/a/", "!/a/x.txt" };
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
