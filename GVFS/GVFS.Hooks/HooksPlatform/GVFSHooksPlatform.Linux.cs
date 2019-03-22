using GVFS.Platform.Linux;

namespace GVFS.Hooks.HooksPlatform
{
    public static class GVFSHooksPlatform
    {
        public static string GetInstallerExtension()
        {
            return LinuxPlatform.InstallerExtension;
        }

        public static bool IsElevated()
        {
            return LinuxPlatform.IsElevatedImplementation();
        }

        public static bool IsProcessActive(int processId)
        {
            return LinuxPlatform.IsProcessActiveImplementation(processId);
        }

        public static string GetNamedPipeName(string enlistmentRoot)
        {
            return LinuxPlatform.GetNamedPipeNameImplementation(enlistmentRoot);
        }

        public static bool IsConsoleOutputRedirectedToFile()
        {
            return LinuxPlatform.IsConsoleOutputRedirectedToFileImplementation();
        }

        public static bool TryGetGVFSEnlistmentRoot(string directory, out string enlistmentRoot, out string errorMessage)
        {
            return LinuxPlatform.TryGetGVFSEnlistmentRootImplementation(directory, out enlistmentRoot, out errorMessage);
        }

        public static bool TryGetNormalizedPath(string path, out string normalizedPath, out string errorMessage)
        {
            return LinuxFileSystem.TryGetNormalizedPathImplementation(path, out normalizedPath, out errorMessage);
        }
    }
}
