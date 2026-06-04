using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Runtime.CompilerServices;

namespace GVFS.Common.Git
{
    /// <summary>
    /// Reads a git multi-pack-index (MIDX) file and performs binary search
    /// lookups against the sorted OID table. Pure managed code, thread-safe.
    /// </summary>
    public sealed class MidxReader : IDisposable
    {
        private const uint MidxMagic = 0x4D494458; // "MIDX"
        private const uint ChunkIdPNAM = 0x504E414D; // Pack Names
        private const uint ChunkIdOIDF = 0x4F494446; // OID Fanout
        private const uint ChunkIdOIDL = 0x4F49444C; // OID Lookup

        private readonly MemoryMappedFile mmf;
        private readonly MemoryMappedViewAccessor accessor;
        private int hashLen;
        private long fanoutOffset;
        private long oidLookupOffset;
        private int totalObjects;
        private HashSet<string> packStems;

        public int TotalObjects => this.totalObjects;

        public MidxReader(string path)
        {
            long fileLength = new FileInfo(path).Length;
            this.mmf = MemoryMappedFile.CreateFromFile(path, FileMode.Open, null, 0, MemoryMappedFileAccess.Read);
            try
            {
                this.accessor = this.mmf.CreateViewAccessor(0, fileLength, MemoryMappedFileAccess.Read);
                try
                {
                    this.InitializeFromAccessor();
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

        private void InitializeFromAccessor()
        {
            // Header: MIDX(4) version(1) oidVersion(1) numChunks(1) reserved(1) numPacks(4)
            uint magic = this.ReadUInt32BE(0);
            if (magic != MidxMagic)
            {
                throw new InvalidDataException($"Not a MIDX file (magic=0x{magic:X8})");
            }

            byte version = this.ReadByte(4);
            if (version != 1)
            {
                throw new InvalidDataException($"Unsupported MIDX version {version}");
            }

            byte oidVersion = this.ReadByte(5);
            this.hashLen = oidVersion == 2 ? 32 : 20;
            int numChunks = this.ReadByte(6);

            // Parse chunk TOC at offset 12
            long tocStart = 12;
            long pnamOffset = 0;
            long pnamEnd = 0;
            this.fanoutOffset = 0;
            this.oidLookupOffset = 0;

            // Read all chunk entries + terminator to get chunk boundaries
            long[] chunkOffsets = new long[numChunks + 1];
            uint[] chunkIds = new uint[numChunks];
            for (int i = 0; i < numChunks; i++)
            {
                long entryOff = tocStart + ((long)i * 12);
                chunkIds[i] = this.ReadUInt32BE(entryOff);
                chunkOffsets[i] = this.ReadInt64BE(entryOff + 4);
            }

            // Terminator entry
            long terminatorOff = tocStart + ((long)numChunks * 12);
            chunkOffsets[numChunks] = this.ReadInt64BE(terminatorOff + 4);

            for (int i = 0; i < numChunks; i++)
            {
                switch (chunkIds[i])
                {
                    case ChunkIdPNAM:
                        pnamOffset = chunkOffsets[i];
                        pnamEnd = chunkOffsets[i + 1];
                        break;
                    case ChunkIdOIDF:
                        this.fanoutOffset = chunkOffsets[i];
                        break;
                    case ChunkIdOIDL:
                        this.oidLookupOffset = chunkOffsets[i];
                        break;
                }
            }

            if (this.fanoutOffset == 0 || this.oidLookupOffset == 0)
            {
                throw new InvalidDataException("MIDX missing required OIDF/OIDL chunks");
            }

            // Total objects from fanout[255]
            this.totalObjects = (int)this.ReadUInt32BE(this.fanoutOffset + (255 * 4));

            // Parse pack names from PNAM chunk
            this.packStems = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (pnamOffset > 0 && pnamEnd > pnamOffset)
            {
                int pnamLen = (int)(pnamEnd - pnamOffset);
                byte[] pnamBuf = new byte[pnamLen];
                this.accessor.ReadArray(pnamOffset, pnamBuf, 0, pnamLen);
                string pnamStr = System.Text.Encoding.ASCII.GetString(pnamBuf);
                foreach (string name in pnamStr.Split('\0', StringSplitOptions.RemoveEmptyEntries))
                {
                    // PNAM stores .idx names; strip extension to get stem
                    string stem = name;
                    if (stem.EndsWith(".idx", StringComparison.OrdinalIgnoreCase))
                    {
                        stem = stem.Substring(0, stem.Length - 4);
                    }

                    this.packStems.Add(stem);
                }
            }
        }

        /// <summary>
        /// Returns the set of pack file stems (without extension) covered by this MIDX.
        /// </summary>
        public HashSet<string> GetPackStems()
        {
            return this.packStems;
        }

        /// <summary>
        /// Check if an object with the given SHA-1 hex string exists in the MIDX.
        /// Thread-safe.
        /// </summary>
        public bool Exists(string shaHex)
        {
            if (shaHex == null || shaHex.Length < this.hashLen * 2)
            {
                return false;
            }

            Span<byte> oid = stackalloc byte[this.hashLen];
            HexToBytes(shaHex, oid);
            return this.Exists(oid);
        }

        /// <summary>
        /// Check if an object with the given binary OID exists in the MIDX.
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
                long offset = this.oidLookupOffset + ((long)mid * this.hashLen);

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

        internal static void HexToBytes(string hex, Span<byte> output)
        {
            for (int i = 0; i < output.Length; i++)
            {
                output[i] = (byte)((HexVal(hex[i * 2]) << 4) | HexVal(hex[(i * 2) + 1]));
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int HexVal(char c)
        {
            if (c >= 'a')
            {
                return c - 'a' + 10;
            }

            if (c >= 'A')
            {
                return c - 'A' + 10;
            }

            return c - '0';
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private byte ReadByte(long offset)
        {
            return this.accessor.ReadByte(offset);
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

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private long ReadInt64BE(long offset)
        {
            Span<byte> buf = stackalloc byte[8];
            for (int i = 0; i < 8; i++)
            {
                buf[i] = this.accessor.ReadByte(offset + i);
            }

            return BinaryPrimitives.ReadInt64BigEndian(buf);
        }

        public void Dispose()
        {
            this.accessor.Dispose();
            this.mmf.Dispose();
        }
    }
}
