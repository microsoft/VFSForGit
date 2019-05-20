using System;
using MirrorProvider.POSIX;

namespace MirrorProvider.Mac
{
    public static class MacNative
    {
        public const int ENAMETOOLONG = 63;

        public static string ReadLink(string path, out int error)
        {
            return POSIXNative.ReadLink(path, ENAMETOOLONG, out error);
        }
    }
}
