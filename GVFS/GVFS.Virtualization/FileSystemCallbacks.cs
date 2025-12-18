using GVFS.Common;
using GVFS.Common.Database;
using GVFS.Common.FileSystem;
using GVFS.Common.Git;
using GVFS.Common.NamedPipes;
using GVFS.Common.Tracing;
using GVFS.Virtualization.Background;
using GVFS.Virtualization.BlobSize;
using GVFS.Virtualization.FileSystem;
using GVFS.Virtualization.Projection;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;

namespace GVFS.Virtualization
{
    public class FileSystemCallbacks : IDisposable, IHeartBeatMetadataProvider
    {
        private const string EtwArea = nameof(FileSystemCallbacks);
        private const int NumberOfRetriesCheckingForDeleted = 10;
        private const int MillisecondsToSleepBeforeCheckingForDeleted = 1;

        private static readonly GitCommandLineParser.Verbs LeavesProjectionUnchangedVerbs =
            GitCommandLineParser.Verbs.AddOrStage |
            GitCommandLineParser.Verbs.Commit |
            GitCommandLineParser.Verbs.Status |
            GitCommandLineParser.Verbs.UpdateIndex;

        private readonly string logsHeadPath;

        private GVFSContext context;
        private IPlaceholderCollection placeholderDatabase;
        private ModifiedPathsDatabase modifiedPaths;
        private ConcurrentHashSet<string> newlyCreatedFileAndFolderPaths;
        private ConcurrentDictionary<string, PlaceHolderCreateCounter> filePlaceHolderCreationCount;
        private ConcurrentDictionary<string, PlaceHolderCreateCounter> folderPlaceHolderCreationCount;
        private ConcurrentDictionary<string, PlaceHolderCreateCounter> fileHydrationCount;
        private BackgroundFileSystemTaskRunner backgroundFileSystemTaskRunner;
        private FileSystemVirtualizer fileSystemVirtualizer;
        private FileProperties logsHeadFileProperties;

        private GitStatusCache gitStatusCache;
        private bool enableGitStatusCache;

        public FileSystemCallbacks(
            GVFSContext context,
            GVFSGitObjects gitObjects,
            RepoMetadata repoMetadata,
            BlobSizes blobSizes,
            GitIndexProjection gitIndexProjection,
            BackgroundFileSystemTaskRunner backgroundFileSystemTaskRunner,
            FileSystemVirtualizer fileSystemVirtualizer,
            IPlaceholderCollection placeholderDatabase,
            ISparseCollection sparseCollection,
            GitStatusCache gitStatusCache = null)
        {
            this.logsHeadFileProperties = null;

            this.context = context;
            this.fileSystemVirtualizer = fileSystemVirtualizer;

            this.filePlaceHolderCreationCount = new ConcurrentDictionary<string, PlaceHolderCreateCounter>(GVFSPlatform.Instance.Constants.PathComparer);
            this.folderPlaceHolderCreationCount = new ConcurrentDictionary<string, PlaceHolderCreateCounter>(GVFSPlatform.Instance.Constants.PathComparer);
            this.fileHydrationCount = new ConcurrentDictionary<string, PlaceHolderCreateCounter>(GVFSPlatform.Instance.Constants.PathComparer);
            this.newlyCreatedFileAndFolderPaths = new ConcurrentHashSet<string>(GVFSPlatform.Instance.Constants.PathComparer);

            string error;
            if (!ModifiedPathsDatabase.TryLoadOrCreate(
                this.context.Tracer,
                Path.Combine(this.context.Enlistment.DotGVFSRoot, GVFSConstants.DotGVFS.Databases.ModifiedPaths),
                this.context.FileSystem,
                out this.modifiedPaths,
                out error))
            {
                throw new InvalidRepoException(error);
            }

            this.BlobSizes = blobSizes ?? new BlobSizes(context.Enlistment.BlobSizesRoot, context.FileSystem, context.Tracer);
            this.BlobSizes.Initialize();

            this.placeholderDatabase = placeholderDatabase;
            this.GitIndexProjection = gitIndexProjection ?? new GitIndexProjection(
                context,
                gitObjects,
                this.BlobSizes,
                repoMetadata,
                fileSystemVirtualizer,
                this.placeholderDatabase,
                sparseCollection,
                this.modifiedPaths);

            if (backgroundFileSystemTaskRunner != null)
            {
                this.backgroundFileSystemTaskRunner = backgroundFileSystemTaskRunner;
                this.backgroundFileSystemTaskRunner.SetCallbacks(
                    this.PreBackgroundOperation,
                    this.ExecuteBackgroundOperation,
                    this.PostBackgroundOperation);
            }
            else
            {
                this.backgroundFileSystemTaskRunner = new BackgroundFileSystemTaskRunner(
                    this.context,
                    this.PreBackgroundOperation,
                    this.ExecuteBackgroundOperation,
                    this.PostBackgroundOperation,
                    Path.Combine(context.Enlistment.DotGVFSRoot, GVFSConstants.DotGVFS.Databases.BackgroundFileSystemTasks));
            }

            this.enableGitStatusCache = gitStatusCache != null;

            // If the status cache is not enabled, create a dummy GitStatusCache that will never be initialized
            // This lets us from having to add null checks to callsites into GitStatusCache.
            this.gitStatusCache = gitStatusCache ?? new GitStatusCache(context, TimeSpan.Zero);

            this.logsHeadPath = Path.Combine(this.context.Enlistment.WorkingDirectoryBackingRoot, GVFSConstants.DotGit.Logs.Head);

            EventMetadata metadata = new EventMetadata();
            metadata.Add("placeholders.Count", this.placeholderDatabase.GetCount());
            metadata.Add("background.Count", this.backgroundFileSystemTaskRunner.Count);
            metadata.Add(TracingConstants.MessageKey.InfoMessage, $"{nameof(FileSystemCallbacks)} created");
            this.context.Tracer.RelatedEvent(EventLevel.Informational, $"{nameof(FileSystemCallbacks)}_Constructor", metadata);
        }

        public IProfilerOnlyIndexProjection GitIndexProjectionProfiler
        {
            get { return this.GitIndexProjection; }
        }

        /// <summary>
        /// Gets the count of tasks in the background operation queue
        /// </summary>
        /// <remarks>
        /// This is an expensive call on .net core and you should avoid calling
        /// in performance critical paths.
        /// </remarks>
        public int BackgroundOperationCount
        {
            get { return this.backgroundFileSystemTaskRunner.Count; }
        }

        public BlobSizes BlobSizes { get; private set; }

        public GitIndexProjection GitIndexProjection { get; private set; }

        /// <summary>
        /// Returns true for paths that begin with ".git\" (regardless of case)
        /// </summary>
        public static bool IsPathInsideDotGit(string relativePath)
        {
            return relativePath.StartsWith(GVFSConstants.DotGit.Root + Path.DirectorySeparatorChar, GVFSPlatform.Instance.Constants.PathComparison);
        }

        public bool TryStart(out string error)
        {
            this.fileSystemVirtualizer.Initialize(this);
            this.modifiedPaths.RemoveEntriesWithParentFolderEntry(this.context.Tracer);
            this.modifiedPaths.WriteAllEntriesAndFlush();

            this.GitIndexProjection.Initialize(this.backgroundFileSystemTaskRunner);

            if (this.enableGitStatusCache)
            {
                this.gitStatusCache.Initialize();
            }

            this.backgroundFileSystemTaskRunner.Start();

            if (!this.fileSystemVirtualizer.TryStart(out error))
            {
                return false;
            }

            return true;
        }

        public void Stop()
        {
            // Shutdown the GitStatusCache before other
            // components that it depends on.
            this.gitStatusCache.Shutdown();

            this.fileSystemVirtualizer.PrepareToStop();
            this.backgroundFileSystemTaskRunner.Shutdown();
            this.GitIndexProjection.Shutdown();
            this.BlobSizes.Shutdown();
            this.fileSystemVirtualizer.Stop();
        }

        public void Dispose()
        {
            if (this.BlobSizes != null)
            {
                this.BlobSizes.Dispose();
                this.BlobSizes = null;
            }

            if (this.fileSystemVirtualizer != null)
            {
                this.fileSystemVirtualizer.Dispose();
                this.fileSystemVirtualizer = null;
            }

            if (this.GitIndexProjection != null)
            {
                this.GitIndexProjection.Dispose();
                this.GitIndexProjection = null;
            }

            if (this.modifiedPaths != null)
            {
                this.modifiedPaths.Dispose();
                this.modifiedPaths = null;
            }

            if (this.gitStatusCache != null)
            {
                this.gitStatusCache.Dispose();
                this.gitStatusCache = null;
            }

            if (this.backgroundFileSystemTaskRunner != null)
            {
                this.backgroundFileSystemTaskRunner.Dispose();
                this.backgroundFileSystemTaskRunner = null;
            }
        }

        public bool IsReadyForExternalAcquireLockRequests(NamedPipeMessages.LockData requester, out string denyMessage)
        {
            if (!this.backgroundFileSystemTaskRunner.IsEmpty)
            {
                denyMessage = "Waiting for GVFS to release the lock";
                return false;
            }

            if (!this.GitIndexProjection.IsProjectionParseComplete())
            {
                denyMessage = "Waiting for GVFS to parse index and update placeholder files";
                return false;
            }

            if (!this.gitStatusCache.IsReadyForExternalAcquireLockRequests(requester, out denyMessage))
            {
                return false;
            }

            // Even though we're returning true and saying it's safe to ask for the lock
            // there is no guarantee that the lock will be acquired, because GVFS itself
            // could obtain the lock before the external holder gets it. Setting up an
            // appropriate error message in case that happens
            denyMessage = "Waiting for GVFS to release the lock";

            return true;
        }

        public EventMetadata GetAndResetHeartBeatMetadata(out bool logToFile)
        {
            logToFile = false;
            EventMetadata metadata = new EventMetadata();

            metadata.Add(
                "FilePlaceholderCreation",
                this.GetProcessInteractionData(this.GetAndResetProcessCountMetadata(ref this.filePlaceHolderCreationCount), ref logToFile));
            metadata.Add(
                "FolderPlaceholderCreation",
                this.GetProcessInteractionData(this.GetAndResetProcessCountMetadata(ref this.folderPlaceHolderCreationCount), ref logToFile));
            metadata.Add(
                "FilePlaceholdersHydrated",
                this.GetProcessInteractionData(this.GetAndResetProcessCountMetadata(ref this.fileHydrationCount), ref logToFile));

            metadata.Add("ModifiedPathsCount", this.modifiedPaths.Count);
            metadata.Add("FilePlaceholderCount", this.placeholderDatabase.GetFilePlaceholdersCount());
            metadata.Add("FolderPlaceholderCount", this.placeholderDatabase.GetFolderPlaceholdersCount());

            if (this.gitStatusCache.WriteTelemetryandReset(metadata))
            {
                logToFile = true;
            }

            metadata.Add(nameof(RepoMetadata.Instance.EnlistmentId), RepoMetadata.Instance.EnlistmentId);
            metadata.Add(
                "PhysicalDiskInfo",
                GVFSPlatform.Instance.GetPhysicalDiskInfo(
                    this.context.Enlistment.WorkingDirectoryBackingRoot,
                    sizeStatsOnly: true));

            return metadata;
        }

        public bool TryDehydrateFolder(string relativePath, out string errorMessage)
        {
            List<IPlaceholderData> removedPlaceholders = null;
            List<string> removedModifiedPaths = null;
            errorMessage = string.Empty;

            try
            {
                relativePath = GVFSDatabase.NormalizePath(relativePath);
                removedPlaceholders = this.placeholderDatabase.RemoveAllEntriesForFolder(relativePath);
                removedModifiedPaths = this.modifiedPaths.RemoveAllEntriesForFolder(relativePath);
                FileSystemResult result = this.fileSystemVirtualizer.DehydrateFolder(relativePath);
                if (result.Result != FSResult.Ok)
                {
                    errorMessage = $"{nameof(this.TryDehydrateFolder)} failed with {result.Result}";
                    this.context.Tracer.RelatedError(errorMessage);
                }
            }
            catch (Exception ex)
            {
                errorMessage = $"{nameof(this.TryDehydrateFolder)} threw an exception - {ex.Message}";
                EventMetadata metadata = this.CreateEventMetadata(relativePath, ex);
                this.context.Tracer.RelatedError(metadata, errorMessage);
            }

            if (!string.IsNullOrEmpty(errorMessage))
            {
                if (removedPlaceholders != null)
                {
                    foreach (IPlaceholderData data in removedPlaceholders)
                    {
                        try
                        {
                            this.placeholderDatabase.AddPlaceholderData(data);
                        }
                        catch (Exception ex)
                        {
                            EventMetadata metadata = this.CreateEventMetadata(data.Path, ex);
                            this.context.Tracer.RelatedError(metadata, $"{nameof(FileSystemCallbacks)}.{nameof(this.TryDehydrateFolder)} failed to add '{data.Path}' back into PlaceholderDatabase");
                        }
                    }
                }

                if (removedModifiedPaths != null)
                {
                    foreach (string modifiedPath in removedModifiedPaths)
                    {
                        if (!this.modifiedPaths.TryAdd(modifiedPath, isFolder: modifiedPath.EndsWith(GVFSConstants.GitPathSeparatorString), isRetryable: out bool isRetryable))
                        {
                            this.context.Tracer.RelatedError($"{nameof(FileSystemCallbacks)}.{nameof(this.TryDehydrateFolder)}: failed to add '{modifiedPath}' back into ModifiedPaths");
                        }
                    }
                }
            }

            return string.IsNullOrEmpty(errorMessage);
        }

        public void ForceIndexProjectionUpdate(bool invalidateProjection, bool invalidateModifiedPaths)
        {
            this.InvalidateState(invalidateProjection, invalidateModifiedPaths);
            this.GitIndexProjection.WaitForProjectionUpdate();
        }

        public NamedPipeMessages.ReleaseLock.Response TryReleaseExternalLock(int pid)
        {
            return this.GitIndexProjection.TryReleaseExternalLock(pid);
        }

        public IEnumerable<string> GetAllModifiedPaths()
        {
            return this.modifiedPaths.GetAllModifiedPaths();
        }

        public virtual void OnIndexFileChange()
        {
            string lockedGitCommand = this.context.Repository.GVFSLock.GetLockedGitCommand();
            GitCommandLineParser gitCommand = new GitCommandLineParser(lockedGitCommand);
            if (!gitCommand.IsValidGitCommand)
            {
                // Something wrote to the index without holding the GVFS lock, so we invalidate the projection
                this.InvalidateState(invalidateProjection: true, invalidateModifiedPaths: false);

                // But this isn't something we expect to see, so log a warning
                EventMetadata metadata = new EventMetadata
                {
                    { "Area", EtwArea },
                    { TracingConstants.MessageKey.WarningMessage, "Index modified without git holding GVFS lock" },
                };

                this.context.Tracer.RelatedEvent(EventLevel.Warning, $"{nameof(this.OnIndexFileChange)}_NoLock", metadata);
            }
        }

        public void InvalidateGitStatusCache()
        {
            this.gitStatusCache.Invalidate();

            // If there are background tasks queued up, then it will be
            // refreshed after they have been processed.
            if (this.backgroundFileSystemTaskRunner.IsEmpty)
            {
                this.gitStatusCache.RefreshAsynchronously();
            }
        }

        public virtual void OnLogsHeadChange()
        {
            // Don't open the .git\logs\HEAD file here to check its attributes as we're in a callback for the .git folder
            this.logsHeadFileProperties = null;
        }

        public void OnHeadOrRefChanged()
        {
            this.InvalidateGitStatusCache();
        }

        /// <summary>
        /// This method signals that the repository git exclude file
        /// has been modified (i.e. .git/info/exclude)
        /// </summary>
        public void OnExcludeFileChanged()
        {
            this.InvalidateGitStatusCache();
        }

        public void OnFileCreated(string relativePath)
        {
            this.AddToNewlyCreatedList(relativePath, isFolder: false);
            this.backgroundFileSystemTaskRunner.Enqueue(FileSystemTask.OnFileCreated(relativePath));
        }

        public void OnFileOverwritten(string relativePath)
        {
            this.backgroundFileSystemTaskRunner.Enqueue(FileSystemTask.OnFileOverwritten(relativePath));
        }

        public void OnFileSuperseded(string relativePath)
        {
            this.backgroundFileSystemTaskRunner.Enqueue(FileSystemTask.OnFileSuperseded(relativePath));
        }

        public void OnFileConvertedToFull(string relativePath)
        {
            this.backgroundFileSystemTaskRunner.Enqueue(FileSystemTask.OnFileConvertedToFull(relativePath));
        }

        public void OnFailedFileHydration(string relativePath)
        {
            this.backgroundFileSystemTaskRunner.Enqueue(FileSystemTask.OnFailedFileHydration(relativePath));
        }

        public virtual void OnFileRenamed(string oldRelativePath, string newRelativePath)
        {
            this.backgroundFileSystemTaskRunner.Enqueue(FileSystemTask.OnFileRenamed(oldRelativePath, newRelativePath));
        }

        public virtual void OnFileHardLinkCreated(string newLinkRelativePath, string existingRelativePath)
        {
            this.backgroundFileSystemTaskRunner.Enqueue(FileSystemTask.OnFileHardLinkCreated(newLinkRelativePath, existingRelativePath));
        }

        public virtual void OnFileSymLinkCreated(string newLinkRelativePath)
        {
            this.backgroundFileSystemTaskRunner.Enqueue(FileSystemTask.OnFileSymLinkCreated(newLinkRelativePath));
        }

        public void OnFileDeleted(string relativePath)
        {
            this.backgroundFileSystemTaskRunner.Enqueue(FileSystemTask.OnFileDeleted(relativePath));
        }

        public void OnFilePreDelete(string relativePath)
        {
            this.backgroundFileSystemTaskRunner.Enqueue(FileSystemTask.OnFilePreDelete(relativePath));
        }

        /// <summary>
        /// Called to indicate a folder was created
        /// </summary>
        /// <param name="relativePath">The relative path to the newly created folder</param>
        /// <param name="sparseFoldersUpdated">
        /// true when the folder is successfully added to the sparse list because it is in the projection but currently excluded.
        /// false when the folder was not excluded or there was a failure adding to the sparse list.
        /// </param>
        public void OnFolderCreated(string relativePath, out bool sparseFoldersUpdated)
        {
            sparseFoldersUpdated = false;
            GitIndexProjection.PathSparseState pathProjectionState = this.GitIndexProjection.GetFolderPathSparseState(relativePath);
            if (pathProjectionState == GitIndexProjection.PathSparseState.Excluded)
            {
                if (this.GitIndexProjection.TryAddSparseFolder(relativePath))
                {
                    sparseFoldersUpdated = true;
                    return;
                }
            }

            this.AddToNewlyCreatedList(relativePath, isFolder: true);
            this.backgroundFileSystemTaskRunner.Enqueue(FileSystemTask.OnFolderCreated(relativePath));
        }

        public virtual void OnFolderRenamed(string oldRelativePath, string newRelativePath)
        {
            this.backgroundFileSystemTaskRunner.Enqueue(FileSystemTask.OnFolderRenamed(oldRelativePath, newRelativePath));
        }

        public void OnFolderDeleted(string relativePath)
        {
            this.backgroundFileSystemTaskRunner.Enqueue(FileSystemTask.OnFolderDeleted(relativePath));
        }

        public void OnPossibleTombstoneFolderCreated(string relativePath)
        {
            this.GitIndexProjection.OnPossibleTombstoneFolderCreated(relativePath);
        }

        public void OnFolderPreDelete(string relativePath)
        {
            this.backgroundFileSystemTaskRunner.Enqueue(FileSystemTask.OnFolderPreDelete(relativePath));
        }

        public void OnPlaceholderFileCreated(string relativePath, string sha, string triggeringProcessImageFileName)
        {
            this.GitIndexProjection.OnPlaceholderFileCreated(relativePath, sha);

            // Note: Because OnPlaceholderFileCreated is not synchronized on all platforms it is possible that GVFS will double count
            // the creation of file placeholders if multiple requests for the same file are received at the same time on different
            // threads.
            this.filePlaceHolderCreationCount.AddOrUpdate(
                triggeringProcessImageFileName,
                (imageName) => { return new PlaceHolderCreateCounter(); },
                (key, oldCount) => { oldCount.Increment(); return oldCount; });
        }

        public void OnPlaceholderCreateBlockedForGit()
        {
            this.GitIndexProjection.OnPlaceholderCreateBlockedForGit();
        }

        public void OnPlaceholderFolderCreated(string relativePath, string triggeringProcessImageFileName)
        {
            this.GitIndexProjection.OnPlaceholderFolderCreated(relativePath);

            this.folderPlaceHolderCreationCount.AddOrUpdate(
                triggeringProcessImageFileName,
                (imageName) => { return new PlaceHolderCreateCounter(); },
                (key, oldCount) => { oldCount.Increment(); return oldCount; });
        }

        public void OnPlaceholderFolderExpanded(string relativePath)
        {
            this.GitIndexProjection.OnPlaceholderFolderExpanded(relativePath);
        }

        public void OnPlaceholderFileHydrated(string triggeringProcessImageFileName)
        {
            this.fileHydrationCount.AddOrUpdate(
                triggeringProcessImageFileName,
                (imageName) => { return new PlaceHolderCreateCounter(); },
                (key, oldCount) => { oldCount.Increment(); return oldCount; });
        }

        public FileProperties GetLogsHeadFileProperties()
        {
            // Use a temporary FileProperties in case another thread sets this.logsHeadFileProperties before this
            // method returns
            FileProperties properties = this.logsHeadFileProperties;
            if (properties == null)
            {
                try
                {
                    properties = this.context.FileSystem.GetFileProperties(this.logsHeadPath);
                    this.logsHeadFileProperties = properties;
                }
                catch (Exception e)
                {
                    EventMetadata metadata = this.CreateEventMetadata(relativePath: null, exception: e);
                    this.context.Tracer.RelatedWarning(metadata, "GetLogsHeadFileProperties: Exception thrown from GetFileProperties", Keywords.Telemetry);

                    properties = FileProperties.DefaultFile;

                    // Leave logsHeadFileProperties null to indicate that it is still needs to be refreshed
                    this.logsHeadFileProperties = null;
                }
            }

            return properties;
        }

        private static bool CheckConditionWithRetry(Func<bool> predicate, int retries, int millisecondsToSleep)
        {
            bool result = predicate();
            while (!result && retries > 0)
            {
                Thread.Sleep(millisecondsToSleep);
                result = predicate();
                --retries;
            }

            return result;
        }

        private EventMetadata GetProcessInteractionData(ConcurrentDictionary<string, PlaceHolderCreateCounter> collectedData, ref bool logToFile)
        {
            EventMetadata metadata = new EventMetadata();

            if (collectedData.Count > 0)
            {
                int count = 0;
                foreach (KeyValuePair<string, PlaceHolderCreateCounter> processCount in
                collectedData.OrderByDescending((KeyValuePair<string, PlaceHolderCreateCounter> kvp) => kvp.Value.Count).Take(10))
                {
                    ++count;
                    metadata.Add("ProcessName" + count, processCount.Key);
                    metadata.Add("ProcessCount" + count, processCount.Value.Count);
                }

                logToFile = true;
            }

            return metadata;
        }

        // Captures the current state of dictionary, and resets it
        // This approach is optimal for our use case to preserve all entries while avoiding additional locking
        private ConcurrentDictionary<string, PlaceHolderCreateCounter> GetAndResetProcessCountMetadata(ref ConcurrentDictionary<string, PlaceHolderCreateCounter> collectedData)
        {
            ConcurrentDictionary<string, PlaceHolderCreateCounter> localData = collectedData;
            collectedData = new ConcurrentDictionary<string, PlaceHolderCreateCounter>(GVFSPlatform.Instance.Constants.PathComparer);
            return localData;
        }

        private void InvalidateState(bool invalidateProjection, bool invalidateModifiedPaths)
        {
            if (invalidateProjection)
            {
                this.GitIndexProjection.InvalidateProjection();
            }

            if (invalidateModifiedPaths)
            {
                this.GitIndexProjection.InvalidateModifiedFiles();
                this.backgroundFileSystemTaskRunner.Enqueue(FileSystemTask.OnIndexWriteRequiringModifiedPathsValidation());
            }

            this.InvalidateGitStatusCache();
            this.newlyCreatedFileAndFolderPaths.Clear();
        }

        private bool GitCommandLeavesProjectionUnchanged(GitCommandLineParser gitCommand)
        {
            return
                gitCommand.IsVerb(LeavesProjectionUnchangedVerbs) ||
                gitCommand.IsResetSoftOrMixed() ||
                gitCommand.IsCheckoutWithFilePaths();
        }

        private bool GitCommandRequiresModifiedPathValidationAfterIndexChange(GitCommandLineParser gitCommand)
        {
            return
                gitCommand.IsVerb(GitCommandLineParser.Verbs.UpdateIndex) ||
                gitCommand.IsResetMixed();
        }

        private FileSystemTaskResult PreBackgroundOperation()
        {
            return this.GitIndexProjection.OpenIndexForRead();
        }

        private FileSystemTaskResult ExecuteBackgroundOperation(FileSystemTask gitUpdate)
        {
            EventMetadata metadata = new EventMetadata();

            FileSystemTaskResult result;

            switch (gitUpdate.Operation)
            {
                case FileSystemTask.OperationType.OnFileCreated:
                case FileSystemTask.OperationType.OnFailedPlaceholderDelete:
                case FileSystemTask.OperationType.OnFileSymLinkCreated:
                    metadata.Add("virtualPath", gitUpdate.VirtualPath);
                    result = this.AddModifiedPathAndRemoveFromPlaceholderList(gitUpdate.VirtualPath);
                    break;

                case FileSystemTask.OperationType.OnFileHardLinkCreated:
                    metadata.Add("virtualPath", gitUpdate.VirtualPath);
                    metadata.Add("oldVirtualPath", gitUpdate.OldVirtualPath);
                    result = FileSystemTaskResult.Success;
                    if (!string.IsNullOrEmpty(gitUpdate.OldVirtualPath) && !IsPathInsideDotGit(gitUpdate.OldVirtualPath))
                    {
                        result = this.AddModifiedPathAndRemoveFromPlaceholderList(gitUpdate.OldVirtualPath);
                    }

                    if ((result == FileSystemTaskResult.Success) &&
                        !string.IsNullOrEmpty(gitUpdate.VirtualPath) && !IsPathInsideDotGit(gitUpdate.VirtualPath))
                    {
                        result = this.AddModifiedPathAndRemoveFromPlaceholderList(gitUpdate.VirtualPath);
                    }

                    break;

                case FileSystemTask.OperationType.OnFileRenamed:
                    metadata.Add("oldVirtualPath", gitUpdate.OldVirtualPath);
                    metadata.Add("virtualPath", gitUpdate.VirtualPath);
                    result = FileSystemTaskResult.Success;
                    if (!string.IsNullOrEmpty(gitUpdate.OldVirtualPath) && !IsPathInsideDotGit(gitUpdate.OldVirtualPath))
                    {
                        if (this.newlyCreatedFileAndFolderPaths.Contains(gitUpdate.OldVirtualPath))
                        {
                            result = this.TryRemoveModifiedPath(gitUpdate.OldVirtualPath, isFolder: false);
                        }
                        else
                        {
                            result = this.AddModifiedPathAndRemoveFromPlaceholderList(gitUpdate.OldVirtualPath);
                        }
                    }

                    if (result == FileSystemTaskResult.Success &&
                        !string.IsNullOrEmpty(gitUpdate.VirtualPath) &&
                        !IsPathInsideDotGit(gitUpdate.VirtualPath))
                    {
                        result = this.AddModifiedPathAndRemoveFromPlaceholderList(gitUpdate.VirtualPath);
                    }

                    break;

                case FileSystemTask.OperationType.OnFilePreDelete:
                    // This code assumes that the current implementations of FileSystemVirtualizer will call either
                    // the PreDelete or the Delete not both so if a new implementation starts calling both
                    // this will need to be cleaned up to not duplicate the work that is being done.
                    metadata.Add("virtualPath", gitUpdate.VirtualPath);
                    if (this.newlyCreatedFileAndFolderPaths.Contains(gitUpdate.VirtualPath))
                    {
                        string fullPathToFile = Path.Combine(this.context.Enlistment.WorkingDirectoryRoot, gitUpdate.VirtualPath);

                        // Because this is a predelete message the file could still be on disk when we make this check
                        // so we retry for a limited time before deciding the delete didn't happen
                        bool fileDeleted = CheckConditionWithRetry(() => !this.context.FileSystem.FileExists(fullPathToFile), NumberOfRetriesCheckingForDeleted, MillisecondsToSleepBeforeCheckingForDeleted);
                        if (fileDeleted)
                        {
                            result = this.TryRemoveModifiedPath(gitUpdate.VirtualPath, isFolder: false);
                        }
                        else
                        {
                            result = FileSystemTaskResult.Success;
                        }
                    }
                    else
                    {
                        result = this.AddModifiedPathAndRemoveFromPlaceholderList(gitUpdate.VirtualPath);
                    }

                    break;

                case FileSystemTask.OperationType.OnFileDeleted:
                    // This code assumes that the current implementations of FileSystemVirtualizer will call either
                    // the PreDelete or the Delete not both so if a new implementation starts calling both
                    // this will need to be cleaned up to not duplicate the work that is being done.
                    metadata.Add("virtualPath", gitUpdate.VirtualPath);
                    if (this.newlyCreatedFileAndFolderPaths.Contains(gitUpdate.VirtualPath))
                    {
                        result = this.TryRemoveModifiedPath(gitUpdate.VirtualPath, isFolder: false);
                    }
                    else
                    {
                        result = this.AddModifiedPathAndRemoveFromPlaceholderList(gitUpdate.VirtualPath);
                    }

                    break;

                case FileSystemTask.OperationType.OnFileOverwritten:
                case FileSystemTask.OperationType.OnFileSuperseded:
                case FileSystemTask.OperationType.OnFileConvertedToFull:
                case FileSystemTask.OperationType.OnFailedPlaceholderUpdate:
                case FileSystemTask.OperationType.OnFailedFileHydration:
                    metadata.Add("virtualPath", gitUpdate.VirtualPath);
                    result = this.AddModifiedPathAndRemoveFromPlaceholderList(gitUpdate.VirtualPath);
                    break;

                case FileSystemTask.OperationType.OnFolderCreated:
                    metadata.Add("virtualPath", gitUpdate.VirtualPath);
                    result = this.TryAddModifiedPath(gitUpdate.VirtualPath, isFolder: true);

                    break;

                case FileSystemTask.OperationType.OnFolderRenamed:
                    result = FileSystemTaskResult.Success;
                    metadata.Add("oldVirtualPath", gitUpdate.OldVirtualPath);
                    metadata.Add("virtualPath", gitUpdate.VirtualPath);

                    if (!string.IsNullOrEmpty(gitUpdate.OldVirtualPath) &&
                        this.newlyCreatedFileAndFolderPaths.Contains(gitUpdate.OldVirtualPath))
                    {
                        result = this.TryRemoveModifiedPath(gitUpdate.OldVirtualPath, isFolder: true);
                    }

                    // An empty destination path means the folder was renamed to somewhere outside of the repo
                    // Note that only full folders can be moved\renamed, and so there will already be a recursive
                    // sparse-checkout entry for the virtualPath of the folder being moved (meaning that no
                    // additional work is needed for any files\folders inside the folder being moved)
                    if (result == FileSystemTaskResult.Success && !string.IsNullOrEmpty(gitUpdate.VirtualPath))
                    {
                        this.AddToNewlyCreatedList(gitUpdate.VirtualPath, isFolder: true);
                        result = this.TryAddModifiedPath(gitUpdate.VirtualPath, isFolder: true);
                        if (result == FileSystemTaskResult.Success)
                        {
                            Queue<string> relativeFolderPaths = new Queue<string>();
                            relativeFolderPaths.Enqueue(gitUpdate.VirtualPath);

                            // Remove old paths from modified paths if in the newly created list
                            while (relativeFolderPaths.Count > 0)
                            {
                                string folderPath = relativeFolderPaths.Dequeue();
                                if (result == FileSystemTaskResult.Success)
                                {
                                    try
                                    {
                                        foreach (DirectoryItemInfo itemInfo in this.context.FileSystem.ItemsInDirectory(Path.Combine(this.context.Enlistment.WorkingDirectoryRoot, folderPath)))
                                        {
                                            string itemVirtualPath = Path.Combine(folderPath, itemInfo.Name);
                                            string oldItemVirtualPath = gitUpdate.OldVirtualPath + itemVirtualPath.Substring(gitUpdate.VirtualPath.Length);

                                            this.AddToNewlyCreatedList(itemVirtualPath, isFolder: itemInfo.IsDirectory);
                                            if (this.newlyCreatedFileAndFolderPaths.Contains(oldItemVirtualPath))
                                            {
                                                result = this.TryRemoveModifiedPath(oldItemVirtualPath, isFolder: itemInfo.IsDirectory);
                                            }

                                            if (itemInfo.IsDirectory)
                                            {
                                                relativeFolderPaths.Enqueue(itemVirtualPath);
                                            }
                                        }
                                    }
                                    catch (DirectoryNotFoundException)
                                    {
                                        // DirectoryNotFoundException can occur when the renamed folder (or one of its children) is
                                        // deleted prior to the background thread running
                                        EventMetadata exceptionMetadata = new EventMetadata();
                                        exceptionMetadata.Add("Area", "ExecuteBackgroundOperation");
                                        exceptionMetadata.Add("Operation", gitUpdate.Operation.ToString());
                                        exceptionMetadata.Add("oldVirtualPath", gitUpdate.OldVirtualPath);
                                        exceptionMetadata.Add("virtualPath", gitUpdate.VirtualPath);
                                        exceptionMetadata.Add(TracingConstants.MessageKey.InfoMessage, "DirectoryNotFoundException while traversing folder path");
                                        exceptionMetadata.Add("folderPath", folderPath);
                                        this.context.Tracer.RelatedEvent(EventLevel.Informational, "DirectoryNotFoundWhileUpdatingModifiedPaths", exceptionMetadata);
                                    }
                                    catch (IOException e)
                                    {
                                        metadata.Add("Details", "IOException while traversing folder path");
                                        metadata.Add("folderPath", folderPath);
                                        metadata.Add("Exception", e.ToString());
                                        result = FileSystemTaskResult.RetryableError;
                                        break;
                                    }
                                    catch (UnauthorizedAccessException e)
                                    {
                                        metadata.Add("Details", "UnauthorizedAccessException while traversing folder path");
                                        metadata.Add("folderPath", folderPath);
                                        metadata.Add("Exception", e.ToString());
                                        result = FileSystemTaskResult.RetryableError;
                                        break;
                                    }
                                }
                                else
                                {
                                    break;
                                }
                            }
                        }
                    }

                    break;

                case FileSystemTask.OperationType.OnFolderPreDelete:
                    // This code assumes that the current implementations of FileSystemVirtualizer will call either
                    // the PreDelete or the Delete not both so if a new implementation starts calling both
                    // this will need to be cleaned up to not duplicate the work that is being done.
                    metadata.Add("virtualPath", gitUpdate.VirtualPath);
                    if (this.newlyCreatedFileAndFolderPaths.Contains(gitUpdate.VirtualPath))
                    {
                        string fullPathToFolder = Path.Combine(this.context.Enlistment.WorkingDirectoryRoot, gitUpdate.VirtualPath);

                        // Because this is a predelete message the file could still be on disk when we make this check
                        // so we retry for a limited time before deciding the delete didn't happen
                        bool folderDeleted = CheckConditionWithRetry(() => !this.context.FileSystem.DirectoryExists(fullPathToFolder), NumberOfRetriesCheckingForDeleted, MillisecondsToSleepBeforeCheckingForDeleted);
                        if (folderDeleted)
                        {
                            result = this.TryRemoveModifiedPath(gitUpdate.VirtualPath, isFolder: true);
                        }
                        else
                        {
                            result = FileSystemTaskResult.Success;
                        }
                    }
                    else
                    {
                        result = this.TryAddModifiedPath(gitUpdate.VirtualPath, isFolder: true);
                    }

                    break;

                case FileSystemTask.OperationType.OnFolderDeleted:
                    // This code assumes that the current implementations of FileSystemVirtualizer will call either
                    // the PreDelete or the Delete not both so if a new implementation starts calling both
                    // this will need to be cleaned up to not duplicate the work that is being done.
                    metadata.Add("virtualPath", gitUpdate.VirtualPath);
                    if (this.newlyCreatedFileAndFolderPaths.Contains(gitUpdate.VirtualPath))
                    {
                        result = this.TryRemoveModifiedPath(gitUpdate.VirtualPath, isFolder: true);
                    }
                    else
                    {
                        result = this.TryAddModifiedPath(gitUpdate.VirtualPath, isFolder: true);
                    }

                    break;

                case FileSystemTask.OperationType.OnFolderFirstWrite:
                    result = FileSystemTaskResult.Success;
                    break;

                case FileSystemTask.OperationType.OnIndexWriteRequiringModifiedPathsValidation:
                    result = this.GitIndexProjection.AddMissingModifiedFiles();
                    break;

                case FileSystemTask.OperationType.OnPlaceholderCreationsBlockedForGit:
                    this.GitIndexProjection.ClearNegativePathCacheIfPollutedByGit();
                    result = FileSystemTaskResult.Success;
                    break;

                default:
                    throw new InvalidOperationException("Invalid background operation");
            }

            if (result != FileSystemTaskResult.Success)
            {
                metadata.Add("Area", "ExecuteBackgroundOperation");
                metadata.Add("Operation", gitUpdate.Operation.ToString());
                metadata.Add(TracingConstants.MessageKey.WarningMessage, "Background operation failed");
                metadata.Add(nameof(result), result.ToString());
                this.context.Tracer.RelatedEvent(EventLevel.Warning, "FailedBackgroundOperation", metadata);
            }

            return result;
        }

        private void AddToNewlyCreatedList(string virtualPath, bool isFolder)
        {
            if (!this.modifiedPaths.Contains(virtualPath, isFolder))
            {
                this.newlyCreatedFileAndFolderPaths.Add(virtualPath);
            }
        }

        private FileSystemTaskResult TryRemoveModifiedPath(string virtualPath, bool isFolder)
        {
            if (!this.modifiedPaths.TryRemove(virtualPath, isFolder, out bool isRetryable))
            {
                return isRetryable ? FileSystemTaskResult.RetryableError : FileSystemTaskResult.FatalError;
            }

            this.newlyCreatedFileAndFolderPaths.TryRemove(virtualPath);

            this.InvalidateGitStatusCache();
            return FileSystemTaskResult.Success;
        }

        private FileSystemTaskResult TryAddModifiedPath(string virtualPath, bool isFolder)
        {
            if (!this.modifiedPaths.TryAdd(virtualPath, isFolder, out bool isRetryable))
            {
                return isRetryable ? FileSystemTaskResult.RetryableError : FileSystemTaskResult.FatalError;
            }

            this.InvalidateGitStatusCache();
            return FileSystemTaskResult.Success;
        }

        private FileSystemTaskResult AddModifiedPathAndRemoveFromPlaceholderList(string virtualPath)
        {
            FileSystemTaskResult result = this.TryAddModifiedPath(virtualPath, isFolder: false);
            if (result != FileSystemTaskResult.Success)
            {
                return result;
            }

            bool isFolder;
            string fileName;

            // We don't want to fill the placeholder list with deletes for files that are
            // not in the projection so we make sure it is in the projection before removing.
            if (this.GitIndexProjection.IsPathProjected(virtualPath, out fileName, out isFolder))
            {
                this.GitIndexProjection.RemoveFromPlaceholderList(virtualPath);
            }

            return result;
        }

        private FileSystemTaskResult PostBackgroundOperation()
        {
            this.modifiedPaths.WriteAllEntriesAndFlush();
            this.gitStatusCache.RefreshAsynchronously();
            return this.GitIndexProjection.CloseIndex();
        }

        private EventMetadata CreateEventMetadata(
            string relativePath = null,
            Exception exception = null)
        {
            EventMetadata metadata = new EventMetadata();
            metadata.Add("Area", EtwArea);

            if (relativePath != null)
            {
                metadata.Add(nameof(relativePath), relativePath);
            }

            if (exception != null)
            {
                metadata.Add("Exception", exception.ToString());
            }

            return metadata;
        }

        private class PlaceHolderCreateCounter
        {
            private long count;

            public PlaceHolderCreateCounter()
            {
                this.count = 1;
            }

            public long Count
            {
                get { return this.count; }
            }

            public void Increment()
            {
                Interlocked.Increment(ref this.count);
            }
        }
    }
}
