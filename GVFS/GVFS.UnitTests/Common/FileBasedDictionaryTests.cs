using GVFS.Common;
using GVFS.Tests.Should;
using GVFS.UnitTests.Category;
using GVFS.UnitTests.Mock;
using GVFS.UnitTests.Mock.FileSystem;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;

namespace GVFS.UnitTests.Common
{
    [TestFixture]
    public class FileBasedDictionaryTests
    {
        private const string MockEntryFileName = "mock:\\entries.dat";

        private const string TestKey = "akey";
        private const string TestValue = "avalue";
        private const string UpdatedTestValue = "avalue2";

        private const string TestEntry = "A {\"Key\":\"akey\",\"Value\":\"avalue\"}\r\n";
        private const string UpdatedTestEntry = "A {\"Key\":\"akey\",\"Value\":\"avalue2\"}\r\n";

        private const string TestKey2 = "bkey";
        private const string TestValue2 = "bvalue";
        private const string UpdatedTestValue2 = "bvalue2";

        private const string TestEntry2 = "A {\"Key\":\"bkey\",\"Value\":\"bvalue\"}\r\n";
        private const string UpdatedTestEntry2 = "A {\"Key\":\"bkey\",\"Value\":\"bvalue2\"}\r\n";

        [TestCase]
        public void ParsesExistingDataCorrectly()
        {
            FileBasedDictionaryFileSystem fs = new FileBasedDictionaryFileSystem();
            FileBasedDictionary<string, string> dut = CreateFileBasedDictionary(fs, TestEntry);

            string value;
            dut.TryGetValue(TestKey, out value).ShouldEqual(true);
            value.ShouldEqual(TestValue);
        }

        [TestCase]
        public void SetValueAndFlushWritesEntryToDisk()
        {
            FileBasedDictionaryFileSystem fs = new FileBasedDictionaryFileSystem();
            FileBasedDictionary<string, string> dut = CreateFileBasedDictionary(fs, string.Empty);
            dut.SetValueAndFlush(TestKey, TestValue);

            this.FileBasedDictionaryFileSystemShouldContain(fs, new[] { TestEntry });
        }

        [TestCase]
        public void SetValuesAndFlushWritesEntriesToDisk()
        {
            FileBasedDictionaryFileSystem fs = new FileBasedDictionaryFileSystem();
            FileBasedDictionary<string, string> dut = CreateFileBasedDictionary(fs, string.Empty);
            dut.SetValuesAndFlush(
                new[]
                {
                    new KeyValuePair<string, string>(TestKey, TestValue),
                    new KeyValuePair<string, string>(TestKey2, TestValue2),
                });
            this.FileBasedDictionaryFileSystemShouldContain(fs, new[] { TestEntry, TestEntry2 });
        }

        [TestCase]
        public void SetValuesAndFlushWritesNewEntryAndUpdatesExistingEntryOnDisk()
        {
            FileBasedDictionaryFileSystem fs = new FileBasedDictionaryFileSystem();
            FileBasedDictionary<string, string> dut = CreateFileBasedDictionary(fs, string.Empty);

            // Add TestKey to disk
            dut.SetValueAndFlush(TestKey, TestValue);
            fs.ExpectedFiles[MockEntryFileName].ReadAsString().ShouldEqual(TestEntry);

            // This call to SetValuesAndFlush should update TestKey and write TestKey2
            dut.SetValuesAndFlush(
                new[]
                {
                    new KeyValuePair<string, string>(TestKey, UpdatedTestValue),
                    new KeyValuePair<string, string>(TestKey2, TestValue2),
                });
            this.FileBasedDictionaryFileSystemShouldContain(fs, new[] { UpdatedTestEntry, TestEntry2 });
        }

        [TestCase]
        public void SetValuesAndFlushWritesUpdatesExistingEntriesOnDisk()
        {
            FileBasedDictionaryFileSystem fs = new FileBasedDictionaryFileSystem();
            FileBasedDictionary<string, string> dut = CreateFileBasedDictionary(fs, string.Empty);

            dut.SetValuesAndFlush(
                new[]
                {
                    new KeyValuePair<string, string>(TestKey, TestValue),
                    new KeyValuePair<string, string>(TestKey2, TestValue2),
                });
            this.FileBasedDictionaryFileSystemShouldContain(fs, new[] { TestEntry, TestEntry2 });

            dut.SetValuesAndFlush(
                new[]
                {
                    new KeyValuePair<string, string>(TestKey, UpdatedTestValue),
                    new KeyValuePair<string, string>(TestKey2, UpdatedTestValue2),
                });
            this.FileBasedDictionaryFileSystemShouldContain(fs, new[] { UpdatedTestEntry, UpdatedTestEntry2 });
        }

        [TestCase]
        public void SetValuesAndFlushUsesLastValueWhenKeyDuplicated()
        {
            FileBasedDictionaryFileSystem fs = new FileBasedDictionaryFileSystem();
            FileBasedDictionary<string, string> dut = CreateFileBasedDictionary(fs, string.Empty);

            dut.SetValuesAndFlush(
                new[]
                {
                    new KeyValuePair<string, string>(TestKey, TestValue),
                    new KeyValuePair<string, string>(TestKey, UpdatedTestValue),
                });
            this.FileBasedDictionaryFileSystemShouldContain(fs, new[] { UpdatedTestEntry });
        }

        [TestCase]
        public void SetValueAndFlushUpdatedEntryOnDisk()
        {
            FileBasedDictionaryFileSystem fs = new FileBasedDictionaryFileSystem();
            FileBasedDictionary<string, string> dut = CreateFileBasedDictionary(fs, TestEntry);
            dut.SetValueAndFlush(TestKey, UpdatedTestValue);

            this.FileBasedDictionaryFileSystemShouldContain(fs, new[] { UpdatedTestEntry });
        }

        [TestCase]
        [NUnit.Framework.Category(CategoryConstants.ExceptionExpected)]
        public void SetValueAndFlushRecoversFromFailedOpenFileStream()
        {
            FileBasedDictionaryFileSystem fs = new FileBasedDictionaryFileSystem(
                openFileStreamFailurePath: MockEntryFileName + ".tmp",
                maxOpenFileStreamFailures: 5,
                fileExistsFailurePath: null,
                maxFileExistsFailures: 0,
                maxMoveAndOverwriteFileFailures: 5);

            FileBasedDictionary<string, string> dut = CreateFileBasedDictionary(fs, string.Empty);
            dut.SetValueAndFlush(TestKey, TestValue);

            this.FileBasedDictionaryFileSystemShouldContain(fs, new[] { TestEntry });
        }

        [TestCase]
        public void SetValueAndFlushRecoversFromDeletedTmp()
        {
            FileBasedDictionaryFileSystem fs = new FileBasedDictionaryFileSystem(
                openFileStreamFailurePath: null,
                maxOpenFileStreamFailures: 0,
                fileExistsFailurePath: MockEntryFileName + ".tmp",
                maxFileExistsFailures: 5,
                maxMoveAndOverwriteFileFailures: 0);

            FileBasedDictionary<string, string> dut = CreateFileBasedDictionary(fs, string.Empty);
            dut.SetValueAndFlush(TestKey, TestValue);

            this.FileBasedDictionaryFileSystemShouldContain(fs, new[] { TestEntry });
        }

        [TestCase]
        [NUnit.Framework.Category(CategoryConstants.ExceptionExpected)]
        public void SetValueAndFlushRecoversFromFailedOverwrite()
        {
            FileBasedDictionaryFileSystem fs = new FileBasedDictionaryFileSystem(
                openFileStreamFailurePath: null,
                maxOpenFileStreamFailures: 0,
                fileExistsFailurePath: null,
                maxFileExistsFailures: 0,
                maxMoveAndOverwriteFileFailures: 5);

            FileBasedDictionary<string, string> dut = CreateFileBasedDictionary(fs, string.Empty);
            dut.SetValueAndFlush(TestKey, TestValue);

            this.FileBasedDictionaryFileSystemShouldContain(fs, new[] { TestEntry });
        }

        [TestCase]
        [NUnit.Framework.Category(CategoryConstants.ExceptionExpected)]
        public void SetValueAndFlushRecoversFromDeletedTempAndFailedOverwrite()
        {
            FileBasedDictionaryFileSystem fs = new FileBasedDictionaryFileSystem(
                openFileStreamFailurePath: null,
                maxOpenFileStreamFailures: 0,
                fileExistsFailurePath: MockEntryFileName + ".tmp",
                maxFileExistsFailures: 5,
                maxMoveAndOverwriteFileFailures: 5);

            FileBasedDictionary<string, string> dut = CreateFileBasedDictionary(fs, string.Empty);
            dut.SetValueAndFlush(TestKey, TestValue);

            this.FileBasedDictionaryFileSystemShouldContain(fs, new[] { TestEntry });
        }

        [TestCase]
        [NUnit.Framework.Category(CategoryConstants.ExceptionExpected)]
        public void SetValueAndFlushRecoversFromMixOfFailures()
        {
            FileBasedDictionaryFileSystem fs = new FileBasedDictionaryFileSystem(failuresAcrossOpenExistsAndOverwritePath: MockEntryFileName + ".tmp");

            FileBasedDictionary<string, string> dut = CreateFileBasedDictionary(fs, string.Empty);
            dut.SetValueAndFlush(TestKey, TestValue);

            this.FileBasedDictionaryFileSystemShouldContain(fs, new[] { TestEntry });
        }

        [TestCase]
        public void DeleteFlushesToDisk()
        {
            FileBasedDictionaryFileSystem fs = new FileBasedDictionaryFileSystem();
            FileBasedDictionary<string, string> dut = CreateFileBasedDictionary(fs, TestEntry);
            dut.RemoveAndFlush(TestKey);

            fs.ExpectedFiles[MockEntryFileName].ReadAsString().ShouldBeEmpty();
        }

        [TestCase]
        public void DeleteUnusedKeyFlushesToDisk()
        {
            FileBasedDictionaryFileSystem fs = new FileBasedDictionaryFileSystem();
            FileBasedDictionary<string, string> dut = CreateFileBasedDictionary(fs, TestEntry);
            dut.RemoveAndFlush("UnusedKey");

            fs.ExpectedFiles[MockEntryFileName].ReadAsString().ShouldEqual(TestEntry);
        }

        private static FileBasedDictionary<string, string> CreateFileBasedDictionary(FileBasedDictionaryFileSystem fs, string initialContents)
        {
            fs.ExpectedFiles.Add(MockEntryFileName, new ReusableMemoryStream(initialContents));

            fs.ExpectedOpenFileStreams.Add(MockEntryFileName + ".tmp", new ReusableMemoryStream(string.Empty));
            fs.ExpectedOpenFileStreams.Add(MockEntryFileName, fs.ExpectedFiles[MockEntryFileName]);

            string error;
            FileBasedDictionary<string, string> dut;
            FileBasedDictionary<string, string>.TryCreate(null, MockEntryFileName, fs, out dut, out error).ShouldEqual(true, error);
            dut.ShouldNotBeNull();

            // FileBasedDictionary should only open a file stream to the non-tmp file when being created.  At all other times it should
            // write to a tmp file and overwrite the non-tmp file
            fs.ExpectedOpenFileStreams.Remove(MockEntryFileName);

            return dut;
        }

        private void FileBasedDictionaryFileSystemShouldContain(
            FileBasedDictionaryFileSystem fs,
            IList<string> expectedEntries)
        {
            string delimiter = "\r\n";
            string fileContents = fs.ExpectedFiles[MockEntryFileName].ReadAsString();
            fileContents.Substring(fileContents.Length - delimiter.Length).ShouldEqual(delimiter);

            // Remove the trailing delimiter
            fileContents = fileContents.Substring(0, fileContents.Length - delimiter.Length);

            string[] fileLines = fileContents.Split(new[] { delimiter }, StringSplitOptions.None);
            fileLines.Length.ShouldEqual(expectedEntries.Count);

            foreach (string expectedEntry in expectedEntries)
            {
                fileLines.ShouldContain(line => line.Equals(expectedEntry.Substring(0, expectedEntry.Length - delimiter.Length)));
            }
        }

        private class FileBasedDictionaryFileSystem : ConfigurableFileSystem
        {
            private int openFileStreamFailureCount;
            private int maxOpenFileStreamFailures;
            private string openFileStreamFailurePath;

            private int fileExistsFailureCount;
            private int maxFileExistsFailures;
            private string fileExistsFailurePath;

            private int moveAndOverwriteFileFailureCount;
            private int maxMoveAndOverwriteFileFailures;

            private string failuresAcrossOpenExistsAndOverwritePath;
            private int failuresAcrossOpenExistsAndOverwriteCount;

            public FileBasedDictionaryFileSystem()
            {
                this.ExpectedOpenFileStreams = new Dictionary<string, ReusableMemoryStream>();
            }

            public FileBasedDictionaryFileSystem(
                string openFileStreamFailurePath,
                int maxOpenFileStreamFailures,
                string fileExistsFailurePath,
                int maxFileExistsFailures,
                int maxMoveAndOverwriteFileFailures)
            {
                this.maxOpenFileStreamFailures = maxOpenFileStreamFailures;
                this.openFileStreamFailurePath = openFileStreamFailurePath;
                this.fileExistsFailurePath = fileExistsFailurePath;
                this.maxFileExistsFailures = maxFileExistsFailures;
                this.maxMoveAndOverwriteFileFailures = maxMoveAndOverwriteFileFailures;
                this.ExpectedOpenFileStreams = new Dictionary<string, ReusableMemoryStream>();
            }

            /// <summary>
            /// Fail a mix of OpenFileStream, FileExists, and Overwrite.
            /// </summary>
            /// <remarks>
            /// Order of failures will be:
            ///  1. OpenFileStream
            ///  2. FileExists
            ///  3. Overwrite
            /// </remarks>
            public FileBasedDictionaryFileSystem(string failuresAcrossOpenExistsAndOverwritePath)
            {
                this.failuresAcrossOpenExistsAndOverwritePath = failuresAcrossOpenExistsAndOverwritePath;
                this.ExpectedOpenFileStreams = new Dictionary<string, ReusableMemoryStream>();
            }

            public Dictionary<string, ReusableMemoryStream> ExpectedOpenFileStreams { get; }

            public override bool FileExists(string path)
            {
                if (this.maxFileExistsFailures > 0)
                {
                    if (this.fileExistsFailureCount < this.maxFileExistsFailures &&
                        string.Equals(path, this.fileExistsFailurePath, GVFSPlatform.Instance.Constants.PathComparison))
                    {
                        if (this.ExpectedFiles.ContainsKey(path))
                        {
                            this.ExpectedFiles.Remove(path);
                        }

                        ++this.fileExistsFailureCount;
                    }
                }
                else if (this.failuresAcrossOpenExistsAndOverwritePath != null)
                {
                    if (this.failuresAcrossOpenExistsAndOverwriteCount == 1 &&
                        string.Equals(path, this.failuresAcrossOpenExistsAndOverwritePath, GVFSPlatform.Instance.Constants.PathComparison))
                    {
                        if (this.ExpectedFiles.ContainsKey(path))
                        {
                            this.ExpectedFiles.Remove(path);
                        }

                        ++this.failuresAcrossOpenExistsAndOverwriteCount;
                    }
                }

                return this.ExpectedFiles.ContainsKey(path);
            }

            public override void MoveAndOverwriteFile(string sourceFileName, string destinationFilename)
            {
                if (this.maxMoveAndOverwriteFileFailures > 0)
                {
                    if (this.moveAndOverwriteFileFailureCount < this.maxMoveAndOverwriteFileFailures)
                    {
                        ++this.moveAndOverwriteFileFailureCount;
                        throw new Win32Exception();
                    }
                }
                else if (this.failuresAcrossOpenExistsAndOverwritePath != null)
                {
                    if (this.failuresAcrossOpenExistsAndOverwriteCount == 2)
                    {
                        ++this.failuresAcrossOpenExistsAndOverwriteCount;
                        throw new Win32Exception();
                    }
                }

                ReusableMemoryStream source;
                this.ExpectedFiles.TryGetValue(sourceFileName, out source).ShouldEqual(true, "Source file does not exist: " + sourceFileName);
                this.ExpectedFiles.ContainsKey(destinationFilename).ShouldEqual(true, "MoveAndOverwriteFile expects the destination file to exist: " + destinationFilename);

                this.ExpectedFiles.Remove(sourceFileName);
                this.ExpectedFiles[destinationFilename] = source;
            }

            public override Stream OpenFileStream(string path, FileMode fileMode, FileAccess fileAccess, FileShare shareMode, FileOptions options, bool flushesToDisk)
            {
                ReusableMemoryStream stream;
                this.ExpectedOpenFileStreams.TryGetValue(path, out stream).ShouldEqual(true, "Unexpected access of file: " + path);

                if (this.maxOpenFileStreamFailures > 0)
                {
                    if (this.openFileStreamFailureCount < this.maxOpenFileStreamFailures &&
                        string.Equals(path, this.openFileStreamFailurePath, GVFSPlatform.Instance.Constants.PathComparison))
                    {
                        ++this.openFileStreamFailureCount;

                        if (this.openFileStreamFailureCount % 2 == 0)
                        {
                            throw new IOException();
                        }
                        else
                        {
                            throw new UnauthorizedAccessException();
                        }
                    }
                }
                else if (this.failuresAcrossOpenExistsAndOverwritePath != null)
                {
                    if (this.failuresAcrossOpenExistsAndOverwriteCount == 0 &&
                        string.Equals(path, this.failuresAcrossOpenExistsAndOverwritePath, GVFSPlatform.Instance.Constants.PathComparison))
                    {
                        ++this.failuresAcrossOpenExistsAndOverwriteCount;
                        throw new IOException();
                    }
                }

                if (fileMode == FileMode.Create)
                {
                    this.ExpectedFiles[path] = new ReusableMemoryStream(string.Empty);
                }

                this.ExpectedFiles.TryGetValue(path, out stream).ShouldEqual(true, "Unexpected access of file: " + path);
                return stream;
            }
        }
    }
}
