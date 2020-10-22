using GVFS.FunctionalTests.FileSystemRunners;
using GVFS.FunctionalTests.Tools;
using GVFS.Tests.Should;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace GVFS.FunctionalTests.Should
{
    public static class FileSystemShouldExtensions
    {
        // This attribute only appears in directory enumeration classes (FILE_DIRECTORY_INFORMATION,
        // FILE_BOTH_DIR_INFORMATION, etc.).  When this attribute is set, it means that the file or
        // directory has no physical representation on the local system; the item is virtual.  Opening the
        // item will be more expensive than normal, e.g. it will cause at least some of it to be fetched
        // from a remote store.
        //
        // #define FILE_ATTRIBUTE_RECALL_ON_OPEN       0x00040000  // winnt
        public const int FileAttributeRecallOnOpen = 0x00040000;

        // When this attribute is set, it means that the file or directory is not fully present locally.
        // For a file that means that not all of its data is on local storage (e.g. it is sparse with some
        // data still in remote storage).  For a directory it means that some of the directory contents are
        // being virtualized from another location.  Reading the file / enumerating the directory will be
        // more expensive than normal, e.g. it will cause at least some of the file/directory content to be
        // fetched from a remote store.  Only kernel-mode callers can set this bit.
        //
        // #define FILE_ATTRIBUTE_RECALL_ON_DATA_ACCESS 0x00400000 // winnt
        public const int FileAttributeRecallOnDataAccess = 0x00400000;

        public static FileAdapter ShouldBeAFile(this string path, FileSystemRunner runner)
        {
            return new FileAdapter(path, runner);
        }

        public static FileAdapter ShouldBeAFile(this FileSystemInfo fileSystemInfo, FileSystemRunner runner)
        {
            return new FileAdapter(fileSystemInfo.FullName, runner);
        }

        public static DirectoryAdapter ShouldBeADirectory(this string path, FileSystemRunner runner)
        {
            return new DirectoryAdapter(path, runner);
        }

        public static DirectoryAdapter ShouldBeADirectory(this FileSystemInfo fileSystemInfo, FileSystemRunner runner)
        {
            return new DirectoryAdapter(fileSystemInfo.FullName, runner);
        }

        public static string ShouldNotExistOnDisk(this string path, FileSystemRunner runner)
        {
            runner.FileExists(path).ShouldEqual(false, "File " + path + " exists when it should not");
            runner.DirectoryExists(path).ShouldEqual(false, "Directory " + path + " exists when it should not");
            return path;
        }

        public class FileAdapter
        {
            private const int MaxWaitMS = 2000;
            private const int ThreadSleepMS = 100;

            private FileSystemRunner runner;

            public FileAdapter(string path, FileSystemRunner runner)
            {
                this.runner = runner;
                this.runner.FileExists(path).ShouldEqual(true, "Path does NOT exist: " + path);
                this.Path = path;
            }

            public string Path
            {
                get; private set;
            }

            public string WithContents()
            {
                return this.runner.ReadAllText(this.Path);
            }

            public FileAdapter WithContents(string expectedContents)
            {
                this.runner.ReadAllText(this.Path).ShouldEqual(expectedContents, "The contents of " + this.Path + " do not match what was expected");
                return this;
            }

            public FileAdapter WithCaseMatchingName(string expectedName)
            {
                FileInfo fileInfo = new FileInfo(this.Path);
                string parentPath = System.IO.Path.GetDirectoryName(this.Path);
                DirectoryInfo parentInfo = new DirectoryInfo(parentPath);
                expectedName.Equals(parentInfo.GetFileSystemInfos(fileInfo.Name)[0].Name, StringComparison.Ordinal)
                    .ShouldEqual(true, this.Path + " does not have the correct case");
                return this;
            }

            public FileInfo WithInfo(DateTime creation, DateTime lastWrite, DateTime lastAccess)
            {
                FileInfo info = new FileInfo(this.Path);
                info.CreationTime.ShouldEqual(creation, "Creation time does not match");
                info.LastAccessTime.ShouldEqual(lastAccess, "Last access time does not match");
                info.LastWriteTime.ShouldEqual(lastWrite, "Last write time does not match");

                return info;
            }

            public FileInfo WithInfo(DateTime creation, DateTime lastWrite, DateTime lastAccess, FileAttributes attributes)
            {
                FileInfo info = this.WithInfo(creation, lastWrite, lastAccess);
                info.Attributes.ShouldEqual(attributes, "Attributes do not match");
                return info;
            }

            public FileInfo WithAttribute(FileAttributes attribute)
            {
                FileInfo info = new FileInfo(this.Path);
                info.Attributes.HasFlag(attribute).ShouldEqual(true, "Attributes do not have correct flag: " + attribute);
                return info;
            }

            public FileInfo WithoutAttribute(FileAttributes attribute)
            {
                FileInfo info = new FileInfo(this.Path);
                info.Attributes.HasFlag(attribute).ShouldEqual(false, "Attributes have incorrect flag: " + attribute);
                return info;
            }
        }

        public class DirectoryAdapter
        {
            private FileSystemRunner runner;

            public DirectoryAdapter(string path, FileSystemRunner runner)
            {
                this.runner = runner;
                this.runner.DirectoryExists(path).ShouldEqual(true, "Directory " + path + " does not exist");
                this.Path = path;
            }

            public string Path
            {
                get; private set;
            }

            public void WithNoItems()
            {
                Directory.EnumerateFileSystemEntries(this.Path).ShouldBeEmpty(this.Path + " is not empty");
            }

            public void WithNoItems(string searchPattern)
            {
                Directory.EnumerateFileSystemEntries(this.Path, searchPattern).ShouldBeEmpty(this.Path + " is not empty");
            }

            public FileSystemInfo WithOneItem()
            {
                return this.WithItems(1).Single();
            }

            public IEnumerable<FileSystemInfo> WithItems(int expectedCount)
            {
                IEnumerable<FileSystemInfo> items = this.WithItems();
                items.Count().ShouldEqual(expectedCount, this.Path + " has an invalid number of items");
                return items;
            }

            public IEnumerable<FileSystemInfo> WithItems()
            {
                return this.WithItems("*");
            }

            public IEnumerable<FileInfo> WithFiles()
            {
                IEnumerable<FileSystemInfo> items = this.WithItems();
                IEnumerable<FileInfo> files = items.Where(info => info is FileInfo).Cast<FileInfo>();
                files.Any().ShouldEqual(true, this.Path + " does not have any files. Contents: " + string.Join(",", items));
                return files;
            }

            public IEnumerable<DirectoryInfo> WithDirectories()
            {
                IEnumerable<FileSystemInfo> items = this.WithItems();
                IEnumerable<DirectoryInfo> directories = items.Where(info => info is DirectoryInfo).Cast<DirectoryInfo>();
                directories.Any().ShouldEqual(true, this.Path + " does not have any directories. Contents: " + string.Join(",", items));
                return directories;
            }

            public IEnumerable<FileSystemInfo> WithItems(string searchPattern)
            {
                DirectoryInfo directory = new DirectoryInfo(this.Path);
                IEnumerable<FileSystemInfo> items = directory.GetFileSystemInfos(searchPattern);
                items.Any().ShouldEqual(true, this.Path + " does not have any items");
                return items;
            }

            public DirectoryAdapter WithDeepStructure(
                FileSystemRunner fileSystem,
                string otherPath,
                bool ignoreCase = false,
                bool compareContent = false,
                string[] withinPrefixes = null)
            {
                otherPath.ShouldBeADirectory(this.runner);
                CompareDirectories(fileSystem, otherPath, this.Path, ignoreCase, compareContent, withinPrefixes);
                return this;
            }

            public DirectoryAdapter WithCaseMatchingName(string expectedName)
            {
                DirectoryInfo info = new DirectoryInfo(this.Path);
                string parentPath = System.IO.Path.GetDirectoryName(this.Path);
                DirectoryInfo parentInfo = new DirectoryInfo(parentPath);
                expectedName.Equals(parentInfo.GetDirectories(info.Name)[0].Name, StringComparison.Ordinal)
                    .ShouldEqual(true, this.Path + " does not have the correct case");
                return this;
            }

            public DirectoryInfo WithInfo(DateTime creation, DateTime lastWrite, DateTime lastAccess)
            {
                DirectoryInfo info = new DirectoryInfo(this.Path);
                info.CreationTime.ShouldEqual(creation, "Creation time does not match");
                info.LastAccessTime.ShouldEqual(lastAccess, "Last access time does not match");
                info.LastWriteTime.ShouldEqual(lastWrite, "Last write time does not match");

                return info;
            }

            public DirectoryInfo WithInfo(DateTime creation, DateTime lastWrite, DateTime lastAccess, FileAttributes attributes, bool ignoreRecallAttributes)
            {
                DirectoryInfo info = this.WithInfo(creation, lastWrite, lastAccess);
                if (ignoreRecallAttributes)
                {
                    FileAttributes attributesWithoutRecall = info.Attributes & (FileAttributes)~(FileAttributeRecallOnOpen | FileAttributeRecallOnDataAccess);
                    attributesWithoutRecall.ShouldEqual(attributes, "Attributes do not match");
                }
                else
                {
                    info.Attributes.ShouldEqual(attributes, "Attributes do not match");
                }

                return info;
            }

            public DirectoryInfo WithAttribute(FileAttributes attribute)
            {
                DirectoryInfo info = new DirectoryInfo(this.Path);
                info.Attributes.HasFlag(attribute).ShouldEqual(true, "Attributes do not have correct flag: " + attribute);
                return info;
            }

            private static bool IsMatchedPath(FileSystemInfo info, string repoRoot, string[] prefixes, bool ignoreCase)
            {
                if (prefixes == null || prefixes.Length == 0)
                {
                    return true;
                }

                string localPath = info.FullName.Substring(repoRoot.Length + 1);
                StringComparison pathComparison = ignoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

                if (localPath.Equals(".git", pathComparison))
                {
                    // Include _just_ the .git folder.
                    // All sub-items are not included in the enumerator.
                    return true;
                }

                if (!localPath.Contains(System.IO.Path.DirectorySeparatorChar) &&
                    (info.Attributes & FileAttributes.Directory) != FileAttributes.Directory)
                {
                    // If it is a file in the root folder, then include it.
                    return true;
                }

                foreach (string prefixDir in prefixes)
                {
                    if (localPath.StartsWith(prefixDir, pathComparison))
                    {
                        return true;
                    }

                    if (prefixDir.StartsWith(localPath, pathComparison) &&
                        Directory.Exists(info.FullName))
                    {
                        // For example: localPath = "GVFS" and prefix is "GVFS\\GVFS".
                        return true;
                    }
                }

                return false;
            }

            private static void CompareDirectories(
                FileSystemRunner fileSystem,
                string expectedPath,
                string actualPath,
                bool ignoreCase,
                bool compareContent,
                string[] withinPrefixes)
            {
                IEnumerable<FileSystemInfo> expectedEntries = new DirectoryInfo(expectedPath).EnumerateFileSystemInfos("*", SearchOption.AllDirectories);
                IEnumerable<FileSystemInfo> actualEntries = new DirectoryInfo(actualPath).EnumerateFileSystemInfos("*", SearchOption.AllDirectories);

                string dotGitFolder = System.IO.Path.DirectorySeparatorChar + TestConstants.DotGit.Root + System.IO.Path.DirectorySeparatorChar;
                IEnumerator<FileSystemInfo> expectedEnumerator = expectedEntries
                    .Where(x => !x.FullName.Contains(dotGitFolder))
                    .OrderBy(x => x.FullName)
                    .Where(x => IsMatchedPath(x, expectedPath, withinPrefixes, ignoreCase))
                    .GetEnumerator();
                IEnumerator<FileSystemInfo> actualEnumerator = actualEntries
                    .Where(x => !x.FullName.Contains(dotGitFolder))
                    .OrderBy(x => x.FullName)
                    .GetEnumerator();

                bool expectedMoved = expectedEnumerator.MoveNext();
                bool actualMoved = actualEnumerator.MoveNext();

                while (expectedMoved && actualMoved)
                {
                    bool nameIsEqual = false;
                    if (ignoreCase)
                    {
                        nameIsEqual = actualEnumerator.Current.Name.Equals(expectedEnumerator.Current.Name, StringComparison.OrdinalIgnoreCase);
                    }
                    else
                    {
                        nameIsEqual = actualEnumerator.Current.Name.Equals(expectedEnumerator.Current.Name, StringComparison.Ordinal);
                    }

                    if (!nameIsEqual)
                    {
                        if ((expectedEnumerator.Current.Attributes & FileAttributes.Directory) == FileAttributes.Directory)
                        {
                            // ignoring directories that are empty in the control repo because GVFS does a better job at removing
                            // empty directories because it is tracking placeholder folders and removes them
                            // Only want to check for an empty directory if the names don't match. If the names match and
                            // both expected and actual directories are empty that is okay
                            if (Directory.GetFileSystemEntries(expectedEnumerator.Current.FullName, "*", SearchOption.TopDirectoryOnly).Length == 0)
                            {
                                expectedMoved = expectedEnumerator.MoveNext();

                                continue;
                            }
                        }

                        Assert.Fail($"File names don't match: expected: {expectedEnumerator.Current.FullName} actual: {actualEnumerator.Current.FullName}");
                    }

                    if ((expectedEnumerator.Current.Attributes & FileAttributes.Directory) == FileAttributes.Directory)
                    {
                        (actualEnumerator.Current.Attributes & FileAttributes.Directory).ShouldEqual(FileAttributes.Directory, $"expected directory path: {expectedEnumerator.Current.FullName} actual file path: {actualEnumerator.Current.FullName}");
                    }
                    else
                    {
                        (actualEnumerator.Current.Attributes & FileAttributes.Directory).ShouldNotEqual(FileAttributes.Directory, $"expected file path: {expectedEnumerator.Current.FullName} actual directory path: {actualEnumerator.Current.FullName}");

                        FileInfo expectedFileInfo = (expectedEnumerator.Current as FileInfo).ShouldNotBeNull();
                        FileInfo actualFileInfo = (actualEnumerator.Current as FileInfo).ShouldNotBeNull();
                        actualFileInfo.Length.ShouldEqual(expectedFileInfo.Length, $"File lengths do not agree expected: {expectedEnumerator.Current.FullName} = {expectedFileInfo.Length} actual: {actualEnumerator.Current.FullName} = {actualFileInfo.Length}");

                        if (compareContent)
                        {
                            actualFileInfo.FullName.ShouldBeAFile(fileSystem).WithContents(expectedFileInfo.FullName.ShouldBeAFile(fileSystem).WithContents());
                        }
                    }

                    expectedMoved = expectedEnumerator.MoveNext();
                    actualMoved = actualEnumerator.MoveNext();
                }

                StringBuilder errorEntries = new StringBuilder();

                if (expectedMoved)
                {
                    do
                    {
                        errorEntries.AppendLine(string.Format("Missing entry {0}", expectedEnumerator.Current.FullName));
                    }
                    while (expectedEnumerator.MoveNext());
                }

                while (actualEnumerator.MoveNext())
                {
                    errorEntries.AppendLine(string.Format("Extra entry {0}", actualEnumerator.Current.FullName));
                }

                if (errorEntries.Length > 0)
                {
                    Assert.Fail(errorEntries.ToString());
                }
            }
        }
    }
}
