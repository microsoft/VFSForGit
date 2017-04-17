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
            runner.FileExists(path).ShouldEqual(false);
            runner.DirectoryExists(path).ShouldEqual(false);
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
                this.runner.FileExists(path).ShouldEqual(true, string.Format("Path does NOT exist: {0}", path));
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
                this.runner.ReadAllText(this.Path).ShouldEqual(expectedContents);
                return this;
            }

            public FileAdapter WithCaseMatchingName(string expectedName)
            {
                FileInfo fileInfo = new FileInfo(this.Path);
                string parentPath = System.IO.Path.GetDirectoryName(this.Path);
                DirectoryInfo parentInfo = new DirectoryInfo(parentPath);
                Assert.AreEqual(expectedName.Equals(parentInfo.GetFileSystemInfos(fileInfo.Name)[0].Name, StringComparison.Ordinal), true);
                return this;
            }

            public FileInfo WithInfo(DateTime creation, DateTime lastWrite, DateTime lastAccess)
            {
                FileInfo info = new FileInfo(this.Path);
                info.CreationTime.ShouldEqual(creation);
                info.LastAccessTime.ShouldEqual(lastAccess);
                info.LastWriteTime.ShouldEqual(lastWrite);

                return info;
            }

            public FileInfo WithInfo(DateTime creation, DateTime lastWrite, DateTime lastAccess, FileAttributes attributes)
            {
                FileInfo info = this.WithInfo(creation, lastWrite, lastAccess);
                info.Attributes.ShouldEqual(attributes);
                return info;
            }

            public FileInfo WithAttribute(FileAttributes attribute)
            {
                FileInfo info = new FileInfo(this.Path);
                info.Attributes.HasFlag(attribute).ShouldEqual(true);
                return info;
            }

            public FileInfo WithoutAttribute(FileAttributes attribute)
            {
                FileInfo info = new FileInfo(this.Path);
                info.Attributes.HasFlag(attribute).ShouldEqual(false);
                return info;
            }
        }

        public class DirectoryAdapter
        {
            private FileSystemRunner runner;

            public DirectoryAdapter(string path, FileSystemRunner runner)
            {
                this.runner = runner;
                this.runner.DirectoryExists(path).ShouldEqual(true);
                this.Path = path;
            }

            public string Path
            {
                get; private set;
            }

            public void WithNoItems()
            {
                Directory.EnumerateFileSystemEntries(this.Path).ShouldBeEmpty();
            }

            public FileSystemInfo WithOneItem()
            {
                return this.WithItems(1).Single();
            }

            public IEnumerable<FileSystemInfo> WithItems(int expectedCount)
            {
                IEnumerable<FileSystemInfo> items = this.WithItems();
                items.Count().ShouldEqual(expectedCount);
                return items;
            }

            public IEnumerable<FileSystemInfo> WithItems()
            {
                DirectoryInfo directory = new DirectoryInfo(this.Path);
                IEnumerable<FileSystemInfo> items = directory.GetFileSystemInfos();
                items.Any().ShouldEqual(true);
                return items;
            }

            public DirectoryAdapter WithDeepStructure(string otherPath, bool skipEmptyDirectories, bool ignoreCase = false)
            {
                otherPath.ShouldBeADirectory(this.runner);
                CompareDirectories(otherPath, this.Path, skipEmptyDirectories, ignoreCase);
                return this;
            }

            public DirectoryAdapter WithCaseMatchingName(string expectedName)
            {
                DirectoryInfo info = new DirectoryInfo(this.Path);
                string parentPath = System.IO.Path.GetDirectoryName(this.Path);
                DirectoryInfo parentInfo = new DirectoryInfo(parentPath);
                Assert.AreEqual(expectedName.Equals(parentInfo.GetDirectories(info.Name)[0].Name, StringComparison.Ordinal), true);
                return this;
            }

            public DirectoryInfo WithInfo(DateTime creation, DateTime lastWrite, DateTime lastAccess)
            {
                DirectoryInfo info = new DirectoryInfo(this.Path);
                info.CreationTime.ShouldEqual(creation);
                info.LastAccessTime.ShouldEqual(lastAccess);
                info.LastWriteTime.ShouldEqual(lastWrite);

                return info;
            }

            public DirectoryInfo WithInfo(DateTime creation, DateTime lastWrite, DateTime lastAccess, FileAttributes attributes, bool ignoreRecallAttributes)
            {
                DirectoryInfo info = this.WithInfo(creation, lastWrite, lastAccess);
                if (ignoreRecallAttributes)
                {
                    FileAttributes attributesWithoutRecall = info.Attributes & (FileAttributes)~(FileAttributeRecallOnOpen | FileAttributeRecallOnDataAccess);
                    attributesWithoutRecall.ShouldEqual(attributes);
                }
                else
                {
                    info.Attributes.ShouldEqual(attributes);
                }

                return info;
            }

            public DirectoryInfo WithAttribute(FileAttributes attribute)
            {
                DirectoryInfo info = new DirectoryInfo(this.Path);
                info.Attributes.HasFlag(attribute).ShouldEqual(true);
                return info;
            }

            private static void CompareDirectories(string expectedPath, string actualPath, bool skipEmptyDirectories, bool ignoreCase)
            {
                IEnumerable<string> expectedEntries = DepthFirstEnumeration(expectedPath, skipEmptyDirectories);
                IEnumerable<string> actualEntries = DepthFirstEnumeration(actualPath, skipEmptyDirectories);

                string dotGitFolder = System.IO.Path.DirectorySeparatorChar + TestConstants.DotGit.Root + System.IO.Path.DirectorySeparatorChar;
                IEnumerator<string> expectedEnumerator = expectedEntries
                    .Where(x => !x.Contains(dotGitFolder))
                    .Select(x => x.Replace(expectedPath, string.Empty))
                    .OrderBy(x => x)
                    .GetEnumerator();
                IEnumerator<string> actualEnumerator = actualEntries
                    .Where(x => !x.Contains(dotGitFolder))
                    .Select(x => x.Replace(actualPath, string.Empty))
                    .OrderBy(x => x)
                    .GetEnumerator();
                
                bool expectedMoved = expectedEnumerator.MoveNext();
                bool actualMoved = actualEnumerator.MoveNext();

                while (expectedMoved && actualMoved)
                {
                    if (ignoreCase)
                    {
                        actualEnumerator.Current.ToLowerInvariant().ShouldEqual(expectedEnumerator.Current.ToLowerInvariant());
                    }
                    else
                    {
                        actualEnumerator.Current.ShouldEqual(expectedEnumerator.Current);
                    }

                    expectedMoved = expectedEnumerator.MoveNext();
                    actualMoved = actualEnumerator.MoveNext();
                }

                StringBuilder errorEntries = new StringBuilder();
                if (expectedMoved)
                {
                    do
                    {
                        errorEntries.AppendLine(string.Format("Missing entry {0}", expectedEnumerator.Current));
                    }
                    while (expectedEnumerator.MoveNext());
                }

                while (actualEnumerator.MoveNext())
                {
                    errorEntries.AppendLine(string.Format("Extra entry {0}", actualEnumerator.Current));
                }

                if (errorEntries.Length > 0)
                {
                    Assert.Fail(errorEntries.ToString());
                }
            }

            private static IEnumerable<string> DepthFirstEnumeration(string path, bool skipEmptyDirectories)
            {
                foreach (string directoryPath in Directory.EnumerateDirectories(path))
                {
                    bool foundItem = false;
                    foreach (string subDirectoryItem in DepthFirstEnumeration(directoryPath, skipEmptyDirectories))
                    {
                        foundItem = true;
                        yield return subDirectoryItem;
                    }

                    if (!skipEmptyDirectories || foundItem)
                    {
                        yield return directoryPath;
                    }
                }
                
                foreach (string filePath in Directory.EnumerateFiles(path))
                {
                    yield return filePath;
                }
            }
        }
    }
}
