#nullable enable

using GVFS.Common.Tracing;
using Microsoft.Win32.SafeHandles;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Threading;

namespace GVFS.Platform.Windows
{
    /// <summary>
    /// Deletes an explicitly authorized timestamped backup, never the common backup root.
    /// The caller owns backup selection, user consent, and successful-dehydrate eligibility.
    /// A timestamp-shaped name or file identity alone does not identify a completed backup.
    /// </summary>
    public sealed partial class WindowsDehydrateBackup : IDisposable
    {
        private const string BackupFolderName = "dehydrate_backup";
        private const int MaximumDepth = 256;
        private const int MaximumFailureSamples = 16;
        private readonly ITracer tracer;
        private readonly string path;
        private readonly SafeFileHandle container;
        private readonly SafeFileHandle backup;
        private int operationInProgress;
        private bool disposed;
        private bool deleted;

        private WindowsDehydrateBackup(
            ITracer tracer,
            string path,
            SafeFileHandle container,
            SafeFileHandle backup,
            string identity)
        {
            this.tracer = tracer;
            this.path = path;
            this.container = container;
            this.backup = backup;
            this.Identity = identity;
        }

        public string Identity { get; }

        /// <param name="backupParentRoot">
        /// The local, non-drive-root directory that contains dehydrate_backup.
        /// For a linked worktree, pass the directory that contains the worktree, not the primary enlistment.
        /// </param>
        public static WindowsDehydrateBackup Open(
            ITracer tracer,
            string backupParentRoot,
            string backupName,
            string? expectedIdentity = null,
            string? worktreeName = null)
        {
            ArgumentNullException.ThrowIfNull(tracer);
            ArgumentException.ThrowIfNullOrWhiteSpace(backupParentRoot);
            ArgumentException.ThrowIfNullOrWhiteSpace(backupName);
            if (!Path.IsPathFullyQualified(backupParentRoot) ||
                !DateTime.TryParseExact(
                    backupName,
                    new[] { "yyyyMMdd_HHmmss_fffffff", "yyyyMMdd_HHmmss" },
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.None,
                    out _) ||
                (worktreeName != null &&
                 (string.IsNullOrWhiteSpace(worktreeName) || worktreeName == "." || worktreeName == ".." ||
                  worktreeName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
                  worktreeName.EndsWith(".", StringComparison.Ordinal) || worktreeName.EndsWith(" ", StringComparison.Ordinal))))
            {
                throw new ArgumentException("Specify an absolute backup parent path and a timestamped backup name.");
            }

            string root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(backupParentRoot));
            if (root.Length <= 3 || root[1] != ':' || root[2] != Path.DirectorySeparatorChar)
            {
                throw new ArgumentException("Specify a local backup parent directory, not a drive root or network path.", nameof(backupParentRoot));
            }

            string containerPath = Path.Combine(root, BackupFolderName);
            string backupPath = worktreeName == null
                ? Path.Combine(containerPath, backupName)
                : Path.Combine(containerPath, worktreeName, backupName);
            SafeFileHandle? container = null;
            SafeFileHandle? worktreeContainer = null;
            SafeFileHandle? backup = null;
            bool opened = false;
            try
            {
                container = Native.OpenContainer(containerPath);
                Native.FileIdentity containerIdentity = Native.GetIdentity(container);
                Native.VerifyOrdinaryDirectory(container);
                Native.VerifyFinalPath(container, containerPath);
                if (worktreeName != null)
                {
                    worktreeContainer = Native.OpenChild(container, worktreeName, directory: true, forDeletion: false);
                    Native.VerifyOrdinaryDirectory(worktreeContainer);
                    container.Dispose();
                    container = worktreeContainer;
                    worktreeContainer = null;
                }

                backup = Native.OpenChild(container, backupName, directory: true);
                Native.VerifyOrdinaryDirectory(backup);
                Native.FileIdentity identity = Native.GetIdentity(backup);
                if (identity.Volume != containerIdentity.Volume ||
                    (expectedIdentity != null && !string.Equals(expectedIdentity, identity.ToString(), StringComparison.Ordinal)))
                {
                    throw new IOException("The backup identity does not match the authorized backup.");
                }

                WindowsDehydrateBackup scope = new WindowsDehydrateBackup(tracer, backupPath, container, backup, identity.ToString());
                opened = true;
                return scope;
            }
            catch (Exception exception) when (exception is IOException || exception is Win32Exception)
            {
                tracer.RelatedWarning(new EventMetadata
                {
                    { "BackupPath", backupPath },
                    { "Exception", exception.ToString() },
                }, "Failed to open the authorized dehydrate backup.");
                throw;
            }
            finally
            {
                worktreeContainer?.Dispose();
                if (!opened)
                {
                    backup?.Dispose();
                    container?.Dispose();
                }
            }
        }

        /// <summary>
        /// Reports progress after each deleted handle closes. Callback errors stop deletion and propagate to the caller.
        /// Concurrent or reentrant calls to Delete or Dispose are not supported.
        /// </summary>
        public DeletionResult Delete(CancellationToken cancellationToken = default, Action<DeletionProgress>? progress = null)
        {
            this.EnterOperation();
            try
            {
                ObjectDisposedException.ThrowIf(this.disposed, this);
                DeletionResult result = new DeletionResult();
                if (this.deleted)
                {
                    result.BackupDeleted = true;
                    return result;
                }

                if (this.DeleteDirectory(this.backup, this.path, depth: 0, cancellationToken, progress, result))
                {
                    this.backup.Dispose();
                    this.deleted = true;
                    result.BackupDeleted = true;
                    progress?.Invoke(new DeletionProgress(result.FilesDeleted, result.DirectoriesDeleted));
                }

                return result;
            }
            finally
            {
                Volatile.Write(ref this.operationInProgress, 0);
            }
        }

        public void Dispose()
        {
            this.EnterOperation();
            try
            {
                if (!this.disposed)
                {
                    this.backup.Dispose();
                    this.container.Dispose();
                    this.disposed = true;
                }
            }
            finally
            {
                Volatile.Write(ref this.operationInProgress, 0);
            }
        }

        private void EnterOperation()
        {
            if (Interlocked.CompareExchange(ref this.operationInProgress, 1, 0) != 0)
            {
                throw new InvalidOperationException("Backup cleanup does not permit concurrent or reentrant operations.");
            }
        }

        private bool DeleteDirectory(
            SafeFileHandle directory,
            string directoryPath,
            int depth,
            CancellationToken cancellationToken,
            Action<DeletionProgress>? progress,
            DeletionResult result)
        {
            if (this.CheckCancellation(cancellationToken, result))
            {
                return false;
            }

            if (depth >= MaximumDepth)
            {
                this.RecordFailure(result, directoryPath, new IOException("The backup exceeds the supported directory depth."));
                return false;
            }

            bool childrenDeleted = true;
            using (IEnumerator<Native.Entry> entries = Native.EnumeratePhysicalEntries(directory).GetEnumerator())
            {
                while (true)
                {
                    if (this.CheckCancellation(cancellationToken, result))
                    {
                        return false;
                    }

                    try
                    {
                        if (!entries.MoveNext())
                        {
                            break;
                        }
                    }
                    catch (Exception exception) when (exception is Win32Exception || exception is IOException)
                    {
                        this.RecordFailure(result, directoryPath, exception);
                        return false;
                    }

                    if (this.CheckCancellation(cancellationToken, result))
                    {
                        return false;
                    }

                    childrenDeleted &= this.DeleteEntry(
                        directory, directoryPath, entries.Current, depth, cancellationToken, progress, result);
                }
            }

            return !this.CheckCancellation(cancellationToken, result) && childrenDeleted &&
                this.TryMarkForDeletion(directory, directoryPath, isDirectory: true, result);
        }

        private bool DeleteEntry(
            SafeFileHandle directory,
            string directoryPath,
            Native.Entry entry,
            int depth,
            CancellationToken cancellationToken,
            Action<DeletionProgress>? progress,
            DeletionResult result)
        {
            string childPath = Path.Combine(directoryPath, entry.Name);
            SafeFileHandle? child = null;
            bool entryDeleted;
            try
            {
                bool recurse;
                try
                {
                    child = Native.OpenChild(directory, entry.Name, entry.IsDirectory);
                    Native.FileIdentity identity = Native.GetIdentity(child);
                    Native.VerifySupportedReparsePoint(child);
                    if (identity.FileId != entry.FileId)
                    {
                        throw new IOException("The backup entry changed after enumeration.");
                    }

                    recurse = entry.IsDirectory && !Native.IsTombstone(child);
                }
                catch (Win32Exception exception) when (exception.NativeErrorCode == 2 || exception.NativeErrorCode == 3)
                {
                    ++result.EntriesAlreadyAbsent;
                    return true;
                }
                catch (Exception exception) when (exception is Win32Exception || exception is IOException)
                {
                    this.RecordFailure(result, childPath, exception);
                    return false;
                }

                entryDeleted = recurse
                    ? this.DeleteDirectory(child, childPath, depth + 1, cancellationToken, progress, result)
                    : this.TryMarkForDeletion(child, childPath, entry.IsDirectory, result);
            }
            finally
            {
                child?.Dispose();
            }

            if (entryDeleted)
            {
                progress?.Invoke(new DeletionProgress(result.FilesDeleted, result.DirectoriesDeleted));
            }

            return entryDeleted;
        }

        private bool TryMarkForDeletion(SafeFileHandle handle, string entryPath, bool isDirectory, DeletionResult result)
        {
            try
            {
                Native.MarkForDeletion(handle);
            }
            catch (Exception exception) when (exception is Win32Exception || exception is IOException)
            {
                this.RecordFailure(result, entryPath, exception);
                return false;
            }

            if (isDirectory)
            {
                ++result.DirectoriesDeleted;
            }
            else
            {
                ++result.FilesDeleted;
            }

            return true;
        }

        private bool CheckCancellation(CancellationToken cancellationToken, DeletionResult result)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                result.Cancelled = true;
                return true;
            }

            return false;
        }

        private void RecordFailure(DeletionResult result, string failurePath, Exception exception)
        {
            ++result.FailureCount;
            result.AddFailure(new DeletionFailure(failurePath, exception));

            this.tracer.RelatedWarning(new EventMetadata
            {
                { "BackupPath", this.path },
                { "Path", failurePath },
                { "Exception", exception.ToString() },
            }, "Dehydrate backup cleanup retained an entry.");
        }

        public sealed class DeletionResult
        {
            private readonly List<DeletionFailure> failureSamples = new List<DeletionFailure>();

            internal DeletionResult()
            {
                this.FailureSamples = this.failureSamples.AsReadOnly();
            }

            public bool BackupDeleted { get; internal set; }
            public bool Cancelled { get; internal set; }
            public long FilesDeleted { get; internal set; }
            public long DirectoriesDeleted { get; internal set; }
            public long EntriesAlreadyAbsent { get; internal set; }
            public long FailureCount { get; internal set; }
            public IReadOnlyList<DeletionFailure> FailureSamples { get; }
            public bool Succeeded => this.BackupDeleted && !this.Cancelled && this.FailureCount == 0;

            internal void AddFailure(DeletionFailure failure)
            {
                if (this.failureSamples.Count < MaximumFailureSamples)
                {
                    this.failureSamples.Add(failure);
                }
            }
        }

        public sealed class DeletionFailure
        {
            internal DeletionFailure(string path, Exception exception)
            {
                this.Path = path;
                this.Exception = exception;
            }

            public string Path { get; }
            public Exception Exception { get; }
        }

        public readonly struct DeletionProgress
        {
            internal DeletionProgress(long filesDeleted, long directoriesDeleted)
            {
                this.FilesDeleted = filesDeleted;
                this.DirectoriesDeleted = directoriesDeleted;
            }

            public long FilesDeleted { get; }
            public long DirectoriesDeleted { get; }
        }
    }
}
