using System;
using System.IO;
using System.Threading.Tasks;

namespace GVFS.Common.Physical.FileSystem
{
    public static class StreamReaderExtensions
    {
        private const int ReadWriteTimeoutMs = 10000;
        private const int BufferSize = 64 * 1024;
        
        public static void CopyBlockTo<TTimeoutException>(this StreamReader input, StreamWriter writer, long numBytes)
            where TTimeoutException : TimeoutException, new()
        {
            char[] buffer = new char[BufferSize];
            int read;
            while (numBytes > 0)
            {
                int bytesToRead = Math.Min(buffer.Length, (int)numBytes);
                read = input.ReadBlockAsync(buffer, 0, bytesToRead).Timeout<int, TTimeoutException>(ReadWriteTimeoutMs);
                if (read <= 0)
                {
                    break;
                }

                writer.WriteAsync(buffer, 0, read).Timeout<TTimeoutException>(ReadWriteTimeoutMs);
                numBytes -= read;
            }
        }

        public static async Task CopyBlockToAsync(this StreamReader input, StreamWriter writer, long numBytes)
        {
            char[] buffer = new char[BufferSize];
            int read;
            while (numBytes > 0)
            {
                int bytesToRead = Math.Min(buffer.Length, (int)Math.Min(numBytes, int.MaxValue));
                read = await input.ReadBlockAsync(buffer, 0, bytesToRead);
                if (read <= 0)
                {
                    break;
                }

                await writer.WriteAsync(buffer, 0, read);
                numBytes -= read;
            }
        }
    }
}
