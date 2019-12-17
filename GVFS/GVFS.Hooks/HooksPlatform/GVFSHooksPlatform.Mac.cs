using GVFS.Platform.Mac;

namespace GVFS.Hooks.HooksPlatform
{
    public static partial class GVFSHooksPlatform
    {
        public static string GetUpgradeHighestAvailableVersionDirectory()
        {
            return MacPlatform.GetUpgradeHighestAvailableVersionDirectoryImplementation();
        }

        public static bool TryGetGVFSEnlistmentRoot(string directory, out string enlistmentRoot, out string errorMessage)
        {
            return MacPlatform.TryGetGVFSEnlistmentRootImplementation(directory, out enlistmentRoot, out errorMessage);
        }

        public static string GetNamedPipeName(string enlistmentRoot)
        {
            return MacPlatform.GetNamedPipeNameImplementation(enlistmentRoot);
        }

        public static string GetGitGuiBlockedMessage()
        {
            return "git gui is not supported in VFS for Git repos on Mac";
        }

        public static string GetUpgradeReminderNotification()
        {
            return MacPlatform.GetUpgradeReminderNotificationImplementation();
        }
    }
}
