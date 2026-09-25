using System;

namespace GVFS.Common.Git
{
    /// <summary>
    /// Detects whether a git repository uses the "reftable" ref storage backend
    /// (git's newer, opt-in binary ref format) rather than the "files" backend
    /// (loose ref files plus packed-refs) that VFS for Git assumes throughout its
    /// ref handling.
    ///
    /// VFS for Git does not populate refs through git's ref backend during a clone -
    /// it writes .git/packed-refs directly from the refs returned by the GVFS protocol
    /// (see CloneVerb.TryInitRepo and GitRefs.ToPackedRefs). A reftable-backed
    /// repository ignores packed-refs entirely, so those refs would be invisible to
    /// git and the clone would fail. Other VFS for Git code also reads and writes
    /// loose ref files and the .git/logs reflog directly (for example
    /// GitHeadRepairJob). Callers should use this detection to fail fast with a clear
    /// error rather than let that ref-file-shaped code silently produce a broken
    /// enlistment.
    /// </summary>
    public static class RefStorage
    {
        public const string RefStorageConfigName = "extensions.refstorage";
        public const string ReftableValue = "reftable";

        /// <summary>
        /// Name of the directory under .git in which the reftable backend stores refs
        /// (.git/reftable/). Its presence identifies a reftable repository even when the
        /// git config is too corrupt for 'git config' to read - useful for 'gvfs repair',
        /// which runs precisely when the config may be unreadable.
        /// </summary>
        public const string ReftableDirectoryName = "reftable";

        public const string UnsupportedReftableErrorMessage =
            "VFS for Git only supports repositories that use the \"files\" ref storage format, but this " +
            "repository was initialized with the \"reftable\" format. VFS for Git writes refs directly " +
            "rather than through git's ref backend, so a reftable repository cannot be used. Re-clone or " +
            "re-initialize the repository with files ref storage (for example, 'git init --ref-format=files').";

        /// <summary>
        /// Returns true if the given value of extensions.refstorage identifies a reftable
        /// repository. A missing/empty value means the repository defaults to the "files"
        /// ref storage format.
        /// </summary>
        public static bool IsReftable(string refStorageConfigValue)
        {
            return string.Equals(refStorageConfigValue?.Trim(), ReftableValue, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Reads extensions.refstorage from the local config of the repo backing the given
        /// GitProcess, and returns true if it identifies a reftable repository.
        ///
        /// A config read failure (e.g. a broken git invocation, as opposed to the key simply
        /// being absent) is treated the same as "not reftable" here, matching the established
        /// behavior of GitProcess.TryGetFromConfig elsewhere in this codebase for optional
        /// config reads. Use <see cref="TryIsReftableRepo"/> if the caller wants to distinguish
        /// and surface that failure instead of silently treating it as the files format.
        /// </summary>
        public static bool IsReftableRepo(GitProcess git)
        {
            TryIsReftableRepo(git, out bool isReftable, out string _);
            return isReftable;
        }

        /// <summary>
        /// Reads extensions.refstorage from the local config of the repo backing the given
        /// GitProcess. Returns false and populates <paramref name="error"/> if the config
        /// could not be read at all (a missing key is not an error - it simply means the
        /// repository defaults to the "files" ref storage format, and isReftable is set to
        /// false).
        /// </summary>
        public static bool TryIsReftableRepo(GitProcess git, out bool isReftable, out string error)
        {
            GitProcess.ConfigResult result = git.GetFromLocalConfig(RefStorageConfigName);
            if (!result.TryParseAsString(out string value, out error))
            {
                isReftable = false;
                return false;
            }

            isReftable = IsReftable(value);
            return true;
        }
    }
}
