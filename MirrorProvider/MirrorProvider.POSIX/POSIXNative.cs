using System.Runtime.InteropServices;
using System.Text;

namespace MirrorProvider.POSIX
{
    public static class POSIXNative
    {
        public static string ReadLink(string path)
        {
            const ulong BufSize = 4096;
            byte[] targetBuffer = new byte[BufSize];
            long bytesRead = __ReadLink(path, targetBuffer, BufSize);
            if (bytesRead < 0)
            {
                return null;
            }

            targetBuffer[bytesRead] = 0;
            return Encoding.UTF8.GetString(targetBuffer);
        }

        [DllImport("libc", EntryPoint = "readlink", SetLastError = true)]
        public static extern long __ReadLink(
            string path,
            byte[] buf,
            ulong bufsize);
    }
}
