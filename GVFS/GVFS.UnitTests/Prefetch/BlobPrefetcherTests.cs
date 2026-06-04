using GVFS.Common;
using GVFS.Common.Prefetch;
using GVFS.Tests.Should;
using GVFS.UnitTests.Mock;
using GVFS.UnitTests.Mock.Common;
using GVFS.UnitTests.Mock.FileSystem;
using NUnit.Framework;
using System.Collections.Generic;
using System.IO;

namespace GVFS.UnitTests.Prefetch
{
    [TestFixture]
    public class BlobPrefetcherTests
    {
        private const string MockCacheFileName = "mock:\\prefetch-cache.dat";

        [TestCase]
        public void AppendToNewlineSeparatedFileTests()
        {
            MockFileSystem fileSystem = new MockFileSystem(new MockDirectory(Path.Combine("mock:", "GVFS", "UnitTests", "Repo"), null, null));

            // Validate can write to a file that doesn't exist.
            string testFileName = Path.Combine("mock:", "GVFS", "UnitTests", "Repo", "appendTests");
            BlobPrefetcher.AppendToNewlineSeparatedFile(fileSystem, testFileName, "expected content line 1");
            fileSystem.ReadAllText(testFileName).ShouldEqual("expected content line 1\n");

            // Validate that if the file doesn't end in a newline it gets a newline added.
            fileSystem.WriteAllText(testFileName, "existing content");
            BlobPrefetcher.AppendToNewlineSeparatedFile(fileSystem, testFileName, "expected line 2");
            fileSystem.ReadAllText(testFileName).ShouldEqual("existing content\nexpected line 2\n");

            // Validate that if the file ends in a newline, we don't end up with two newlines
            fileSystem.WriteAllText(testFileName, "existing content\n");
            BlobPrefetcher.AppendToNewlineSeparatedFile(fileSystem, testFileName, "expected line 2");
            fileSystem.ReadAllText(testFileName).ShouldEqual("existing content\nexpected line 2\n");
        }

        [TestCase]
        public void ComputeCacheKeyIsDeterministic()
        {
            List<string> files = new List<string> { "src/a.cs", "src/b.cs" };
            List<string> folders = new List<string> { "src/dir1", "src/dir2" };

            string key1 = BlobPrefetcher.ComputeCacheKey(files, folders, hydrate: false);
            string key2 = BlobPrefetcher.ComputeCacheKey(files, folders, hydrate: false);

            key1.ShouldEqual(key2);
        }

        [TestCase]
        public void ComputeCacheKeyDiffersForDifferentFiles()
        {
            List<string> files1 = new List<string> { "src/a.cs" };
            List<string> files2 = new List<string> { "src/b.cs" };
            List<string> folders = new List<string> { "src/dir1" };

            string key1 = BlobPrefetcher.ComputeCacheKey(files1, folders, hydrate: false);
            string key2 = BlobPrefetcher.ComputeCacheKey(files2, folders, hydrate: false);

            key1.ShouldNotEqual(key2);
        }

        [TestCase]
        public void ComputeCacheKeyDiffersForDifferentFolders()
        {
            List<string> files = new List<string> { "src/a.cs" };
            List<string> folders1 = new List<string> { "src/dir1" };
            List<string> folders2 = new List<string> { "src/dir2" };

            string key1 = BlobPrefetcher.ComputeCacheKey(files, folders1, hydrate: false);
            string key2 = BlobPrefetcher.ComputeCacheKey(files, folders2, hydrate: false);

            key1.ShouldNotEqual(key2);
        }

        [TestCase]
        public void ComputeCacheKeyDiffersForHydrateFlag()
        {
            List<string> files = new List<string> { "src/a.cs" };
            List<string> folders = new List<string> { "src/dir1" };

            string key1 = BlobPrefetcher.ComputeCacheKey(files, folders, hydrate: false);
            string key2 = BlobPrefetcher.ComputeCacheKey(files, folders, hydrate: true);

            key1.ShouldNotEqual(key2);
        }

        [TestCase]
        public void ComputeCacheKeyIsOrderIndependent()
        {
            List<string> filesA = new List<string> { "src/b.cs", "src/a.cs" };
            List<string> filesB = new List<string> { "src/a.cs", "src/b.cs" };
            List<string> folders = new List<string> { "src/dir1" };

            string key1 = BlobPrefetcher.ComputeCacheKey(filesA, folders, hydrate: false);
            string key2 = BlobPrefetcher.ComputeCacheKey(filesB, folders, hydrate: false);

            key1.ShouldEqual(key2);
        }

        [TestCase]
        public void ComputeCacheKeyFolderOrderIndependent()
        {
            List<string> files = new List<string> { "src/a.cs" };
            List<string> foldersA = new List<string> { "src/dir2", "src/dir1" };
            List<string> foldersB = new List<string> { "src/dir1", "src/dir2" };

            string key1 = BlobPrefetcher.ComputeCacheKey(files, foldersA, hydrate: false);
            string key2 = BlobPrefetcher.ComputeCacheKey(files, foldersB, hydrate: false);

            key1.ShouldEqual(key2);
        }

        [TestCase]
        public void IsNoopPrefetchReturnsFalseWhenCacheIsNull()
        {
            MockTracer tracer = new MockTracer();
            List<string> files = new List<string> { "src/a.cs" };
            List<string> folders = new List<string> { "src/dir1" };

            BlobPrefetcher.IsNoopPrefetch(tracer, null, "abc123", files, folders, false).ShouldEqual(false);
        }

        [TestCase]
        public void IsNoopPrefetchReturnsFalseWhenCacheIsEmpty()
        {
            MockTracer tracer = new MockTracer();
            List<string> files = new List<string> { "src/a.cs" };
            List<string> folders = new List<string> { "src/dir1" };
            FileBasedDictionary<string, string> cache = CreateEmptyCache();

            BlobPrefetcher.IsNoopPrefetch(tracer, cache, "abc123", files, folders, false).ShouldEqual(false);
        }

        [TestCase]
        public void IsNoopPrefetchReturnsTrueOnCacheHit()
        {
            MockTracer tracer = new MockTracer();
            List<string> files = new List<string> { "src/a.cs" };
            List<string> folders = new List<string> { "src/dir1" };
            string commitId = "abc123";

            FileBasedDictionary<string, string> cache = CreateEmptyCache();
            string cacheKey = BlobPrefetcher.ComputeCacheKey(files, folders, hydrate: false);
            cache.SetValueAndFlush(cacheKey, commitId);

            BlobPrefetcher.IsNoopPrefetch(tracer, cache, commitId, files, folders, false).ShouldEqual(true);
        }

        [TestCase]
        public void IsNoopPrefetchReturnsFalseWhenCommitIdChanged()
        {
            MockTracer tracer = new MockTracer();
            List<string> files = new List<string> { "src/a.cs" };
            List<string> folders = new List<string> { "src/dir1" };

            FileBasedDictionary<string, string> cache = CreateEmptyCache();
            string cacheKey = BlobPrefetcher.ComputeCacheKey(files, folders, hydrate: false);
            cache.SetValueAndFlush(cacheKey, "oldcommit");

            BlobPrefetcher.IsNoopPrefetch(tracer, cache, "newcommit", files, folders, false).ShouldEqual(false);
        }

        [TestCase]
        public void IsNoopPrefetchSupportsMultipleEntries()
        {
            MockTracer tracer = new MockTracer();
            List<string> filesA = new List<string> { "src/a.cs" };
            List<string> filesB = new List<string> { "src/b.cs" };
            List<string> folders = new List<string> { "src/dir1" };
            string commitId = "abc123";

            FileBasedDictionary<string, string> cache = CreateEmptyCache();

            string keyA = BlobPrefetcher.ComputeCacheKey(filesA, folders, hydrate: false);
            cache.SetValueAndFlush(keyA, commitId);

            string keyB = BlobPrefetcher.ComputeCacheKey(filesB, folders, hydrate: false);
            cache.SetValueAndFlush(keyB, commitId);

            // Both should hit
            BlobPrefetcher.IsNoopPrefetch(tracer, cache, commitId, filesA, folders, false).ShouldEqual(true);
            BlobPrefetcher.IsNoopPrefetch(tracer, cache, commitId, filesB, folders, false).ShouldEqual(true);

            // A third pattern should miss
            List<string> filesC = new List<string> { "src/c.cs" };
            BlobPrefetcher.IsNoopPrefetch(tracer, cache, commitId, filesC, folders, false).ShouldEqual(false);
        }

        private static FileBasedDictionary<string, string> CreateEmptyCache()
        {
            CacheFileSystem fs = new CacheFileSystem();
            fs.ExpectedFiles.Add(MockCacheFileName, new ReusableMemoryStream(string.Empty));
            fs.ExpectedOpenFileStreams.Add(MockCacheFileName + ".tmp", new ReusableMemoryStream(string.Empty));
            fs.ExpectedOpenFileStreams.Add(MockCacheFileName, fs.ExpectedFiles[MockCacheFileName]);

            FileBasedDictionary<string, string>.TryCreate(
                null,
                MockCacheFileName,
                fs,
                out FileBasedDictionary<string, string> cache,
                out string error).ShouldEqual(true, error);

            fs.ExpectedOpenFileStreams.Remove(MockCacheFileName);
            return cache;
        }

        private class CacheFileSystem : ConfigurableFileSystem
        {
            public CacheFileSystem()
            {
                this.ExpectedOpenFileStreams = new Dictionary<string, ReusableMemoryStream>();
            }

            public Dictionary<string, ReusableMemoryStream> ExpectedOpenFileStreams { get; }

            public override Stream OpenFileStream(string path, FileMode fileMode, FileAccess fileAccess, FileShare shareMode, FileOptions options, bool flushesToDisk)
            {
                this.ExpectedOpenFileStreams.TryGetValue(path, out ReusableMemoryStream stream);

                if (fileMode == FileMode.Create)
                {
                    this.ExpectedFiles[path] = new ReusableMemoryStream(string.Empty);
                }

                this.ExpectedFiles.TryGetValue(path, out stream).ShouldEqual(true, "Unexpected access of file: " + path);
                return stream;
            }

            public override void MoveAndOverwriteFile(string sourceFileName, string destinationFilename)
            {
                this.ExpectedFiles.TryGetValue(sourceFileName, out ReusableMemoryStream source).ShouldEqual(true, "Source file does not exist: " + sourceFileName);
                this.ExpectedFiles.ContainsKey(destinationFilename).ShouldEqual(true, "MoveAndOverwriteFile expects the destination file to exist: " + destinationFilename);

                this.ExpectedFiles.Remove(sourceFileName);
                this.ExpectedFiles[destinationFilename] = source;
            }
        }
    }
}