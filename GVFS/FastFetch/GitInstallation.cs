using GVFS.Common;
using Microsoft.Win32;
using System.IO;
using System.Runtime.InteropServices;

namespace FastFetch
{
    public class GitInstallation
    {
        private const string GitProcessName = "git.exe";
        private const string GitBinRelativePath = "cmd\\git.exe";
        private const string GitInstallationRegistryKey = "SOFTWARE\\GitForWindows";
        private const string GitInstallationRegistryInstallPathValue = "InstallPath";

        public static bool GitExists(string gitBinPath)
        {
            if (!string.IsNullOrWhiteSpace(gitBinPath))
            {
                return File.Exists(gitBinPath);
            }

            return ProcessHelper.WhereDirectory(GitProcessName) != null;
        }

        public static string GetInstalledGitBinPath()
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                string gitBinPath = GetStringFromRegistry(GitInstallationRegistryKey, GitInstallationRegistryInstallPathValue);
                if (!string.IsNullOrWhiteSpace(gitBinPath))
                {
                    gitBinPath = Path.Combine(gitBinPath, GitBinRelativePath);
                    if (File.Exists(gitBinPath))
                    {
                        return gitBinPath;
                    }
                }
            }

            return null;
        }

        private static string GetStringFromRegistry(string key, string valueName)
        {
            object value = GetValueFromRegistry(RegistryHive.LocalMachine, key, valueName);
            return value as string;
        }

        private static object GetValueFromRegistry(RegistryHive registryHive, string key, string valueName)
        {
            object value = GetValueFromRegistry(registryHive, key, valueName, RegistryView.Registry64);
            if (value == null)
            {
                value = GetValueFromRegistry(registryHive, key, valueName, RegistryView.Registry32);
            }

            return value;
        }

        private static object GetValueFromRegistry(RegistryHive registryHive, string key, string valueName, RegistryView view)
        {
            RegistryKey localKey = RegistryKey.OpenBaseKey(registryHive, view);
            RegistryKey localKeySub = localKey.OpenSubKey(key);

            object value = localKeySub == null ? null : localKeySub.GetValue(valueName);
            return value;
        }
    }
}
