using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace GVFS.Common.NetworkStreams
{
    /// <summary>
    /// Deserializer for packs and indexes for prefetch packs.
    /// </summary>
    public class PrefetchPacksDeserializer
    {
        private const int NumPackHeaderBytes = 3 * sizeof(long);

        private static readonly byte[] PrefetchPackExpectedHeader =
            new byte[]
            {
                (byte)'G', (byte)'P', (byte)'R', (byte)'E', (byte)' ', // Magic
                1 // Version
            };

        private readonly Stream source;

        public PrefetchPacksDeserializer(Stream source)
        {
            this.source = source;
        }

        /// <summary>
        /// Read all the packs and indexes from the source stream and return a <see cref="PackAndIndex"/> for each pack
        /// and index. Caller must consume pack stream fully before the index stream.
        /// </summary>
        public IEnumerable<PackAndIndex> EnumeratePacks()
        {
            this.ValidateHeader();

            byte[] buffer = new byte[NumPackHeaderBytes];

            int packCount = this.ReadPackCount(buffer);

            for (int i = 0; i < packCount; i++)
            {
                long timestamp;
                long packLength;
                long indexLength;
                this.ReadPackHeader(buffer, out timestamp, out packLength, out indexLength);

                using (Stream packData = new RestrictedStream(this.source, packLength))
                using (Stream indexData = indexLength > 0 ? new RestrictedStream(this.source, indexLength) : null)
                {
                    yield return new PackAndIndex(packData, indexData, timestamp);
                }
            }
        }

        /// <summary>
        /// Read the ushort pack count
        /// </summary>
        private ushort ReadPackCount(byte[] buffer)
        {
            StreamUtil.TryReadGreedy(this.source, buffer, 0, 2);
            return BitConverter.ToUInt16(buffer, 0);
        }

        /// <summary>
        /// Parse the current pack header
        /// </summary>
        private void ReadPackHeader(
            byte[] buffer,
            out long timestamp,
            out long packLength,
            out long indexLength)
        {
            int totalBytes = StreamUtil.TryReadGreedy(
                this.source,
                buffer,
                0,
                NumPackHeaderBytes);

            if (totalBytes == NumPackHeaderBytes)
            {
                timestamp = BitConverter.ToInt64(buffer, 0);
                packLength = BitConverter.ToInt64(buffer, 8);
                indexLength = BitConverter.ToInt64(buffer, 16);
            }
            else
            {
                throw new RetryableException(
                    string.Format(
                        "Reached end of stream before expected {0} bytes. Got {1}. Buffer: {2}",
                        NumPackHeaderBytes,
                        totalBytes,
                        SHA1Util.HexStringFromBytes(buffer)));
            }
        }

        private void ValidateHeader()
        {
            byte[] headerBuf = new byte[PrefetchPackExpectedHeader.Length];
            StreamUtil.TryReadGreedy(this.source, headerBuf, 0, headerBuf.Length);
            if (!headerBuf.SequenceEqual(PrefetchPackExpectedHeader))
            {
                throw new InvalidDataException("Unexpected header: " + Encoding.UTF8.GetString(headerBuf));
            }
        }

        public class PackAndIndex
        {
            public PackAndIndex(Stream packStream, Stream idxStream, long timestamp)
            {
                this.PackStream = packStream;
                this.IndexStream = idxStream;
                this.Timestamp = timestamp;
                this.UniqueId = Guid.NewGuid().ToString("N");
            }

            public Stream PackStream { get; }
            public Stream IndexStream { get; }
            public long Timestamp { get; }
            public string UniqueId { get; }
        }
    }
}
