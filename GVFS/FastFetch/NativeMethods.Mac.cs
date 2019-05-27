using GVFS.Common.Tracing;
using GVFS.Platform.Mac;
using System;
using System.ComponentModel;
using System.IO.MemoryMappedFiles;
using System.Runtime.InteropServices;

namespace FastFetch
{
    internal static class NativeMethods
    {
        public const int ReadOnly = 0x0000;
        public const int WriteOnly = 0x0001;

        public const int Create = 0x0200;
        public const int Truncate = 0x0400;
        private const int InvalidFileDescriptor = -1;
        private const ushort SymLinkMode = 0xA000;

        public static unsafe void WriteFile(ITracer tracer, byte* originalData, long originalSize, string destination, ushort mode)
        {
            int fileDescriptor = InvalidFileDescriptor;
            try
            {
                if (mode == SymLinkMode)
                {
                    string linkTarget = Marshal.PtrToStringUTF8(new IntPtr(originalData));
                    int result = CreateSymLink(linkTarget, destination);
                    if (result == -1)
                    {
                        throw new Win32Exception(Marshal.GetLastWin32Error(), $"Failed to create symlink({linkTarget}, {destination}).");
                    }
                }
                else
                {
                    fileDescriptor = Open(destination, WriteOnly | Create | Truncate, mode);
                    if (fileDescriptor == InvalidFileDescriptor)
                    {
                        throw new Win32Exception(Marshal.GetLastWin32Error(), $"Failed to open({destination}.)");
                    }

                    IntPtr result = Write(fileDescriptor, originalData, (IntPtr)originalSize);
                    if (result.ToInt32() == -1)
                    {
                        throw new Win32Exception(Marshal.GetLastWin32Error(), $"Failed to write contents into {destination}.");
                    }
                }
            }
            catch (Win32Exception e)
            {
                EventMetadata metadata = new EventMetadata();
                metadata.Add("filemode", mode);
                metadata.Add("destination", destination);
                metadata.Add("exception", e.ToString());
                tracer.RelatedError(metadata, $"Failed to properly create {destination}");
                throw;
            }
            finally
            {
                Close(fileDescriptor);
            }
        }

        public static bool TryStatFileAndUpdateIndex(ITracer tracer, string path, MemoryMappedViewAccessor indexView, long offset)
        {
            try
            {
                MacFileSystem.NativeStat.StatBuffer st = MacFileSystem.StatFile(path);
                Index.IndexEntry indexEntry = new Index.IndexEntry(indexView, offset);
                indexEntry.MtimeSeconds = (uint)st.MTimespec.Sec;
                indexEntry.MtimeNanosecondFraction = (uint)st.MTimespec.Nsec;
                indexEntry.CtimeSeconds = (uint)st.CTimespec.Sec;
                indexEntry.CtimeNanosecondFraction = (uint)st.CTimespec.Nsec;
                indexEntry.Size = (uint)st.Size;
                indexEntry.Dev = (uint)st.Dev;
                indexEntry.Ino = (uint)st.Ino;
                indexEntry.Uid = st.UID;
                indexEntry.Gid = st.GID;
                return true;
            }
            catch (Win32Exception e)
            {
                EventMetadata metadata = new EventMetadata();
                metadata.Add("path", path);
                metadata.Add("exception", e.ToString());
                tracer.RelatedError(metadata, "Error stat-ing file.");
                return false;
            }
        }

        [DllImport("libc", EntryPoint = "open", SetLastError = true)]
        public static extern int Open(string path, int flag, ushort creationMode);

        [DllImport("libc", EntryPoint = "close", SetLastError = true)]
        public static extern int Close(int fd);

        [DllImport("libc", EntryPoint = "write", SetLastError = true)]
        private static unsafe extern IntPtr Write(int fileDescriptor, void* buf, IntPtr count);

        [DllImport("libc", EntryPoint = "symlink", SetLastError = true)]
        private static extern int CreateSymLink(string linkTarget, string newLinkPath);
    }
}
