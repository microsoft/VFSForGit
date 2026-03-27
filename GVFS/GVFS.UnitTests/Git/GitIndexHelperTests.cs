using GVFS.Common.Git;
using GVFS.Tests.Should;
using NUnit.Framework;
using System;
using System.IO;

namespace GVFS.UnitTests.Git
{
    [TestFixture]
    public class GitIndexHelperTests
    {
        [TestCase]
        public void ReadEntryCount_ValidIndex()
        {
            // Git index header: 4-byte signature + 4-byte version + 4-byte entry count (big-endian)
            byte[] header = BuildIndexHeader(42);
            using (MemoryStream stream = new MemoryStream(header))
            {
                GitIndexHelper.ReadEntryCount(stream).ShouldEqual(42);
            }
        }

        [TestCase]
        public void ReadEntryCount_LargeCount()
        {
            byte[] header = BuildIndexHeader(2_448_906);
            using (MemoryStream stream = new MemoryStream(header))
            {
                GitIndexHelper.ReadEntryCount(stream).ShouldEqual(2_448_906);
            }
        }

        [TestCase]
        public void ReadEntryCount_ZeroEntries()
        {
            byte[] header = BuildIndexHeader(0);
            using (MemoryStream stream = new MemoryStream(header))
            {
                GitIndexHelper.ReadEntryCount(stream).ShouldEqual(0);
            }
        }

        [TestCase]
        public void ReadEntryCount_StreamTooShort()
        {
            using (MemoryStream stream = new MemoryStream(new byte[8]))
            {
                GitIndexHelper.ReadEntryCount(stream).ShouldEqual(-1);
            }
        }

        [TestCase]
        public void ReadEntryCount_EmptyStream()
        {
            using (MemoryStream stream = new MemoryStream(new byte[0]))
            {
                GitIndexHelper.ReadEntryCount(stream).ShouldEqual(-1);
            }
        }

        [TestCase]
        public void ReadEntryCount_StreamPositionIsReset()
        {
            // Even if the stream starts at a non-zero position, ReadEntryCount seeks to offset 8
            byte[] header = BuildIndexHeader(99);
            using (MemoryStream stream = new MemoryStream(header))
            {
                stream.Position = 4;
                GitIndexHelper.ReadEntryCount(stream).ShouldEqual(99);
            }
        }

        [TestCase]
        public void ReadEntryCount_ExactlyTwelveBytes()
        {
            byte[] header = BuildIndexHeader(1);
            using (MemoryStream stream = new MemoryStream(header))
            {
                GitIndexHelper.ReadEntryCount(stream).ShouldEqual(1);
            }
        }

        [TestCase]
        public void ReadEntryCount_WithTrailingData()
        {
            // Real index files have entries after the header
            byte[] data = new byte[1024];
            byte[] header = BuildIndexHeader(500);
            Array.Copy(header, data, header.Length);
            using (MemoryStream stream = new MemoryStream(data))
            {
                GitIndexHelper.ReadEntryCount(stream).ShouldEqual(500);
            }
        }

        /// <summary>
        /// Builds a minimal 12-byte git index header with the given entry count.
        /// Format: "DIRC" (signature) + version 2 (4 bytes BE) + entry count (4 bytes BE)
        /// </summary>
        private static byte[] BuildIndexHeader(int entryCount)
        {
            byte[] header = new byte[12];

            // Signature: "DIRC"
            header[0] = (byte)'D';
            header[1] = (byte)'I';
            header[2] = (byte)'R';
            header[3] = (byte)'C';

            // Version: 2 (big-endian)
            header[4] = 0;
            header[5] = 0;
            header[6] = 0;
            header[7] = 2;

            // Entry count (big-endian)
            byte[] countBytes = BitConverter.GetBytes(entryCount);
            if (BitConverter.IsLittleEndian)
            {
                Array.Reverse(countBytes);
            }

            Array.Copy(countBytes, 0, header, 8, 4);
            return header;
        }
    }
}
