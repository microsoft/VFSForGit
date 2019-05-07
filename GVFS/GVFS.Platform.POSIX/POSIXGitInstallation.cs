using GVFS.Common;
using GVFS.Common.Git;
using System.IO;

namespace GVFS.Platform.POSIX
{
    public class POSIXGitInstallation : IGitInstallation
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
            ProcessResult result = ProcessHelper.Run("which", args: "git", redirectOutput: true);
            if (result.ExitCode != 0)
            {
                return null;
            }

            return result.Output.Trim();
        }
    }
}
