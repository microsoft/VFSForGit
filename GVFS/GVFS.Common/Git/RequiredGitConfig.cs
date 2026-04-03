using System;
using System.Collections.Generic;
using System.IO;

namespace GVFS.Common.Git
{
    /// <summary>
    /// Single source of truth for the git config settings required by GVFS.
    /// These settings are enforced during clone, mount, and repair.
    /// </summary>
    public static class RequiredGitConfig
    {
        /// <summary>
        /// Returns the dictionary of required git config settings for a GVFS enlistment.
        /// These settings override any existing local configuration values.
        /// </summary>
        public static Dictionary<string, string> GetRequiredSettings(Enlistment enlistment)
        {
            string expectedHooksPath = Path.Combine(enlistment.WorkingDirectoryBackingRoot, GVFSConstants.DotGit.Hooks.Root);
            expectedHooksPath = Paths.ConvertPathToGitFormat(expectedHooksPath);

            string gitStatusCachePath = null;
            if (!GVFSEnlistment.IsUnattended(tracer: null) && GVFSPlatform.Instance.IsGitStatusCacheSupported())
            {
                gitStatusCachePath = Path.Combine(
                    enlistment.EnlistmentRoot,
                    GVFSPlatform.Instance.Constants.DotGVFSRoot,
                    GVFSConstants.DotGVFS.GitStatusCache.CachePath);

                gitStatusCachePath = Paths.ConvertPathToGitFormat(gitStatusCachePath);
            }

            string coreGVFSFlags = Convert.ToInt32(
                GitCoreGVFSFlags.SkipShaOnIndex |
                GitCoreGVFSFlags.BlockCommands |
                GitCoreGVFSFlags.MissingOk |
                GitCoreGVFSFlags.NoDeleteOutsideSparseCheckout |
                GitCoreGVFSFlags.FetchSkipReachabilityAndUploadPack |
                GitCoreGVFSFlags.BlockFiltersAndEolConversions)
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
                { GitConfigSetting.CoreVirtualFileSystemName, Paths.ConvertPathToGitFormat(GVFSConstants.DotGit.Hooks.VirtualFileSystemPath) },

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
                { "status.deserializePath", gitStatusCachePath },

                // The GVFS Protocol forbids submodules, so prevent a user's
                // global config of "status.submoduleSummary=true" from causing
                // extreme slowness in "git status"
                { "status.submoduleSummary", "false" },

                // Generation number v2 isn't ready for full use. Wait for v3.
                { "commitGraph.generationVersion", "1" },

                // Disable the builtin FS Monitor in case it was enabled globally.
                { "core.useBuiltinFSMonitor", "false" },
            };
        }
    }
}
