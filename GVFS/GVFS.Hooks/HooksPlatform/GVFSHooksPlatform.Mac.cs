using GVFS.Platform.Mac;

namespace GVFS.Hooks.HooksPlatform
{
    public static class GVFSHooksPlatform
    {
        public static string GetInstallerExtension()
        {
            return MacPlatform.InstallerExtension;
        }

        public static bool IsElevated()
        {
            return MacPlatform.IsElevatedImplementation();
        }

        public static bool IsProcessActive(int processId)
        {
            return MacPlatform.IsProcessActiveImplementation(processId);
        }

        public static string GetNamedPipeName(string enlistmentRoot)
        {
            return MacPlatform.GetNamedPipeNameImplementation(enlistmentRoot);
        }

        public static bool IsConsoleOutputRedirectedToFile()
        {
            return MacPlatform.IsConsoleOutputRedirectedToFileImplementation();
        }

        public static bool TryGetGVFSEnlistmentRoot(string directory, out string enlistmentRoot, out string errorMessage)
        {
            return MacPlatform.TryGetGVFSEnlistmentRootImplementation(directory, out enlistmentRoot, out errorMessage);
        }

        public static bool TryGetNormalizedPath(string path, out string normalizedPath, out string errorMessage)
        {
            return MacFileSystem.TryGetNormalizedPathImplementation(path, out normalizedPath, out errorMessage);
        }
    }
}