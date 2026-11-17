using GVFS.Common.Git;
using System;
using System.Collections.Generic;

namespace GVFS.Common.Sparse
{
    /// <summary>
    /// The mount services that <see cref="AutoSparseIndexConeManager"/> uses. The mount
    /// implements them; unit tests replace them, so that no test starts Git or needs a mount.
    /// </summary>
    public interface IConeManagerEnvironment
    {
        /// <summary>The modified-path entries of the worktree (folders end with a slash).</summary>
        IEnumerable<string> GetModifiedPaths();

        /// <summary>
        /// Return true when the Git process <paramref name="processId"/> holds the GVFS lock.
        /// Only then can a Git process that the mount starts replace the index: the holder
        /// waits in its pre-command hook, and no other Git command writes the index.
        /// </summary>
        bool CanWriteIndex(int processId);

        /// <summary>
        /// Get the sparse-directory entries of the index (each ends with a slash).
        /// </summary>
        bool TryGetSparseDirectories(out IReadOnlyList<string> sparseDirectories, out string error);

        /// <summary>
        /// Return true when the working directory has a file at the modified path
        /// <paramref name="gitPath"/>. A modified file is a full file or is deleted, so the
        /// check does not hydrate anything.
        /// </summary>
        bool IsFileInWorkingDirectory(string gitPath);

        /// <summary>
        /// Take the lock file that Git's own <c>sparse-checkout</c> writers take
        /// (<c>info/sparse-checkout.lock</c>). Dispose the result to release it.
        /// </summary>
        bool TryLockConeFile(out IDisposable coneFileLock, out Exception error);

        /// <summary>
        /// Run Git to make the sparse-directory entries of the index match the cone file. A
        /// result with a success exit code means that Git replaced the index.
        /// </summary>
        GitProcess.Result ApplyConeToIndex();

        /// <summary>Read the start time of a process, to tell it apart from a later process with the same id.</summary>
        ProcessStartTimeResult TryGetProcessStartTime(int processId, out long startTime);

        /// <summary>
        /// True when Git changes only the index when the cone changes, and never writes or deletes
        /// a working-tree file. Then a widen can expand the directory of a deleted file, and the
        /// manager does not need <see cref="TryGetSparseDirectories"/>.
        /// </summary>
        bool SparsityChangeIsIndexOnly { get; }

        /// <summary>
        /// Create (<paramref name="pending"/> true) or delete the file that tells the pre-command
        /// hook that a cone change is pending (see <see cref="GVFSConstants.DotGVFS.ConeChangePending"/>).
        /// This runs in file system callbacks, so it must be fast and must not throw.
        /// </summary>
        bool TrySetConeChangePending(bool pending, out Exception error);

        /// <summary>
        /// Get the paths that differ between HEAD and the index or the working tree, as Git
        /// reports them: changed tracked paths, and untracked files and folders (a folder ends
        /// with a slash). Git compares the content of a file whose stat data changed, it does not
        /// list ignored files, and it does not write the index.
        /// </summary>
        bool TryGetPathsThatDifferFromHead(out ICollection<string> paths, out string error);

        /// <summary>
        /// Return a value that changes when Git writes the index or moves HEAD, so that the
        /// manager can skip a check when nothing changed.
        /// </summary>
        long GetRepositoryStamp();

        /// <summary>
        /// Return why modified files cannot return to placeholders now, or null when they can.
        /// For example, a merge, rebase, cherry-pick, or revert is in progress, or
        /// <c>gvfs dehydrate</c> is working on folders.
        /// </summary>
        string GetDehydrateBlocker();

        /// <summary>
        /// Read the blob of each path in the index and in HEAD. Return in <paramref name="blobs"/>
        /// only the paths that have the same blob in both, and in <paramref name="notInIndex"/>
        /// the paths that the index does not have. The caller holds the GVFS lock for a Git
        /// command, so the index and HEAD do not change.
        /// </summary>
        bool TryGetBlobsThatMatchHead(IReadOnlyList<string> gitPaths, out IReadOnlyDictionary<string, HeadBlob> blobs, out IReadOnlyCollection<string> notInIndex, out string error);

        /// <summary>
        /// Return the modified file <paramref name="gitPath"/> to a placeholder for
        /// <paramref name="blob"/>, and remove it from the modified paths. This happens only when
        /// the bytes on disk are exactly the blob, because the conversion discards them.
        /// </summary>
        CleanFileDehydrateResult TryDehydrateCleanFile(string gitPath, HeadBlob blob);

        /// <summary>
        /// Add a file that <see cref="TryDehydrateCleanFile"/> removed back to the modified paths,
        /// so that Git sees it again when the index cannot change.
        /// </summary>
        bool TryRestoreModifiedPath(string gitPath);

        /// <summary>
        /// Return true when the projection has the file <paramref name="gitPath"/> for the blob
        /// <paramref name="sha"/>. The projection leaves out every index entry without the
        /// skip-worktree bit, but it keeps an entry that had the bit at the last rebuild.
        /// </summary>
        bool IsProjectedFile(string gitPath, string sha);

        /// <summary>
        /// Make the mount read the index again, because Git set the skip-worktree bit of files
        /// that the projection does not have. Returns at once.
        /// </summary>
        void InvalidateProjection();
    }
}
