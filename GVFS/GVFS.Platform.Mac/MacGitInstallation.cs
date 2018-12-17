using GVFS.Common.Git;
using System.IO;

namespace GVFS.Platform.Mac
{
    public class MacGitInstallation : IGitInstallation
    {
        private const string GitProcessName = "git";

        public bool GitExists(string gitBinPath)
        {
            if (!string.IsNullOrWhiteSpace(gitBinPath))
            {
                return File.Exists(gitBinPath);
            }

            return this.GetInstalledGitBinPath() != null;
        }

        public string GetInstalledGitBinPath()
        {
            // TODO(Mac): Use 'which' to find git (like the Windows platform uses "where")
            string gitBinPath = $"/usr/local/bin/{GitProcessName}";
            if (File.Exists(gitBinPath))
            {
                return gitBinPath;
            }

            return null;
        }
    }
}
