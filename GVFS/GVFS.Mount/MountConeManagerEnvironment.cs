using GVFS.Common;
using GVFS.Common.Git;
using GVFS.Common.NamedPipes;
using GVFS.Common.Sparse;
using GVFS.Virtualization;
using System;
using System.Collections.Generic;
using System.IO;

namespace GVFS.Mount
{
    /// <summary>
    /// The mount services that <see cref="AutoSparseIndexConeManager"/> uses.
    /// </summary>
    internal class MountConeManagerEnvironment : IConeManagerEnvironment
    {
        /// <summary>
        /// The <c>git version --build-options</c> line of a Git that changes only the index when
        /// the sparse-checkout cone changes under a virtual file system.
        /// </summary>
        internal const string IndexOnlySparsityFeature = "feature: vfs-sparse-index-sparsity-index-only";

        // The background check stops Git after this long. The cone then keeps the files.
        private const int ListPathsTimeoutMs = 2 * 60 * 1000;

        // The files and folders that Git keeps in its git directory while a merge, rebase,
        // cherry-pick, or revert waits for the user.
        private static readonly string[] InProgressOperationMarkers =
        {
            "MERGE_HEAD",
            "CHERRY_PICK_HEAD",
            "REVERT_HEAD",
            "rebase-merge",
            "rebase-apply",
            "sequencer",
        };

        private readonly GVFSContext context;
        private readonly FileSystemCallbacks fileSystemCallbacks;
        private readonly string coneFileLockPath;
        private readonly string pendingFilePath;
        private readonly Func<bool> isFolderDehydrateInProgress;

        public MountConeManagerEnvironment(GVFSContext context, FileSystemCallbacks fileSystemCallbacks, string sparseCheckoutPath, Func<bool> isFolderDehydrateInProgress)
        {
            ArgumentNullException.ThrowIfNull(context);
            ArgumentNullException.ThrowIfNull(fileSystemCallbacks);
            ArgumentNullException.ThrowIfNull(sparseCheckoutPath);
            ArgumentNullException.ThrowIfNull(isFolderDehydrateInProgress);

            this.context = context;
            this.fileSystemCallbacks = fileSystemCallbacks;
            this.coneFileLockPath = sparseCheckoutPath + GVFSConstants.DotGit.LockExtension;
            this.pendingFilePath = GetPendingFilePath(context.Enlistment);
            this.isFolderDehydrateInProgress = isFolderDehydrateInProgress;
            this.SparsityChangeIsIndexOnly = HasBuildOption(context.Enlistment, IndexOnlySparsityFeature);
        }

        public bool SparsityChangeIsIndexOnly { get; }

        /// <summary>The path of the file that tells the hooks that a cone change is pending.</summary>
        public static string GetPendingFilePath(GVFSEnlistment enlistment)
        {
            return Path.Combine(enlistment.DotGVFSRoot, GVFSConstants.DotGVFS.ConeChangePending);
        }

        public IEnumerable<string> GetModifiedPaths()
        {
            return this.fileSystemCallbacks.GetAllModifiedPaths();
        }

        public bool CanWriteIndex(int processId)
        {
            // While the holder waits in its pre-command hook, no other Git command writes the
            // index. The virtualizer also blocks a rename to .git\index while no Git command
            // holds the lock.
            NamedPipeMessages.LockData holder = this.context.Repository.GVFSLock.GetExternalHolder();
            return holder != null && holder.PID == processId;
        }

        public bool TryGetSparseDirectories(out IReadOnlyList<string> sparseDirectories, out string error)
        {
            List<string> directories = new List<string>();
            GitProcess.Result result = new GitProcess(this.context.Enlistment).ListSparseDirectories(directories.Add);
            if (result.ExitCodeIsFailure)
            {
                sparseDirectories = null;
                error = result.Errors;
                return false;
            }

            sparseDirectories = directories;
            error = null;
            return true;
        }

        public bool IsFileInWorkingDirectory(string gitPath)
        {
            string path = Path.Combine(
                this.context.Enlistment.WorkingDirectoryBackingRoot,
                gitPath.Replace(GVFSConstants.GitPathSeparator, Path.DirectorySeparatorChar));
            return this.context.FileSystem.FileExists(path);
        }

        public bool TryLockConeFile(out IDisposable coneFileLock, out Exception error)
        {
            coneFileLock = null;
            error = null;
            try
            {
                // Git creates the lock file with O_EXCL, so its sparse-checkout writers fail while
                // this file exists. Windows deletes the file when the handle closes, also when the
                // mount process ends, so a lock file never stays behind.
                coneFileLock = this.context.FileSystem.OpenFileStream(
                    this.coneFileLockPath,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.None,
                    FileOptions.DeleteOnClose,
                    callFlushFileBuffers: false);
                return true;
            }
            catch (IOException e)
            {
                error = e;
                return false;
            }
            catch (UnauthorizedAccessException e)
            {
                error = e;
                return false;
            }
        }

        public GitProcess.Result ApplyConeToIndex()
        {
            // Git reports success even when it cannot replace the index (update_working_directory
            // ignores the result of write_locked_index). A replaced index is a new file.
            string indexPath = this.context.Enlistment.GitIndexPath;
            DateTime before = File.GetLastWriteTimeUtc(indexPath);
            GitProcess.Result result = new GitProcess(this.context.Enlistment).ReapplySparseCheckout();
            if (!result.ExitCodeIsFailure && File.GetLastWriteTimeUtc(indexPath) == before)
            {
                return new GitProcess.Result(result.Output, "Git did not replace the index. " + result.Errors, GitProcess.Result.GenericFailureCode);
            }

            return result;
        }

        public ProcessStartTimeResult TryGetProcessStartTime(int processId, out long startTime)
        {
            return GVFSPlatform.Instance.TryGetActiveProcessStartTime(processId, out startTime);
        }

        public bool TrySetConeChangePending(bool pending, out Exception error)
        {
            error = null;
            try
            {
                if (pending)
                {
                    // The hook only checks that the file exists.
                    using (new FileStream(this.pendingFilePath, FileMode.OpenOrCreate, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete))
                    {
                    }
                }
                else
                {
                    File.Delete(this.pendingFilePath);
                }

                return true;
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                error = e;
                return false;
            }
        }

        public bool TryGetPathsThatDifferFromHead(out ICollection<string> paths, out string error)
        {
            List<string> differing = new List<string>();
            GitProcess.Result result = new GitProcess(this.context.Enlistment).ListPathsThatDifferFromHead(differing.Add, ListPathsTimeoutMs);
            if (result.ExitCodeIsFailure)
            {
                paths = null;
                error = result.Errors;
                return false;
            }

            paths = differing;
            error = null;
            return true;
        }

        public long GetRepositoryStamp()
        {
            try
            {
                // "git reset --soft" moves HEAD without a write to the index, but Git logs the
                // move in the HEAD reflog.
                long index = File.GetLastWriteTimeUtc(this.context.Enlistment.GitIndexPath).Ticks;
                long headLog = File.GetLastWriteTimeUtc(this.context.Enlistment.GitHeadLogPath).Ticks;
                return unchecked((index * 31) + headLog);
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                // Unknown: the manager checks again.
                return DateTime.UtcNow.Ticks;
            }
        }

        public string GetDehydrateBlocker()
        {
            if (this.isFolderDehydrateInProgress())
            {
                return "FolderDehydrate";
            }

            // Git keeps these in the git directory of the worktree while the operation waits
            // for the user. The index can then have conflicts or a state that the user resolves.
            string gitDirectory = Path.GetDirectoryName(this.context.Enlistment.GitIndexPath);
            foreach (string name in InProgressOperationMarkers)
            {
                if (this.context.FileSystem.FileExists(Path.Combine(gitDirectory, name)) ||
                    this.context.FileSystem.DirectoryExists(Path.Combine(gitDirectory, name)))
                {
                    return name;
                }
            }

            return null;
        }

        public bool TryGetBlobsThatMatchHead(IReadOnlyList<string> gitPaths, out IReadOnlyDictionary<string, HeadBlob> blobs, out IReadOnlyCollection<string> notInIndex, out string error)
        {
            StringComparer pathComparer = GVFSPlatform.Instance.Constants.PathComparer;
            Dictionary<string, HeadBlob> matching = new Dictionary<string, HeadBlob>(pathComparer);
            List<string> missing = new List<string>();
            GitProcess.Result result = new GitProcess(this.context.Enlistment).GetBlobsThatMatchHead(gitPaths, pathComparer, matching, missing);
            if (result.ExitCodeIsFailure)
            {
                blobs = null;
                notInIndex = null;
                error = result.Errors;
                return false;
            }

            blobs = matching;
            notInIndex = missing;
            error = null;
            return true;
        }

        public CleanFileDehydrateResult TryDehydrateCleanFile(string gitPath, HeadBlob blob)
        {
            return this.fileSystemCallbacks.TryDehydrateCleanFile(gitPath, blob);
        }

        public bool TryRestoreModifiedPath(string gitPath)
        {
            return this.fileSystemCallbacks.TryRestoreModifiedPath(gitPath);
        }

        public bool IsProjectedFile(string gitPath, string sha)
        {
            string virtualPath = gitPath.Replace(GVFSConstants.GitPathSeparator, Path.DirectorySeparatorChar);
            return this.fileSystemCallbacks.GitIndexProjection.IsFileProjectedWithSha(virtualPath, sha);
        }

        public void InvalidateProjection()
        {
            // The projection does not include an entry without the skip-worktree bit. Git set
            // the bit of each dehydrated file, so the projection must include them again. The
            // file system callbacks wait for the new projection.
            this.fileSystemCallbacks.GitIndexProjection.InvalidateProjection();
        }

        private static bool HasBuildOption(GVFSEnlistment enlistment, string option)
        {
            GitProcess.Result result = new GitProcess(enlistment).VersionBuildOptions();
            return !result.ExitCodeIsFailure && GitProcess.HasBuildOption(result.Output, option);
        }
    }
}
