using GVFS.Platform.POSIX;

namespace GVFS.Hooks.HooksPlatform
{
    public static class GVFSHooksPlatform
    {
        public static string GetInstallerExtension()
        {
            return "dmg";
        }

        public static bool IsElevated()
        {
            return POSIXPlatform.IsElevatedImplementation();
        }

        public static bool IsProcessActive(int processId)
        {
            return POSIXPlatform.IsProcessActiveImplementation(processId);
        }

        public static string GetNamedPipeName(string enlistmentRoot)
        {
            return POSIXPlatform.GetNamedPipeNameImplementation(enlistmentRoot);
        }

        public static bool IsConsoleOutputRedirectedToFile()
        {
            return POSIXPlatform.IsConsoleOutputRedirectedToFileImplementation();
        }

        public static bool TryGetGVFSEnlistmentRoot(string directory, out string enlistmentRoot, out string errorMessage)
        {
            return POSIXPlatform.TryGetGVFSEnlistmentRootImplementation(directory, out enlistmentRoot, out errorMessage);
        }

        public static bool TryGetNormalizedPath(string path, out string normalizedPath, out string errorMessage)
        {
            return POSIXFileSystem.TryGetNormalizedPathImplementation(path, out normalizedPath, out errorMessage);
        }
    }
}
