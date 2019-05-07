using GVFS.Common;
using System;
using System.Diagnostics;
using System.IO;

namespace GVFS.Platform.POSIX
{
    public abstract partial class POSIXPlatform
    {
        public static bool IsElevatedImplementation()
        {
            // TODO(POSIX): Implement proper check
            return false;
        }

        public static bool IsProcessActiveImplementation(int processId)
        {
            try
            {
                Process process = Process.GetProcessById(processId);
            }
            catch (ArgumentException)
            {
                return false;
            }

            return true;
        }

        public static string GetNamedPipeNameImplementation(string enlistmentRoot)
        {
            // Pipes are stored as files on POSIX, use a rooted pipe name to keep full control of the location of the file
            return Path.Combine(enlistmentRoot, GVFSConstants.DotGVFS.Root, "GVFS_NetCorePipe");
        }

        public static bool IsConsoleOutputRedirectedToFileImplementation()
        {
            // TODO(POSIX): Implement proper check
            return false;
        }

        public static bool TryGetGVFSEnlistmentRootImplementation(string directory, out string enlistmentRoot, out string errorMessage)
        {
            // TODO(POSIX): Merge this code with the implementation in WindowsPlatform

            enlistmentRoot = null;

            string finalDirectory;
            if (!POSIXFileSystem.TryGetNormalizedPathImplementation(directory, out finalDirectory, out errorMessage))
            {
                return false;
            }

            enlistmentRoot = Paths.GetRoot(finalDirectory, GVFSConstants.DotGVFS.Root);
            if (enlistmentRoot == null)
            {
                errorMessage = $"Failed to find the root directory for {GVFSConstants.DotGVFS.Root} in {finalDirectory}";
                return false;
            }

            return true;
        }
    }
}
