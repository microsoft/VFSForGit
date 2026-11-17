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
    }
}
