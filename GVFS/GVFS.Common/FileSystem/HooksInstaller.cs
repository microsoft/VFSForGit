using GVFS.Common.Git;
using GVFS.Common.Tracing;
using System;
using System.Collections.Generic;
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
            // Environment.ProcessPath can be null in NativeAOT or certain hosting scenarios.
            string processPath = Environment.ProcessPath;
            ExecutingDirectory = !string.IsNullOrEmpty(processPath)
                ? Path.GetDirectoryName(processPath)
                : AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);
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

            // Update the pre-command and post-command hook loaders (GitHooksLoader copies).
            // These are deployed at clone time by InstallHooks but also need updating on
            // mount so that upgrading GVFS and remounting refreshes all hooks.
            string loaderSourcePath = Path.Combine(ExecutingDirectory, GVFSConstants.DotGit.Hooks.LoaderExecutable);

            string precommandHookPath = Path.Combine(
                context.Enlistment.WorkingDirectoryBackingRoot,
                GVFSConstants.DotGit.Hooks.PreCommandPath + GVFSPlatform.Instance.Constants.ExecutableExtension);
            if (!TryUpdateHook(context, GVFSConstants.DotGit.Hooks.PreCommandHookName, loaderSourcePath, precommandHookPath, out errorMessage))
            {
                return false;
            }

            string postcommandHookPath = Path.Combine(
                context.Enlistment.WorkingDirectoryBackingRoot,
                GVFSConstants.DotGit.Hooks.PostCommandPath + GVFSPlatform.Instance.Constants.ExecutableExtension);
            if (!TryUpdateHook(context, GVFSConstants.DotGit.Hooks.PostCommandHookName, loaderSourcePath, postcommandHookPath, out errorMessage))
            {
                return false;
            }

            // Refresh the corresponding .hooks text files. These hold the
            // absolute path of GVFS.Hooks.exe that the loader execs at hook
            // time, and were originally written at clone time pointing at
            // wherever GVFS was installed back then. If GVFS has moved
            // (system-to-user migration, version-junction swap, hand-edited
            // install), those paths go stale and the loader exits non-zero
            // on every git invocation that fires a hook - making the
            // enlistment unrecoverable through normal mount. Refreshing on
            // every mount makes us self-healing against install-location
            // drift, and is a no-op when paths are already current.
            string precommandBasePath = Path.Combine(context.Enlistment.WorkingDirectoryBackingRoot, GVFSConstants.DotGit.Hooks.PreCommandPath);
            if (!GVFSPlatform.Instance.TryInstallGitCommandHooks(context, ExecutingDirectory, GVFSConstants.DotGit.Hooks.PreCommandHookName, precommandBasePath, out errorMessage))
            {
                return false;
            }

            string postcommandBasePath = Path.Combine(context.Enlistment.WorkingDirectoryBackingRoot, GVFSConstants.DotGit.Hooks.PostCommandPath);
            if (!GVFSPlatform.Instance.TryInstallGitCommandHooks(context, ExecutingDirectory, GVFSConstants.DotGit.Hooks.PostCommandHookName, postcommandBasePath, out errorMessage))
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
                        errorMessage = (re.InnerException ?? re).ToString();
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
            string enlistmentHookPath = Path.Combine(context.Enlistment.WorkingDirectoryBackingRoot, hook.Path + GVFSPlatform.Instance.Constants.ExecutableExtension);
            string installedHookPath = Path.Combine(ExecutingDirectory, hook.ExecutableName);
            return TryUpdateHook(context, hook.Name, installedHookPath, enlistmentHookPath, out errorMessage);
        }

        internal static bool TryUpdateHook(
            GVFSContext context,
            string hookName,
            string installedHookPath,
            string enlistmentHookPath,
            out string errorMessage)
        {
            bool copyHook = false;

            if (!context.FileSystem.FileExists(installedHookPath))
            {
                errorMessage = Path.GetFileName(installedHookPath) + " cannot be found at " + installedHookPath;
                return false;
            }

            if (!context.FileSystem.FileExists(enlistmentHookPath))
            {
                copyHook = true;

                EventMetadata metadata = CreateHookEventMetadata(installedHookPath, enlistmentHookPath);
                metadata.Add("HookUpdateResult", "MissingFromEnlistment");
                context.Tracer.RelatedWarning(metadata, hookName + " not found in enlistment, copying from installation folder", Keywords.Telemetry);
            }
            else
            {
                try
                {
                    // Compare the enlistment hook against the installed hook by FileVersion.
                    // These native hook binaries embed their GVFS version in the PE version
                    // resource, so the version differs only when a GVFS upgrade changed the
                    // hook - which is rare (roughly monthly) compared to daily mounts. So the
                    // common daily mount does no copy, and a copy happens on the first mount
                    // after an upgrade.
                    copyHook = !HookVersionsMatch(context, installedHookPath, enlistmentHookPath);
                }
                catch (Exception e)
                {
                    // Reading the version opens the hook files, either of which can be
                    // transiently locked (open handle, AV scan) - the same failure class the
                    // copy path is hardened against. Do not fail the mount here: assume the
                    // enlistment hook may be stale, set copyHook so the resilient copy path
                    // runs (retry with backoff, then the "already matches" recheck). If a lock
                    // persists, that path reports the error after exhausting retries.
                    EventMetadata metadata = CreateHookEventMetadata(installedHookPath, enlistmentHookPath);
                    metadata.Add("Exception", e.ToString());
                    metadata.Add("HookUpdateResult", "CompareFailed");
                    context.Tracer.RelatedWarning(metadata, "Failed to compare " + hookName + " version; will attempt to refresh the hook", Keywords.Telemetry);
                    copyHook = true;
                }
            }

            if (copyHook)
            {
                // Retry the copy with backoff, matching the clone-time InstallHooks path.
                // The enlistment hook can be transiently locked (open handle, AV scan),
                // in which case the rename fails with a RetryableException wrapping
                // ERROR_ACCESS_DENIED. A transient lock must not be fatal to the mount.
                if (!TryHooksInstallationAction(() => CopyHook(context, installedHookPath, enlistmentHookPath), out string copyError))
                {
                    // The copy could not complete after retries. If the enlistment hook
                    // already matches the installed one, the binary is correct and the
                    // lock is harmless - treat it as success rather than killing the mount.
                    if (HookExistsAndVersionMatches(context, installedHookPath, enlistmentHookPath))
                    {
                        EventMetadata alreadyCorrect = CreateHookEventMetadata(installedHookPath, enlistmentHookPath);
                        alreadyCorrect.Add("CopyError", copyError);
                        alreadyCorrect.Add("HookUpdateResult", "LockedButAlreadyCorrect");
                        context.Tracer.RelatedWarning(
                            alreadyCorrect,
                            hookName + " could not be re-copied but already matches the installed hook; continuing",
                            Keywords.Telemetry);

                        errorMessage = null;
                        return true;
                    }

                    EventMetadata metadata = CreateHookEventMetadata(installedHookPath, enlistmentHookPath);
                    metadata.Add("Exception", copyError);
                    metadata.Add("HookUpdateResult", "CopyFailed");
                    context.Tracer.RelatedError(metadata, "Failed to copy " + hookName + " to enlistment");
                    errorMessage = "Error copying " + hookName + " to enlistment. " + ConsoleHelper.GetGVFSLogMessage(context.Enlistment.WorkingDirectoryRoot);
                    return false;
                }
            }

            errorMessage = null;
            return true;
        }

        /// <summary>
        /// Seeds an <see cref="EventMetadata"/> with the fields common to every mount-time
        /// hook-update outcome. Callers add an outcome-specific "HookUpdateResult" value (and
        /// any exception detail) so all outcomes are queryable by that field.
        /// </summary>
        private static EventMetadata CreateHookEventMetadata(string installedHookPath, string enlistmentHookPath)
        {
            EventMetadata metadata = new EventMetadata();
            metadata.Add("Area", "Mount");
            metadata.Add(nameof(enlistmentHookPath), enlistmentHookPath);
            metadata.Add(nameof(installedHookPath), installedHookPath);
            return metadata;
        }

        /// <summary>
        /// Returns true only when both files report the same, non-empty FileVersion. An
        /// absent/empty version is treated as "cannot confirm identical" (not a match), so the
        /// resilient copy path runs. Otherwise two version-less binaries would compare equal
        /// (string.Equals(null, null) == true) and the hook would never be refreshed, silently
        /// defeating the self-heal this comparison provides. Both files must exist.
        /// </summary>
        private static bool HookVersionsMatch(GVFSContext context, string installedHookPath, string enlistmentHookPath)
        {
            string installedVersion = context.FileSystem.GetFileVersion(installedHookPath);
            string enlistmentVersion = context.FileSystem.GetFileVersion(enlistmentHookPath);

            return !string.IsNullOrEmpty(installedVersion)
                && string.Equals(installedVersion, enlistmentVersion, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Returns true only when the enlistment hook exists and its FileVersion matches the
        /// installed hook. Any failure to read or compare (for example, the file is exclusively
        /// locked) is treated as "does not match" so callers do not mistake an unknown state
        /// for success.
        /// </summary>
        private static bool HookExistsAndVersionMatches(GVFSContext context, string installedHookPath, string enlistmentHookPath)
        {
            try
            {
                return context.FileSystem.FileExists(enlistmentHookPath)
                    && HookVersionsMatch(context, installedHookPath, enlistmentHookPath);
            }
            catch (Exception)
            {
                return false;
            }
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
