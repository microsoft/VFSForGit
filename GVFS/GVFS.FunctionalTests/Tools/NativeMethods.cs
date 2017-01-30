using System.Runtime.InteropServices;

namespace GVFS.FunctionalTests.Tools
{
    public class NativeMethods
    {
        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool MoveFile(string lpExistingFileName, string lpNewFileName);
    }
}
