using GVFS.Common.Tracing;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;

namespace GVFS.Common.Git
{
    /// <summary>
    /// Single source of truth for the git config settings required by GVFS.
    /// These settings are enforced during clone, mount, and repair.
    /// </summary>
    public static class RequiredGitConfig
    {
        internal const string WorktreeConfigExtension = "extensions.worktreeConfig";
        internal const string StatusDeserializePath = "status.deserializePath";

        internal const int SharedWriteAttempts = 5;
        private const int SharedWriteRetryDelayMs = 100;

        /// <summary>
        /// Writes the required settings that are missing or different.
        /// </summary>
        /// <remarks>
        /// Git resolves --local config for a linked worktree to the config that all
        /// worktrees share. So a linked worktree writes <see cref="StatusDeserializePath"/>,
        /// the only required setting whose value is specific to an enlistment, to its own
        /// config.worktree, and enables extensions.worktreeConfig so that git reads that file.
        /// The shared config keeps the value of the primary enlistment. A config.worktree
        /// value overrides the shared value, so every other required setting is removed from
        /// the config.worktree file of the enlistment.
        /// </remarks>
        public static bool TrySetRequiredSettings(GVFSEnlistment enlistment, out string error)
        {
            return TrySetRequiredSettings(enlistment, NullTracer.Instance, out error);
        }

        /// <summary>
        /// Writes the required settings like the overload without a tracer, and sends
        /// config read retries to <paramref name="tracer"/>.
        /// </summary>
        public static bool TrySetRequiredSettings(GVFSEnlistment enlistment, ITracer tracer, out string error)
        {
            return TrySetRequiredSettings(enlistment, tracer, GetRequiredSettings(enlistment), out error);
        }

        /// <summary>
        /// Applies <paramref name="requiredSettings"/> like the public overload. Tests use this overload.
        /// </summary>
        internal static bool TrySetRequiredSettings(GVFSEnlistment enlistment, Dictionary<string, string> requiredSettings, out string error)
        {
            return TrySetRequiredSettings(enlistment, NullTracer.Instance, requiredSettings, out error);
        }

        private static bool TrySetRequiredSettings(GVFSEnlistment enlistment, ITracer tracer, Dictionary<string, string> requiredSettings, out string error)
        {
            List<KeyValuePair<string, string>> sharedSettings = new List<KeyValuePair<string, string>>();
            Dictionary<string, string> worktreeConfigSettings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            string primaryStatusCachePath = enlistment.IsWorktree ? GetStatusCachePath(enlistment.PrimaryDotGVFSRoot) : null;

            foreach (KeyValuePair<string, string> setting in requiredSettings)
            {
                if (enlistment.IsWorktree && string.Equals(setting.Key, StatusDeserializePath, StringComparison.OrdinalIgnoreCase))
                {
                    // An empty value replaces a path that an attended mount of this worktree
                    // wrote. It does not disable a readable shared path, because git keeps the
                    // last readable path. Git rejects the cache of the primary, because that
                    // cache does not match the index of this worktree.
                    worktreeConfigSettings.Add(setting.Key, setting.Value ?? string.Empty);
                    if (setting.Value != null && primaryStatusCachePath != null)
                    {
                        sharedSettings.Add(new KeyValuePair<string, string>(setting.Key, primaryStatusCachePath));
                    }
                }
                else
                {
                    sharedSettings.Add(setting);
                }
            }

            GitProcess git = enlistment.CreateGitProcess(tracer);

            Dictionary<string, List<string>> existingSharedEntries;
            if (!git.TryGetLocalConfigEntries(out existingSharedEntries))
            {
                error = "Could not read the local git config";
                return false;
            }

            if (enlistment.IsWorktree || HasLinkedWorktreeConfig(enlistment))
            {
                sharedSettings.Insert(0, new KeyValuePair<string, string>(WorktreeConfigExtension, "true"));
            }

            if (!TryApplySharedSettings(git, sharedSettings, existingSharedEntries, out error))
            {
                return false;
            }

            return TryApplyWorktreeConfigSettings(git, enlistment.WorktreeConfigPath, requiredSettings.Keys, worktreeConfigSettings, out error);
        }

        private static bool TryApplySharedSettings(
            GitProcess git,
            List<KeyValuePair<string, string>> settings,
            Dictionary<string, List<string>> existingEntries,
            out string error)
        {
            foreach (KeyValuePair<string, string> setting in settings)
            {
                if (setting.Value != null)
                {
                    if (!HasOnlyValue(existingEntries, setting.Key, setting.Value) &&
                        !TrySetSharedSetting(git, setting.Key, setting.Value, out error))
                    {
                        return false;
                    }
                }
                else if (TryGetEntry(existingEntries, setting.Key, out _))
                {
                    git.DeleteFromLocalConfig(setting.Key);
                }
            }

            error = null;
            return true;
        }

        private static bool TrySetSharedSetting(GitProcess git, string settingName, string value, out string error)
        {
            for (int attempt = 1; ; attempt++)
            {
                // Git uses the last value of a key, so replace every value.
                GitProcess.Result result = git.SetInLocalConfig(settingName, value, replaceAll: true);

                // Concurrent mounts of linked worktrees can write the same value. A write
                // fails while another process holds the config lock, so check whether
                // the value is now present before the next attempt.
                if (result.ExitCodeIsSuccess || HasLocalConfigValue(git, settingName, value))
                {
                    error = null;
                    return true;
                }

                if (attempt == SharedWriteAttempts)
                {
                    error = $"Could not set '{settingName}': {result.Errors}";
                    return false;
                }

                Thread.Sleep(SharedWriteRetryDelayMs);
            }
        }

        private static bool HasLocalConfigValue(GitProcess git, string settingName, string expectedValue)
        {
            return git.TryGetLocalConfigEntries(out Dictionary<string, List<string>> entries) &&
                HasOnlyValue(entries, settingName, expectedValue);
        }

        private static bool TryApplyWorktreeConfigSettings(
            GitProcess git,
            string worktreeConfigPath,
            IEnumerable<string> requiredSettingNames,
            Dictionary<string, string> worktreeConfigSettings,
            out string error)
        {
            Dictionary<string, List<string>> existingEntries = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            if (File.Exists(worktreeConfigPath) && !git.TryGetFileConfigEntries(worktreeConfigPath, out existingEntries))
            {
                error = "Could not read " + worktreeConfigPath;
                return false;
            }

            foreach (string settingName in requiredSettingNames)
            {
                if (!worktreeConfigSettings.ContainsKey(settingName) && TryGetEntry(existingEntries, settingName, out _))
                {
                    GitProcess.Result result = git.DeleteFromFileConfig(worktreeConfigPath, settingName);
                    if (result.ExitCodeIsFailure)
                    {
                        error = $"Could not remove '{settingName}' from {worktreeConfigPath}: {result.Errors}";
                        return false;
                    }
                }
            }

            foreach (KeyValuePair<string, string> setting in worktreeConfigSettings)
            {
                if (HasOnlyValue(existingEntries, setting.Key, setting.Value))
                {
                    continue;
                }

                GitProcess.Result result = git.SetInFileConfig(worktreeConfigPath, setting.Key, setting.Value, replaceAll: true);
                if (result.ExitCodeIsFailure)
                {
                    error = $"Could not set '{setting.Key}' in {worktreeConfigPath}: {result.Errors}";
                    return false;
                }
            }

            error = null;
            return true;
        }

        /// <summary>
        /// True when the key has exactly one entry and that entry has the value. Git
        /// uses the last entry of a key, and a key without a value means true.
        /// </summary>
        private static bool HasOnlyValue(Dictionary<string, List<string>> entries, string settingName, string value)
        {
            return TryGetEntry(entries, settingName, out List<string> values) &&
                values.Count == 1 &&
                string.Equals(values[0], value, StringComparison.Ordinal);
        }

        /// <summary>
        /// Finds a key in parsed `git config --list` output. That output shows a
        /// subsection without quotes, for example credential.https://dev.azure.com.usehttppath.
        /// </summary>
        private static bool TryGetEntry(Dictionary<string, List<string>> entries, string settingName, out List<string> values)
        {
            return entries.TryGetValue(settingName.Replace("\"", string.Empty), out values);
        }

        private static bool HasLinkedWorktreeConfig(GVFSEnlistment enlistment)
        {
            string worktreesRoot = Path.Combine(enlistment.DotGitRoot, GVFSConstants.DotGit.WorktreesName);
            if (!Directory.Exists(worktreesRoot))
            {
                return false;
            }

            try
            {
                foreach (string worktreeGitDir in Directory.EnumerateDirectories(worktreesRoot))
                {
                    if (File.Exists(Path.Combine(worktreeGitDir, GVFSConstants.DotGit.WorktreeConfigName)))
                    {
                        return true;
                    }
                }
            }
            catch (DirectoryNotFoundException)
            {
                // `git worktree prune` can remove the directory during the enumeration.
            }

            return false;
        }

        private static string GetStatusCachePath(string dotGVFSRoot)
        {
            if (GVFSEnlistment.IsUnattended(tracer: null) || !GVFSPlatform.Instance.IsGitStatusCacheSupported())
            {
                return null;
            }

            return Paths.ConvertPathToGitFormat(Path.Combine(dotGVFSRoot, GVFSConstants.DotGVFS.GitStatusCache.CachePath));
        }

        /// <summary>
        /// Returns the dictionary of required git config settings for a GVFS enlistment.
        /// These settings override any existing local configuration values.
        /// </summary>
        public static Dictionary<string, string> GetRequiredSettings(GVFSEnlistment enlistment)
        {
            string expectedHooksPath = Path.Combine(enlistment.DotGitRoot, GVFSConstants.DotGit.Hooks.RootName);
            expectedHooksPath = Paths.ConvertPathToGitFormat(expectedHooksPath);

            // Single-quote the path: git executes core.virtualfilesystem via the
            // shell (use_shell=1 in virtualfilesystem.c), so spaces in an absolute
            // path would split the command.  Git's config parser strips double quotes
            // but preserves single quotes, and bash treats single-quoted strings as
            // a single token.
            //
            // Include the platform's executable extension (matching the file actually
            // written to disk by HooksInstaller) so the shell can exec it directly
            // instead of probing PATHEXT candidates (.COM, .EXE, ...) on every hook
            // invocation.
            string virtualFileSystemPath = "'" + Paths.ConvertPathToGitFormat(
                Path.Combine(enlistment.DotGitRoot, GVFSConstants.DotGit.Hooks.RootName, GVFSConstants.DotGit.Hooks.VirtualFileSystemName) +
                GVFSPlatform.Instance.Constants.ExecutableExtension) + "'";

            string gitStatusCachePath = GetStatusCachePath(enlistment.DotGVFSRoot);

            string coreGVFSFlags = Convert.ToInt32(
                GitCoreGVFSFlags.SkipShaOnIndex |
                GitCoreGVFSFlags.BlockCommands |
                GitCoreGVFSFlags.MissingOk |
                GitCoreGVFSFlags.NoDeleteOutsideSparseCheckout |
                GitCoreGVFSFlags.FetchSkipReachabilityAndUploadPack |
                GitCoreGVFSFlags.BlockFiltersAndEolConversions |
                GitCoreGVFSFlags.SupportsWorktrees)
                .ToString();

            return new Dictionary<string, string>
            {
                // When running 'git am' it will remove the CRs from the patch file by default. This causes the patch to fail to apply because the
                // file that is getting the patch applied will still have the CRs. There is a --keep-cr option that you can pass the 'git am' command
                // but since we always want to keep CRs it is better to just set the config setting to always keep them so the user doesn't have to
                // remember to pass the flag.
                { "am.keepcr", "true" },

                // Update git settings to enable optimizations in git 2.20
                // Set 'checkout.optimizeNewBranch=true' to enable optimized 'checkout -b'
                { "checkout.optimizenewbranch", "true" },

                // Enable parallel checkout by auto-detecting the number of workers based on CPU count.
                { "checkout.workers", "0" },

                // We don't support line ending conversions - automatic conversion of LF to Crlf by git would cause un-necessary hydration. Disabling it.
                { "core.autocrlf", "false" },

                // Enable commit graph. https://devblogs.microsoft.com/devops/supercharging-the-git-commit-graph/
                { "core.commitGraph", "true" },

                // Perf - Git for Windows uses this to bulk-read and cache lstat data of entire directories (instead of doing lstat file by file).
                { "core.fscache", "true" },

                // Turns on all special gvfs logic. https://github.com/microsoft/git/blob/be5e0bb969495c428e219091e6976b52fb33b301/gvfs.h
                { "core.gvfs", coreGVFSFlags },

                // Use 'multi-pack-index' builtin instead of 'midx' to match upstream implementation
                { "core.multiPackIndex", "true" },

                // Perf - Enable parallel index preload for operations like git diff
                { "core.preloadIndex", "true" },

                // VFS4G never wants git to adjust line endings (causes un-necessary hydration of files)- explicitly setting core.safecrlf to false.
                { "core.safecrlf", "false" },

                // Possibly cause hydration while creating untrackedCache.
                { "core.untrackedCache", "false" },

                // This is to match what git init does.
                { "core.repositoryformatversion", "0" },

                // Turn on support for file modes on Mac & Linux.
                { "core.filemode", GVFSPlatform.Instance.FileSystem.SupportsFileMode ? "true" : "false" },

                // For consistency with git init.
                { "core.bare", "false" },

                // For consistency with git init.
                { "core.logallrefupdates", "true" },

                // Git to download objects on demand.
                { GitConfigSetting.CoreVirtualizeObjectsName, "true" },

                // Configure hook that git calls to get the paths git needs to consider for changes or untracked files
                { GitConfigSetting.CoreVirtualFileSystemName, virtualFileSystemPath },

                // Ensure hooks path is configured correctly.
                { "core.hookspath", expectedHooksPath },

                // Hostname is no longer sufficent for VSTS authentication. VSTS now requires dev.azure.com/account to determine the tenant.
                // By setting useHttpPath, credential managers will get the path which contains the account as the first parameter. They can then use this information for auth appropriately.
                { GitConfigSetting.CredentialUseHttpPath, "true" },

                // Turn off credential validation(https://github.com/microsoft/Git-Credential-Manager-for-Windows/blob/master/Docs/Configuration.md#validate).
                // We already have logic to call git credential if we get back a 401, so there's no need to validate the PAT each time we ask for it.
                { "credential.validate", "false" },

                // This setting is not needed anymore, because current version of gvfs does not use index.lock.
                // (This change was introduced initially to prevent `git diff` from acquiring index.lock file.)
                // Explicitly setting this to true (which also is the default value) because the repo could have been
                // cloned in the past when autoRefreshIndex used to be set to false.
                { "diff.autoRefreshIndex", "true" },

                // In Git 2.24.0, some new config settings were created. Disable them locally in VFS for Git repos in case a user has set them globally.
                // https://github.com/microsoft/VFSForGit/pull/1594
                // This applies to feature.manyFiles, feature.experimental and fetch.writeCommitGraph settings.
                { "feature.manyFiles", "false" },
                { "feature.experimental", "false" },
                { "fetch.writeCommitGraph", "false" },

                // Turn off of git garbage collection. Git garbage collection does not work with virtualized object.
                // We do run maintenance jobs now that do the packing of loose objects so in theory we shouldn't need
                // this - but it is not hurting anything and it will prevent a gc from getting kicked off if for some
                // reason the maintenance jobs have not been running and there are too many loose objects
                { "gc.auto", "0" },

                // Prevent git GUI from displaying GC warnings.
                { "gui.gcwarning", "false" },

                // Update git settings to enable optimizations in git 2.20
                // Set 'index.threads=true' to enable multi-threaded index reads
                { "index.threads", "true" },

                // index parsing code in VFSForGit currently only supports version 4.
                { "index.version", "4" },

                // Perf - avoid un-necessary blob downloads during a merge.
                { "merge.stat", "false" },

                // Perf - avoid un-necessary blob downloads while git tries to search and find renamed files.
                { "merge.renames", "false" },

                // Don't use bitmaps to determine pack file contents, because we use MIDX for this.
                { "pack.useBitmaps", "false" },

                // Update Git to include sparse push algorithm
                { "pack.useSparse", "true" },

                // Stop automatic git GC
                { "receive.autogc", "false" },

                // Update git settings to enable optimizations in git 2.20
                // Set 'reset.quiet=true' to speed up 'git reset <foo>"
                { "reset.quiet", "true" },

                // Configure git to use our serialize status file - make git use the serialized status file rather than compute the status by
                // parsing the index file and going through the files to determine changes.
                { StatusDeserializePath, gitStatusCachePath },

                // The GVFS Protocol forbids submodules, so prevent a user's
                // global config of "status.submoduleSummary=true" from causing
                // extreme slowness in "git status"
                { "status.submoduleSummary", "false" },

                // Generation number v2 isn't ready for full use. Wait for v3.
                { "commitGraph.generationVersion", "1" },

                // Disable the builtin FS Monitor in case it was enabled globally.
                { "core.useBuiltinFSMonitor", "false" },

                // Disable the FS Monitor in case it was enabled globally.
                { "core.fsmonitor", "false" },
            };
        }
    }
}
