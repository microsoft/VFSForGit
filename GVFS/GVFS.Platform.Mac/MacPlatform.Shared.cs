using GVFS.Common;
using System.IO;

namespace GVFS.Platform.Mac
{
    public partial class MacPlatform
    {
        public const string InstallerExtension = "dmg";

        public static bool IsElevatedImplementation()
        {
            // TODO(Mac): Implement proper check
            return false;
        }

        public static bool IsProcessActiveImplementation(int processId)
        {
            // TODO(Mac): Implement proper check
            return true;
        }

        public static string GetNamedPipeNameImplementation(string enlistmentRoot)
        {
            // Pipes are stored as files on OSX, use a rooted pipe name to keep full control of the location of the file
            return Path.Combine(enlistmentRoot, GVFSConstants.DotGVFS.Root, "GVFS_NetCorePipe");
        }

        public static bool IsConsoleOutputRedirectedToFileImplementation()
        {
            // TODO(Mac): Implement proper check
            return false;
        }

        public static bool TryGetGVFSEnlistmentRootImplementation(string directory, out string enlistmentRoot, out string errorMessage)
        {
            // TODO(Mac): Merge this code with the implementation in WindowsPlatform

            enlistmentRoot = null;

            string finalDirectory;
            if (!MacFileSystem.TryGetNormalizedPathImplementation(directory, out finalDirectory, out errorMessage))
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
