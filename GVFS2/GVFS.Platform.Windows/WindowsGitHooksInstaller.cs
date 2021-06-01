using GVFS.Common;
using GVFS.Common.FileSystem;
using GVFS.Common.Git;
using System;
using System.IO;

namespace GVFS.Platform.Windows
{
    internal static class WindowsGitHooksInstaller
    {
        private const string HooksConfigContentTemplate =
@"########################################################################
#   Automatically generated file, do not modify.
#   See {0} config setting
########################################################################
{1}";

        public static void CreateHookCommandConfig(GVFSContext context, string hookName, string commandHookPath)
        {
            string targetPath = commandHookPath + GVFSConstants.GitConfig.HooksExtension;

            try
            {
                string configSetting = GVFSConstants.GitConfig.HooksPrefix + hookName;
                string mergedHooks = MergeHooks(context, configSetting, hookName);

                string contents = string.Format(HooksConfigContentTemplate, configSetting, mergedHooks);
                Exception ex;
                if (!context.FileSystem.TryWriteTempFileAndRename(targetPath, contents, out ex))
                {
                    throw new RetryableException("Error installing " + targetPath, ex);
                }
            }
            catch (IOException io)
            {
                throw new RetryableException("Error installing " + targetPath, io);
            }
        }

        private static string MergeHooks(GVFSContext context, string configSettingName, string hookName)
        {
            GitProcess configProcess = new GitProcess(context.Enlistment);
            string filename;
            string[] defaultHooksLines = { };

            // Pass false for forceOutsideEnlistment to allow hooks to be configured at the per-repo level
            if (configProcess.TryGetFromConfig(configSettingName, forceOutsideEnlistment: false, value: out filename) && filename != null)
            {
                filename = filename.Trim(' ', '\n');
                defaultHooksLines = File.ReadAllLines(filename);
            }

            return HooksInstaller.MergeHooksData(defaultHooksLines, filename, hookName);
        }
    }
}
