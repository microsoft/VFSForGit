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

        [TestCase("a/b/first/file.txt", "a/b/second/file.txt", 2, false)]
        [TestCase("a/b/file.txt", "a/bc/file.txt", 1, false)]
        [TestCase("a/b/file.txt", "a/b/file2.txt", 2, true)]
        [TestCase("a/b/file.txt", "a/b/file.txt/child.txt", 2, false)]
        [TestCase("a/b/file.txt", "a/b.txt", 1, false)]
        [TestCase("a/b/file.txt", "a/file.txt", 1, false)]
        [TestCase("a/b/file.txt", "a/b/c/file.txt", 2, false)]
        [TestCase("a/file.txt", "a.txt", 0, false)]
        [TestCase("a.txt", "b.txt", 0, false)]
        [TestCase("a.txt", "a/b/file.txt", 0, false)]
        [TestCase("A/b/file.txt", "a/b/file2.txt", 0, false)]
        [TestCase("a/B/file.txt", "a/b/file2.txt", 1, false)]
        [TestCase("é/目录/first/file.txt", "é/目录/second/file.txt", 2, false)]
        [TestCase("é/目录/file.txt", "é/目弓/file.txt", 1, false)]
        public void ReusesOnlyCompleteUnchangedDirectories(string previous, string path, int reusedDirectories, bool hasSameParent)
        {
            GitIndexEntry indexEntry = this.SetupIndexEntry(previous);
            LazyUTF8String[] previousParts = this.buildingNewProjection
                ? (LazyUTF8String[])indexEntry.BuildingProjection_PathParts.Clone()
                : null;
            int namesBefore = LazyUTF8String.StringPoolObjectsUsed();
            int bytesBefore = LazyUTF8String.BytePoolBytesUsed();
            byte[] previousBytes = Encoding.UTF8.GetBytes(previous);
            byte[] pathBytes = Encoding.UTF8.GetBytes(path);
            int prefixLength = 0;
            while (prefixLength < Math.Min(previousBytes.Length, pathBytes.Length) &&
                previousBytes[prefixLength] == pathBytes[prefixLength])
            {
                ++prefixLength;
            }

            this.ParsePathForIndexEntry(indexEntry, path, prefixLength);
            string[] parts = path.Split('/');
            this.TestPathParts(indexEntry, parts, hasSameParent);
            if (this.buildingNewProjection)
            {
                LazyUTF8String.StringPoolObjectsUsed().ShouldEqual(namesBefore + parts.Length - reusedDirectories);
                int allocatedBytes = 0;
                for (int i = 0; i < parts.Length; ++i)
                {
                    if (i < reusedDirectories)
                    {
                        Assert.AreSame(previousParts[i], indexEntry.BuildingProjection_PathParts[i]);
                    }
                    else
                    {
                        Assert.AreNotSame(previousParts[i], indexEntry.BuildingProjection_PathParts[i]);
                        if (Array.TrueForAll(Encoding.UTF8.GetBytes(parts[i]), value => value <= 127))
                        {
                            allocatedBytes += parts[i].Length;
                        }
                    }
                }

                LazyUTF8String.BytePoolBytesUsed().ShouldEqual(bytesBefore + allocatedBytes);
            }
        }

        [TestCase]
        public void ReusedAncestorCanBecomeTheNextParent()
        {
            GitIndexEntry indexEntry = this.SetupIndexEntry("a/b/file.txt");
            this.ParsePathForIndexEntry(indexEntry, "a/root.txt", replaceIndex: 2);
            this.TestPathParts(indexEntry, new[] { "a", "root.txt" }, hasSameParent: false);
            this.ParsePathForIndexEntry(indexEntry, "a/root2.txt", replaceIndex: 6);
            this.TestPathParts(indexEntry, new[] { "a", "root2.txt" }, hasSameParent: true);
        }

        [TestCase]
        public void ClearLastParentInvalidatesAllReusableDirectories()
        {
            GitIndexEntry indexEntry = this.SetupIndexEntry("a/b/file.txt");
            if (this.buildingNewProjection)
            {
                LazyUTF8String[] previousParts = (LazyUTF8String[])indexEntry.BuildingProjection_PathParts.Clone();
                indexEntry.ClearLastParent();
                int namesBefore = LazyUTF8String.StringPoolObjectsUsed();
                this.ParsePathForIndexEntry(indexEntry, "a/b/file2.txt", replaceIndex: 8);
                this.TestPathParts(indexEntry, new[] { "a", "b", "file2.txt" }, hasSameParent: false);
                LazyUTF8String.StringPoolObjectsUsed().ShouldEqual(namesBefore + 3);
                Assert.AreNotSame(previousParts[0], indexEntry.BuildingProjection_PathParts[0]);
                Assert.AreNotSame(previousParts[1], indexEntry.BuildingProjection_PathParts[1]);
            }
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
