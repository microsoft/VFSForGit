using System;
using System.IO;
using System.Linq;
using System.Text;

namespace GVFS.Common.NetworkStreams
{
    /// <summary>
    /// Deserializer for concatenated loose objects.
    /// </summary>
    public class BatchedLooseObjectDeserializer
    {
        private const int NumObjectIdBytes = 20;
        private const int NumObjectHeaderBytes = NumObjectIdBytes + sizeof(long);
        private static readonly byte[] ExpectedHeader
            = new byte[]
            {
                (byte)'G', (byte)'V', (byte)'F', (byte)'S', (byte)' ', // Magic
                1 // Version
            };

        private readonly Stream source;
        private readonly OnLooseObject onLooseObject;

        public BatchedLooseObjectDeserializer(Stream source, OnLooseObject onLooseObject)
        {
            this.source = source;
            this.onLooseObject = onLooseObject;
        }

        /// <summary>
        /// Invoked when the full content of a single loose object is available.
        /// </summary>
        public delegate void OnLooseObject(Stream objectStream, string sha1);

        /// <summary>
        /// Read all the objects from the source stream and call <see cref="OnLooseObject"/> for each.
        /// </summary>
        /// <returns>The total number of objects read</returns>
        public int ProcessObjects()
        {
            this.ValidateHeader();

            // Start reading objects
            int numObjectsRead = 0;
            byte[] curObjectHeader = new byte[NumObjectHeaderBytes];

            while (true)
            {
                bool keepReading = this.ShouldContinueReading(curObjectHeader);
                if (!keepReading)
                {
                    break;
                }

                // Get the length
                long curLength = BitConverter.ToInt64(curObjectHeader, NumObjectIdBytes);

                // Handle the loose object
                using (Stream rawObjectData = new RestrictedStream(this.source, curLength))
                {
                    string objectId = SHA1Util.HexStringFromBytes(curObjectHeader, NumObjectIdBytes);

                    if (objectId.Equals(GVFSConstants.AllZeroSha))
                    {
                        throw new RetryableException("Received all-zero SHA before end of stream");
                    }

                    this.onLooseObject(rawObjectData, objectId);
                    numObjectsRead++;
                }
            }

            return numObjectsRead;
        }

        /// <summary>
        /// Parse the current object header to check if we've reached the end.
        /// </summary>
        /// <returns>true if the end of the stream has been reached, false if not</returns>
        private bool ShouldContinueReading(byte[] curObjectHeader)
        {
            int totalBytes = StreamUtil.TryReadGreedy(
                this.source,
                curObjectHeader,
                0,
                curObjectHeader.Length);

            if (totalBytes == NumObjectHeaderBytes)
            {
                // Successful header read
                return true;
            }
            else if (totalBytes == NumObjectIdBytes)
            {
                // We may have finished reading all the objects
                for (int i = 0; i < NumObjectIdBytes; i++)
                {
                    if (curObjectHeader[i] != 0)
                    {
                        throw new RetryableException(
                            string.Format(
                                "Reached end of stream before we got the expected zero-object ID Buffer: {0}",
                                SHA1Util.HexStringFromBytes(curObjectHeader)));
                    }
                }

                return false;
            }
            else
            {
                throw new RetryableException(
                    string.Format(
                        "Reached end of stream before expected {0} or {1} bytes. Got {2}. Buffer: {3}",
                        NumObjectHeaderBytes,
                        NumObjectIdBytes,
                        totalBytes,
                        SHA1Util.HexStringFromBytes(curObjectHeader)));
            }
        }

        private void ValidateHeader()
        {
            byte[] headerBuf = new byte[ExpectedHeader.Length];
            StreamUtil.TryReadGreedy(this.source, headerBuf, 0, headerBuf.Length);
            if (!headerBuf.SequenceEqual(ExpectedHeader))
            {
                throw new InvalidDataException("Unexpected header: " + Encoding.UTF8.GetString(headerBuf));
            }
        }
    }
}
