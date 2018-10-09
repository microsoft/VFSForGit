using GVFS.Platform.Windows;

namespace GVFS.Hooks.HooksPlatform
{
    public static class GVFSHooksPlatform
    {
        public static string GetInstallerExtension()
        {
            return WindowsPlatform.InstallerExtension;
        }

        public static bool IsElevated()
        {
            return WindowsPlatform.IsElevatedImplementation();
        }

        public static bool IsProcessActive(int processId)
        {
            return WindowsPlatform.IsProcessActiveImplementation(processId);
        }

        public static string GetNamedPipeName(string enlistmentRoot)
        {
            return WindowsPlatform.GetNamedPipeNameImplementation(enlistmentRoot);
        }

        public static bool IsConsoleOutputRedirectedToFile()
        {
            return WindowsPlatform.IsConsoleOutputRedirectedToFileImplementation();
        }

        public static bool TryGetGVFSEnlistmentRoot(string directory, out string enlistmentRoot, out string errorMessage)
        {
            return WindowsPlatform.TryGetGVFSEnlistmentRootImplementation(directory, out enlistmentRoot, out errorMessage);
        }

        public static bool TryGetNormalizedPath(string path, out string normalizedPath, out string errorMessage)
        {
            return WindowsFileSystem.TryGetNormalizedPathImplementation(path, out normalizedPath, out errorMessage);
        }
    }
}