using GVFS.Common;
using GVFS.Common.Git;
using GVFS.Tests.Should;
using GVFS.UnitTests.Mock.Common;
using NUnit.Framework;
using System;
using System.IO;
using System.Linq;

namespace GVFS.UnitTests.Prefetch
{
    [TestFixture]
    public class PackIndexObjectExistenceCheckerTests
    {
        private string tempDir;
        private string objectsRoot;
        private string packDir;

        [SetUp]
        public void SetUp()
        {
            this.tempDir = Path.Combine(Path.GetTempPath(), "PackIdxCheckerTests_" + Guid.NewGuid().ToString("N").Substring(0, 8));
            this.objectsRoot = Path.Combine(this.tempDir, "objects");
            this.packDir = Path.Combine(this.objectsRoot, "pack");
            Directory.CreateDirectory(this.packDir);
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(this.tempDir))
            {
                Directory.Delete(this.tempDir, recursive: true);
            }
        }

        [Test]
        public void FindsObjectInMidx()
        {
            string[] oids = MidxReaderTests.GenerateSortedOids(100);
            MidxReaderTests.WriteMidxFile(this.packDir, oids, new[] { "pack-abc" });

            using (PackIndexObjectExistenceChecker checker = new PackIndexObjectExistenceChecker(
                MockTracerProvider.CreateMockTracer(),
                this.objectsRoot))
            {
                checker.ObjectExists(oids[0]).ShouldBeTrue();
                checker.ObjectExists(oids[50]).ShouldBeTrue();
                checker.ObjectExists(oids[99]).ShouldBeTrue();
            }
        }

        [Test]
        public void FindsObjectInSupplementalPack()
        {
            // Create MIDX with one set of OIDs
            string[] midxOids = MidxReaderTests.GenerateSortedOids(50);
            MidxReaderTests.WriteMidxFile(this.packDir, midxOids, new[] { "pack-inmidx" });

            // Create a supplemental .idx NOT listed in the MIDX
            string[] extraOids = MidxReaderTests.GenerateSortedOids(30);
            // Use a different seed to get different OIDs
            Random rng = new Random(999);
            extraOids = Enumerable.Range(0, 30)
                .Select(_ =>
                {
                    byte[] raw = new byte[20];
                    rng.NextBytes(raw);
                    return BitConverter.ToString(raw).Replace("-", "").ToLowerInvariant();
                })
                .Distinct()
                .OrderBy(x => x, StringComparer.Ordinal)
                .ToArray();

            PackIndexReaderTests.WritePackIndexV2(this.packDir, "pack-supplemental", extraOids);

            using (PackIndexObjectExistenceChecker checker = new PackIndexObjectExistenceChecker(
                MockTracerProvider.CreateMockTracer(),
                this.objectsRoot))
            {
                // MIDX objects should still be found
                checker.ObjectExists(midxOids[0]).ShouldBeTrue("MIDX object should be found");

                // Supplemental pack objects should be found
                checker.ObjectExists(extraOids[0]).ShouldBeTrue("Supplemental pack object should be found");
                checker.ObjectExists(extraOids[extraOids.Length - 1]).ShouldBeTrue("Last supplemental object should be found");
            }
        }

        [Test]
        public void FindsLooseObject()
        {
            // No packs at all — just a loose object
            string sha = "aabbccddee112233445566778899001122334455";
            string prefix = sha.Substring(0, 2);
            string suffix = sha.Substring(2);
            string looseDir = Path.Combine(this.objectsRoot, prefix);
            Directory.CreateDirectory(looseDir);
            File.WriteAllBytes(Path.Combine(looseDir, suffix), new byte[] { 0x78, 0x01 }); // zlib header

            using (PackIndexObjectExistenceChecker checker = new PackIndexObjectExistenceChecker(
                MockTracerProvider.CreateMockTracer(),
                this.objectsRoot))
            {
                checker.ObjectExists(sha).ShouldBeTrue("Loose object should be found");
                checker.ObjectExists("0000000000000000000000000000000000000000").ShouldBeFalse("Non-existent loose should not be found");
            }
        }

        [Test]
        public void ReturnsFalseForMissingObject()
        {
            string[] oids = MidxReaderTests.GenerateSortedOids(50);
            MidxReaderTests.WriteMidxFile(this.packDir, oids, new[] { "pack-abc" });

            using (PackIndexObjectExistenceChecker checker = new PackIndexObjectExistenceChecker(
                MockTracerProvider.CreateMockTracer(),
                this.objectsRoot))
            {
                checker.ObjectExists("0000000000000000000000000000000000000000").ShouldBeFalse();
                checker.ObjectExists("ffffffffffffffffffffffffffffffffffffffff").ShouldBeFalse();
            }
        }

        [Test]
        public void HandlesEmptyPackDir()
        {
            using (PackIndexObjectExistenceChecker checker = new PackIndexObjectExistenceChecker(
                MockTracerProvider.CreateMockTracer(),
                this.objectsRoot))
            {
                checker.ObjectExists("0000000000000000000000000000000000000000").ShouldBeFalse();
            }
        }

        [Test]
        public void HandlesMissingPackDir()
        {
            string noPackRoot = Path.Combine(this.tempDir, "nopack");
            Directory.CreateDirectory(noPackRoot);
            // No "pack" subdirectory

            using (PackIndexObjectExistenceChecker checker = new PackIndexObjectExistenceChecker(
                MockTracerProvider.CreateMockTracer(),
                noPackRoot))
            {
                checker.ObjectExists("0000000000000000000000000000000000000000").ShouldBeFalse();
            }
        }

        [Test]
        public void DeduplicatesIdenticalRoots()
        {
            string[] oids = MidxReaderTests.GenerateSortedOids(10);
            MidxReaderTests.WriteMidxFile(this.packDir, oids, new[] { "pack-dedup" });

            // Pass the same root twice (simulates LocalObjectsRoot == GitObjectsRoot)
            using (PackIndexObjectExistenceChecker checker = new PackIndexObjectExistenceChecker(
                MockTracerProvider.CreateMockTracer(),
                this.objectsRoot,
                this.objectsRoot))
            {
                checker.ObjectExists(oids[0]).ShouldBeTrue();
            }
        }

        [Test]
        public void SearchesMultipleRoots()
        {
            // Root 1 with some objects
            string root1 = Path.Combine(this.tempDir, "root1");
            string packDir1 = Path.Combine(root1, "pack");
            Directory.CreateDirectory(packDir1);
            string[] oids1 = MidxReaderTests.GenerateSortedOids(20);
            MidxReaderTests.WriteMidxFile(packDir1, oids1, new[] { "pack-r1" });

            // Root 2 with different objects
            string root2 = Path.Combine(this.tempDir, "root2");
            string packDir2 = Path.Combine(root2, "pack");
            Directory.CreateDirectory(packDir2);
            Random rng = new Random(12345);
            string[] oids2 = Enumerable.Range(0, 20)
                .Select(_ =>
                {
                    byte[] raw = new byte[20];
                    rng.NextBytes(raw);
                    return BitConverter.ToString(raw).Replace("-", "").ToLowerInvariant();
                })
                .Distinct()
                .OrderBy(x => x, StringComparer.Ordinal)
                .ToArray();
            MidxReaderTests.WriteMidxFile(packDir2, oids2, new[] { "pack-r2" });

            using (PackIndexObjectExistenceChecker checker = new PackIndexObjectExistenceChecker(
                MockTracerProvider.CreateMockTracer(),
                root1,
                root2))
            {
                checker.ObjectExists(oids1[0]).ShouldBeTrue("Root1 object should be found");
                checker.ObjectExists(oids2[0]).ShouldBeTrue("Root2 object should be found");
                checker.ObjectExists("0000000000000000000000000000000000000000").ShouldBeFalse();
            }
        }
    }

    /// <summary>
    /// Helper to create mock tracers for tests that need ITracer.
    /// </summary>
    internal static class MockTracerProvider
    {
        public static MockTracer CreateMockTracer()
        {
            return new MockTracer();
        }
    }
}
