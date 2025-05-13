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
        private static readonly string ExecutingDirectory;
        private static readonly HookData[] NativeHooks = new[]
        {
            new HookData(GVFSConstants.DotGit.Hooks.ReadObjectName, GVFSConstants.DotGit.Hooks.ReadObjectPath, GVFSPlatform.Instance.Constants.GVFSReadObjectHookExecutableName),
            new HookData(GVFSConstants.DotGit.Hooks.VirtualFileSystemName, GVFSConstants.DotGit.Hooks.VirtualFileSystemPath, GVFSPlatform.Instance.Constants.GVFSVirtualFileSystemHookExecutableName),
            new HookData(GVFSConstants.DotGit.Hooks.PostIndexChangedName, GVFSConstants.DotGit.Hooks.PostIndexChangedPath, GVFSPlatform.Instance.Constants.GVFSPostIndexChangedHookExecutableName),
        };

        static HooksInstaller()
        {
            ExecutingDirectory = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
        }

        public static string MergeHooksData(string[] defaultHooksLines, string filename, string hookName)
        {
            IEnumerable<string> valuableHooksLines = defaultHooksLines.Where(line => !string.IsNullOrEmpty(line.Trim()));
            /* Wrap in quotes to handle spaces in the path */
            string absolutePathToHooksExecutable = $"\"{Path.Combine(ExecutingDirectory, GVFSPlatform.Instance.Constants.GVFSHooksExecutableName)}\"";

            if (valuableHooksLines.Contains(GVFSPlatform.Instance.Constants.GVFSHooksExecutableName, GVFSPlatform.Instance.Constants.PathComparer))
            {
                throw new HooksConfigurationException(
                    $"{GVFSPlatform.Instance.Constants.GVFSHooksExecutableName} should not be specified in the configuration for "
                    + GVFSConstants.DotGit.Hooks.PostCommandHookName + " hooks (" + filename + ").");
            }
            else if (!valuableHooksLines.Any())
            {
                return absolutePathToHooksExecutable;
            }
            else if (hookName.Equals(GVFSConstants.DotGit.Hooks.PostCommandHookName))
            {
                return string.Join("\n", new string[] { absolutePathToHooksExecutable }.Concat(valuableHooksLines));
            }
            else
            {
                return string.Join("\n", valuableHooksLines.Concat(new string[] { absolutePathToHooksExecutable }));
            }
        }

        public static bool InstallHooks(GVFSContext context, out string error)
        {
            error = string.Empty;
            try
            {
                foreach (HookData hook in NativeHooks)
                {
                    string installedHookPath = Path.Combine(ExecutingDirectory, hook.ExecutableName);
                    string targetHookPath = Path.Combine(context.Enlistment.WorkingDirectoryBackingRoot, hook.Path + GVFSPlatform.Instance.Constants.ExecutableExtension);
                    if (!TryHooksInstallationAction(() => CopyHook(context, installedHookPath, targetHookPath), out error))
                    {
                        error = "Failed to copy " + installedHookPath + "\n" + error;
                        return false;
                    }
                }

                string precommandHookPath = Path.Combine(context.Enlistment.WorkingDirectoryBackingRoot, GVFSConstants.DotGit.Hooks.PreCommandPath);
                if (!GVFSPlatform.Instance.TryInstallGitCommandHooks(context, ExecutingDirectory, GVFSConstants.DotGit.Hooks.PreCommandHookName, precommandHookPath, out error))
                {
                    return false;
                }

                string postcommandHookPath = Path.Combine(context.Enlistment.WorkingDirectoryBackingRoot, GVFSConstants.DotGit.Hooks.PostCommandPath);
                if (!GVFSPlatform.Instance.TryInstallGitCommandHooks(context, ExecutingDirectory, GVFSConstants.DotGit.Hooks.PostCommandHookName, postcommandHookPath, out error))
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
            errorMessage = string.Empty;
            foreach (HookData hook in NativeHooks)
            {
                if (!TryUpdateHook(context, hook, out errorMessage))
                {
                    return false;
                }
            }

            return true;
        }

        public static void CopyHook(GVFSContext context, string sourcePath, string destinationPath)
        {
            Exception ex;
            if (!context.FileSystem.TryCopyToTempFileAndRename(sourcePath, destinationPath, out ex))
            {
                throw new RetryableException($"Error installing {sourcePath} to {destinationPath}", ex);
            }
        }

        /// <summary>
        /// Try to perform the specified action.  The action will be retried (with backoff) up to 3 times.
        /// </summary>
        /// <param name="action">Action to perform</param>
        /// <param name="errorMessage">Error message</param>
        /// <returns>True if the action succeeded and false otherwise</returns>
        /// <remarks>This method is optimized for the hooks installation process and should not be used
        /// as a generic retry mechanism.  See RetryWrapper for a general purpose retry mechanism</remarks>
        public static bool TryHooksInstallationAction(Action action, out string errorMessage)
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
            HookData hook,
            out string errorMessage)
        {
            bool copyHook = false;
            string enlistmentHookPath = Path.Combine(context.Enlistment.WorkingDirectoryBackingRoot, hook.Path + GVFSPlatform.Instance.Constants.ExecutableExtension);
            string installedHookPath = Path.Combine(ExecutingDirectory, hook.ExecutableName);

            if (!context.FileSystem.FileExists(installedHookPath))
            {
                errorMessage = hook.ExecutableName + " cannot be found at " + installedHookPath;
                return false;
            }

            if (!context.FileSystem.FileExists(enlistmentHookPath))
            {
                copyHook = true;

                EventMetadata metadata = new EventMetadata();
                metadata.Add("Area", "Mount");
                metadata.Add(nameof(enlistmentHookPath), enlistmentHookPath);
                metadata.Add(nameof(installedHookPath), installedHookPath);
                metadata.Add(TracingConstants.MessageKey.WarningMessage, hook.Name + " not found in enlistment, copying from installation folder");
                context.Tracer.RelatedWarning(hook.Name + " MissingFromEnlistment", metadata);
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
                    context.Tracer.RelatedError(metadata, "Failed to compare " + hook.Name + " version");
                    errorMessage = "Error comparing " + hook.Name + " versions. " + ConsoleHelper.GetGVFSLogMessage(context.Enlistment.EnlistmentRoot);
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
                    context.Tracer.RelatedError(metadata, "Failed to copy " + hook.Name + " to enlistment");
                    errorMessage = "Error copying " + hook.Name + " to enlistment. " + ConsoleHelper.GetGVFSLogMessage(context.Enlistment.EnlistmentRoot);
                    return false;
                }
            }

            errorMessage = null;
            return true;
        }

        public class HooksConfigurationException : Exception
        {
            public HooksConfigurationException(string message)
                : base(message)
            {
            }
        }

        private class HookData
        {
            public HookData(string name, string path, string executableName)
            {
                this.Name = name;
                this.Path = path;
                this.ExecutableName = executableName;
            }

            public string Name { get; }
            public string Path { get; }
            public string ExecutableName { get; }
        }
    }
}
