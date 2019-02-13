using GVFS.Tests;
using GVFS.Tests.Should;
using GVFS.UnitTests.Mock.Common;
using NUnit.Framework;
using System;
using System.IO;
using System.Text;
using static GVFS.Virtualization.Projection.GitIndexProjection;

namespace GVFS.UnitTests.Virtualization.Git
{
    [TestFixtureSource(typeof(DataSources), nameof(DataSources.AllBools))]
    public class GitIndexEntryTests
    {
        private const int DefaultIndexEntryCount = 10;
        private bool buildingNewProjection;

        public GitIndexEntryTests(bool buildingNewProjection)
        {
            this.buildingNewProjection = buildingNewProjection;
        }

        [OneTimeSetUp]
        public void Setup()
        {
            LazyUTF8String.InitializePools(new MockTracer(), DefaultIndexEntryCount);
        }

        [TestCase]
        public void TopLevelPath()
        {
            string[] pathParts = new[] { ".gitignore" };
            GitIndexEntry indexEntry = this.SetupIndexEntry(".gitignore");
            this.TestPathParts(indexEntry, pathParts, hasSameParent: false);
        }

        [TestCase]
        public void TwoLevelPath()
        {
            string[] pathParts = new[] { "folder", "file.txt" };
            GitIndexEntry indexEntry = this.SetupIndexEntry(string.Join("/", pathParts));
            this.TestPathParts(indexEntry, pathParts, hasSameParent: false);
        }

        [TestCase]
        public void ReplaceFileName()
        {
            string[] pathParts = new[] { "folder", "file.txt" };
            GitIndexEntry indexEntry = this.SetupIndexEntry(string.Join("/", pathParts));
            this.TestPathParts(indexEntry, pathParts, hasSameParent: false);

            string[] pathParts2 = new[] { "folder", "newfile.txt" };
            this.ParsePathForIndexEntry(indexEntry, string.Join("/", pathParts2), replaceIndex: 7);
            this.TestPathParts(indexEntry, pathParts2, hasSameParent: true);
        }

        [TestCase]
        public void ReplaceNonASCIIFileName()
        {
            string[] pathParts = new[] { "توبر", "مارسأغ", "FCIBBinaries.kml" };
            string path = string.Join("/", pathParts);
            GitIndexEntry indexEntry = this.SetupIndexEntry(path);
            this.TestPathParts(indexEntry, pathParts, hasSameParent: false);

            string[] pathParts2 = new[] { "توبر", "مارسأغ", "FCIBBinaries.txt" };
            this.ParsePathForIndexEntry(indexEntry, string.Join("/", pathParts2), replaceIndex: Encoding.UTF8.GetByteCount(path) - 3);
            this.TestPathParts(indexEntry, pathParts2, hasSameParent: true);
        }

        [TestCase]
        public void ReplaceFileNameShorter()
        {
            string[] pathParts = new[] { "MergedComponents", "InstrumentedBinCatalogs", "dirs" };
            GitIndexEntry indexEntry = this.SetupIndexEntry(string.Join("/", pathParts));
            this.TestPathParts(indexEntry, pathParts, hasSameParent: false);

            string[] pathParts2 = new[] { "MergedComponents", "InstrumentedBinCatalogs", "pgi", "sources.dep" };
            this.ParsePathForIndexEntry(indexEntry, string.Join("/", pathParts2), replaceIndex: 41);
            this.TestPathParts(indexEntry, pathParts2, hasSameParent: false);
        }

        [TestCase]
        public void TestComponentsWithSimilarNames()
        {
            string[] pathParts = new[] { "MergedComponents", "SDK", "FCIBBinaries.kml" };
            string path = string.Join("/", pathParts);
            GitIndexEntry indexEntry = this.SetupIndexEntry(path);
            this.TestPathParts(indexEntry, pathParts, hasSameParent: false);

            string[] pathParts2 = new[] { "MergedComponents", "SDK", "FCIBBinaries", "TH2Legacy", "amd64", "mdmerge.exe" };
            this.ParsePathForIndexEntry(indexEntry, string.Join("/", pathParts2), replaceIndex: path.Length - 4);
            this.TestPathParts(indexEntry, pathParts2, hasSameParent: false);
        }

        [TestCase]
        public void TestComponentsWithSimilarNonASCIINames()
        {
            string[] pathParts = new[] { "توبر", "مارسأغ", "FCIBBinaries.kml" };
            string path = string.Join("/", pathParts);
            GitIndexEntry indexEntry = this.SetupIndexEntry(path);
            this.TestPathParts(indexEntry, pathParts, hasSameParent: false);

            string[] pathParts2 = new[] { "توبر", "مارسأغ", "FCIBBinaries", "TH2Legacy", "amd64", "mdmerge.exe" };
            this.ParsePathForIndexEntry(indexEntry, string.Join("/", pathParts2), replaceIndex: Encoding.UTF8.GetByteCount(path) - 4);
            this.TestPathParts(indexEntry, pathParts2, hasSameParent: false);
        }

        [TestCase]
        public void AddFolder()
        {
            string[] pathParts = new[] { "folder", "file.txt" };
            GitIndexEntry indexEntry = this.SetupIndexEntry(string.Join("/", pathParts));
            this.TestPathParts(indexEntry, pathParts, hasSameParent: false);

            string[] pathParts2 = new[] { "folder", "folder2", "file.txt" };
            this.ParsePathForIndexEntry(indexEntry, string.Join("/", pathParts2), replaceIndex: 8);
            this.TestPathParts(indexEntry, pathParts2, hasSameParent: false);
        }

        [TestCase]
        public void RemoveFolder()
        {
            string[] pathParts = new[] { "folder", "folder2", "file.txt" };
            GitIndexEntry indexEntry = this.SetupIndexEntry(string.Join("/", pathParts));
            this.TestPathParts(indexEntry, pathParts, hasSameParent: false);

            string[] pathParts2 = new[] { "folder", "file.txt" };
            this.ParsePathForIndexEntry(indexEntry, string.Join("/", pathParts2), replaceIndex: 8);
            this.TestPathParts(indexEntry, pathParts2, hasSameParent: false);
        }

        [TestCase]
        public void NewSimilarRootFolder()
        {
            string[] pathParts = new[] { "folder", "file.txt" };
            GitIndexEntry indexEntry = this.SetupIndexEntry(string.Join("/", pathParts));
            this.TestPathParts(indexEntry, pathParts, hasSameParent: false);

            string[] pathParts2 = new[] { "folder1", "file.txt" };
            this.ParsePathForIndexEntry(indexEntry, string.Join("/", pathParts2), replaceIndex: 6);
            this.TestPathParts(indexEntry, pathParts2, hasSameParent: false);
        }

        [TestCase]
        public void ReplaceFullPath()
        {
            string[] pathParts = new[] { "folder", "file.txt" };
            GitIndexEntry indexEntry = this.SetupIndexEntry(string.Join("/", pathParts));
            this.TestPathParts(indexEntry, pathParts, hasSameParent: false);

            string[] pathParts2 = new[] { "another", "one", "new.txt" };
            this.ParsePathForIndexEntry(indexEntry, string.Join("/", pathParts2), replaceIndex: 0);
            this.TestPathParts(indexEntry, pathParts2, hasSameParent: false);
        }

        [TestCase]
        public void ClearLastParent()
        {
            string[] pathParts = new[] { "folder", "one", "file.txt" };
            GitIndexEntry indexEntry = this.SetupIndexEntry(string.Join("/", pathParts));
            this.TestPathParts(indexEntry, pathParts, hasSameParent: false);

            string[] pathParts2 = new[] { "folder", "one", "newfile.txt" };
            this.ParsePathForIndexEntry(indexEntry, string.Join("/", pathParts2), replaceIndex: 11);
            this.TestPathParts(indexEntry, pathParts2, hasSameParent: true);

            if (this.buildingNewProjection)
            {
                indexEntry.BuildingProjection_LastParent = new FolderData();
                indexEntry.ClearLastParent();
                indexEntry.BuildingProjection_HasSameParentAsLastEntry.ShouldBeFalse();
                indexEntry.BuildingProjection_LastParent.ShouldBeNull();
            }
        }

        private GitIndexEntry SetupIndexEntry(string path)
        {
            GitIndexEntry indexEntry = new GitIndexEntry(this.buildingNewProjection);
            this.ParsePathForIndexEntry(indexEntry, path, replaceIndex: 0);
            return indexEntry;
        }

        private void ParsePathForIndexEntry(GitIndexEntry indexEntry, string path, int replaceIndex)
        {
            byte[] pathBuffer = Encoding.UTF8.GetBytes(path);
            Buffer.BlockCopy(pathBuffer, 0, indexEntry.PathBuffer, 0, pathBuffer.Length);
            indexEntry.PathLength = pathBuffer.Length;
            indexEntry.ReplaceIndex = replaceIndex;

            if (this.buildingNewProjection)
            {
                indexEntry.BuildingProjection_ParsePath();
            }
            else
            {
                indexEntry.BackgroundTask_ParsePath();
            }
        }

        private void TestPathParts(GitIndexEntry indexEntry, string[] pathParts, bool hasSameParent)
        {
            if (this.buildingNewProjection)
            {
                indexEntry.BuildingProjection_HasSameParentAsLastEntry.ShouldEqual(hasSameParent, nameof(indexEntry.BuildingProjection_HasSameParentAsLastEntry));
                indexEntry.BuildingProjection_NumParts.ShouldEqual(pathParts.Length, nameof(indexEntry.BuildingProjection_NumParts));
            }

            for (int i = 0; i < pathParts.Length; i++)
            {
                if (this.buildingNewProjection)
                {
                    indexEntry.BuildingProjection_PathParts[i].ShouldNotBeNull();
                    indexEntry.BuildingProjection_PathParts[i].GetString().ShouldEqual(pathParts[i]);
                }
            }

            if (this.buildingNewProjection)
            {
                indexEntry.BuildingProjection_GetChildName().GetString().ShouldEqual(pathParts[pathParts.Length - 1]);
                indexEntry.BuildingProjection_GetGitRelativePath().ShouldEqual(string.Join("/", pathParts));
            }
            else
            {
                indexEntry.BackgroundTask_GetPlatformRelativePath().ShouldEqual(string.Join(Path.DirectorySeparatorChar.ToString(), pathParts));
            }
        }
    }
}
