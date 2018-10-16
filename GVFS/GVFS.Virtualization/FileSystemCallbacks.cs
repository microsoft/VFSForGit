using GVFS.Common;
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
        private const string PostFetchLock = "post-fetch.lock";
        private const string PostFetchTelemetryKey = "post-fetch";

        private static readonly GitCommandLineParser.Verbs LeavesProjectionUnchangedVerbs =
            GitCommandLineParser.Verbs.AddOrStage |
            GitCommandLineParser.Verbs.Commit |
            GitCommandLineParser.Verbs.Status |
            GitCommandLineParser.Verbs.UpdateIndex;

        private readonly string logsHeadPath;

        private GVFSContext context;
        private GVFSGitObjects gitObjects;
        private ModifiedPathsDatabase modifiedPaths;
        private ConcurrentHashSet<string> newlyCreatedFileAndFolderPaths;
        private ConcurrentDictionary<string, PlaceHolderCreateCounter> placeHolderCreationCount;
        private BackgroundFileSystemTaskRunner backgroundFileSystemTaskRunner;
        private FileSystemVirtualizer fileSystemVirtualizer;
        private FileProperties logsHeadFileProperties;
        private Thread postFetchJobThread;
        private object postFetchJobLock;
        private bool stopping;

        private GitStatusCache gitStatusCache;
        private bool enableGitStatusCache;

        public FileSystemCallbacks(GVFSContext context, GVFSGitObjects gitObjects, RepoMetadata repoMetadata, FileSystemVirtualizer fileSystemVirtualizer, GitStatusCache gitStatusCache)
            : this(
                  context,
                  gitObjects,
                  repoMetadata,
                  new BlobSizes(context.Enlistment.BlobSizesRoot, context.FileSystem, context.Tracer),
                  gitIndexProjection: null,
                  backgroundFileSystemTaskRunner: null,
                  fileSystemVirtualizer: fileSystemVirtualizer,
                  gitStatusCache: gitStatusCache)
        {
        }

        public FileSystemCallbacks(
            GVFSContext context,
            GVFSGitObjects gitObjects,
            RepoMetadata repoMetadata,
            BlobSizes blobSizes,
            GitIndexProjection gitIndexProjection,
            BackgroundFileSystemTaskRunner backgroundFileSystemTaskRunner,
            FileSystemVirtualizer fileSystemVirtualizer,
            GitStatusCache gitStatusCache = null)
        {
            this.logsHeadFileProperties = null;
            this.postFetchJobLock = new object();

            this.context = context;
            this.gitObjects = gitObjects;
            this.fileSystemVirtualizer = fileSystemVirtualizer;

            this.placeHolderCreationCount = new ConcurrentDictionary<string, PlaceHolderCreateCounter>(StringComparer.OrdinalIgnoreCase);
            this.newlyCreatedFileAndFolderPaths = new ConcurrentHashSet<string>(StringComparer.OrdinalIgnoreCase);

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

            this.BlobSizes = blobSizes;
            this.BlobSizes.Initialize();

            PlaceholderListDatabase placeholders;
            if (!PlaceholderListDatabase.TryCreate(
                this.context.Tracer,
                Path.Combine(this.context.Enlistment.DotGVFSRoot, GVFSConstants.DotGVFS.Databases.PlaceholderList),
                this.context.FileSystem,
                out placeholders,
                out error))
            {
                throw new InvalidRepoException(error);
            }

            this.GitIndexProjection = gitIndexProjection ?? new GitIndexProjection(
                context,
                gitObjects,
                this.BlobSizes,
                repoMetadata,
                fileSystemVirtualizer,
                placeholders,
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

            this.logsHeadPath = Path.Combine(this.context.Enlistment.WorkingDirectoryRoot, GVFSConstants.DotGit.Logs.Head);

            EventMetadata metadata = new EventMetadata();
            metadata.Add("placeholders.Count", placeholders.EstimatedCount);
            metadata.Add("background.Count", this.backgroundFileSystemTaskRunner.Count);
            metadata.Add(TracingConstants.MessageKey.InfoMessage, $"{nameof(FileSystemCallbacks)} created");
            this.context.Tracer.RelatedEvent(EventLevel.Informational, $"{nameof(FileSystemCallbacks)}_Constructor", metadata);
        }

        public IProfilerOnlyIndexProjection GitIndexProjectionProfiler
        {
            get { return this.GitIndexProjection; }
        }

        public int BackgroundOperationCount
        {
            get { return this.backgroundFileSystemTaskRunner.Count; }
        }

        public BlobSizes BlobSizes { get; private set; }

        public GitIndexProjection GitIndexProjection { get; private set; }

        public bool IsMounted { get; private set; }

        /// <summary>
        /// Returns true for paths that begin with ".git\" (regardless of case)
        /// </summary>
        public static bool IsPathInsideDotGit(string relativePath)
        {
            return relativePath.StartsWith(GVFSConstants.DotGit.Root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
        }

        public bool TryStart(out string error)
        {
            this.modifiedPaths.RemoveEntriesWithParentFolderEntry(this.context.Tracer);
            this.modifiedPaths.WriteAllEntriesAndFlush();

            if (!this.fileSystemVirtualizer.TryStart(this, out error))
            {
                return false;
            }

            this.GitIndexProjection.Initialize(this.backgroundFileSystemTaskRunner);

            if (this.enableGitStatusCache)
            {
                this.gitStatusCache.Initialize();
            }

            this.backgroundFileSystemTaskRunner.Start();

            this.IsMounted = true;
            return true;
        }

        public void Stop()
        {
            this.stopping = true;
            lock (this.postFetchJobLock)
            {
                // TODO(Mac): System.PlatformNotSupportedException: Thread abort is not supported on this platform
                this.postFetchJobThread?.Abort();
            }

            // Shutdown the GitStatusCache before other
            // components that it depends on.
            this.gitStatusCache.Shutdown();

            this.fileSystemVirtualizer.PrepareToStop();
            this.backgroundFileSystemTaskRunner.Shutdown();
            this.GitIndexProjection.Shutdown();
            this.BlobSizes.Shutdown();
            this.fileSystemVirtualizer.Stop();
            this.IsMounted = false;
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

            if (this.context != null)
            {
                this.context.Dispose();
                this.context = null;
            }
        }

        public bool IsReadyForExternalAcquireLockRequests(NamedPipeMessages.LockData requester, out string denyMessage)
        {
            if (!this.IsMounted)
            {
                denyMessage = "Waiting for mount to complete";
                return false;
            }

            if (this.BackgroundOperationCount != 0)
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

        public EventMetadata GetMetadataForHeartBeat(ref EventLevel eventLevel)
        {
            EventMetadata metadata = new EventMetadata();
            if (this.placeHolderCreationCount.Count > 0)
            {
                ConcurrentDictionary<string, PlaceHolderCreateCounter> collectedData = this.placeHolderCreationCount;
                this.placeHolderCreationCount = new ConcurrentDictionary<string, PlaceHolderCreateCounter>(StringComparer.OrdinalIgnoreCase);

                int count = 0;
                foreach (KeyValuePair<string, PlaceHolderCreateCounter> processCount in
                    collectedData.OrderByDescending((KeyValuePair<string, PlaceHolderCreateCounter> kvp) => kvp.Value.Count))
                {
                    ++count;
                    if (count > 10)
                    {
                        break;
                    }

                    metadata.Add("ProcessName" + count, processCount.Key);
                    metadata.Add("ProcessCount" + count, processCount.Value.Count);
                }

                eventLevel = EventLevel.Informational;
            }

            metadata.Add("ModifiedPathsCount", this.modifiedPaths.Count);
            metadata.Add("PlaceholderCount", this.GitIndexProjection.EstimatedPlaceholderCount);
            if (this.gitStatusCache.WriteTelemetryandReset(metadata))
            {
                eventLevel = EventLevel.Informational;
            }

            metadata.Add(nameof(RepoMetadata.Instance.EnlistmentId), RepoMetadata.Instance.EnlistmentId);

            return metadata;
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
                this.GitIndexProjection.InvalidateProjection();
                this.InvalidateGitStatusCache();

                // But this isn't something we expect to see, so log a warning
                EventMetadata metadata = new EventMetadata
                {
                    { "Area", EtwArea },
                    { TracingConstants.MessageKey.WarningMessage, "Index modified without git holding GVFS lock" },
                };

                this.context.Tracer.RelatedEvent(EventLevel.Warning, $"{nameof(this.OnIndexFileChange)}_NoLock", metadata);
            }
            else if (this.GitCommandLeavesProjectionUnchanged(gitCommand))
            {
                this.GitIndexProjection.InvalidateModifiedFiles();
                this.backgroundFileSystemTaskRunner.Enqueue(FileSystemTask.OnIndexWriteWithoutProjectionChange());
                this.InvalidateGitStatusCache();
            }
            else
            {
                this.GitIndexProjection.InvalidateProjection();
                this.InvalidateGitStatusCache();
            }

            this.newlyCreatedFileAndFolderPaths.Clear();
        }

        public void InvalidateGitStatusCache()
        {
            this.gitStatusCache.Invalidate();

            // If there are background tasks queued up, then it will be
            // refreshed after they have been processed.
            if (this.backgroundFileSystemTaskRunner.Count == 0)
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
            this.newlyCreatedFileAndFolderPaths.Add(relativePath);
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

        public virtual void OnFileRenamed(string oldRelativePath, string newRelativePath)
        {
            this.backgroundFileSystemTaskRunner.Enqueue(FileSystemTask.OnFileRenamed(oldRelativePath, newRelativePath));
        }

        public virtual void OnFileHardLinkCreated(string newLinkRelativePath)
        {
            this.backgroundFileSystemTaskRunner.Enqueue(FileSystemTask.OnFileHardLinkCreated(newLinkRelativePath));
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

        public void OnFolderCreated(string relativePath)
        {
            this.newlyCreatedFileAndFolderPaths.Add(relativePath);
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
            this.placeHolderCreationCount.AddOrUpdate(
                triggeringProcessImageFileName,
                (imageName) => { return new PlaceHolderCreateCounter(); },
                (key, oldCount) => { oldCount.Increment(); return oldCount; });
        }

        public void OnPlaceholderCreateBlockedForGit()
        {
            this.GitIndexProjection.OnPlaceholderCreateBlockedForGit();
        }

        public void OnPlaceholderFolderCreated(string relativePath)
        {
            this.GitIndexProjection.OnPlaceholderFolderCreated(relativePath);
        }

        public void OnPlaceholderFolderExpanded(string relativePath)
        {
            this.GitIndexProjection.OnPlaceholderFolderExpanded(relativePath);
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

        public void LaunchPostFetchJob(List<string> packIndexes)
        {
            lock (this.postFetchJobLock)
            {
                if (this.postFetchJobThread?.IsAlive == true)
                {
                    this.context.Tracer.RelatedWarning("Dropping post-fetch job since previous job is still running");
                    return;
                }

                if (this.stopping)
                {
                    this.context.Tracer.RelatedWarning("Dropping post-fetch job since attempting to unmount");
                    return;
                }

                this.postFetchJobThread = new Thread(() => this.PostFetchJob(packIndexes));
                this.postFetchJobThread.IsBackground = true;
                this.postFetchJobThread.Start();
            }
        }

        private void PostFetchJob(List<string> packIndexes)
        {
            try
            {
                using (FileBasedLock postFetchFileLock = GVFSPlatform.Instance.CreateFileBasedLock(
                    this.context.FileSystem,
                    this.context.Tracer,
                    Path.Combine(this.context.Enlistment.GitObjectsRoot, PostFetchLock)))
                {
                    if (!postFetchFileLock.TryAcquireLock())
                    {
                        this.context.Tracer.RelatedInfo(PostFetchTelemetryKey + ": Skipping post-fetch work since another process holds the lock");
                        return;
                    }

                    if (!this.gitObjects.TryWriteMultiPackIndex(this.context.Tracer, this.context.Enlistment, this.context.FileSystem))
                    {
                        this.context.Tracer.RelatedWarning(
                            metadata: null,
                            message: PostFetchTelemetryKey + ": Failed to generate midx for new packfiles",
                            keywords: Keywords.Telemetry);
                    }

                    if (packIndexes == null || packIndexes.Count == 0)
                    {
                        this.context.Tracer.RelatedInfo(PostFetchTelemetryKey + ": Skipping commit-graph write due to no new packfiles");
                        return;
                    }

                    using (ITracer activity = this.context.Tracer.StartActivity("TryWriteGitCommitGraph", EventLevel.Informational, Keywords.Telemetry, metadata: null))
                    {
                        GitProcess process = new GitProcess(this.context.Enlistment);
                        GitProcess.Result result = process.WriteCommitGraph(this.context.Enlistment.GitObjectsRoot, packIndexes);

                        if (result.HasErrors)
                        {
                            this.context.Tracer.RelatedWarning(
                                metadata: null,
                                message: PostFetchTelemetryKey + ": Failed to generate commit-graph for new packfiles:" + result.Errors,
                                keywords: Keywords.Telemetry);
                            return;
                        }
                    }
                }
            }
            catch (ThreadAbortException)
            {
                this.context.Tracer.RelatedInfo("Aborting post-fetch job due to ThreadAbortException");
            }
            catch (IOException e)
            {
                this.context.Tracer.RelatedWarning(
                    metadata: this.CreateEventMetadata(null, e),
                    message: PostFetchTelemetryKey + ": IOException while running post-fetch job: " + e.Message,
                    keywords: Keywords.Telemetry);
            }
            catch (Exception e)
            {
                this.context.Tracer.RelatedError(
                    metadata: this.CreateEventMetadata(null, e),
                    message: PostFetchTelemetryKey + ": Exception while running post-fetch job: " + e.Message,
                    keywords: Keywords.Telemetry);
                Environment.Exit((int)ReturnCode.GenericError);
            }
        }

        private bool GitCommandLeavesProjectionUnchanged(GitCommandLineParser gitCommand)
        {
            return
                gitCommand.IsVerb(LeavesProjectionUnchangedVerbs) ||
                gitCommand.IsResetSoftOrMixed() ||
                gitCommand.IsCheckoutWithFilePaths();
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
                case FileSystemTask.OperationType.OnFileHardLinkCreated:
                case FileSystemTask.OperationType.OnFileSymLinkCreated:
                    metadata.Add("virtualPath", gitUpdate.VirtualPath);
                    result = this.AddModifiedPathAndRemoveFromPlaceholderList(gitUpdate.VirtualPath);
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
                        string fullPathToFolder = Path.Combine(this.context.Enlistment.WorkingDirectoryRoot, gitUpdate.VirtualPath);
                        if (!this.context.FileSystem.FileExists(fullPathToFolder))
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
                        result = this.TryAddModifiedPath(gitUpdate.VirtualPath, isFolder: true);
                        if (result == FileSystemTaskResult.Success)
                        {
                            this.newlyCreatedFileAndFolderPaths.Add(gitUpdate.VirtualPath);

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

                                            this.newlyCreatedFileAndFolderPaths.Add(itemVirtualPath);
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
                        if (!this.context.FileSystem.DirectoryExists(fullPathToFolder))
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

                case FileSystemTask.OperationType.OnIndexWriteWithoutProjectionChange:
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
