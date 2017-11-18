using RGFS.Common;
using RGFS.Common.Git;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;

namespace RGFS.CommandLine
{
    public static class HooksInstaller
    {
        private const string HooksConfigContentTemplate =
@"########################################################################
#   Automatically generated file, do not modify.
#   See {0} config setting
########################################################################
{1}";
        public static bool InstallHooks(RGFSEnlistment enlistment, out string error)
        {
            error = string.Empty;
            try
            {
                string installedReadObjectHookPath = Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), RGFSConstants.RGFSReadObjectHookExecutableName);
                if (!TryAction(() => CopyReadObjectHook(enlistment, installedReadObjectHookPath), out error))
                {
                    error = "Failed to copy " + installedReadObjectHookPath + "\n" + error;
                    return false;
                }

                string precommandHookPath = Path.Combine(enlistment.WorkingDirectoryRoot, RGFSConstants.DotGit.Hooks.PreCommandPath);
                if (!TryInstallGitCommandHooks(enlistment, RGFSConstants.DotGit.Hooks.PreCommandHookName, precommandHookPath, out error))
                {
                    return false;
                }

                string postcommandHookPath = Path.Combine(enlistment.WorkingDirectoryRoot, RGFSConstants.DotGit.Hooks.PostCommandPath);
                if (!TryInstallGitCommandHooks(enlistment, RGFSConstants.DotGit.Hooks.PostCommandHookName, postcommandHookPath, out error))
                {
                    return false;
                }
            }
            catch (Exception e)
            {
                error = e.ToString();
                return false;
            }

            return true;
        }

        public static string MergeHooksData(string[] defaultHooksLines, string filename, string hookName)
        {
            IEnumerable<string> valuableHooksLines = defaultHooksLines.Where(line => !string.IsNullOrEmpty(line.Trim()));

            if (valuableHooksLines.Contains(RGFSConstants.RGFSHooksExecutableName, StringComparer.OrdinalIgnoreCase))
            {
                throw new HooksConfigurationException(
                    "RGFS.Hooks.exe should not be specified in the configuration for "
                    + RGFSConstants.DotGit.Hooks.PostCommandHookName + " hooks (" + filename + ").");
            }
            else if (!valuableHooksLines.Any())
            {
                return RGFSConstants.RGFSHooksExecutableName;
            }
            else if (hookName.Equals(RGFSConstants.DotGit.Hooks.PostCommandHookName))
            {
                return string.Join("\n", new string[] { RGFSConstants.RGFSHooksExecutableName }.Concat(valuableHooksLines));
            }
            else
            {
                return string.Join("\n", valuableHooksLines.Concat(new string[] { RGFSConstants.RGFSHooksExecutableName }));
            }
        }

        private static void CopyReadObjectHook(RGFSEnlistment enlistment, string installedReadObjectHookPath)
        {
            string targetPath = Path.Combine(enlistment.WorkingDirectoryRoot, RGFSConstants.DotGit.Hooks.ReadObjectPath + RGFSConstants.ExecutableExtension);

            try
            {
                if (File.Exists(targetPath))
                {
                    File.Delete(targetPath);
                }

                File.Copy(
                    installedReadObjectHookPath,
                    targetPath);
            }
            catch (IOException io)
            {
                throw new RetryableException("Error installing ReadObject hook to " + targetPath, io);
            }
        }

        private static bool TryInstallGitCommandHooks(RGFSEnlistment enlistment, string hookName, string commandHookPath, out string errorMessage)
        {
            // The GitHooksLoader requires the following setup to invoke a hook:
            //      Copy GithooksLoader.exe to hook-name.exe
            //      Create a text file named hook-name.hooks that lists the applications to execute for the hook, one application per line

            string gitHooksloaderPath = Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), RGFSConstants.DotGit.Hooks.LoaderExecutable);
            if (!TryAction(() => CopyGitHooksLoader(gitHooksloaderPath, commandHookPath), out errorMessage))
            {
                errorMessage = "Failed to copy " + RGFSConstants.DotGit.Hooks.LoaderExecutable + " to " + commandHookPath + RGFSConstants.ExecutableExtension + "\n" + errorMessage;
                return false;
            }

            if (!TryAction(() => CreateHookCommandConfig(enlistment, hookName, commandHookPath), out errorMessage))
            {
                errorMessage = "Failed to create " + commandHookPath + RGFSConstants.DotGit.Hooks.ConfigExtension + "\n" + errorMessage;
                return false;
            }

            return true;
        }

        private static void CopyGitHooksLoader(string gitHooksLoaderPath, string commandHookPath)
        {
            string targetPath = commandHookPath + RGFSConstants.ExecutableExtension;

            try
            {
                if (File.Exists(targetPath))
                {
                    File.Delete(targetPath);
                }

                File.Copy(
                        gitHooksLoaderPath,
                        targetPath);
            }
            catch (IOException io)
            {
                throw new RetryableException("Error installing GitHooksLoader to " + targetPath, io);
            }
        }

        private static void CreateHookCommandConfig(RGFSEnlistment enlistment, string hookName, string commandHookPath)
        {
            string targetPath = commandHookPath + RGFSConstants.DotGit.Hooks.ConfigExtension;

            try
            {
                string configSetting = RGFSConstants.DotGit.Hooks.ConfigNamePrefix + hookName;
                string mergedHooks = MergeHooks(enlistment, configSetting, hookName);

                if (File.Exists(targetPath))
                {
                    File.Delete(targetPath);
                }

                File.WriteAllText(
                    targetPath,
                    string.Format(
                        HooksConfigContentTemplate,
                        configSetting,
                        mergedHooks));
            }
            catch (IOException io)
            {
                throw new RetryableException("Error installing " + targetPath, io);
            }
        }

        private static string MergeHooks(RGFSEnlistment enlistment, string configSettingName, string hookName)
        {
            GitProcess configProcess = new GitProcess(enlistment);
            string filename;
            string[] defaultHooksLines = { };

            if (configProcess.TryGetFromConfig(configSettingName, forceOutsideEnlistment: true, value: out filename))
            {
                filename = filename.Trim(' ', '\n');
                defaultHooksLines = File.ReadAllLines(filename);
            }

            return MergeHooksData(defaultHooksLines, filename, hookName);
        }

        private static bool TryAction(Action action, out string errorMessage)
        {
            int retriesLeft = 3;
            int retryWaitMillis = 500; // Will grow exponentially on each retry attempt
            errorMessage = null;

            while (true)
            {
                try
                {
                    action();
                    return true;
                }
                catch (RetryableException re)
                {
                    if (retriesLeft == 0)
                    {
                        errorMessage = re.InnerException.ToString();
                        return false;
                    }

                    Thread.Sleep(retryWaitMillis);
                    retriesLeft -= 1;
                    retryWaitMillis *= 2;
                }
                catch (Exception e)
                {
                    errorMessage = e.ToString();
                    return false;
                }
            }
        }

        public class HooksConfigurationException : Exception
        {
            public HooksConfigurationException(string message)
                : base(message)
            {
            }
        }
    }
}
