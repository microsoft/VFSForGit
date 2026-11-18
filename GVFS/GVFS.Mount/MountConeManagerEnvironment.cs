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
        private readonly GVFSContext context;
        private readonly FileSystemCallbacks fileSystemCallbacks;
        private readonly string coneFileLockPath;

        public MountConeManagerEnvironment(GVFSContext context, FileSystemCallbacks fileSystemCallbacks, string sparseCheckoutPath)
        {
            ArgumentNullException.ThrowIfNull(context);
            ArgumentNullException.ThrowIfNull(fileSystemCallbacks);
            ArgumentNullException.ThrowIfNull(sparseCheckoutPath);

            this.context = context;
            this.fileSystemCallbacks = fileSystemCallbacks;
            this.coneFileLockPath = sparseCheckoutPath + GVFSConstants.DotGit.LockExtension;
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
    }
}
