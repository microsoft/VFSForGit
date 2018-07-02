using GVFS.Common.Git;
using GVFS.Common.Tracing;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;

namespace GVFS.Common.FileSystem
{
    public static class HooksInstaller
    {
        private const string HooksConfigContentTemplate =
@"########################################################################
#   Automatically generated file, do not modify.
#   See {0} config setting
########################################################################
{1}";
        private static readonly string ExecutingDirectory;

        static HooksInstaller()
        {
            ExecutingDirectory = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
        }

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

        public static bool InstallHooks(GVFSContext context, out string error)
        {
            error = string.Empty;
            try
            {
                string installedReadObjectHookPath = Path.Combine(ExecutingDirectory, GVFSConstants.GVFSReadObjectHookExecutableName);
                string targetReadObjectHookPath = Path.Combine(context.Enlistment.WorkingDirectoryRoot, GVFSConstants.DotGit.Hooks.ReadObjectPath + GVFSConstants.ExecutableExtension);
                if (!TryAction(() => CopyHook(context, installedReadObjectHookPath, targetReadObjectHookPath), out error))
                {
                    error = "Failed to copy " + installedReadObjectHookPath + "\n" + error;
                    return false;
                }

                string installedVirtualFileSystemHookPath = Path.Combine(ExecutingDirectory, GVFSConstants.GVFSVirtualFileSystemHookExecutableName);
                string targetVirtualFileSystemHookPath = Path.Combine(context.Enlistment.WorkingDirectoryRoot, GVFSConstants.DotGit.Hooks.VirtualFileSystemPath + GVFSConstants.ExecutableExtension);
                if (!TryAction(() => CopyHook(context, installedVirtualFileSystemHookPath, targetVirtualFileSystemHookPath), out error))
                {
                    error = "Failed to copy " + installedVirtualFileSystemHookPath + "\n" + error;
                    return false;
                }

                string precommandHookPath = Path.Combine(context.Enlistment.WorkingDirectoryRoot, GVFSConstants.DotGit.Hooks.PreCommandPath);
                if (!TryInstallGitCommandHooks(context, GVFSConstants.DotGit.Hooks.PreCommandHookName, precommandHookPath, out error))
                {
                    return false;
                }

                string postcommandHookPath = Path.Combine(context.Enlistment.WorkingDirectoryRoot, GVFSConstants.DotGit.Hooks.PostCommandPath);
                if (!TryInstallGitCommandHooks(context, GVFSConstants.DotGit.Hooks.PostCommandHookName, postcommandHookPath, out error))
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

        public static bool TryUpdateHooks(GVFSContext context, out string errorMessage)
        {
            if (!TryUpdateHook(
                context,
                GVFSConstants.DotGit.Hooks.ReadObjectName,
                GVFSConstants.DotGit.Hooks.ReadObjectPath,
                GVFSConstants.GVFSReadObjectHookExecutableName,
                out errorMessage))
            {
                return false;
            }

            if (!TryUpdateHook(
                context,
                GVFSConstants.DotGit.Hooks.VirtualFileSystemName,
                GVFSConstants.DotGit.Hooks.VirtualFileSystemPath,
                GVFSConstants.GVFSVirtualFileSystemHookExecutableName,
                out errorMessage))
            {
                return false;
            }

            return true;
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

        private static bool TryUpdateHook(
            GVFSContext context,
            string hookName,
            string hookPath,
            string hookExecutableName,
            out string errorMessage)
        {
            bool copyHook = false;
            string enlistmentHookPath = Path.Combine(context.Enlistment.WorkingDirectoryRoot, hookPath + GVFSConstants.ExecutableExtension);
            string installedHookPath = Path.Combine(ExecutingDirectory, hookExecutableName);

            if (!context.FileSystem.FileExists(installedHookPath))
            {
                errorMessage = hookExecutableName + " cannot be found at " + installedHookPath;
                return false;
            }

            if (!context.FileSystem.FileExists(enlistmentHookPath))
            {
                copyHook = true;

                EventMetadata metadata = new EventMetadata();
                metadata.Add("Area", "Mount");
                metadata.Add(nameof(enlistmentHookPath), enlistmentHookPath);
                metadata.Add(nameof(installedHookPath), installedHookPath);
                metadata.Add(TracingConstants.MessageKey.WarningMessage, hookName + " not found in enlistment, copying from installation folder");
                context.Tracer.RelatedWarning(hookName + " MissingFromEnlistment", metadata);
            }
            else
            {
                try
                {
                    FileVersionInfo enlistmentVersion = FileVersionInfo.GetVersionInfo(enlistmentHookPath);
                    FileVersionInfo installedVersion = FileVersionInfo.GetVersionInfo(installedHookPath);
                    copyHook = enlistmentVersion.FileVersion != installedVersion.FileVersion;
                }
                catch (Exception e)
                {
                    EventMetadata metadata = new EventMetadata();
                    metadata.Add("Area", "Mount");
                    metadata.Add(nameof(enlistmentHookPath), enlistmentHookPath);
                    metadata.Add(nameof(installedHookPath), installedHookPath);
                    metadata.Add("Exception", e.ToString());
                    context.Tracer.RelatedError(metadata, "Failed to compare " + hookName + " version");
                    errorMessage = "Error comparing " + hookName + " versions. " + ConsoleHelper.GetGVFSLogMessage(context.Enlistment.EnlistmentRoot);
                    return false;
                }
            }

            if (copyHook)
            {
                try
                {
                    CopyHook(context, installedHookPath, enlistmentHookPath);
                }
                catch (Exception e)
                {
                    EventMetadata metadata = new EventMetadata();
                    metadata.Add("Area", "Mount");
                    metadata.Add(nameof(enlistmentHookPath), enlistmentHookPath);
                    metadata.Add(nameof(installedHookPath), installedHookPath);
                    metadata.Add("Exception", e.ToString());
                    context.Tracer.RelatedError(metadata, "Failed to copy " + hookName + " to enlistment");
                    errorMessage = "Error copying " + hookName + " to enlistment. " + ConsoleHelper.GetGVFSLogMessage(context.Enlistment.EnlistmentRoot);
                    return false;
                }
            }

            errorMessage = null;
            return true;
        }

        private static bool TryInstallGitCommandHooks(GVFSContext context, string hookName, string commandHookPath, out string errorMessage)
        {
            // The GitHooksLoader requires the following setup to invoke a hook:
            //      Copy GithooksLoader.exe to hook-name.exe
            //      Create a text file named hook-name.hooks that lists the applications to execute for the hook, one application per line

            string gitHooksloaderPath = Path.Combine(ExecutingDirectory, GVFSConstants.DotGit.Hooks.LoaderExecutable);
            if (!TryAction(() => CopyHook(context, gitHooksloaderPath, commandHookPath + GVFSConstants.ExecutableExtension), out errorMessage))
            {
                errorMessage = "Failed to copy " + GVFSConstants.DotGit.Hooks.LoaderExecutable + " to " + commandHookPath + GVFSConstants.ExecutableExtension + "\n" + errorMessage;
                return false;
            }

            if (!TryAction(() => CreateHookCommandConfig(context, hookName, commandHookPath), out errorMessage))
            {
                errorMessage = "Failed to create " + commandHookPath + GVFSConstants.GitConfig.HooksExtension + "\n" + errorMessage;
                return false;
            }

            return true;
        }

        private static void CopyHook(GVFSContext context, string sourcePath, string destinationPath)
        {
            Exception ex;
            if (!context.FileSystem.TryCopyToTempFileAndRename(sourcePath, destinationPath, out ex))
            {
                throw new RetryableException($"Error installing {sourcePath} to {destinationPath}", ex);
            }
        }

        private static void CreateHookCommandConfig(GVFSContext context, string hookName, string commandHookPath)
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

            if (configProcess.TryGetFromConfig(configSettingName, forceOutsideEnlistment: true, value: out filename))
            {
                filename = filename.Trim(' ', '\n');
                defaultHooksLines = File.ReadAllLines(filename);
            }

            return MergeHooksData(defaultHooksLines, filename, hookName);
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
