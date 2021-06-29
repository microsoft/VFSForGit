using GVFS.Platform.Windows;

namespace GVFS.Hooks.HooksPlatform
{
    public static class GVFSHooksPlatform
    {
        public static bool IsElevated()
        {
            return WindowsPlatform.IsElevatedImplementation();
        }

        public static bool IsProcessActive(int processId)
        {
            // Since the hooks are children of the running git process, they will have permissions
            // to OpenProcess and don't need to try the expessive GetProcessById method to determine
            // if the process is still active.
            return WindowsPlatform.IsProcessActiveImplementation(processId, tryGetProcessById: false);
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

        public static string GetUpgradeHighestAvailableVersionDirectory()
        {
            return WindowsPlatform.GetUpgradeHighestAvailableVersionDirectoryImplementation();
        }

        public static string GetGitGuiBlockedMessage()
        {
            return "To access the 'git gui' in a GVFS repo, please invoke 'git-gui.exe' instead.";
        }

        public static string GetUpgradeReminderNotification()
        {
            return WindowsPlatform.GetUpgradeReminderNotificationImplementation();
        }
    }
}
