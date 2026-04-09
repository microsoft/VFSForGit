using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace GVFS.Common.NamedPipes
{
    /// <summary>
    /// Implements the NamedPipe protocol as described in NamedPipeServer.
    /// </summary>
    public class NamedPipeStreamReader
    {
        private const int InitialListSize = 1024;
        private const byte TerminatorByte = 0x3;
        private readonly byte[] buffer;

        private Stream stream;

        public NamedPipeStreamReader(Stream stream)
        {
            this.stream = stream;
            this.buffer = new byte[1];
        }

        /// <summary>
        /// Read a message from the stream.
        /// </summary>
        /// <returns>The message read from the stream, or null if the end of the input stream has been reached. </returns>
        public string ReadMessage()
        {
            byte currentByte;

            bool streamOpen = this.TryReadByte(out currentByte);
            if (!streamOpen)
            {
                // The end of the stream has been reached - return null to indicate this.
                return null;
            }

            List<byte> bytes = new List<byte>(InitialListSize);

            do
            {
                bytes.Add(currentByte);
                streamOpen = this.TryReadByte(out currentByte);

                if (!streamOpen)
                {
                    // We have read a partial message (the last byte received does not indicate that
                    // this was the end of the message), but the stream has been closed. Throw an exception
                    // and let upper layer deal with this condition.

                    throw new IOException("Incomplete message read from stream. The end of the stream was reached without the expected terminating byte.");
                }
            }
            while (currentByte != TerminatorByte);

            return Encoding.UTF8.GetString(bytes.ToArray());
        }

        /// <summary>
        /// Read a byte from the stream.
        /// </summary>
        /// <param name="readByte">The byte read from the stream</param>
        /// <returns>True if byte read, false if end of stream has been reached</returns>
        private bool TryReadByte(out byte readByte)
        {
            this.buffer[0] = 0;

            int numBytesRead = this.stream.Read(this.buffer, 0, 1);
            readByte = this.buffer[0];

            return numBytesRead == 1;
        }
    }
}
