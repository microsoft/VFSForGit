using GVFS.Common.Git;
using GVFS.Tests.Should;
using NUnit.Framework;
using System;
using System.IO;
using System.Linq;

namespace GVFS.UnitTests.Prefetch
{
    [TestFixture]
    public class PackIndexReaderTests
    {
        private string tempDir;

        [SetUp]
        public void SetUp()
        {
            this.tempDir = Path.Combine(Path.GetTempPath(), "PackIndexReaderTests_" + Guid.NewGuid().ToString("N").Substring(0, 8));
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
            string[] oids = MidxReaderTests.GenerateSortedOids(50);
            string idxPath = WritePackIndexV2(this.tempDir, "pack-test1", oids);

            using (PackIndexReader reader = new PackIndexReader(idxPath))
            {
                reader.TotalObjects.ShouldEqual(50);
                reader.Exists(oids[0]).ShouldBeTrue();
                reader.Exists(oids[25]).ShouldBeTrue();
                reader.Exists(oids[49]).ShouldBeTrue();
            }
        }

        [Test]
        public void ReturnsFalseForMissingObject()
        {
            string[] oids = MidxReaderTests.GenerateSortedOids(50);
            string idxPath = WritePackIndexV2(this.tempDir, "pack-test2", oids);

            using (PackIndexReader reader = new PackIndexReader(idxPath))
            {
                reader.Exists("0000000000000000000000000000000000000000").ShouldBeFalse();
                reader.Exists("ffffffffffffffffffffffffffffffffffffffff").ShouldBeFalse();
            }
        }

        [Test]
        public void HandlesSingleObject()
        {
            string[] oids = MidxReaderTests.GenerateSortedOids(1);
            string idxPath = WritePackIndexV2(this.tempDir, "pack-single", oids);

            using (PackIndexReader reader = new PackIndexReader(idxPath))
            {
                reader.TotalObjects.ShouldEqual(1);
                reader.Exists(oids[0]).ShouldBeTrue();
                reader.Exists("0000000000000000000000000000000000000000").ShouldBeFalse();
            }
        }

        [Test]
        public void ThrowsOnInvalidMagic()
        {
            string path = Path.Combine(this.tempDir, "bad.idx");
            File.WriteAllBytes(path, new byte[] { 0, 0, 0, 0, 0, 0, 0, 2 });

            Assert.Throws<InvalidDataException>(() =>
            {
                using (PackIndexReader _ = new PackIndexReader(path)) { }
            });
        }

        /// <summary>
        /// Writes a synthetic pack index v2 file.
        /// Format: Magic(4) + Version(4) + Fanout(256*4) + OIDs(N*20) + CRC32(N*4) + Offsets(N*4) + PackSHA(20) + IdxSHA(20)
        /// </summary>
        internal static string WritePackIndexV2(string dir, string packStem, string[] sortedOidHexes)
        {
            int numObjects = sortedOidHexes.Length;

            // Fanout
            uint[] fanout = new uint[256];
            foreach (string hex in sortedOidHexes)
            {
                int firstByte = (HexVal(hex[0]) << 4) | HexVal(hex[1]);
                fanout[firstByte]++;
            }

            for (int i = 1; i < 256; i++)
            {
                fanout[i] += fanout[i - 1];
            }

            // OID table
            byte[] oidBytes = new byte[numObjects * 20];
            for (int i = 0; i < numObjects; i++)
            {
                byte[] oid = HexToByteArray(sortedOidHexes[i]);
                Array.Copy(oid, 0, oidBytes, i * 20, 20);
            }

            string path = Path.Combine(dir, packStem + ".idx");
            using (FileStream fs = File.Create(path))
            using (BinaryWriter bw = new BinaryWriter(fs))
            {
                // Magic
                bw.Write(new byte[] { 0xFF, 0x74, 0x4F, 0x63 });
                // Version
                WriteBE32(bw, 2);

                // Fanout
                for (int i = 0; i < 256; i++)
                {
                    WriteBE32(bw, fanout[i]);
                }

                // OID table
                bw.Write(oidBytes);

                // CRC32 table (dummy)
                bw.Write(new byte[numObjects * 4]);

                // Offset table (dummy)
                bw.Write(new byte[numObjects * 4]);

                // Pack SHA + Idx SHA (dummy)
                bw.Write(new byte[40]);
            }

            return path;
        }

        private static void WriteBE32(BinaryWriter bw, uint value)
        {
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
