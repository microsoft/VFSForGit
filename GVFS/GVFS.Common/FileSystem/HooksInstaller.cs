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

        static HooksInstaller()
        {
            ExecutingDirectory = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
        }

        public static string MergeHooksData(string[] defaultHooksLines, string filename, string hookName)
        {
            IEnumerable<string> valuableHooksLines = defaultHooksLines.Where(line => !string.IsNullOrEmpty(line.Trim()));

            if (valuableHooksLines.Contains(GVFSPlatform.Instance.Constants.GVFSHooksExecutableName, StringComparer.OrdinalIgnoreCase))
            {
                throw new HooksConfigurationException(
                    $"{GVFSPlatform.Instance.Constants.GVFSHooksExecutableName} should not be specified in the configuration for "
                    + GVFSConstants.DotGit.Hooks.PostCommandHookName + " hooks (" + filename + ").");
            }
            else if (!valuableHooksLines.Any())
            {
                return GVFSPlatform.Instance.Constants.GVFSHooksExecutableName;
            }
            else if (hookName.Equals(GVFSConstants.DotGit.Hooks.PostCommandHookName))
            {
                return string.Join("\n", new string[] { GVFSPlatform.Instance.Constants.GVFSHooksExecutableName }.Concat(valuableHooksLines));
            }
            else
            {
                return string.Join("\n", valuableHooksLines.Concat(new string[] { GVFSPlatform.Instance.Constants.GVFSHooksExecutableName }));
            }
        }

        public static bool InstallHooks(GVFSContext context, out string error)
        {
            error = string.Empty;
            try
            {
                string installedReadObjectHookPath = Path.Combine(ExecutingDirectory, GVFSPlatform.Instance.Constants.GVFSReadObjectHookExecutableName);
                string targetReadObjectHookPath = Path.Combine(context.Enlistment.WorkingDirectoryRoot, GVFSConstants.DotGit.Hooks.ReadObjectPath + GVFSPlatform.Instance.Constants.ExecutableExtension);
                if (!TryHooksInstallationAction(() => CopyHook(context, installedReadObjectHookPath, targetReadObjectHookPath), out error))
                {
                    error = "Failed to copy " + installedReadObjectHookPath + "\n" + error;
                    return false;
                }

                string installedVirtualFileSystemHookPath = Path.Combine(ExecutingDirectory, GVFSPlatform.Instance.Constants.GVFSVirtualFileSystemHookExecutableName);
                string targetVirtualFileSystemHookPath = Path.Combine(context.Enlistment.WorkingDirectoryRoot, GVFSConstants.DotGit.Hooks.VirtualFileSystemPath + GVFSPlatform.Instance.Constants.ExecutableExtension);
                if (!TryHooksInstallationAction(() => CopyHook(context, installedVirtualFileSystemHookPath, targetVirtualFileSystemHookPath), out error))
                {
                    error = "Failed to copy " + installedVirtualFileSystemHookPath + "\n" + error;
                    return false;
                }

                string precommandHookPath = Path.Combine(context.Enlistment.WorkingDirectoryRoot, GVFSConstants.DotGit.Hooks.PreCommandPath);
                if (!GVFSPlatform.Instance.TryInstallGitCommandHooks(context, ExecutingDirectory, GVFSConstants.DotGit.Hooks.PreCommandHookName, precommandHookPath, out error))
                {
                    return false;
                }

                string postcommandHookPath = Path.Combine(context.Enlistment.WorkingDirectoryRoot, GVFSConstants.DotGit.Hooks.PostCommandPath);
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
            if (!TryUpdateHook(
                context,
                GVFSConstants.DotGit.Hooks.ReadObjectName,
                GVFSConstants.DotGit.Hooks.ReadObjectPath,
                GVFSPlatform.Instance.Constants.GVFSReadObjectHookExecutableName,
                out errorMessage))
            {
                return false;
            }

            if (!TryUpdateHook(
                context,
                GVFSConstants.DotGit.Hooks.VirtualFileSystemName,
                GVFSConstants.DotGit.Hooks.VirtualFileSystemPath,
                GVFSPlatform.Instance.Constants.GVFSVirtualFileSystemHookExecutableName,
                out errorMessage))
            {
                return false;
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
            string hookName,
            string hookPath,
            string hookExecutableName,
            out string errorMessage)
        {
            bool copyHook = false;
            string enlistmentHookPath = Path.Combine(context.Enlistment.WorkingDirectoryRoot, hookPath + GVFSPlatform.Instance.Constants.ExecutableExtension);
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

        public class HooksConfigurationException : Exception
        {
            public HooksConfigurationException(string message)
                : base(message)
            {
            }
        }
    }
}
