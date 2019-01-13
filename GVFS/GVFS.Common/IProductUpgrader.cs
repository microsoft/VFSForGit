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

    public interface IProductUpgrader : IDisposable
    {
        bool UpgradeAllowed(out string message);

        bool TryQueryNewestVersion(out Version newVersion, out string message);

        bool TryDownloadNewestVersion(out string errorMessage);

        bool TryRunInstaller(InstallActionWrapper installActionWrapper, out string error);

        /// <summary>
        /// Deletes latest upgrade installer after installation from the Download directory.
        /// Any previously downloaded installers which were not installed, will remain
        /// in the Downloads directory.
        /// </summary>
        /// <param name="error">any file system errors encountered during deletion</param>
        /// <returns>success or failure</returns>
        bool TryCleanup(out string error);

        bool TrySetupToolsDirectory(out string upgraderToolPath, out string error);
    }
}
