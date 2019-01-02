using GVFS.Common.Git;
using System;

namespace GVFS.Common
{
    /// <summary>
    /// Delegate to wrap install action steps in.
    /// This can be used to report the beginning / end of each install step.
    /// </summary>
    /// <param name="method">The method to run inside wrapper</param>
    /// <param name="message">The message to display</param>
    /// <returns>success or failure return from the method run.</returns>
    public delegate bool InstallActionWrapper(Func<bool> method, string message);

    public interface IProductUpgrader
    {
        bool TryGetConfigAllowsUpgrade(out bool isConfigError, out string message);

        bool TryInitialize(out string errorMessage);

        bool TryGetNewerVersion(out Version newVersion, out string message);

        bool TryGetGitVersion(out GitVersion gitVersion, out string error);

        bool TryDownloadNewestVersion(out string errorMessage);

        bool TryRunInstaller(InstallActionWrapper installActionWrapper, out string error);

        bool TryCleanup(out string error);

        void CleanupDownloadDirectory();

        bool TrySetupToolsDirectory(out string upgraderToolPath, out string error);
    }
}
