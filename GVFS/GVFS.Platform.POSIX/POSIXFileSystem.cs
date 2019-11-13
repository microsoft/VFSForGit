using GVFS.Common;
using GVFS.Common.FileSystem;
using GVFS.Common.Tracing;
using System;
using System.IO;
using System.Runtime.InteropServices;

namespace GVFS.Platform.POSIX
{
    public abstract partial class POSIXFileSystem : IPlatformFileSystem
    {
        // https://github.com/dotnet/corefx/blob/103639b6ff5aa6ab6097f70732530e411817f09b/src/Common/src/CoreLib/Interop/Unix/System.Native/Interop.OpenFlags.cs#L12
        [Flags]
        public enum OpenFlags
        {
            // Access modes (mutually exclusive)
            O_RDONLY = 0x0000,
            O_WRONLY = 0x0001,
            O_RDWR = 0x0002,

            // Flags (combinable)
            O_CLOEXEC = 0x0010,
            O_CREAT = 0x0020,
            O_EXCL = 0x0040,
            O_TRUNC = 0x0080,
            O_SYNC = 0x0100,
        }

        public bool SupportsFileMode { get; } = true;

        public void FlushFileBuffers(string path)
        {
            // TODO(#1057): Use native API to flush file
        }

        public void MoveAndOverwriteFile(string sourceFileName, string destinationFilename)
        {
            if (Rename(sourceFileName, destinationFilename) != 0)
            {
                NativeMethods.ThrowLastWin32Exception($"Failed to rename {sourceFileName} to {destinationFilename}");
            }
        }

        public abstract void ChangeMode(string path, ushort mode);

        public bool TryGetNormalizedPath(string path, out string normalizedPath, out string errorMessage)
        {
            return POSIXFileSystem.TryGetNormalizedPathImplementation(path, out normalizedPath, out errorMessage);
        }

        public abstract bool HydrateFile(string fileName, byte[] buffer);

        public abstract bool IsExecutable(string fileName);

        public abstract bool IsSocket(string fileName);

        public bool TryCreateDirectoryAccessibleByAuthUsers(string directoryPath, out string error, ITracer tracer = null)
        {
            try
            {
                Directory.CreateDirectory(directoryPath);
                error = null;
                return true;
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                if (tracer != null)
                {
                    EventMetadata metadataData = new EventMetadata();
                    metadataData.Add("Exception", e.ToString());
                    metadataData.Add(nameof(directoryPath), directoryPath);
                    tracer.RelatedError(metadataData, $"{nameof(this.TryCreateDirectoryAccessibleByAuthUsers)}: Failed to create directory");
                }

                error = e.Message;
                return false;
            }
        }

        public bool TryCreateDirectoryWithAdminAndUserModifyPermissions(string directoryPath, out string error)
        {
            throw new NotImplementedException();
        }

        public bool TryCreateOrUpdateDirectoryToAdminModifyPermissions(ITracer tracer, string directoryPath, out string error)
        {
            throw new NotImplementedException();
        }

        public virtual bool IsFileSystemSupported(string path, out string error)
        {
            error = null;
            return true;
        }

        [DllImport("libc", EntryPoint = "rename", SetLastError = true)]
        private static extern int Rename(string oldPath, string newPath);
    }
}
