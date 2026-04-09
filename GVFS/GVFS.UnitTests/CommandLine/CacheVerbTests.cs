using GVFS.CommandLine;
using NUnit.Framework;
using System;
using System.Globalization;
using System.IO;

namespace GVFS.UnitTests.CommandLine
{
    [TestFixture]
    public class CacheVerbTests
    {
        private CacheVerb cacheVerb;
        private string testDir;

        [SetUp]
        public void Setup()
        {
            this.cacheVerb = new CacheVerb();
            this.testDir = Path.Combine(Path.GetTempPath(), "CacheVerbTests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(this.testDir);
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(this.testDir))
            {
                Directory.Delete(this.testDir, recursive: true);
            }
        }

        [TestCase(0, "0 bytes")]
        [TestCase(512, "512 bytes")]
        [TestCase(1023, "1023 bytes")]
        [TestCase(1024, "1.0 KB")]
        [TestCase(1536, "1.5 KB")]
        [TestCase(1048576, "1.0 MB")]
        [TestCase(1572864, "1.5 MB")]
        [TestCase(1073741824, "1.0 GB")]
        [TestCase(1610612736, "1.5 GB")]
        [TestCase(10737418240, "10.0 GB")]
        public void FormatSizeForUserDisplayReturnsExpectedString(long bytes, string expected)
        {
            CultureInfo savedCulture = CultureInfo.CurrentCulture;
            try
            {
                CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
                Assert.AreEqual(expected, this.cacheVerb.FormatSizeForUserDisplay(bytes));
            }
            finally
            {
                CultureInfo.CurrentCulture = savedCulture;
            }
        }

        [TestCase]
        public void GetPackSummaryWithNoPacks()
        {
            string packDir = Path.Combine(this.testDir, "pack");
            Directory.CreateDirectory(packDir);

            this.cacheVerb.GetPackSummary(
                packDir,
                out int prefetchCount,
                out long prefetchSize,
                out int otherCount,
                out long otherSize,
                out long latestTimestamp);

            Assert.AreEqual(0, prefetchCount);
            Assert.AreEqual(0, prefetchSize);
            Assert.AreEqual(0, otherCount);
            Assert.AreEqual(0, otherSize);
            Assert.AreEqual(0, latestTimestamp);
        }

        [TestCase]
        public void GetPackSummaryCategorizesPrefetchAndOtherPacks()
        {
            string packDir = Path.Combine(this.testDir, "pack");
            Directory.CreateDirectory(packDir);

            this.CreateFileWithSize(Path.Combine(packDir, "prefetch-1000-aabbccdd.pack"), 100);
            this.CreateFileWithSize(Path.Combine(packDir, "prefetch-2000-eeff0011.pack"), 200);
            this.CreateFileWithSize(Path.Combine(packDir, "pack-abcdef1234567890.pack"), 50);

            this.cacheVerb.GetPackSummary(
                packDir,
                out int prefetchCount,
                out long prefetchSize,
                out int otherCount,
                out long otherSize,
                out long latestTimestamp);

            Assert.AreEqual(2, prefetchCount);
            Assert.AreEqual(300, prefetchSize);
            Assert.AreEqual(1, otherCount);
            Assert.AreEqual(50, otherSize);
            Assert.AreEqual(2000, latestTimestamp);
        }

        [TestCase]
        public void GetPackSummaryIgnoresNonPackFiles()
        {
            string packDir = Path.Combine(this.testDir, "pack");
            Directory.CreateDirectory(packDir);

            this.CreateFileWithSize(Path.Combine(packDir, "prefetch-1000-aabb.pack"), 100);
            this.CreateFileWithSize(Path.Combine(packDir, "prefetch-1000-aabb.idx"), 50);
            this.CreateFileWithSize(Path.Combine(packDir, "multi-pack-index"), 10);

            this.cacheVerb.GetPackSummary(
                packDir,
                out int prefetchCount,
                out long prefetchSize,
                out int otherCount,
                out long otherSize,
                out long latestTimestamp);

            Assert.AreEqual(1, prefetchCount);
            Assert.AreEqual(100, prefetchSize);
            Assert.AreEqual(0, otherCount);
            Assert.AreEqual(0, otherSize);
        }

        [TestCase]
        public void GetPackSummaryHandlesBothGuidAndSHA1HashFormats()
        {
            string packDir = Path.Combine(this.testDir, "pack");
            Directory.CreateDirectory(packDir);

            // GVFS format: 32-char GUID
            this.CreateFileWithSize(Path.Combine(packDir, "prefetch-1000-b8d9efad32194d98894532905daf88ec.pack"), 100);
            // Scalar format: 40-char SHA1
            this.CreateFileWithSize(Path.Combine(packDir, "prefetch-2000-9babd9b75521f9caf693b485329d3d5669c88564.pack"), 200);

            this.cacheVerb.GetPackSummary(
                packDir,
                out int prefetchCount,
                out long prefetchSize,
                out int otherCount,
                out long otherSize,
                out long latestTimestamp);

            Assert.AreEqual(2, prefetchCount);
            Assert.AreEqual(300, prefetchSize);
            Assert.AreEqual(2000, latestTimestamp);
        }

        [TestCase]
        public void CountLooseObjectsWithNoObjects()
        {
            int count = this.cacheVerb.CountLooseObjects(this.testDir);
            Assert.AreEqual(0, count);
        }

        [TestCase]
        public void CountLooseObjectsCountsFilesInHexDirectories()
        {
            Directory.CreateDirectory(Path.Combine(this.testDir, "00"));
            File.WriteAllText(Path.Combine(this.testDir, "00", "abc123"), string.Empty);
            File.WriteAllText(Path.Combine(this.testDir, "00", "def456"), string.Empty);

            Directory.CreateDirectory(Path.Combine(this.testDir, "ff"));
            File.WriteAllText(Path.Combine(this.testDir, "ff", "789abc"), string.Empty);

            int count = this.cacheVerb.CountLooseObjects(this.testDir);
            Assert.AreEqual(3, count);
        }

        [TestCase]
        public void CountLooseObjectsIgnoresNonHexDirectories()
        {
            // "pack" and "info" are valid directories in a git objects dir but not hex dirs
            Directory.CreateDirectory(Path.Combine(this.testDir, "pack"));
            File.WriteAllText(Path.Combine(this.testDir, "pack", "somefile"), string.Empty);

            Directory.CreateDirectory(Path.Combine(this.testDir, "info"));
            File.WriteAllText(Path.Combine(this.testDir, "info", "somefile"), string.Empty);

            // "ab" is a valid hex dir
            Directory.CreateDirectory(Path.Combine(this.testDir, "ab"));
            File.WriteAllText(Path.Combine(this.testDir, "ab", "object1"), string.Empty);

            int count = this.cacheVerb.CountLooseObjects(this.testDir);
            Assert.AreEqual(1, count);
        }

        private void CreateFileWithSize(string path, int size)
        {
            byte[] data = new byte[size];
            File.WriteAllBytes(path, data);
        }
    }
}
