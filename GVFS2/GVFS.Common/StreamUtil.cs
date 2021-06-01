using System;
using System.IO;

namespace GVFS.Common
{
    public class StreamUtil
    {
        /// <summary>
        /// .NET default buffer size <see cref="Stream.CopyTo"/> uses as of 8/30/16
        /// </summary>
        public const int DefaultCopyBufferSize = 81920;

        /// <summary>
        /// Copies all bytes from the source stream to the destination stream.  This is an exact copy
        /// of Stream.CopyTo(), but can uses the supplied buffer instead of allocating a new one.
        /// </summary>
        /// <remarks>
        /// As of .NET 4.6, each call to Stream.CopyTo() allocates a new 80K byte[] buffer, which
        /// consumes many more resources than reusing one we already have if the scenario allows it.
        /// </remarks>
        /// <param name="source">Source stream to copy from</param>
        /// <param name="destination">Destination stream to copy to</param>
        /// <param name="buffer">
        /// Shared buffer to use. If null, we allocate one with the same size .NET would otherwise use.
        /// </param>
        public static void CopyToWithBuffer(Stream source, Stream destination, byte[] buffer = null)
        {
            buffer = buffer ?? new byte[DefaultCopyBufferSize];
            int read;
            while (true)
            {
                try
                {
                    read = source.Read(buffer, 0, buffer.Length);
                }
                catch (Exception ex)
                {
                    // All exceptions potentially from network should be retried
                    throw new RetryableException("Exception while reading stream. See inner exception for details.", ex);
                }

                if (read == 0)
                {
                    break;
                }

                destination.Write(buffer, 0, read);
            }
        }

        /// <summary>
        /// Call <see cref="Stream.Read"/> until either <paramref name="count"/> bytes are read or
        /// the end of <paramref name="stream"/> is reached.
        /// </summary>
        /// <param name="buf">Buffer to read bytes into.</param>
        /// <param name="offset">Offset in <paramref name="buf"/> to start reading into.</param>
        /// <param name="count">Maximum number of bytes to read.</param>
        /// <returns>
        /// Number of bytes read, may be less than <paramref name="count"/> if end was reached.
        /// </returns>
        public static int TryReadGreedy(Stream stream, byte[] buf, int offset, int count)
        {
            int totalRead = 0;
            int read = 0;
            while (totalRead < count)
            {
                int start = offset + totalRead;
                int length = count - totalRead;

                try
                {
                    read = stream.Read(buf, start, length);
                }
                catch (Exception ex)
                {
                    // All exceptions potentially from network should be retried
                    throw new RetryableException("Exception while reading stream. See inner exception for details.", ex);
                }

                if (read == 0)
                {
                    break;
                }

                totalRead += read;
            }

            return totalRead;
        }
    }
}
