using GVFS.Common;
using GVFS.Platform.POSIX;
using PrjFSLib.Linux;
using System.Runtime.InteropServices;

namespace GVFS.Platform.Linux
{
    public class LinuxFileSystem : POSIXFileSystem
    {
        public override void ChangeMode(string path, ushort mode)
        {
            Chmod(path, mode);
        }

        public override bool HydrateFile(string fileName, byte[] buffer)
        {
            return NativeFileReader.TryReadFirstByteOfFile(fileName, buffer);
        }

        public override bool IsExecutable(string fileName)
        {
            LinuxNative.StatBuffer statBuffer = this.StatFile(fileName);
            return LinuxNative.IsExecutable(statBuffer.Mode);
        }

        public override bool IsSocket(string fileName)
        {
            LinuxNative.StatBuffer statBuffer = this.StatFile(fileName);
            return LinuxNative.IsSock(statBuffer.Mode);
        }

        [DllImport("libc", EntryPoint = "chmod", SetLastError = true)]
        private static extern int Chmod(string pathname, uint mode);

        private LinuxNative.StatBuffer StatFile(string fileName)
        {
            if (LinuxNative.Stat(fileName, out LinuxNative.StatBuffer statBuffer) != 0)
            {
                NativeMethods.ThrowLastWin32Exception($"Failed to stat {fileName}");
            }

            return statBuffer;
        }

        private static class NativeFileReader
        {
            private const int ReadOnly = 0x0000;

            internal static bool TryReadFirstByteOfFile(string fileName, byte[] buffer)
            {
                int fileDescriptor = -1;
                bool readStatus = false;
                try
                {
                    fileDescriptor = Open(fileName, ReadOnly);
                    if (fileDescriptor != -1)
                    {
                        readStatus = TryReadOneByte(fileDescriptor, buffer);
                    }
                }
                finally
                {
                    Close(fileDescriptor);
                }

                return readStatus;
            }

            [DllImport("libc", EntryPoint = "open", SetLastError = true)]
            private static extern int Open(string path, int flag);

            [DllImport("libc", EntryPoint = "close", SetLastError = true)]
            private static extern int Close(int fd);

            [DllImport("libc", EntryPoint = "read", SetLastError = true)]
            private static extern long Read(int fd, [Out] byte[] buf, ulong count);

            private static bool TryReadOneByte(int fileDescriptor, byte[] buffer)
            {
                long numBytes = Read(fileDescriptor, buffer, 1);

                if (numBytes == -1)
                {
                    return false;
                }

                return true;
            }
        }
    }
}
