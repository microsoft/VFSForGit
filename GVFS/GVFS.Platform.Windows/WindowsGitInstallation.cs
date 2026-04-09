using GVFS.Common;
using GVFS.Common.Git;
using Microsoft.Win32;
using System.IO;

namespace GVFS.Platform.Windows
{
    public class WindowsGitInstallation : IGitInstallation
    {
        private const string GitProcessName = "git.exe";
        private const string GitBinRelativePath = "cmd\\git.exe";
        private const string GitInstallationRegistryKey = "SOFTWARE\\GitForWindows";
        private const string GitInstallationRegistryInstallPathValue = "InstallPath";

        public bool GitExists(string gitBinPath)
        {
            if (!string.IsNullOrWhiteSpace(gitBinPath))
            {
                return File.Exists(gitBinPath);
            }

            return !string.IsNullOrEmpty(GetInstalledGitBinPath());
        }

        public string GetInstalledGitBinPath()
        {
            string gitBinPath = WindowsPlatform.GetStringFromRegistry(GitInstallationRegistryKey, GitInstallationRegistryInstallPathValue);
            if (!string.IsNullOrWhiteSpace(gitBinPath))
            {
                gitBinPath = Path.Combine(gitBinPath, GitBinRelativePath);
                if (File.Exists(gitBinPath))
                {
                    return gitBinPath;
                }
            }

            return null;
        }
    }
}
