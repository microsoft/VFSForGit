using GVFS.Common;
using GVFS.Common.FileSystem;
using System;
using System.IO;
using System.Runtime.InteropServices;

namespace GVFS.Platform.Mac
{
    public partial class MacFileSystem : IPlatformFileSystem
    {
        public bool SupportsFileMode { get; } = true;

        public void FlushFileBuffers(string path)
        {
            // TODO(Mac): Use native API to flush file
        }

        public void MoveAndOverwriteFile(string sourceFileName, string destinationFilename)
        {
            if (Rename(sourceFileName, destinationFilename) != 0)
            {
                NativeMethods.ThrowLastWin32Exception($"Failed to renname {sourceFileName} to {destinationFilename}");
            }
        }

        public void CreateHardLink(string newFileName, string existingFileName)
        {
            // TODO(Mac): Use native API to create a hardlink
            File.Copy(existingFileName, newFileName);
        }

        public void ChangeMode(string path, int mode)
        {
           Chmod(path, mode);
        }

        public bool IsPathUnderDirectory(string directoryPath, string path)
        {
            // TODO(Mac): Check if the user has set HFS+/APFS to case sensitive
            // TODO(Mac): Normalize paths
            // Note: this may be called with paths or volumes which do not exist/are not mounted
            return path.StartsWith(directoryPath, StringComparison.OrdinalIgnoreCase);
        }

        public string GetVolumeRoot(string path)
        {
            // TODO(Mac): Query the volume mount points and check if the path is under any of those
            // For now just assume everything is under the system root.
            return "/";
        }

        public bool IsVolumeAvailable(string path)
        {
            // TODO(Mac): Perform any additional checks for locked or encrypted volumes
            return Directory.Exists(path) || File.Exists(path);
        }

        public IVolumeStateWatcher CreateVolumeStateWatcher()
        {
            return new MacVolumeStateWatcher();
        }

        public bool TryGetNormalizedPath(string path, out string normalizedPath, out string errorMessage)
        {
            return MacFileSystem.TryGetNormalizedPathImplementation(path, out normalizedPath, out errorMessage);
        }

        [DllImport("libc", EntryPoint = "chmod", SetLastError = true)]
        private static extern int Chmod(string pathname, int mode);

        [DllImport("libc", EntryPoint = "rename", SetLastError = true)]
        private static extern int Rename(string oldPath, string newPath);
    }
}
