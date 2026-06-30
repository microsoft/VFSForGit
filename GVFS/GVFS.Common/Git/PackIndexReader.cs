using System;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Runtime.CompilerServices;

namespace GVFS.Common.Git
{
    /// <summary>
    /// Reads a git pack index (.idx) v2 file and performs binary search
    /// lookups against the sorted OID table. Pure managed code, thread-safe.
    /// </summary>
    public sealed class PackIndexReader : IDisposable
    {
        // Pack index v2 magic: 0xff 0x74 0x4f 0x63
        private const uint IdxV2Magic = 0xFF744F63;
        private const int FanoutEntries = 256;
        private const int FanoutSize = FanoutEntries * 4;
        private const int HeaderSize = 8; // magic(4) + version(4)

        private readonly MemoryMappedFile mmf;
        private readonly MemoryMappedViewAccessor accessor;
        private readonly int totalObjects;
        private readonly long fanoutOffset;
        private readonly long oidTableOffset;
        private readonly int hashLen;

        public int TotalObjects => this.totalObjects;

        public PackIndexReader(string idxPath)
        {
            long fileLength = new FileInfo(idxPath).Length;
            this.mmf = MemoryMappedFile.CreateFromFile(idxPath, FileMode.Open, null, 0, MemoryMappedFileAccess.Read);
            try
            {
                this.accessor = this.mmf.CreateViewAccessor(0, fileLength, MemoryMappedFileAccess.Read);
                try
                {
                    uint magic = this.ReadUInt32BE(0);
                    if (magic != IdxV2Magic)
                    {
                        throw new InvalidDataException($"Unsupported pack index format (magic=0x{magic:X8}), expected v2");
                    }

                    uint version = this.ReadUInt32BE(4);
                    if (version != 2)
                    {
                        throw new InvalidDataException($"Unsupported pack index version {version}");
                    }

                    this.hashLen = 20; // SHA-1
                    this.fanoutOffset = HeaderSize;
                    this.oidTableOffset = HeaderSize + FanoutSize;

                    // Total objects from fanout[255]
                    this.totalObjects = (int)this.ReadUInt32BE(this.fanoutOffset + (255 * 4));
                }
                catch
                {
                    this.accessor.Dispose();
                    throw;
                }
            }
            catch
            {
                this.mmf.Dispose();
                throw;
            }
        }

        /// <summary>
        /// Check if an object with the given SHA-1 hex string exists in this pack index.
        /// Thread-safe.
        /// </summary>
        public bool Exists(string shaHex)
        {
            if (shaHex == null || shaHex.Length < this.hashLen * 2)
            {
                return false;
            }

            Span<byte> oid = stackalloc byte[this.hashLen];
            MidxReader.HexToBytes(shaHex, oid);
            return this.Exists(oid);
        }

        /// <summary>
        /// Check if an object with the given binary OID exists in this pack index.
        /// Thread-safe.
        /// </summary>
        public bool Exists(ReadOnlySpan<byte> oid)
        {
            int firstByte = oid[0];

            uint lo = firstByte == 0 ? 0 : this.ReadUInt32BE(this.fanoutOffset + ((firstByte - 1) * 4));
            uint hi = this.ReadUInt32BE(this.fanoutOffset + (firstByte * 4));

            if (lo >= hi)
            {
                return false;
            }

            return this.BinarySearchOid(oid, (int)lo, (int)hi - 1);
        }

        private bool BinarySearchOid(ReadOnlySpan<byte> target, int lo, int hi)
        {
            while (lo <= hi)
            {
                int mid = lo + ((hi - lo) / 2);
                long offset = this.oidTableOffset + ((long)mid * this.hashLen);

                int cmp = this.CompareOidAtOffset(target, offset);
                if (cmp == 0)
                {
                    return true;
                }
                else if (cmp < 0)
                {
                    hi = mid - 1;
                }
                else
                {
                    lo = mid + 1;
                }
            }

            return false;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private int CompareOidAtOffset(ReadOnlySpan<byte> target, long fileOffset)
        {
            for (int i = 0; i < this.hashLen; i++)
            {
                int diff = target[i] - this.accessor.ReadByte(fileOffset + i);
                if (diff != 0)
                {
                    return diff;
                }
            }

            return 0;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private uint ReadUInt32BE(long offset)
        {
            byte b0 = this.accessor.ReadByte(offset);
            byte b1 = this.accessor.ReadByte(offset + 1);
            byte b2 = this.accessor.ReadByte(offset + 2);
            byte b3 = this.accessor.ReadByte(offset + 3);
            return ((uint)b0 << 24) | ((uint)b1 << 16) | ((uint)b2 << 8) | b3;
        }

        public void Dispose()
        {
            this.accessor.Dispose();
            this.mmf.Dispose();
        }
    }
}
