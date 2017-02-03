using GVFS.GVFlt.DotGit;
using GVFS.Tests.Should;
using GVFS.UnitTests.Virtual;
using NUnit.Framework;
using System.Collections.Generic;
using System.IO;

namespace GVFS.UnitTests.GVFlt.DotGit
{
    [TestFixture]
    public class ExcludeFileTests : TestsWithCommonRepo
    {
        [TestCase]
        public void HasDefaultEntriesAfterLoad()
        {
            string excludeFilePath = Path.Combine(this.Repo.GitParentPath, GVFS.Common.GVFSConstants.DotGit.Info.ExcludeName);
            ExcludeFile excludeFile = new ExcludeFile(this.Repo.Context, excludeFilePath);
            this.Repo.Context.FileSystem.FileExists(excludeFilePath).ShouldEqual(false);
            excludeFile.LoadOrCreate();
            this.Repo.Context.FileSystem.FileExists(excludeFilePath).ShouldEqual(true);

            List<string> expectedContents = new List<string>() { "*" };
            this.CheckFileContents(excludeFilePath, expectedContents);
        }

        [TestCase]
        public void WritesOnFolderChange()
        {
            string excludeFilePath = Path.Combine(this.Repo.GitParentPath, GVFS.Common.GVFSConstants.DotGit.Info.ExcludeName);
            ExcludeFile excludeFile = new ExcludeFile(this.Repo.Context, excludeFilePath);
            this.Repo.Context.FileSystem.FileExists(excludeFilePath).ShouldEqual(false);
            excludeFile.LoadOrCreate();
            this.Repo.Context.FileSystem.FileExists(excludeFilePath).ShouldEqual(true);

            excludeFile.FolderChanged("A\\B\\C");
            excludeFile.FolderChanged("A\\D\\E");

            List<string> expectedContents = new List<string>() { "*", "!/A", "!/A/B", "!/A/B/C", "!/A/B/C/*", "!/A/D", "!/A/D/E", "!/A/D/E/*" };
            this.CheckFileContents(excludeFilePath, expectedContents);
        }

        [TestCase]

        public void DoesNotWriteDuplicateFolderEntries()
        {
            string excludeFilePath = Path.Combine(this.Repo.GitParentPath, GVFS.Common.GVFSConstants.DotGit.Info.ExcludeName);
            ExcludeFile excludeFile = new ExcludeFile(this.Repo.Context, excludeFilePath);
            this.Repo.Context.FileSystem.FileExists(excludeFilePath).ShouldEqual(false);
            excludeFile.LoadOrCreate();
            this.Repo.Context.FileSystem.FileExists(excludeFilePath).ShouldEqual(true);

            excludeFile.FolderChanged("A\\B");
            excludeFile.FolderChanged("a\\b");
            excludeFile.FolderChanged("A\\D");
            excludeFile.FolderChanged("A\\d");
            excludeFile.FolderChanged("a\\f");
            excludeFile.FolderChanged("a\\F");

            List<string> expectedContents = new List<string>() { "*", "!/A", "!/A/B", "!/A/B/*", "!/A/D", "!/A/D/*", "!/a/f", "!/a/f/*" };
            this.CheckFileContents(excludeFilePath, expectedContents);
        }

        [TestCase]
        public void WritesAfterLoad()
        {
            string excludeFilePath = Path.Combine(this.Repo.GitParentPath, GVFS.Common.GVFSConstants.DotGit.Info.ExcludeName);
            ExcludeFile excludeFile = new ExcludeFile(this.Repo.Context, excludeFilePath);
            this.Repo.Context.FileSystem.FileExists(excludeFilePath).ShouldEqual(false);
            excludeFile.LoadOrCreate();
            this.Repo.Context.FileSystem.FileExists(excludeFilePath).ShouldEqual(true);

            excludeFile.FolderChanged("A\\B");
            excludeFile.FolderChanged("A\\D");

            List<string> expectedContents = new List<string>() { "*", "!/A", "!/A/B", "!/A/B/*", "!/A/D", "!/A/D/*" };
            this.CheckFileContents(excludeFilePath, expectedContents);

            excludeFile = new ExcludeFile(this.Repo.Context, excludeFilePath);
            excludeFile.LoadOrCreate();
            excludeFile.FolderChanged("a\\f");

            expectedContents = new List<string>() { "*", "!/A", "!/A/B", "!/A/B/*", "!/A/D", "!/A/D/*", "!/a/f", "!/a/f/*" };
            this.CheckFileContents(excludeFilePath, expectedContents);
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
