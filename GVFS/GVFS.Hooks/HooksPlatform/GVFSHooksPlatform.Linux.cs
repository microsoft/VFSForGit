using GVFS.Platform.Linux;

namespace GVFS.Hooks.HooksPlatform
{
    public static partial class GVFSHooksPlatform
    {
        public static string GetUpgradeHighestAvailableVersionDirectory()
        {
            return LinuxPlatform.GetUpgradeHighestAvailableVersionDirectoryImplementation();
        }

        public static bool TryGetGVFSEnlistmentRoot(string directory, out string enlistmentRoot, out string errorMessage)
        {
            return LinuxPlatform.TryGetGVFSEnlistmentRootImplementation(directory, out enlistmentRoot, out errorMessage);
        }

        public static string GetNamedPipeName(string enlistmentRoot)
        {
            return LinuxPlatform.GetNamedPipeNameImplementation(enlistmentRoot);
        }

        public static string GetGitGuiBlockedMessage()
        {
            return "git gui is not supported in VFS for Git repos on Linux";
        }

        public static string GetUpgradeReminderNotification()
        {
            return LinuxPlatform.GetUpgradeReminderNotificationImplementation();
        }
    }
}
