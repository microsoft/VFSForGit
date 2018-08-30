using GVFS.Common.FileSystem;
using System.IO;
using System.Runtime.InteropServices;

namespace GVFS.Platform.Mac
{
    public partial class MacFileSystem : IPlatformFileSystem
    {
        public bool SupportsFileMode { get; } = true;
        public bool EnumerationExpandsDirectories { get; } = true;

        public void FlushFileBuffers(string path)
        {
            // TODO(Mac): Use native API to flush file
        }

        public void MoveAndOverwriteFile(string sourceFileName, string destinationFilename)
        {
            // TODO(Mac): Use native API
            if (File.Exists(destinationFilename))
            {
                File.Delete(destinationFilename);
            }

            File.Move(sourceFileName, destinationFilename);
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

        public bool TryGetNormalizedPath(string path, out string normalizedPath, out string errorMessage)
        {
            return MacFileSystem.TryGetNormalizedPathImplementation(path, out normalizedPath, out errorMessage);
        }

        [DllImport("libc", EntryPoint = "chmod", SetLastError = true)]
        private static extern int Chmod(string pathname, int mode);
    }
}
