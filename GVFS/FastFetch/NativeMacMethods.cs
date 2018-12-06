using GVFS.Common.Tracing;
using System;
using System.ComponentModel;
using System.Runtime.InteropServices;

namespace FastFetch
{
    public class NativeMacMethods
    {
        public const int ReadOnly = 0x0000;
        public const int WriteOnly = 0x0001;

        public const int Create = 0x0200;
        public const int Truncate = 0x0400;

        public static unsafe void WriteFile(ITracer tracer, byte* originalData, long originalSize, string destination, ushort mode)
        {
            int fileDescriptor = -1;
            try
            {
                fileDescriptor = Open(destination, WriteOnly | Create | Truncate, mode);
                if (fileDescriptor != -1)
                {
                    IntPtr result = Write(fileDescriptor, originalData, (IntPtr)originalSize);
                    if (result.ToInt32() == -1)
                    {
                        throw new Win32Exception(Marshal.GetLastWin32Error());
                    }
                }
            }
            catch (Win32Exception e)
            {
                tracer.RelatedError("Error writing file {0}. Win32Exception: {1}", destination, e);
                throw;
            }
            finally
            {
                Close(fileDescriptor);
            }
        }
        [DllImport("libc", EntryPoint = "open", SetLastError = true)]
        public static extern int Open(string path, int flag, int creationMode);

        [DllImport("libc", EntryPoint = "close", SetLastError = true)]
        public static extern int Close(int fd);

        [DllImport("libc", EntryPoint = "write", SetLastError = true)]
        private static unsafe extern IntPtr Write(int fileDescriptor, void* buf, IntPtr count);
    }
}
