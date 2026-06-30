using GVFS.Common.Git;
using GVFS.Tests.Should;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace GVFS.UnitTests.Prefetch
{
    [TestFixture]
    public class MidxReaderTests
    {
        private string tempDir;

        [SetUp]
        public void SetUp()
        {
            this.tempDir = Path.Combine(Path.GetTempPath(), "MidxReaderTests_" + Guid.NewGuid().ToString("N").Substring(0, 8));
            Directory.CreateDirectory(this.tempDir);
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
        public void FindsExistingObject()
        {
            string[] oids = GenerateSortedOids(100);
            string midxPath = WriteMidxFile(this.tempDir, oids, new[] { "pack-abc123" });

            using (MidxReader reader = new MidxReader(midxPath))
            {
                reader.TotalObjects.ShouldEqual(100);
                reader.Exists(oids[0]).ShouldBeTrue("First OID should exist");
                reader.Exists(oids[50]).ShouldBeTrue("Middle OID should exist");
                reader.Exists(oids[99]).ShouldBeTrue("Last OID should exist");
            }
        }

        [Test]
        public void ReturnsFalseForMissingObject()
        {
            string[] oids = GenerateSortedOids(100);
            string midxPath = WriteMidxFile(this.tempDir, oids, new[] { "pack-abc123" });

            using (MidxReader reader = new MidxReader(midxPath))
            {
                reader.Exists("0000000000000000000000000000000000000000").ShouldBeFalse();
                reader.Exists("ffffffffffffffffffffffffffffffffffffffff").ShouldBeFalse();
                reader.Exists("deadbeefdeadbeefdeadbeefdeadbeefdeadbeef").ShouldBeFalse();
            }
        }

        [Test]
        public void ReturnsFalseForNullOrShortSha()
        {
            string[] oids = GenerateSortedOids(10);
            string midxPath = WriteMidxFile(this.tempDir, oids, new[] { "pack-abc123" });

            using (MidxReader reader = new MidxReader(midxPath))
            {
                reader.Exists((string)null).ShouldBeFalse();
                reader.Exists("abc").ShouldBeFalse();
            }
        }

        [Test]
        public void ParsesPackNames()
        {
            string[] oids = GenerateSortedOids(10);
            string[] packs = new[] { "pack-aaaa", "pack-bbbb", "prefetch-cccc" };
            string midxPath = WriteMidxFile(this.tempDir, oids, packs);

            using (MidxReader reader = new MidxReader(midxPath))
            {
                HashSet<string> stems = reader.GetPackStems();
                stems.Count.ShouldEqual(3);
                stems.Contains("pack-aaaa").ShouldBeTrue();
                stems.Contains("pack-bbbb").ShouldBeTrue();
                stems.Contains("prefetch-cccc").ShouldBeTrue();
            }
        }

        [Test]
        public void HandlesEmptyMidx()
        {
            string midxPath = WriteMidxFile(this.tempDir, Array.Empty<string>(), new[] { "pack-empty" });

            using (MidxReader reader = new MidxReader(midxPath))
            {
                reader.TotalObjects.ShouldEqual(0);
                reader.Exists("0000000000000000000000000000000000000000").ShouldBeFalse();
            }
        }

        [Test]
        public void ThrowsOnInvalidMagic()
        {
            string path = Path.Combine(this.tempDir, "bad-midx");
            File.WriteAllBytes(path, new byte[] { 0, 0, 0, 0, 1, 1, 3, 0, 0, 0, 0, 1 });

            Assert.Throws<InvalidDataException>(() =>
            {
                using (MidxReader _ = new MidxReader(path)) { }
            });
        }

        [Test]
        public void HandlesAllFanoutBuckets()
        {
            // Create OIDs that span all 256 fanout buckets
            List<string> oids = new List<string>();
            for (int i = 0; i < 256; i++)
            {
                byte[] raw = new byte[20];
                raw[0] = (byte)i;
                raw[1] = 0x42;
                oids.Add(BitConverter.ToString(raw).Replace("-", "").ToLowerInvariant());
            }

            oids.Sort(StringComparer.Ordinal);
            string midxPath = WriteMidxFile(this.tempDir, oids.ToArray(), new[] { "pack-full" });

            using (MidxReader reader = new MidxReader(midxPath))
            {
                reader.TotalObjects.ShouldEqual(256);
                foreach (string oid in oids)
                {
                    reader.Exists(oid).ShouldBeTrue($"OID {oid} should exist");
                }
            }
        }

        /// <summary>
        /// Writes a synthetic MIDX v1 file.
        /// Format: Header(12) + ChunkTOC(numChunks*12 + 12 terminator) + PNAM + OIDF + OIDL + OOFF
        /// </summary>
        internal static string WriteMidxFile(string dir, string[] sortedOidHexes, string[] packNames)
        {
            int numObjects = sortedOidHexes.Length;
            int numPacks = packNames.Length;

            // PNAM chunk: null-terminated .idx filenames concatenated
            List<byte> pnamBytes = new List<byte>();
            foreach (string name in packNames)
            {
                byte[] nameBytes = System.Text.Encoding.ASCII.GetBytes(name + ".idx\0");
                pnamBytes.AddRange(nameBytes);
            }

            // Pad PNAM to 4-byte alignment
            while (pnamBytes.Count % 4 != 0)
            {
                pnamBytes.Add(0);
            }

            // OIDF (fanout): 256 * 4 bytes
            uint[] fanout = new uint[256];
            foreach (string hex in sortedOidHexes)
            {
                int firstByte = (HexVal(hex[0]) << 4) | HexVal(hex[1]);
                fanout[firstByte]++;
            }

            // Make cumulative
            for (int i = 1; i < 256; i++)
            {
                fanout[i] += fanout[i - 1];
            }

            // OIDL: sorted 20-byte OIDs
            byte[] oidlBytes = new byte[numObjects * 20];
            for (int i = 0; i < numObjects; i++)
            {
                byte[] oid = HexToByteArray(sortedOidHexes[i]);
                Array.Copy(oid, 0, oidlBytes, i * 20, 20);
            }

            // OOFF: dummy 8-byte entries per object (pack-id:4 + offset:4)
            byte[] ooffBytes = new byte[numObjects * 8];

            // Chunk layout: 3 chunks (PNAM, OIDF, OIDL) + OOFF for terminator boundary
            int numChunks = 4; // PNAM, OIDF, OIDL, OOFF
            int headerSize = 12;
            int tocSize = (numChunks * 12) + 12; // +12 for terminator
            long dataStart = headerSize + tocSize;

            long pnamOff = dataStart;
            long oidfOff = pnamOff + pnamBytes.Count;
            long oidlOff = oidfOff + (256 * 4);
            long ooffOff = oidlOff + oidlBytes.Length;
            long endOff = ooffOff + ooffBytes.Length;

            string path = Path.Combine(dir, "multi-pack-index");
            using (FileStream fs = File.Create(path))
            using (BinaryWriter bw = new BinaryWriter(fs))
            {
                // Header
                bw.Write(new byte[] { 0x4D, 0x49, 0x44, 0x58 }); // MIDX
                bw.Write((byte)1); // version
                bw.Write((byte)1); // oid version (SHA-1)
                bw.Write((byte)numChunks);
                bw.Write((byte)0); // reserved
                WriteBE32(bw, (uint)numPacks);

                // Chunk TOC
                WriteTocEntry(bw, 0x504E414D, pnamOff);  // PNAM
                WriteTocEntry(bw, 0x4F494446, oidfOff);  // OIDF
                WriteTocEntry(bw, 0x4F49444C, oidlOff);  // OIDL
                WriteTocEntry(bw, 0x4F4F4646, ooffOff);  // OOFF
                WriteTocEntry(bw, 0x00000000, endOff);    // Terminator

                // PNAM
                bw.Write(pnamBytes.ToArray());

                // OIDF (fanout)
                for (int i = 0; i < 256; i++)
                {
                    WriteBE32(bw, fanout[i]);
                }

                // OIDL
                bw.Write(oidlBytes);

                // OOFF
                bw.Write(ooffBytes);
            }

            return path;
        }

        internal static string[] GenerateSortedOids(int count)
        {
            Random rng = new Random(42); // deterministic
            HashSet<string> set = new HashSet<string>();
            while (set.Count < count)
            {
                byte[] raw = new byte[20];
                rng.NextBytes(raw);
                set.Add(BitConverter.ToString(raw).Replace("-", "").ToLowerInvariant());
            }

            string[] result = set.ToArray();
            Array.Sort(result, StringComparer.Ordinal);
            return result;
        }

        private static void WriteTocEntry(BinaryWriter bw, uint chunkId, long offset)
        {
            WriteBE32(bw, chunkId);
            WriteBE64(bw, offset);
        }

        private static void WriteBE32(BinaryWriter bw, uint value)
        {
            bw.Write((byte)(value >> 24));
            bw.Write((byte)(value >> 16));
            bw.Write((byte)(value >> 8));
            bw.Write((byte)value);
        }

        private static void WriteBE64(BinaryWriter bw, long value)
        {
            bw.Write((byte)(value >> 56));
            bw.Write((byte)(value >> 48));
            bw.Write((byte)(value >> 40));
            bw.Write((byte)(value >> 32));
            bw.Write((byte)(value >> 24));
            bw.Write((byte)(value >> 16));
            bw.Write((byte)(value >> 8));
            bw.Write((byte)value);
        }

        private static byte[] HexToByteArray(string hex)
        {
            byte[] result = new byte[hex.Length / 2];
            for (int i = 0; i < result.Length; i++)
            {
                result[i] = (byte)((HexVal(hex[i * 2]) << 4) | HexVal(hex[(i * 2) + 1]));
            }

            return result;
        }

        private static int HexVal(char c)
        {
            if (c >= 'a') return c - 'a' + 10;
            if (c >= 'A') return c - 'A' + 10;
            return c - '0';
        }
    }
}
