using System;
using System.Collections.Generic;
using System.IO;

namespace GVFS.Common.NetworkStreams
{
    public static class StreamReaderExtensions
    {
        /// <summary>
        /// Reads the underlying stream until it ends returning all content as a string.
        /// </summary>
        public static string RetryableReadToEnd(this StreamReader reader)
        {
            try
            {
                return reader.ReadToEnd();
            }
            catch (Exception ex)
            {
                // All exceptions potentially from network should be retried
                throw new RetryableException("Exception while reading stream. See inner exception for details.", ex);
            }
        }

        /// <summary>
        /// Reads the stream until it ends returning each line as a string.
        /// </summary>
        public static List<string> RetryableReadAllLines(this StreamReader reader)
        {
            List<string> output = new List<string>();

            while (true)
            {
                string line;
                try
                {
                    if (reader.EndOfStream)
                    {
                        break;
                    }

                    line = reader.ReadLine();
                }
                catch (Exception ex)
                {
                    // All exceptions potentially from network should be retried
                    throw new RetryableException("Exception while reading stream. See inner exception for details.", ex);
                }

                output.Add(line);
            }

            return output;
        }
        
        public static void CopyBlockTo(this Stream input, Stream destination, long numBytes, byte[] buffer)
        {
            int read;
            while (numBytes > 0)
            {
                int bytesToRead = Math.Min(buffer.Length, (int)numBytes);
                read = input.Read(buffer, 0, bytesToRead);
                if (read <= 0)
                {
                    break;
                }

                destination.Write(buffer, 0, read);
                numBytes -= read;
            }
        }
    }
}
