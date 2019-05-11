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
        public bool SupportsFileMode { get; } = true;

        public void FlushFileBuffers(string path)
        {
            // TODO(POSIX): Use native API to flush file
        }

        public void MoveAndOverwriteFile(string sourceFileName, string destinationFilename)
        {
            if (Rename(sourceFileName, destinationFilename) != 0)
            {
                NativeMethods.ThrowLastWin32Exception($"Failed to rename {sourceFileName} to {destinationFilename}");
            }
        }

        public void CreateHardLink(string newFileName, string existingFileName)
        {
            // TODO(POSIX): Use native API to create a hardlink
            File.Copy(existingFileName, newFileName);
        }

        public abstract void ChangeMode(string path, ushort mode);

        public bool TryGetNormalizedPath(string path, out string normalizedPath, out string errorMessage)
        {
            return POSIXFileSystem.TryGetNormalizedPathImplementation(path, out normalizedPath, out errorMessage);
        }

        public abstract bool HydrateFile(string fileName, byte[] buffer);

        public abstract bool IsExecutable(string fileName);

        public abstract bool IsSocket(string fileName);

        public bool TryCreateDirectoryWithAdminAndUserModifyPermissions(string directoryPath, out string error)
        {
            throw new NotImplementedException();
        }

        public bool TryCreateOrUpdateDirectoryToAdminModifyPermissions(ITracer tracer, string directoryPath, out string error)
        {
            throw new NotImplementedException();
        }

        [DllImport("libc", EntryPoint = "rename", SetLastError = true)]
        private static extern int Rename(string oldPath, string newPath);
    }
}
