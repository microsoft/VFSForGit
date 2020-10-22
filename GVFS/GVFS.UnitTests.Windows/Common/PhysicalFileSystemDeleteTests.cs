using GVFS.Common;
using GVFS.Common.FileSystem;
using GVFS.Common.Tracing;
using GVFS.Tests.Should;
using GVFS.UnitTests.Category;
using GVFS.UnitTests.Mock.Common;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace GVFS.UnitTests.Common
{
    [TestFixture]
    public class PhysicalFileSystemDeleteTests
    {
        [TestCase]
        public void TryDeleteFileDeletesFile()
        {
            string path = "mock:\\file.txt";
            DeleteTestsFileSystem fileSystem = new DeleteTestsFileSystem(new[] { new KeyValuePair<string, FileAttributes>(path, FileAttributes.ReadOnly) });

            fileSystem.TryDeleteFile(path);
            fileSystem.ExistingFiles.ContainsKey(path).ShouldBeFalse("DeleteUtils failed to delete file");
        }

        [TestCase]
        public void TryDeleteFileSetsAttributesToNormalBeforeDeletingFile()
        {
            string path = "mock:\\file.txt";
            DeleteTestsFileSystem fileSystem = new DeleteTestsFileSystem(
                new[] { new KeyValuePair<string, FileAttributes>(path, FileAttributes.ReadOnly) },
                allFilesExist: false,
                noOpDelete: true);

            fileSystem.TryDeleteFile(path);
            fileSystem.ExistingFiles.ContainsKey(path).ShouldBeTrue("DeleteTestsFileSystem is configured as no-op delete, file should still be present");
            fileSystem.ExistingFiles[path].ShouldEqual(FileAttributes.Normal, "TryDeleteFile should set attributes to Normal before deleting");
        }

        [TestCase]
        [Category(CategoryConstants.ExceptionExpected)]
        public void TryDeleteFileReturnsTrueWhenSetAttributesFailsToFindFile()
        {
            string path = "mock:\\file.txt";
            DeleteTestsFileSystem fileSystem = new DeleteTestsFileSystem(
                Enumerable.Empty<KeyValuePair<string, FileAttributes>>(),
                allFilesExist: true,
                noOpDelete: false);

            fileSystem.TryDeleteFile(path).ShouldEqual(true, "TryDeleteFile should return true when SetAttributes throws FileNotFoundException");
        }

        [TestCase]
        public void TryDeleteFileReturnsNullExceptionOnSuccess()
        {
            string path = "mock:\\file.txt";
            DeleteTestsFileSystem fileSystem = new DeleteTestsFileSystem(new[] { new KeyValuePair<string, FileAttributes>(path, FileAttributes.ReadOnly) });

            Exception e = new Exception();
            fileSystem.TryDeleteFile(path, out e);
            fileSystem.ExistingFiles.ContainsKey(path).ShouldBeFalse("DeleteUtils failed to delete file");
            e.ShouldBeNull("Exception should be null when TryDeleteFile succeeds");
        }

        [TestCase]
        [Category(CategoryConstants.ExceptionExpected)]
        public void TryDeleteFileReturnsThrownException()
        {
            string path = "mock:\\file.txt";
            Exception deleteException = new IOException();
            DeleteTestsFileSystem fileSystem = new DeleteTestsFileSystem(new[] { new KeyValuePair<string, FileAttributes>(path, FileAttributes.ReadOnly) });
            fileSystem.DeleteException = deleteException;

            Exception e;
            fileSystem.TryDeleteFile(path, out e).ShouldBeFalse("TryDeleteFile should fail on IOException");
            ReferenceEquals(e, deleteException).ShouldBeTrue("TryDeleteFile should return the thrown exception");

            deleteException = new UnauthorizedAccessException();
            fileSystem.DeleteException = deleteException;
            fileSystem.TryDeleteFile(path, out e).ShouldBeFalse("TryDeleteFile should fail on UnauthorizedAccessException");
            ReferenceEquals(e, deleteException).ShouldBeTrue("TryDeleteFile should return the thrown exception");
        }

        [TestCase]
        public void TryDeleteFileDoesNotUpdateMetadataOnSuccess()
        {
            string path = "mock:\\file.txt";
            DeleteTestsFileSystem fileSystem = new DeleteTestsFileSystem(new[] { new KeyValuePair<string, FileAttributes>(path, FileAttributes.ReadOnly) });

            EventMetadata metadata = new EventMetadata();
            fileSystem.TryDeleteFile(path, "metadataKey", metadata).ShouldBeTrue("TryDeleteFile should succeed");
            metadata.ShouldBeEmpty("TryDeleteFile should not update metadata on success");
        }

        [TestCase]
        [Category(CategoryConstants.ExceptionExpected)]
        public void TryDeleteFileUpdatesMetadataOnFailure()
        {
            string path = "mock:\\file.txt";
            DeleteTestsFileSystem fileSystem = new DeleteTestsFileSystem(new[] { new KeyValuePair<string, FileAttributes>(path, FileAttributes.ReadOnly) });
            fileSystem.DeleteException = new IOException();

            EventMetadata metadata = new EventMetadata();
            fileSystem.TryDeleteFile(path, "testKey", metadata).ShouldBeFalse("TryDeleteFile should fail when IOException is thrown");
            metadata.ContainsKey("testKey_DeleteFailed").ShouldBeTrue();
            metadata["testKey_DeleteFailed"].ShouldEqual("true");
            metadata.ContainsKey("testKey_DeleteException").ShouldBeTrue();
            metadata["testKey_DeleteException"].ShouldBeOfType<string>().ShouldContain("IOException");
        }

        [TestCase]
        [Category(CategoryConstants.ExceptionExpected)]
        public void TryWaitForDeleteSucceedsAfterFailures()
        {
            string path = "mock:\\file.txt";
            DeleteTestsFileSystem fileSystem = new DeleteTestsFileSystem(new[] { new KeyValuePair<string, FileAttributes>(path, FileAttributes.ReadOnly) });
            fileSystem.DeleteException = new IOException();

            fileSystem.MaxDeleteFileExceptions = 5;
            fileSystem.TryWaitForDelete(null, path, retryDelayMs: 0, maxRetries: 10, retryLoggingThreshold: 1).ShouldBeTrue();
            fileSystem.DeleteFileCallCount.ShouldEqual(fileSystem.MaxDeleteFileExceptions + 1);

            fileSystem.ExistingFiles.Add(path, FileAttributes.ReadOnly);
            fileSystem.DeleteFileCallCount = 0;
            fileSystem.MaxDeleteFileExceptions = 9;
            fileSystem.TryWaitForDelete(null, path, retryDelayMs: 0, maxRetries: 10, retryLoggingThreshold: 1).ShouldBeTrue();
            fileSystem.DeleteFileCallCount.ShouldEqual(fileSystem.MaxDeleteFileExceptions + 1);
        }

        [TestCase]
        [Category(CategoryConstants.ExceptionExpected)]
        public void TryWaitForDeleteFailsAfterMaxRetries()
        {
            string path = "mock:\\file.txt";
            DeleteTestsFileSystem fileSystem = new DeleteTestsFileSystem(new[] { new KeyValuePair<string, FileAttributes>(path, FileAttributes.ReadOnly) });
            fileSystem.DeleteException = new IOException();

            int maxRetries = 10;
            fileSystem.TryWaitForDelete(null, path, retryDelayMs: 0, maxRetries: maxRetries, retryLoggingThreshold: 1).ShouldBeFalse();
            fileSystem.DeleteFileCallCount.ShouldEqual(maxRetries + 1);

            fileSystem.DeleteFileCallCount = 0;
            fileSystem.TryWaitForDelete(null, path, retryDelayMs: 1, maxRetries: maxRetries, retryLoggingThreshold: 1).ShouldBeFalse();
            fileSystem.DeleteFileCallCount.ShouldEqual(maxRetries + 1);

            fileSystem.DeleteFileCallCount = 0;
            fileSystem.TryWaitForDelete(null, path, retryDelayMs: 1, maxRetries: 0, retryLoggingThreshold: 1).ShouldBeFalse();
            fileSystem.DeleteFileCallCount.ShouldEqual(1);
        }

        [TestCase]
        [Category(CategoryConstants.ExceptionExpected)]
        public void TryWaitForDeleteAlwaysLogsFirstAndLastFailure()
        {
            string path = "mock:\\file.txt";
            DeleteTestsFileSystem fileSystem = new DeleteTestsFileSystem(new[] { new KeyValuePair<string, FileAttributes>(path, FileAttributes.ReadOnly) });
            fileSystem.DeleteException = new IOException();

            MockTracer mockTracer = new MockTracer();
            int maxRetries = 10;
            fileSystem.TryWaitForDelete(mockTracer, path, retryDelayMs: 0, maxRetries: maxRetries, retryLoggingThreshold: 1000).ShouldBeFalse();
            fileSystem.DeleteFileCallCount.ShouldEqual(maxRetries + 1);

            mockTracer.RelatedWarningEvents.Count.ShouldEqual(2, "There should be two warning events, the first and last");
            mockTracer.RelatedWarningEvents[0].ShouldContain(
                new[]
                {
                    "Failed to delete file, retrying ...",
                    "\"failureCount\":1",
                    "IOException"
                });
            mockTracer.RelatedWarningEvents[1].ShouldContain(
                new[]
                {
                    "Failed to delete file.",
                    "\"failureCount\":11",
                    "IOException"
                });
        }

        [TestCase]
        [Category(CategoryConstants.ExceptionExpected)]
        public void TryWaitForDeleteLogsAtSpecifiedInterval()
        {
            string path = "mock:\\file.txt";
            DeleteTestsFileSystem fileSystem = new DeleteTestsFileSystem(new[] { new KeyValuePair<string, FileAttributes>(path, FileAttributes.ReadOnly) });
            fileSystem.DeleteException = new IOException();

            MockTracer mockTracer = new MockTracer();
            int maxRetries = 10;
            fileSystem.TryWaitForDelete(mockTracer, path, retryDelayMs: 0, maxRetries: maxRetries, retryLoggingThreshold: 3).ShouldBeFalse();
            fileSystem.DeleteFileCallCount.ShouldEqual(maxRetries + 1);

            mockTracer.RelatedWarningEvents.Count.ShouldEqual(5, "There should be five warning events, the first and last, and the 4th, 7th, and 10th");
            mockTracer.RelatedWarningEvents[0].ShouldContain(
                new[]
                {
                    "Failed to delete file, retrying ...",
                    "\"failureCount\":1",
                    "IOException"
                });

            mockTracer.RelatedWarningEvents[1].ShouldContain(
                new[]
                {
                    "Failed to delete file, retrying ...",
                    "\"failureCount\":4",
                    "IOException"
                });
            mockTracer.RelatedWarningEvents[2].ShouldContain(
                new[]
                {
                    "Failed to delete file, retrying ...",
                    "\"failureCount\":7",
                    "IOException"
                });
            mockTracer.RelatedWarningEvents[3].ShouldContain(
                new[]
                {
                    "Failed to delete file, retrying ...",
                    "\"failureCount\":10",
                    "IOException"
                });
            mockTracer.RelatedWarningEvents[4].ShouldContain(
                new[]
                {
                    "Failed to delete file.",
                    "\"failureCount\":11",
                    "IOException"
                });
        }

        private class DeleteTestsFileSystem : PhysicalFileSystem
        {
            private bool allFilesExist;
            private bool noOpDelete;

            public DeleteTestsFileSystem(
                IEnumerable<KeyValuePair<string, FileAttributes>> existingFiles,
                bool allFilesExist = false,
                bool noOpDelete = false)
            {
                this.ExistingFiles = new Dictionary<string, FileAttributes>(GVFSPlatform.Instance.Constants.PathComparer);
                foreach (KeyValuePair<string, FileAttributes> kvp in existingFiles)
                {
                    this.ExistingFiles[kvp.Key] = kvp.Value;
                }

                this.allFilesExist = allFilesExist;
                this.noOpDelete = noOpDelete;
                this.DeleteFileCallCount = 0;
                this.MaxDeleteFileExceptions = -1;
            }

            public Dictionary<string, FileAttributes> ExistingFiles { get; private set; }
            public Exception DeleteException { get; set; }
            public int MaxDeleteFileExceptions { get; set; }
            public int DeleteFileCallCount { get; set; }

            public override bool FileExists(string path)
            {
                if (this.allFilesExist)
                {
                    return true;
                }

                return this.ExistingFiles.ContainsKey(path);
            }

            public override void SetAttributes(string path, FileAttributes fileAttributes)
            {
                if (this.ExistingFiles.ContainsKey(path))
                {
                    this.ExistingFiles[path] = fileAttributes;
                }
                else
                {
                    throw new FileNotFoundException();
                }
            }

            public override void DeleteFile(string path)
            {
                this.DeleteFileCallCount++;

                if (!this.noOpDelete)
                {
                    if (this.DeleteException != null &&
                        (this.MaxDeleteFileExceptions == -1 || this.MaxDeleteFileExceptions >= this.DeleteFileCallCount))
                    {
                        throw this.DeleteException;
                    }

                    if (this.ExistingFiles.ContainsKey(path))
                    {
                        if ((this.ExistingFiles[path] & FileAttributes.ReadOnly) == FileAttributes.ReadOnly)
                        {
                            throw new UnauthorizedAccessException();
                        }
                        else
                        {
                            this.ExistingFiles.Remove(path);
                        }
                    }
                }
            }
        }
    }
}
