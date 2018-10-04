using System;
using System.Text;
using GVFS.Common;
using GVFS.Common.FileSystem;

namespace GVFS.Platform.Windows
{
    public partial class WindowsFileSystem : IPlatformFileSystem
    {
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

        public bool IsPathUnderDirectory(string directoryPath, string path)
        {
            // TODO: Normalize paths
            // We can't use the existing TryGetNormalizedPathImplementation method
            // because it relies on actual calls to the disk to check if directories exist.
            // This may be called with paths or volumes which do not actually exist.
            return path.StartsWith(directoryPath, StringComparison.OrdinalIgnoreCase);
        }

        public string GetVolumeRoot(string path)
        {
            var volumePathName = new StringBuilder(GVFSConstants.MaxPath);
            if (NativeMethods.GetVolumePathName(path, volumePathName, GVFSConstants.MaxPath))
            {
                return volumePathName.ToString();
            }

            return null;
        }

        public bool IsVolumeAvailable(string path)
        {
            // No paths 'exist' on locked BitLocker volumes so it is sufficent to just
            // check if the directory/file exists using the framework APIs.
            return System.IO.Directory.Exists(path) || System.IO.File.Exists(path);
        }

        public IVolumeStateWatcher CreateVolumeStateWatcher()
        {
            // TODO: Extract the polling interval to a configuration value?
            return new WindowsVolumeStateWatcher(TimeSpan.FromSeconds(15));
        }

        public bool TryGetNormalizedPath(string path, out string normalizedPath, out string errorMessage)
        {
            return WindowsFileSystem.TryGetNormalizedPathImplementation(path, out normalizedPath, out errorMessage);
        }
    }
}
