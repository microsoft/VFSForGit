using System;
using System.IO;
using System.Linq;
using System.Text;
using static GVFS.Virtualization.Projection.GitIndexProjection.GitIndexParser;

namespace GVFS.UnitTests.Mock.Git
{
    /// <summary>
    /// Writes a minimal git index, version 4, with the fields that GitIndexParser reads.
    /// Every entry has the extended flags. The writer omits extensions and the checksum.
    /// Supply the entries in git sort order, because version 4 compresses each path against
    /// the previous path. The prefix compression supports only ASCII paths.
    /// </summary>
    internal static class GitIndexV4Writer
    {
        public const ushort RegularFileMode = 0x81A4;    // 100644
        public const ushort ExecutableFileMode = 0x81ED; // 100755
        public const ushort SparseDirectoryMode = 0x4000; // 040000

        private const ushort ExtendedFlagBit = 0x4000;
        private const ushort SkipWorktreeFlagBit = 0x4000;

        public static byte[] Create(params Entry[] entries)
        {
            using (MemoryStream stream = new MemoryStream())
            using (BinaryWriter writer = new BinaryWriter(stream))
            {
                writer.Write(Encoding.ASCII.GetBytes("DIRC"));
                WriteBigEndian32(writer, 4);
                WriteBigEndian32(writer, (uint)entries.Length);

                string previousPath = string.Empty;
                foreach (Entry entry in entries)
                {
                    // 40 bytes of stat data. The 16-bit type and mode is at offset 26.
                    byte[] statData = new byte[40];
                    statData[26] = (byte)(entry.IndexMode >> 8);
                    statData[27] = (byte)entry.IndexMode;
                    writer.Write(statData);

                    writer.Write(Enumerable.Range(0, 20).Select(i => Convert.ToByte(entry.Sha.Substring(i * 2, 2), 16)).ToArray());

                    byte[] pathBytes = Encoding.UTF8.GetBytes(entry.Path);
                    WriteBigEndian16(writer, (ushort)(Math.Min(pathBytes.Length, 0xFFF) | ExtendedFlagBit | ((ushort)entry.MergeState << 12)));
                    WriteBigEndian16(writer, entry.SkipWorktree ? SkipWorktreeFlagBit : (ushort)0);

                    int commonLength = 0;
                    while (commonLength < Math.Min(previousPath.Length, entry.Path.Length) &&
                        previousPath[commonLength] == entry.Path[commonLength])
                    {
                        commonLength++;
                    }

                    WriteVarint(writer, previousPath.Length - commonLength);
                    writer.Write(Encoding.UTF8.GetBytes(entry.Path.Substring(commonLength)));
                    writer.Write((byte)0);

                    previousPath = entry.Path;
                }

                return stream.ToArray();
            }
        }

        private static void WriteBigEndian32(BinaryWriter writer, uint value)
        {
            writer.Write((byte)(value >> 24));
            writer.Write((byte)(value >> 16));
            writer.Write((byte)(value >> 8));
            writer.Write((byte)value);
        }

        private static void WriteBigEndian16(BinaryWriter writer, ushort value)
        {
            writer.Write((byte)(value >> 8));
            writer.Write((byte)value);
        }

        private static void WriteVarint(BinaryWriter writer, int value)
        {
            // The git index version 4 offset encoding. GitIndexParser.ReadReplaceLength reads it.
            byte[] bytes = new byte[5];
            int position = bytes.Length - 1;
            bytes[position] = (byte)(value & 0x7F);
            while ((value >>= 7) != 0)
            {
                value--;
                bytes[--position] = (byte)(0x80 | (value & 0x7F));
            }

            writer.Write(bytes, position, bytes.Length - position);
        }

        public class Entry
        {
            public Entry(string path, ushort indexMode = RegularFileMode, string sha = null)
            {
                this.Path = path;
                this.IndexMode = indexMode;
                this.Sha = sha ?? new string('0', 40);
            }

            public string Path { get; }
            public ushort IndexMode { get; }
            public string Sha { get; }
            public bool SkipWorktree { get; set; } = true;
            public MergeStage MergeState { get; set; } = MergeStage.NoConflicts;
        }
    }
}
