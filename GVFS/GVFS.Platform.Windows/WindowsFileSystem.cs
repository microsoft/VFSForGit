using GVFS.Common;
using GVFS.Common.FileSystem;
using GVFS.Common.Tracing;
using Microsoft.Win32.SafeHandles;
using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using static GVFS.Common.Git.LibGit2Repo;

namespace GVFS.Platform.Windows
{
    public partial class WindowsFileSystem : IPlatformFileSystem
    {
        private const int AccessDeniedWin32Error = 5;

        public bool SupportsFileMode { get; } = false;

        public void FlushFileBuffers(string path)
        {
            NativeMethods.FlushFileBuffers(path);
        }

        public void MoveAndOverwriteFile(string sourceFileName, string destinationFilename)
        {
            NativeMethods.MoveFile(
                sourceFileName,
                destinationFilename,
                NativeMethods.MoveFileFlags.MoveFileReplaceExisting);
        }

        public void CreateHardLink(string newFileName, string existingFileName)
        {
            NativeMethods.CreateHardLink(newFileName, existingFileName);
        }

        public void ChangeMode(string path, int mode)
        {
        }

        public bool TryGetNormalizedPath(string path, out string normalizedPath, out string errorMessage)
        {
            return WindowsFileSystem.TryGetNormalizedPathImplementation(path, out normalizedPath, out errorMessage);
        }

        public bool HydrateFile(string fileName, byte[] buffer)
        {
            return NativeFileReader.TryReadFirstByteOfFile(fileName, buffer);
        }

        public bool IsExecutable(string fileName)
        {
            string fileExtension = Path.GetExtension(fileName);
            return string.Equals(fileExtension, ".exe", StringComparison.OrdinalIgnoreCase);
        }

        public bool IsSocket(string fileName)
        {
            return false;
        }

        public unsafe void WriteFile(ITracer tracer, byte* originalData, long originalSize, string destination)
        {
            try
            {
                using (SafeFileHandle fileHandle = OpenForWrite(tracer, destination))
                {
                    if (fileHandle.IsInvalid)
                    {
                        throw new Win32Exception(Marshal.GetLastWin32Error());
                    }

                    byte* data = originalData;
                    long size = originalSize;
                    uint written = 0;
                    while (size > 0)
                    {
                        uint toWrite = size < uint.MaxValue ? (uint)size : uint.MaxValue;
                        if (!NativeWriteFile(fileHandle, data, toWrite, out written, IntPtr.Zero))
                        {
                            throw new Win32Exception(Marshal.GetLastWin32Error());
                        }

                        size -= written;
                        data = data + written;
                    }
                }
            }
            catch (Exception e)
            {
                tracer.RelatedError("Exception writing {0}: {1}", destination, e);
                throw;
            }
        }

        private static SafeFileHandle OpenForWrite(ITracer tracer, string fileName)
        {
            SafeFileHandle handle = CreateFile(fileName, FileAccess.Write, FileShare.None, IntPtr.Zero, FileMode.Create, FileAttributes.Normal, IntPtr.Zero);
            if (handle.IsInvalid)
            {
                // If we get a access denied, try reverting the acls to defaults inherited by parent
                if (Marshal.GetLastWin32Error() == AccessDeniedWin32Error)
                {
                    tracer.RelatedEvent(
                        EventLevel.Warning,
                        "FailedOpenForWrite",
                        new EventMetadata
                        {
                            { TracingConstants.MessageKey.WarningMessage, "Received access denied. Attempting to delete." },
                            { "FileName", fileName }
                        });

                    File.SetAttributes(fileName, FileAttributes.Normal);
                    File.Delete(fileName);

                    handle = CreateFile(fileName, FileAccess.Write, FileShare.None, IntPtr.Zero, FileMode.Create, FileAttributes.Normal, IntPtr.Zero);
                }
            }

            return handle;
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern SafeFileHandle CreateFile(
            [MarshalAs(UnmanagedType.LPTStr)] string filename,
            [MarshalAs(UnmanagedType.U4)] FileAccess access,
            [MarshalAs(UnmanagedType.U4)] FileShare share,
            IntPtr securityAttributes, // optional SECURITY_ATTRIBUTES struct or IntPtr.Zero
            [MarshalAs(UnmanagedType.U4)] FileMode creationDisposition,
            [MarshalAs(UnmanagedType.U4)] FileAttributes flagsAndAttributes,
            IntPtr templateFile);

        [DllImport("kernel32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static unsafe extern bool NativeWriteFile(
            SafeFileHandle file,
            byte* buffer,
            uint numberOfBytesToWrite,
            out uint numberOfBytesWritten,
            IntPtr overlapped);

        private class NativeFileReader
        {
            private const uint GenericRead = 0x80000000;
            private const uint OpenExisting = 3;

            public static bool TryReadFirstByteOfFile(string fileName, byte[] buffer)
            {
                using (SafeFileHandle handle = Open(fileName))
                {
                    if (!handle.IsInvalid)
                    {
                        return ReadOneByte(handle, buffer);
                    }
                }

                return false;
            }

            private static SafeFileHandle Open(string fileName)
            {
                return CreateFile(fileName, GenericRead, (uint)(FileShare.ReadWrite | FileShare.Delete), 0, OpenExisting, 0, 0);
            }

            private static bool ReadOneByte(SafeFileHandle handle, byte[] buffer)
            {
                int bytesRead = 0;
                return ReadFile(handle, buffer, 1, ref bytesRead, 0);
            }

            [DllImport("kernel32", SetLastError = true, ThrowOnUnmappableChar = true, CharSet = CharSet.Unicode)]
            private static extern SafeFileHandle CreateFile(
                string fileName,
                uint desiredAccess,
                uint shareMode,
                uint securityAttributes,
                uint creationDisposition,
                uint flagsAndAttributes,
                int hemplateFile);

            [DllImport("kernel32", SetLastError = true)]
            private static extern bool ReadFile(
                SafeFileHandle file,
                [Out] byte[] buffer,
                int numberOfBytesToRead,
                ref int numberOfBytesRead,
                int overlapped);
        }
    }
}
