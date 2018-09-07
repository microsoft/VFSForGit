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
        private const int DefaultBufferSize = 1024;
        private const byte TerminatorByte = 0x3;

        private int bufferSize;
        private byte[] buffer;
        private Stream stream;

        public NamedPipeStreamReader(Stream stream, int bufferSize)
        {
            this.stream = stream;
            this.bufferSize = bufferSize;
            this.buffer = new byte[this.bufferSize];
        }

        public NamedPipeStreamReader(Stream stream)
            : this(stream, DefaultBufferSize)
        {
        }

        /// <summary>
        /// Read a message from the stream.
        /// </summary>
        /// <returns>The message read from the stream, or null if the end of the input stream has been reached. </returns>
        public string ReadMessage()
        {
            int bytesRead = this.Read();
            if (bytesRead == 0)
            {
                // The end of the stream has been reached - return null to indicate this.
                return null;
            }

            // If we have read in the entire message in the first read (mainline scenario),
            // then just process the data directly from the buffer.
            if (this.buffer[bytesRead - 1] == TerminatorByte)
            {
                return Encoding.UTF8.GetString(this.buffer, 0, bytesRead - 1);
            }

            // We need to process multiple chunks - collect data from multiple chunks
            // into a single list
            List<byte> bytes = new List<byte>(this.bufferSize * 2);

            while (true)
            {
                bool encounteredTerminatorByte = this.buffer[bytesRead - 1] == TerminatorByte;
                int lengthToCopy = encounteredTerminatorByte ? bytesRead - 1 : bytesRead;

                bytes.AddRange(new ArraySegment<byte>(this.buffer, 0, lengthToCopy));
                if (encounteredTerminatorByte)
                {
                    break;
                }

                bytesRead = this.Read();

                if (bytesRead == 0)
                {
                    // We have read a partial message (the last byte received does not indicate that
                    // this was the end of the message), but the stream has been closed. Throw an exception
                    // and let upper layer deal with this condition.

                    throw new IOException("Incomplete message read from stream. The end of the stream was reached without the expected terminating byte.");
                }
            }

            return Encoding.UTF8.GetString(bytes.ToArray());
        }

        /// <summary>
        /// Read the next chunk of bytes from the stream.
        /// </summary>
        /// <returns>The number of bytes read.</returns>
        private int Read()
        {
            return this.stream.Read(this.buffer, 0, this.buffer.Length);
        }
    }
}
