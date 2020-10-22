using GVFS.Common;
using GVFS.Tests.Should;
using GVFS.UnitTests.Mock;
using GVFS.UnitTests.Mock.Common;
using GVFS.UnitTests.Mock.FileSystem;
using NUnit.Framework;
using System;
using System.IO;

namespace GVFS.UnitTests.Common
{
    [TestFixture]
    public class ModifiedPathsDatabaseTests
    {
        private const string MockEntryFileName = "mock:\\entries.dat";

        private const string DefaultEntry = @".gitattributes";
        private const string ExistingEntries = @"A file.txt
A dir/file2.txt
A dir1/dir2/file3.txt
";
        private const string EntriesToCompress = @"A file.txt
D deleted.txt
A dir/file2.txt
A dir/dir3/dir4/
A dir1/dir2/file3.txt
A dir/
D deleted/
A dir1/dir2/
A dir1/file.txt
A dir1/dir2/dir3/dir4/dir5/
A dir/dir2/file3.txt
A dir/dir4/dir5/
D dir/dir2/deleted.txt
A dir1/dir2
";

        [TestCase]
        public void ParsesExistingDataCorrectly()
        {
            ModifiedPathsDatabase modifiedPathsDatabase = CreateModifiedPathsDatabase(ExistingEntries);
            modifiedPathsDatabase.Count.ShouldEqual(3);
            modifiedPathsDatabase.Contains("file.txt", isFolder: false).ShouldBeTrue();
            modifiedPathsDatabase.Contains("dir/file2.txt", isFolder: false).ShouldBeTrue();
            modifiedPathsDatabase.Contains("dir1/dir2/file3.txt", isFolder: false).ShouldBeTrue();
        }

        [TestCase]
        public void AddsDefaultEntry()
        {
            ModifiedPathsDatabase modifiedPathsDatabase = CreateModifiedPathsDatabase(initialContents: string.Empty);
            modifiedPathsDatabase.Count.ShouldEqual(1);
            modifiedPathsDatabase.Contains(DefaultEntry, isFolder: false).ShouldBeTrue();
        }

        [TestCase]
        public void BadDataFailsToLoad()
        {
            ConfigurableFileSystem configurableFileSystem = new ConfigurableFileSystem();
            configurableFileSystem.ExpectedFiles.Add(MockEntryFileName, new ReusableMemoryStream("This is bad data!\r\n"));

            string error;
            ModifiedPathsDatabase modifiedPathsDatabase;
            ModifiedPathsDatabase.TryLoadOrCreate(null, MockEntryFileName, configurableFileSystem, out modifiedPathsDatabase, out error).ShouldBeFalse();
            modifiedPathsDatabase.ShouldBeNull();
        }

        [TestCase]
        public void BasicAddFile()
        {
            TestAddingPath(Path.Combine("dir", "somefile.txt"));
        }

        [TestCase]
        public void DirectorySeparatorsNormalized()
        {
            TestAddingPath(Path.Combine("dir", "dir2", "dir3", "somefile.txt"));
        }

        [TestCase]
        public void BeginningDirectorySeparatorRemoved()
        {
            string filePath = Path.Combine("dir", "somefile.txt");
            TestAddingPath(pathToAdd: Path.DirectorySeparatorChar + filePath, pathInList: filePath);
        }

        [TestCase]
        public void DirectorySeparatorAddedForFolder()
        {
            TestAddingPath(pathToAdd: Path.Combine("dir", "subdir"), pathInList: Path.Combine("dir", "subdir") + Path.DirectorySeparatorChar, isFolder: true);
        }

        [TestCase]
        public void EntryNotAddedIfParentDirectoryExists()
        {
            ModifiedPathsDatabase modifiedPathsDatabase = CreateModifiedPathsDatabase(initialContents: "A dir/\r\n");
            modifiedPathsDatabase.Count.ShouldEqual(1);
            modifiedPathsDatabase.Contains("dir", isFolder: true).ShouldBeTrue();

            // Try adding a file for the directory that is in the modified paths
            modifiedPathsDatabase.TryAdd("dir/file.txt", isFolder: false, isRetryable: out _);
            modifiedPathsDatabase.Count.ShouldEqual(1);
            modifiedPathsDatabase.Contains("dir", isFolder: true).ShouldBeTrue();

            // Try adding a directory for the directory that is in the modified paths
            modifiedPathsDatabase.TryAdd("dir/dir2", isFolder: true, isRetryable: out _);
            modifiedPathsDatabase.Count.ShouldEqual(1);
            modifiedPathsDatabase.Contains("dir", isFolder: true).ShouldBeTrue();

            // Try adding a file for a directory that is not in the modified paths
            modifiedPathsDatabase.TryAdd("dir2/file.txt", isFolder: false, isRetryable: out _);
            modifiedPathsDatabase.Count.ShouldEqual(2);
            modifiedPathsDatabase.Contains("dir", isFolder: true).ShouldBeTrue();
            modifiedPathsDatabase.Contains("dir2/file.txt", isFolder: false).ShouldBeTrue();

            // Try adding a directory for a the directory that is not in the modified paths
            modifiedPathsDatabase.TryAdd("dir2/dir", isFolder: true, isRetryable: out _);
            modifiedPathsDatabase.Count.ShouldEqual(3);
            modifiedPathsDatabase.Contains("dir", isFolder: true).ShouldBeTrue();
            modifiedPathsDatabase.Contains("dir2/file.txt", isFolder: false).ShouldBeTrue();
            modifiedPathsDatabase.Contains("dir2/dir", isFolder: true).ShouldBeTrue();

            // Try adding a file in a subdirectory that is in the modified paths
            modifiedPathsDatabase.TryAdd("dir2/dir/file.txt", isFolder: false, isRetryable: out _);
            modifiedPathsDatabase.Count.ShouldEqual(3);
            modifiedPathsDatabase.Contains("dir", isFolder: true).ShouldBeTrue();
            modifiedPathsDatabase.Contains("dir2/file.txt", isFolder: false).ShouldBeTrue();
            modifiedPathsDatabase.Contains("dir2/dir", isFolder: true).ShouldBeTrue();

            // Try adding a directory for a subdirectory that is in the modified paths
            modifiedPathsDatabase.TryAdd("dir2/dir/dir3", isFolder: true, isRetryable: out _);
            modifiedPathsDatabase.Count.ShouldEqual(3);
            modifiedPathsDatabase.Contains("dir", isFolder: true).ShouldBeTrue();
            modifiedPathsDatabase.Contains("dir2/file.txt", isFolder: false).ShouldBeTrue();
            modifiedPathsDatabase.Contains("dir2/dir", isFolder: true).ShouldBeTrue();
        }

        [TestCase]
        public void RemoveEntriesWithParentFolderEntry()
        {
            ModifiedPathsDatabase modifiedPathsDatabase = CreateModifiedPathsDatabase(EntriesToCompress);
            modifiedPathsDatabase.RemoveEntriesWithParentFolderEntry(new MockTracer());
            modifiedPathsDatabase.Count.ShouldEqual(5);
            modifiedPathsDatabase.Contains("file.txt", isFolder: false).ShouldBeTrue();
            modifiedPathsDatabase.Contains("dir/", isFolder: true).ShouldBeTrue();
            modifiedPathsDatabase.Contains("dir1/dir2", isFolder: false).ShouldBeTrue();
            modifiedPathsDatabase.Contains("dir1/dir2/", isFolder: true).ShouldBeTrue();
            modifiedPathsDatabase.Contains("dir1/file.txt", isFolder: false).ShouldBeTrue();
        }

        private static void TestAddingPath(string path, bool isFolder = false)
        {
            TestAddingPath(path, path, isFolder);
        }

        private static void TestAddingPath(string pathToAdd, string pathInList, bool isFolder = false)
        {
            ModifiedPathsDatabase modifiedPathsDatabase = CreateModifiedPathsDatabase(initialContents: $"A {DefaultEntry}\r\n");
            bool isRetryable;
            modifiedPathsDatabase.TryAdd(pathToAdd, isFolder, out isRetryable);
            modifiedPathsDatabase.Count.ShouldEqual(2);
            modifiedPathsDatabase.Contains(pathInList, isFolder).ShouldBeTrue();
            modifiedPathsDatabase.Contains(ToGitPathSeparators(pathInList), isFolder).ShouldBeTrue();
            modifiedPathsDatabase.GetAllModifiedPaths().ShouldContainSingle(x => string.Compare(x, ToGitPathSeparators(pathInList), GVFSPlatform.Instance.Constants.PathComparison) == 0);
        }

        private static string ToGitPathSeparators(string path)
        {
            return path.Replace(Path.DirectorySeparatorChar, GVFSConstants.GitPathSeparator);
        }

        private static string ToPathSeparators(string path)
        {
            return path.Replace(GVFSConstants.GitPathSeparator, Path.DirectorySeparatorChar);
        }

        private static ModifiedPathsDatabase CreateModifiedPathsDatabase(string initialContents)
        {
            ConfigurableFileSystem configurableFileSystem = new ConfigurableFileSystem();
            configurableFileSystem.ExpectedFiles.Add(MockEntryFileName, new ReusableMemoryStream(initialContents));

            string error;
            ModifiedPathsDatabase modifiedPathsDatabase;
            ModifiedPathsDatabase.TryLoadOrCreate(null, MockEntryFileName, configurableFileSystem, out modifiedPathsDatabase, out error).ShouldBeTrue();
            modifiedPathsDatabase.ShouldNotBeNull();
            return modifiedPathsDatabase;
        }
    }
}
