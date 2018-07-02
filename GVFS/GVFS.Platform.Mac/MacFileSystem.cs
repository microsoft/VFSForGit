using GVFS.Common;
using GVFS.Common.FileSystem;
using System.IO;

namespace GVFS.Platform.Mac
{
    public class MacFileSystem : IPlatformFileSystem
    {
        public bool SupportsFileMode { get; } = true;

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

        public bool TryGetNormalizedPath(string path, out string normalizedPath, out string errorMessage)
        {
            // TODO(Mac): Properly determine normalized paths (e.g. across links)
            errorMessage = null;
            normalizedPath = path;
            return true;
        }
    }
}
