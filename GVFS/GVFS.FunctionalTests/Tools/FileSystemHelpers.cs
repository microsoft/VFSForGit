using System;
using System.Runtime.InteropServices;

namespace GVFS.FunctionalTests.Tools
{
    public static class FileSystemHelpers
    {
        public static StringComparison PathComparison
        {
            get
            {
                return CaseSensitiveFileSystem ?
                    StringComparison.Ordinal :
                    StringComparison.OrdinalIgnoreCase;
            }
        }

        public static StringComparer PathComparer
        {
            get
            {
                return CaseSensitiveFileSystem ?
                    StringComparer.Ordinal :
                    StringComparer.OrdinalIgnoreCase;
            }
        }

        public static bool CaseSensitiveFileSystem
        {
            get
            {
                return RuntimeInformation.IsOSPlatform(OSPlatform.Linux);
            }
        }
    }
}
