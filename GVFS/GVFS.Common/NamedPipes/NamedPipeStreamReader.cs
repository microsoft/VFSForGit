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

        /// <summary>
        /// The index in the buffer that the next message starts at. If this
        /// is greater than or equal to the number of bytes that have been read into the buffer,
        /// then we need to read more bytes from the stream.
        /// </summary>
        private int nextMessageChunkStartIndex;

        /// <summary>
        /// The number of bytes that have been read from the stream and into
        /// the buffer.
        /// </summary>
        private int numBytesReadIntoBuffer;

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
            if (this.nextMessageChunkStartIndex >= this.numBytesReadIntoBuffer)
            {
                this.Read();
                if (this.numBytesReadIntoBuffer == 0)
                {
                    // The end of the stream has been reached - return null to indicate this.
                    return null;
                }
            }

            // The index in the buffer of the first and last byte that belongs
            // to the message being read (inclusive).
            int messageChunkStartIndex, messageChunkEndIndex;

            int messageChunkLength = this.ReadMessageChunkFromBuffer(out messageChunkStartIndex, out messageChunkEndIndex);
            bool finishedReadingMessage = this.DidReadCompleteMessage(messageChunkEndIndex);

            // If we have read in the entire message in the first read,
            // then just process the data directly from the buffer.
            // This is the most frequent scenario.
            if (finishedReadingMessage)
            {
                return Encoding.UTF8.GetString(this.buffer, messageChunkStartIndex, messageChunkLength);
            }

            // If the message is contained in multiple chunks (i.e. we need to read bytes from the stream multiple times),
            // collect the data into a single list
            List<byte> bytes = new List<byte>(this.bufferSize * 2);

            while (true)
            {
                if (messageChunkLength > 0)
                {
                    bytes.AddRange(new ArraySegment<byte>(this.buffer, messageChunkStartIndex, messageChunkLength));
                }

                if (finishedReadingMessage)
                {
                    break;
                }

                this.Read();

                if (this.numBytesReadIntoBuffer == 0)
                {
                    // We have read a partial message (the last byte received does not indicate that
                    // this was the end of the message), but the stream has been closed. Throw an exception
                    // and let upper layer deal with this condition.

                    throw new IOException("Incomplete message read from stream. The end of the stream was reached without the expected terminating byte.");
                }

                messageChunkLength = this.ReadMessageChunkFromBuffer(out messageChunkStartIndex, out messageChunkEndIndex);

                finishedReadingMessage = this.DidReadCompleteMessage(messageChunkEndIndex);
            }

            return Encoding.UTF8.GetString(bytes.ToArray());
        }

        /// <summary>
        /// Identifies the next message chunk from the buffer
        /// </summary>
        /// <param name="messageChunkStartIndex">The start index of the message chunk</param>
        /// <param name="messageChunkEndIndex">The end index of the message chunk</param>
        /// <returns>The length of the message chunk.</returns>
        private int ReadMessageChunkFromBuffer(out int messageChunkStartIndex, out int messageChunkEndIndex)
        {
            messageChunkStartIndex = this.nextMessageChunkStartIndex;
            int i;
            for (i = messageChunkStartIndex; i < this.numBytesReadIntoBuffer; i++)
            {
                if (this.buffer[i] == TerminatorByte)
                {
                    break;
                }
            }

            // If we broke out of loop early, then i will be the
            // index of the message terminating byte. If the loop ran until
            // the loop condition was false, then i will be the count
            // of bytes read (1 greater than the index of the last byte).
            // Either way, the index of the last byte of the message
            // will be the previous byte.
            messageChunkEndIndex = i - 1;

            if (i == this.numBytesReadIntoBuffer)
            {
                // We have read all the bytes in the buffer -
                // set the start of the next message to be
                // the following index.
                this.nextMessageChunkStartIndex = i;
            }
            else
            {
                this.nextMessageChunkStartIndex = i + 1;
            }

            return messageChunkEndIndex - messageChunkStartIndex + 1;
        }

        /// <summary>
        /// Has the entire message been read from the stream into the buffer.
        /// </summary>
        /// <param name="messageChunkEndIndex">The index of the last byte of the message chunk</param>
        private bool DidReadCompleteMessage(int messageChunkEndIndex)
        {
            return messageChunkEndIndex < this.numBytesReadIntoBuffer - 1;
        }

        /// <summary>
        /// Read the next chunk of bytes from the stream.
        /// </summary>
        /// <returns>The number of bytes read.</returns>
        private void Read()
        {
            this.nextMessageChunkStartIndex = 0;
            this.numBytesReadIntoBuffer = this.stream.Read(this.buffer, 0, this.buffer.Length);
        }
    }
}
