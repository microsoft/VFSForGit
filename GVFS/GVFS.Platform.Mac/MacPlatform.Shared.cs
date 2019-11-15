using System;
using System.IO;
using GVFS.Common;
using GVFS.Platform.POSIX;

namespace GVFS.Platform.Mac
{
    public partial class MacPlatform
    {
        public const string DotGVFSRoot = ".gvfs";
        public const string UpgradeConfirmMessage = "`sudo gvfs upgrade --confirm --no-verify`";

        public static string GetDataRootForGVFSImplementation()
        {
            return Path.Combine(
                Environment.GetEnvironmentVariable("HOME"),
                "Library",
                "Application Support",
                "GVFS");
        }

        public static string GetDataRootForGVFSComponentImplementation(string componentName)
        {
            return Path.Combine(GetDataRootForGVFSImplementation(), componentName);
        }

        public static bool TryGetGVFSEnlistmentRootImplementation(string directory, out string enlistmentRoot, out string errorMessage)
        {
            return POSIXPlatform.TryGetGVFSEnlistmentRootImplementation(directory, DotGVFSRoot, out enlistmentRoot, out errorMessage);
        }

        public static string GetUpgradeHighestAvailableVersionDirectoryImplementation()
        {
            return GetUpgradeNonProtectedDirectoryImplementation();
        }

        public static string GetUpgradeNonProtectedDirectoryImplementation()
        {
            return Path.Combine(GetDataRootForGVFSImplementation(), ProductUpgraderInfo.UpgradeDirectoryName);
        }

        public static string GetNamedPipeNameImplementation(string enlistmentRoot)
        {
            return POSIXPlatform.GetNamedPipeNameImplementation(enlistmentRoot, DotGVFSRoot);
        }

        public static string GetUpgradeReminderNotificationImplementation()
        {
            return $"A new version of VFS for Git is available. Run {UpgradeConfirmMessage} to upgrade.";
        }

        private string GetUpgradeNonProtectedDataDirectory()
        {
            return GetUpgradeNonProtectedDirectoryImplementation();
        }
    }
}
