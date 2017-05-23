using GVFS.Common;
using GVFS.Common.Git;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

namespace GVFS.CommandLine
{
    public static class HooksInstallHelper
    {
        private const string HooksConfigContentTemplate =
@"########################################################################
#   Automatically generated file, do not modify.
#   See {0} config setting
########################################################################
{1}";
        public static bool InstallHooks(GVFSEnlistment enlistment, out string error)
        {
            error = string.Empty;
            try
            {
                string installedReadObjectHookPath = Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), GVFSConstants.GVFSReadObjectHookExecutableName);
                TryActionAndThrow(() => CopyReadObjectHook(enlistment, installedReadObjectHookPath), "Failed to copy" + installedReadObjectHookPath);

                string precommandHookPath = Path.Combine(enlistment.WorkingDirectoryRoot, GVFSConstants.DotGit.Hooks.PreCommandPath);
                InstallGitCommandHooks(enlistment, GVFSConstants.DotGit.Hooks.PreCommandHookName, precommandHookPath);

                string postcommandHookPath = Path.Combine(enlistment.WorkingDirectoryRoot, GVFSConstants.DotGit.Hooks.PostCommandPath);
                InstallGitCommandHooks(enlistment, GVFSConstants.DotGit.Hooks.PostCommandHookName, postcommandHookPath);
            }
            catch (HooksConfigurationException he)
            {
                error = he.Message;
                return false;
            }
            catch (Exception e)
            {
                error = e.ToString();
                return false;
            }

            return true;
        }

        /// <summary>
        /// Public accessibility to simplify testing.
        /// </summary>
        public static string MergeHooksData(string[] defaultHooksLines, string filename, string hookName)
        {
            IEnumerable<string> valuableHooksLines = defaultHooksLines.Where(line => !string.IsNullOrEmpty(line.Trim()));

            if (valuableHooksLines.Contains(GVFSConstants.GVFSHooksExecutableName, StringComparer.OrdinalIgnoreCase))
            {
                throw new HooksConfigurationException(
                    "GVFS.Hooks.exe should not be specified in the configuration for "
                    + GVFSConstants.DotGit.Hooks.PostCommandHookName + " hooks (" + filename + ").");
            }
            else if (!valuableHooksLines.Any())
            {
                return GVFSConstants.GVFSHooksExecutableName;
            }
            else if (hookName.Equals(GVFSConstants.DotGit.Hooks.PostCommandHookName))
            {
                return string.Join("\n", new string[] { GVFSConstants.GVFSHooksExecutableName }.Concat(valuableHooksLines));
            }
            else
            {
                return string.Join("\n", valuableHooksLines.Concat(new string[] { GVFSConstants.GVFSHooksExecutableName }));
            }
        }

        private static void CopyReadObjectHook(GVFSEnlistment enlistment, string installedReadObjectHookPath)
        {
            string targetPath = Path.Combine(enlistment.WorkingDirectoryRoot, GVFSConstants.DotGit.Hooks.ReadObjectPath + GVFSConstants.ExecutableExtension);
            if (File.Exists(targetPath))
            {
                File.Delete(targetPath);
            }

            File.Copy(
                installedReadObjectHookPath,
                targetPath);
        }

        private static void InstallGitCommandHooks(GVFSEnlistment enlistment, string hookName, string commandHookPath)
        {
            // The GitHooksLoader requires the following setup to invoke a hook:
            //      Copy GithooksLoader.exe to hook-name.exe
            //      Create a text file named hook-name.hooks that lists the applications to execute for the hook, one application per line

            string gitHooksloaderPath = Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), GVFSConstants.DotGit.Hooks.LoaderExecutable);
            TryActionAndThrow(
                () => CopyGitHooksLoader(gitHooksloaderPath, commandHookPath),
                "Failed to copy " + GVFSConstants.DotGit.Hooks.LoaderExecutable + " to " + commandHookPath + GVFSConstants.ExecutableExtension);

            TryActionAndThrow(
                () => CreateHookCommandConfig(enlistment, hookName, commandHookPath),
                "Failed to create " + commandHookPath + GVFSConstants.DotGit.Hooks.ConfigExtension);
        }

        private static void CopyGitHooksLoader(string gitHooksLoaderPath, string commandHookPath)
        {
            string targetPath = commandHookPath + GVFSConstants.ExecutableExtension;
            if (File.Exists(targetPath))
            {
                File.Delete(targetPath);
            }

            File.Copy(
                    gitHooksLoaderPath,
                    targetPath);
        }

        private static void CreateHookCommandConfig(GVFSEnlistment enlistment, string hookName, string commandHookPath)
        {
            string targetPath = commandHookPath + GVFSConstants.DotGit.Hooks.ConfigExtension;

            string configSetting = GVFSConstants.DotGit.Hooks.ConfigNamePrefix + hookName;
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

        private static string MergeHooks(GVFSEnlistment enlistment, string configSettingName, string hookName)
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

        private static void TryActionAndThrow(Action action, string errorMessage)
        {
            try
            {
                action();
            }
            catch (HooksConfigurationException)
            {
                throw;
            }
            catch (Exception e)
            {
                throw new Exception(errorMessage + ", Exception: " + e.ToString(), e);
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
