using GVFS.Common;
using GVFS.Tests.Should;
using GVFS.UnitTests.Mock;
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

        [TestCase]
        public void ParsesExistingDataCorrectly()
        {
            ModifiedPathsDatabase mpd = CreateModifiedPathsDatabase(ExistingEntries);
            mpd.Count.ShouldEqual(3);
            mpd.Contains("file.txt", isFolder: false).ShouldBeTrue();
            mpd.Contains("dir/file2.txt", isFolder: false).ShouldBeTrue();
            mpd.Contains("dir1/dir2/file3.txt", isFolder: false).ShouldBeTrue();
        }

        [TestCase]
        public void AddsDefaultEntry()
        {
            ModifiedPathsDatabase mpd = CreateModifiedPathsDatabase(initialContents: string.Empty);
            mpd.Count.ShouldEqual(1);
            mpd.Contains(DefaultEntry, isFolder: false).ShouldBeTrue();
        }

        [TestCase]
        public void BadDataFailsToLoad()
        {
            ConfigurableFileSystem fs = new ConfigurableFileSystem();
            fs.ExpectedFiles.Add(MockEntryFileName, new ReusableMemoryStream("This is bad data!\r\n"));

            string error;
            ModifiedPathsDatabase mpd;
            ModifiedPathsDatabase.TryLoadOrCreate(null, MockEntryFileName, fs, out mpd, out error).ShouldBeFalse();
            mpd.ShouldBeNull();
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

        private static void TestAddingPath(string path, bool isFolder = false)
        {
            TestAddingPath(path, path, isFolder);
        }

        private static void TestAddingPath(string pathToAdd, string pathInList, bool isFolder = false)
        {
            ModifiedPathsDatabase mpd = CreateModifiedPathsDatabase(initialContents: $"A {DefaultEntry}\r\n");
            bool isRetryable;
            mpd.TryAdd(pathToAdd, isFolder, out isRetryable);
            mpd.Count.ShouldEqual(2);
            mpd.Contains(pathInList, isFolder).ShouldBeTrue();
            mpd.Contains(ToGitPathSeparators(pathInList), isFolder).ShouldBeTrue();
            mpd.GetAllModifiedPaths().ShouldContainSingle(x => string.Compare(x, ToGitPathSeparators(pathInList), StringComparison.OrdinalIgnoreCase) == 0);
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
            ConfigurableFileSystem fs = new ConfigurableFileSystem();
            fs.ExpectedFiles.Add(MockEntryFileName, new ReusableMemoryStream(initialContents));

            string error;
            ModifiedPathsDatabase mpd;
            ModifiedPathsDatabase.TryLoadOrCreate(null, MockEntryFileName, fs, out mpd, out error).ShouldBeTrue();
            mpd.ShouldNotBeNull();
            return mpd;
        }
    }
}
