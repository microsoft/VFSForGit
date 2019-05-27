using GVFS.Common.Tracing;
using Microsoft.Win32.SafeHandles;
using System;
using System.ComponentModel;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Runtime.InteropServices;

namespace FastFetch
{
    internal static class NativeMethods
    {
        private const int AccessDeniedWin32Error = 5;

        public static unsafe void WriteFile(ITracer tracer, byte* originalData, long originalSize, string destination, ushort mode)
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
                        if (!WriteFile(fileHandle, data, toWrite, out written, IntPtr.Zero))
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
                EventMetadata metadata = new EventMetadata();
                metadata.Add("destination", destination);
                metadata.Add("exception", e.ToString());
                tracer.RelatedError(metadata, "Error writing file.");
                throw;
            }
        }

        public static bool TryStatFileAndUpdateIndex(ITracer tracer, string path, MemoryMappedViewAccessor indexView, long offset)
        {
            try
            {
                FileInfo file = new FileInfo(path);
                if (file.Exists)
                {
                    Index.IndexEntry indexEntry = new Index.IndexEntry(indexView, offset);
                    indexEntry.Mtime = file.LastWriteTimeUtc;
                    indexEntry.Ctime = file.CreationTimeUtc;
                    indexEntry.Size = (uint)file.Length;
                    return true;
                }
            }
            catch (System.Security.SecurityException)
            {
                // Skip these.
            }
            catch (System.UnauthorizedAccessException)
            {
                // Skip these.
            }

            return false;
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

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static unsafe extern bool WriteFile(
            SafeFileHandle file,
            byte* buffer,
            uint numberOfBytesToWrite,
            out uint numberOfBytesWritten,
            IntPtr overlapped);
    }
}
