using GVFS.Common;
using GVFS.Tests.Should;
using GVFS.UnitTests.Category;
using GVFS.UnitTests.Mock.Common;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Text;
using static GVFS.Virtualization.Projection.GitIndexProjection;

namespace GVFS.UnitTests.Virtualization.Git
{
    [TestFixture]
    public class SortedFolderEntriesTests
    {
        private const int DefaultIndexEntryCount = 100;
        private static string[] defaultFiles = new string[]
        {
            "_test",
            "zero",
            "a",
            "{file}",
            "(1)",
            "file.txt",
            "01",
        };

        private static string[] caseDifferingFiles = new string[]
        {
            "file1.txt",
            "File1.txt",
        };

        private static string[] defaultFolders = new string[]
        {
            "zf",
            "af",
            "01f",
            "{folder}",
            "_f",
            "(1f)",
            "folder",
        };

        private static string[] caseDifferingFolders = new string[]
        {
            "folder1",
            "Folder1",
        };

        [OneTimeSetUp]
        public void Setup()
        {
            LazyUTF8String.InitializePools(new MockTracer(), DefaultIndexEntryCount);
            SortedFolderEntries.InitializePools(new MockTracer(), DefaultIndexEntryCount);
        }

        [SetUp]
        public void TestSetup()
        {
            LazyUTF8String.ResetPool(new MockTracer(), DefaultIndexEntryCount);
            SortedFolderEntries.ResetPool(new MockTracer(), DefaultIndexEntryCount);
        }

        [TestCase]
        public void EmptyFolderEntries_NotFound()
        {
            SortedFolderEntries sfe = new SortedFolderEntries();
            LazyUTF8String findName = ConstructLazyUTF8String("Anything");
            sfe.TryGetValue(findName, out FolderEntryData folderEntryData).ShouldBeFalse();
            folderEntryData.ShouldBeNull();
        }

        [TestCase]
        public void EntryNotFound()
        {
            SortedFolderEntries sfe = SetupDefaultEntries();
            LazyUTF8String findName = ConstructLazyUTF8String("Anything");
            sfe.TryGetValue(findName, out FolderEntryData folderEntryData).ShouldBeFalse();
            folderEntryData.ShouldBeNull();
        }

        [TestCase]
        public void EntryFoundMatchingCase()
        {
            SortedFolderEntries sfe = SetupDefaultEntries();
            LazyUTF8String findName = ConstructLazyUTF8String("folder");
            sfe.TryGetValue(findName, out FolderEntryData folderEntryData).ShouldBeTrue();
            folderEntryData.ShouldNotBeNull();
        }

        [TestCase]
        [Category(CategoryConstants.CaseInsensitiveFileSystemOnly)]
        public void EntryFoundDifferentCase()
        {
            SortedFolderEntries sfe = SetupDefaultEntries();
            LazyUTF8String findName = ConstructLazyUTF8String("Folder");
            sfe.TryGetValue(findName, out FolderEntryData folderEntryData).ShouldBeTrue();
            folderEntryData.ShouldNotBeNull();
        }

        [TestCase]
        [Category(CategoryConstants.CaseSensitiveFileSystemOnly)]
        public void EntryNotFoundDifferentCase()
        {
            SortedFolderEntries sfe = SetupDefaultEntries();
            LazyUTF8String findName = ConstructLazyUTF8String("Folder");
            sfe.TryGetValue(findName, out FolderEntryData folderEntryData).ShouldBeFalse();
            folderEntryData.ShouldBeNull();
        }

        [TestCase]
        public void AddItemAtEnd()
        {
            SortedFolderEntries sfe = SetupDefaultEntries();
            LazyUTF8String name = ConstructLazyUTF8String("{{shouldbeattheend");
            sfe.AddFile(name, new byte[20]);
            sfe[GetDefaultEntriesLength()].Name.ShouldEqual(name, "Item added at incorrect index.");
        }

        [TestCase]
        public void AddItemAtTheBeginning()
        {
            SortedFolderEntries sfe = SetupDefaultEntries();
            LazyUTF8String name = ConstructLazyUTF8String("((shouldbeatthestart");
            sfe.AddFile(name, new byte[20]);
            sfe[0].Name.ShouldEqual(name, "Item added at incorrect index.");
        }

        [TestCase]
        [Category(CategoryConstants.CaseInsensitiveFileSystemOnly)]
        public void ValidateCaseInsensitiveOrderOfDefaultEntries()
        {
            List<string> allEntries = new List<string>(defaultFiles);
            allEntries.AddRange(defaultFolders);
            allEntries.Sort(CaseInsensitiveStringCompare);
            SortedFolderEntries sfe = SetupDefaultEntries();
            sfe.Count.ShouldEqual(14);
            for (int i = 0; i < allEntries.Count; i++)
            {
                sfe[i].Name.GetString().ShouldEqual(allEntries[i]);
            }
        }

        [TestCase]
        [Category(CategoryConstants.CaseSensitiveFileSystemOnly)]
        public void ValidateCaseSensitiveOrderOfDefaultEntries()
        {
            List<string> allEntries = new List<string>(defaultFiles);
            allEntries.AddRange(defaultFolders);
            allEntries.AddRange(caseDifferingFiles);
            allEntries.AddRange(caseDifferingFolders);
            allEntries.Sort(CaseSensitiveStringCompare);
            SortedFolderEntries sfe = SetupDefaultEntries();
            sfe.Count.ShouldEqual(18);
            for (int i = 0; i < allEntries.Count; i++)
            {
                sfe[i].Name.GetString().ShouldEqual(allEntries[i]);
            }
        }

        [TestCase]
        public void FoldersShouldBeIncludedWhenSparseFolderDataIsEmpty()
        {
            SortedFolderEntries sfe = SetupDefaultEntries();
            LazyUTF8String name = ConstructLazyUTF8String("IsIncludedFalse");
            sfe.GetOrAddFolder(new[] { name }, partIndex: 0, parentIsIncluded: false, rootSparseFolderData: new SparseFolderData());
            ValidateFolder(sfe, name, isIncludedValue: true);
        }

        [TestCase]
        public void AddFolderWhereParentIncludedIsFalseAndIncluded()
        {
            SortedFolderEntries sfe = SetupDefaultEntries();
            LazyUTF8String name = ConstructLazyUTF8String("Child");
            SparseFolderData sparseFolderData = new SparseFolderData();
            sparseFolderData.Children.Add("Child", new SparseFolderData());
            sfe.GetOrAddFolder(new[] { name }, partIndex: 0, parentIsIncluded: false, rootSparseFolderData: sparseFolderData);
            ValidateFolder(sfe, name, isIncludedValue: false);
        }

        [TestCase]
        public void AddFolderWhereParentIncludedIsTrueAndChildIsIncluded()
        {
            SortedFolderEntries sfe = SetupDefaultEntries();
            LazyUTF8String name = ConstructLazyUTF8String("Child");
            SparseFolderData sparseFolderData = new SparseFolderData();
            sparseFolderData.Children.Add("Child", new SparseFolderData());
            sfe.GetOrAddFolder(new[] { name }, partIndex: 0, parentIsIncluded: true, rootSparseFolderData: sparseFolderData);
            ValidateFolder(sfe, name, isIncludedValue: true);
        }

        [TestCase]
        public void AddFolderWhereParentIncludedIsTrueAndChildIsNotIncluded()
        {
            SortedFolderEntries sfe = SetupDefaultEntries();
            LazyUTF8String name = ConstructLazyUTF8String("ChildNotIncluded");
            SparseFolderData sparseFolderData = new SparseFolderData();
            sparseFolderData.Children.Add("Child", new SparseFolderData());
            sfe.GetOrAddFolder(new[] { name }, partIndex: 0, parentIsIncluded: true, rootSparseFolderData: sparseFolderData);
            ValidateFolder(sfe, name, isIncludedValue: false);
        }

        [TestCase]
        public void AddFolderWhereParentIsRecursive()
        {
            SortedFolderEntries sfe = SetupDefaultEntries();
            LazyUTF8String name = ConstructLazyUTF8String("Child");
            LazyUTF8String name2 = ConstructLazyUTF8String("GrandChild");
            SparseFolderData sparseFolderData = new SparseFolderData() { IsRecursive = true };
            sparseFolderData.Children.Add("Child", new SparseFolderData());
            sfe.GetOrAddFolder(new[] { name, name2 }, partIndex: 1, parentIsIncluded: true, rootSparseFolderData: sparseFolderData);
            ValidateFolder(sfe, name2, isIncludedValue: true);
        }

        [TestCase]
        public void AddFolderBelowTopLevelNotIncluded()
        {
            SortedFolderEntries sfe = SetupDefaultEntries();
            LazyUTF8String name = ConstructLazyUTF8String("Child");
            LazyUTF8String name2 = ConstructLazyUTF8String("GrandChild");
            SparseFolderData sparseFolderData = new SparseFolderData();
            sparseFolderData.Children.Add("Child", new SparseFolderData());
            sfe.GetOrAddFolder(new[] { name, name2 }, partIndex: 1, parentIsIncluded: true, rootSparseFolderData: sparseFolderData);
            ValidateFolder(sfe, name2, isIncludedValue: false);
        }

        [TestCase]
        public void AddFolderBelowTopLevelIsIncluded()
        {
            SortedFolderEntries sfe = SetupDefaultEntries();
            LazyUTF8String name = ConstructLazyUTF8String("Child");
            LazyUTF8String name2 = ConstructLazyUTF8String("GrandChild");
            SparseFolderData sparseFolderData = new SparseFolderData();
            SparseFolderData childSparseFolderData = new SparseFolderData();
            childSparseFolderData.Children.Add("GrandChild", new SparseFolderData());
            sparseFolderData.Children.Add("Child", childSparseFolderData);
            sfe.GetOrAddFolder(new[] { name, name2 }, partIndex: 1, parentIsIncluded: true, rootSparseFolderData: sparseFolderData);
            ValidateFolder(sfe, name2, isIncludedValue: true);
        }

        [TestCase]
        public void Clear()
        {
            SortedFolderEntries sfe = SetupDefaultEntries();
            sfe.Clear();
            sfe.Count.ShouldEqual(0);
        }

        [TestCase]
        public void SmallEntries()
        {
            SortedFolderEntries.FreePool();

            SortedFolderEntries.InitializePools(new MockTracer(), indexEntryCount: 0);
            SortedFolderEntries.FilePoolSize().ShouldBeAtLeast(1);
            SortedFolderEntries.FolderPoolSize().ShouldBeAtLeast(1);

            SortedFolderEntries.ResetPool(new MockTracer(), indexEntryCount: 0);
            SortedFolderEntries.FilePoolSize().ShouldBeAtLeast(1);
            SortedFolderEntries.FolderPoolSize().ShouldBeAtLeast(1);
        }

        private static int CaseInsensitiveStringCompare(string x, string y)
        {
            return string.Compare(x, y, StringComparison.OrdinalIgnoreCase);
        }

        private static int CaseSensitiveStringCompare(string x, string y)
        {
            return string.Compare(x, y, StringComparison.Ordinal);
        }

        private static SortedFolderEntries SetupDefaultEntries()
        {
            SortedFolderEntries sfe = new SortedFolderEntries();
            AddFiles(sfe, defaultFiles);
            AddFolders(sfe, defaultFolders);
            if (GVFSPlatform.Instance.Constants.CaseSensitiveFileSystem)
            {
                AddFiles(sfe, caseDifferingFiles);
                AddFolders(sfe, caseDifferingFolders);
            }

            sfe.Count.ShouldEqual(GetDefaultEntriesLength());
            return sfe;
        }

        private static int GetDefaultEntriesLength()
        {
            int length = defaultFiles.Length + defaultFolders.Length;
            if (GVFSPlatform.Instance.Constants.CaseSensitiveFileSystem)
            {
                length += caseDifferingFiles.Length + caseDifferingFolders.Length;
            }

            return length;
        }

        private static unsafe LazyUTF8String ConstructLazyUTF8String(string name)
        {
            byte[] buffer = Encoding.ASCII.GetBytes(name);
            fixed (byte* bufferPtr = buffer)
            {
                return LazyUTF8String.FromByteArray(bufferPtr, name.Length);
            }
        }

        private static void AddFiles(SortedFolderEntries entries, params string[] names)
        {
            for (int i = 0; i < names.Length; i++)
            {
                LazyUTF8String entryString = ConstructLazyUTF8String(names[i]);
                entries.AddFile(entryString, new byte[20]);
                entries.TryGetValue(entryString, out FolderEntryData folderEntryData).ShouldBeTrue();
                folderEntryData.ShouldNotBeNull();
                folderEntryData.IsFolder.ShouldBeFalse();
            }
        }

        private static void AddFolders(SortedFolderEntries entries, params string[] names)
        {
            for (int i = 0; i < names.Length; i++)
            {
                LazyUTF8String entryString = ConstructLazyUTF8String(names[i]);
                entries.GetOrAddFolder(new[] { entryString }, partIndex: 0, parentIsIncluded: true, rootSparseFolderData: new SparseFolderData());
                ValidateFolder(entries, entryString, isIncludedValue: true);
            }
        }

        private static void ValidateFolder(SortedFolderEntries entries, LazyUTF8String entryToValidate, bool isIncludedValue)
        {
            entries.TryGetValue(entryToValidate, out FolderEntryData folderEntryData).ShouldBeTrue();
            folderEntryData.ShouldNotBeNull();
            folderEntryData.IsFolder.ShouldBeTrue();

            FolderData folderData = folderEntryData as FolderData;
            folderData.ShouldNotBeNull();
            folderData.IsIncluded.ShouldEqual(isIncludedValue, "IsIncluded does not match expected value.");
        }
    }
}
