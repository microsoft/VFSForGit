using System;
using MirrorProvider.POSIX;

namespace MirrorProvider.Linux
{
    public static class LinuxNative
    {
        public const int ENAMETOOLONG = 36;

        public static string ReadLink(string path, out int error)
        {
            return POSIXNative.ReadLink(path, ENAMETOOLONG, out error);
        }
    }
}
