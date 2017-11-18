using RGFS.GVFlt.DotGit;
using RGFS.Tests.Should;
using RGFS.UnitTests.Virtual;
using NUnit.Framework;
using System.Collections.Generic;
using System.IO;

namespace RGFS.UnitTests.GVFlt.DotGit
{
    [TestFixture]
    public class AlwaysExcludeFileTests : TestsWithCommonRepo
    {
        [TestCase]
        public void HasDefaultEntriesAfterLoad()
        {
            string alwaysExcludeFilePath = Path.Combine(this.Repo.GitParentPath, RGFS.Common.RGFSConstants.DotGit.Info.AlwaysExcludeName);
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
            string alwaysExcludeFilePath = Path.Combine(this.Repo.GitParentPath, RGFS.Common.RGFSConstants.DotGit.Info.AlwaysExcludeName);
            AlwaysExcludeFile alwaysExcludeFile = new AlwaysExcludeFile(this.Repo.Context, alwaysExcludeFilePath);
            this.Repo.Context.FileSystem.FileExists(alwaysExcludeFilePath).ShouldEqual(false);
            alwaysExcludeFile.LoadOrCreate();
            this.Repo.Context.FileSystem.FileExists(alwaysExcludeFilePath).ShouldEqual(true);

            alwaysExcludeFile.AddEntriesForFile("a\\1.txt");
            alwaysExcludeFile.AddEntriesForFile("a\\2.txt");
            alwaysExcludeFile.AddEntriesForFile("a\\3.txt");
            alwaysExcludeFile.AddEntriesForFile("a\\b\\1.txt");
            alwaysExcludeFile.AddEntriesForFile("c\\1.txt");

            List<string> expectedContents = new List<string>() { "*", "!/a/", "!/a/1.txt", "!/a/2.txt", "!/a/3.txt", "!/a/b/", "!/a/b/1.txt", "!/c/", "!/c/1.txt" };
            this.CheckFileContents(alwaysExcludeFilePath, expectedContents);
        }

        [TestCase]
        public void HandlesCaseCorrectly()
        {
            string alwaysExcludeFilePath = Path.Combine(this.Repo.GitParentPath, RGFS.Common.RGFSConstants.DotGit.Info.AlwaysExcludeName);
            AlwaysExcludeFile alwaysExcludeFile = new AlwaysExcludeFile(this.Repo.Context, alwaysExcludeFilePath);
            this.Repo.Context.FileSystem.FileExists(alwaysExcludeFilePath).ShouldEqual(false);
            alwaysExcludeFile.LoadOrCreate();
            this.Repo.Context.FileSystem.FileExists(alwaysExcludeFilePath).ShouldEqual(true);

            alwaysExcludeFile.AddEntriesForFile("a\\1.txt");
            alwaysExcludeFile.AddEntriesForFile("A\\2.txt");
            alwaysExcludeFile.AddEntriesForFile("a\\b\\1.txt");
            alwaysExcludeFile.AddEntriesForFile("a\\B\\2.txt");
            alwaysExcludeFile.AddEntriesForFile("A\\b\\3.txt");
            alwaysExcludeFile.AddEntriesForFile("A\\B\\4.txt");

            List<string> expectedContents = new List<string>() { "*", "!/a/", "!/a/1.txt", "!/A/2.txt", "!/a/b/", "!/a/b/1.txt", "!/a/B/2.txt", "!/A/b/3.txt", "!/A/B/4.txt" };
            this.CheckFileContents(alwaysExcludeFilePath, expectedContents);
        }

        [TestCase]
        public void WritesAfterLoad()
        {
            string alwaysExcludeFilePath = Path.Combine(this.Repo.GitParentPath, RGFS.Common.RGFSConstants.DotGit.Info.AlwaysExcludeName);
            AlwaysExcludeFile alwaysExcludeFile = new AlwaysExcludeFile(this.Repo.Context, alwaysExcludeFilePath);
            this.Repo.Context.FileSystem.FileExists(alwaysExcludeFilePath).ShouldEqual(false);
            alwaysExcludeFile.LoadOrCreate();
            this.Repo.Context.FileSystem.FileExists(alwaysExcludeFilePath).ShouldEqual(true);

            alwaysExcludeFile.AddEntriesForFile("a\\1.txt");
            alwaysExcludeFile.AddEntriesForFile("a\\2.txt");

            List<string> expectedContents = new List<string>() { "*", "!/a/", "!/a/1.txt", "!/a/2.txt" };
            this.CheckFileContents(alwaysExcludeFilePath, expectedContents);

            alwaysExcludeFile = new AlwaysExcludeFile(this.Repo.Context, alwaysExcludeFilePath);
            alwaysExcludeFile.LoadOrCreate();
            alwaysExcludeFile.AddEntriesForFile("a\\3.txt");

            expectedContents = new List<string>() { "*", "!/a/", "!/a/1.txt", "!/a/2.txt", "!/a/3.txt" };
            this.CheckFileContents(alwaysExcludeFilePath, expectedContents);
        }

        [TestCase]
        public void RemovesEntries()
        {
            string alwaysExcludeFilePath = Path.Combine(this.Repo.GitParentPath, RGFS.Common.RGFSConstants.DotGit.Info.AlwaysExcludeName);
            AlwaysExcludeFile alwaysExcludeFile = new AlwaysExcludeFile(this.Repo.Context, alwaysExcludeFilePath);
            this.Repo.Context.FileSystem.FileExists(alwaysExcludeFilePath).ShouldEqual(false);
            alwaysExcludeFile.LoadOrCreate();
            this.Repo.Context.FileSystem.FileExists(alwaysExcludeFilePath).ShouldEqual(true);

            alwaysExcludeFile.AddEntriesForFile("a\\1.txt");
            alwaysExcludeFile.AddEntriesForFile("a\\2.txt");
            alwaysExcludeFile.RemoveEntriesForFiles(new List<string> { "a\\1.txt" });
            alwaysExcludeFile.FlushAndClose();

            List<string> expectedContents = new List<string>() { "*", "!/a/", "!/a/2.txt" };
            this.CheckFileContents(alwaysExcludeFilePath, expectedContents);
        }

        [TestCase]
        public void RemovesEntriesWithDifferentCase()
        {
            string alwaysExcludeFilePath = Path.Combine(this.Repo.GitParentPath, RGFS.Common.RGFSConstants.DotGit.Info.AlwaysExcludeName);
            AlwaysExcludeFile alwaysExcludeFile = new AlwaysExcludeFile(this.Repo.Context, alwaysExcludeFilePath);
            this.Repo.Context.FileSystem.FileExists(alwaysExcludeFilePath).ShouldEqual(false);
            alwaysExcludeFile.LoadOrCreate();
            this.Repo.Context.FileSystem.FileExists(alwaysExcludeFilePath).ShouldEqual(true);

            alwaysExcludeFile.AddEntriesForFile("a\\x.txt");
            alwaysExcludeFile.AddEntriesForFile("A\\y.txt");
            alwaysExcludeFile.AddEntriesForFile("a\\Z.txt");
            alwaysExcludeFile.RemoveEntriesForFiles(new List<string> { "a\\y.txt", "a\\z.txt" });
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
