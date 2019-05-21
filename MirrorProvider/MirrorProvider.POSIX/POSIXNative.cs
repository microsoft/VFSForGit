using System.Runtime.InteropServices;
using System.Text;

namespace MirrorProvider.POSIX
{
    public static class POSIXNative
    {
        public static string ReadLink(string path, int enametoolong, out int error)
        {
            const long BufSize = 4096;
            byte[] targetBuffer = new byte[BufSize];
            long bytesRead = __ReadLink(path, targetBuffer, BufSize);
            if (bytesRead < 0)
            {
                error = Marshal.GetLastWin32Error();
                return null;
            }
            if (bytesRead == BufSize)
            {
                error = enametoolong;
                return null;
            }

            error = 0;
            return Encoding.UTF8.GetString(targetBuffer, 0, (int)bytesRead);
        }

        [DllImport("libc", EntryPoint = "readlink", SetLastError = true)]
        public static extern long __ReadLink(
            string path,
            byte[] buf,
            ulong bufsize);
    }
}
