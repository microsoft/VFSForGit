using System;

namespace GVFS.Common.Git
{
    [Flags]
    public enum GitCoreGVFSFlags
    {
        // GVFS_SKIP_SHA_ON_INDEX
        // Disables the calculation of the sha when writing the index
        SkipShaOnIndex = 1 << 0,

        // GVFS_BLOCK_COMMANDS
        // Blocks git commands that are not allowed in a GVFS/Scalar repo
        BlockCommands = 1 << 1,

        // GVFS_MISSING_OK
        // Normally git write-tree ensures that the objects referenced by the
        // directory exist in the object database.This option disables this check.
        MissingOk = 1 << 2,

        // GVFS_NO_DELETE_OUTSIDE_SPARSECHECKOUT
        // When marking entries to remove from the index and the working
        // directory this option will take into account what the
        // skip-worktree bit was set to so that if the entry has the
        // skip-worktree bit set it will not be removed from the working
        // directory.  This will allow virtualized working directories to
        // detect the change to HEAD and use the new commit tree to show
        // the files that are in the working directory.
        NoDeleteOutsideSparseCheckout = 1 << 3,

        // GVFS_FETCH_SKIP_REACHABILITY_AND_UPLOADPACK
        // While performing a fetch with a virtual file system we know
        // that there will be missing objects and we don't want to download
        // them just because of the reachability of the commits.  We also
        // don't want to download a pack file with commits, trees, and blobs
        // since these will be downloaded on demand.  This flag will skip the
        // checks on the reachability of objects during a fetch as well as
        // the upload pack so that extraneous objects don't get downloaded.
        FetchSkipReachabilityAndUploadPack = 1 << 4,

        // 1 << 5 has been deprecated

        // GVFS_BLOCK_FILTERS_AND_EOL_CONVERSIONS
        // With a virtual file system we only know the file size before any
        // CRLF or smudge/clean filters processing is done on the client.
        // To prevent file corruption due to truncation or expansion with
        // garbage at the end, these filters must not run when the file
        // is first accessed and brought down to the client. Git.exe can't
        // currently tell the first access vs subsequent accesses so this
        // flag just blocks them from occurring at all.
        BlockFiltersAndEolConversions = 1 << 6,

        // GVFS_PREFETCH_DURING_FETCH
        // While performing a `git fetch` command, use the gvfs-helper to
        // perform a "prefetch" of commits and trees.
        PrefetchDuringFetch = 1 << 7,

        // GVFS_SUPPORTS_WORKTREES
        // Signals that this GVFS version supports git worktrees,
        // allowing `git worktree add/remove` on VFS-enabled repos.
        SupportsWorktrees = 1 << 8,
    }
}
