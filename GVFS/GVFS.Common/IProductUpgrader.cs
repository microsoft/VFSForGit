using GVFS.Common.Git;
using System;

namespace GVFS.Common
{
    public interface IProductUpgrader
    {
        bool CanRunUsingCurrentConfig(
            out bool isConfigError,
            out string consoleMessage,
            out string errorMessage);

        bool Initialize(out string errorMessage);

        bool TryGetNewerVersion(out Version newVersion, out string consoleMessage, out string errorMessage);

        bool TryGetGitVersion(out GitVersion gitVersion, out string error);

        bool TryDownloadNewestVersion(out string errorMessage);

        bool TryRunGitInstaller(out bool installationSucceeded, out string error);

        bool TryRunGVFSInstaller(out bool installationSucceeded, out string error);

        bool TryCleanup(out string error);

        void CleanupDownloadDirectory();

        bool TrySetupToolsDirectory(out string upgraderToolPath, out string error);
    }
}
