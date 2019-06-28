using GVFS.Common;
using GVFS.Common.Database;
using GVFS.Common.Git;
using GVFS.Common.Http;
using GVFS.Common.NamedPipes;
using GVFS.Common.Tracing;
using GVFS.Virtualization.Background;
using GVFS.Virtualization.BlobSize;
using GVFS.Virtualization.FileSystem;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace GVFS.Virtualization.Projection
{
    public partial class GitIndexProjection : IDisposable, IProfilerOnlyIndexProjection
    {
        public const string ProjectionIndexBackupName = "GVFS_projection";

        public static readonly ushort FileMode755 = Convert.ToUInt16("755", 8);
        public static readonly ushort FileMode664 = Convert.ToUInt16("664", 8);
        public static readonly ushort FileMode644 = Convert.ToUInt16("644", 8);

        private const int IndexFileStreamBufferSize = 512 * 1024;

        private const UpdatePlaceholderType FolderPlaceholderDeleteFlags =
            UpdatePlaceholderType.AllowDirtyMetadata |
            UpdatePlaceholderType.AllowReadOnly |
            UpdatePlaceholderType.AllowTombstone;

        private const UpdatePlaceholderType FilePlaceholderUpdateFlags =
            UpdatePlaceholderType.AllowDirtyMetadata |
            UpdatePlaceholderType.AllowReadOnly;

        private const string EtwArea = "GitIndexProjection";

        private GVFSContext context;
        private RepoMetadata repoMetadata;
        private FileSystemVirtualizer fileSystemVirtualizer;
        private ModifiedPathsDatabase modifiedPaths;

        private FolderData rootFolderData = new FolderData();
        private GitIndexParser indexParser;

        // Cache of folder paths (in Windows format) to folder data
        private ConcurrentDictionary<string, FolderData> projectionFolderCache = new ConcurrentDictionary<string, FolderData>(GVFSPlatform.Instance.Constants.PathComparer);

        // nonDefaultFileTypesAndModes is only populated when the platform supports file mode
        // On platforms that support file modes, file paths that are not in nonDefaultFileTypesAndModes are regular files with mode 644
        private Dictionary<string, FileTypeAndMode> nonDefaultFileTypesAndModes = new Dictionary<string, FileTypeAndMode>(GVFSPlatform.Instance.Constants.PathComparer);

        private BlobSizes blobSizes;
        private PlaceholderListDatabase placeholderList;
        private GVFSGitObjects gitObjects;
        private BackgroundFileSystemTaskRunner backgroundFileSystemTaskRunner;
        private ReaderWriterLockSlim projectionReadWriteLock;
        private ManualResetEventSlim projectionParseComplete;

        private volatile bool projectionInvalid;

        // Number of times that the negative path cache has (potentially) been updated by GVFS preventing
        // git from creating a placeholder (since the last time the cache was cleared)
        private int negativePathCacheUpdatedForGitCount;

        // modifiedFilesInvalid: If true, a change to the index that did not trigger a new projection
        // has been made and GVFS has not yet validated that all entries whose skip-worktree bit is
        // cleared are in the ModifiedFilesDatabase
        private volatile bool modifiedFilesInvalid;

        private ConcurrentHashSet<string> updatePlaceholderFailures;
        private ConcurrentHashSet<string> deletePlaceholderFailures;

        private string projectionIndexBackupPath;
        private string indexPath;

        private FileStream indexFileStream;

        private AutoResetEvent wakeUpIndexParsingThread;
        private Task indexParsingThread;
        private bool isStopping;

        public GitIndexProjection(
            GVFSContext context,
            GVFSGitObjects gitObjects,
            BlobSizes blobSizes,
            RepoMetadata repoMetadata,
            FileSystemVirtualizer fileSystemVirtualizer,
            PlaceholderListDatabase placeholderList,
            ModifiedPathsDatabase modifiedPaths)
        {
            this.context = context;
            this.gitObjects = gitObjects;
            this.blobSizes = blobSizes;
            this.repoMetadata = repoMetadata;
            this.fileSystemVirtualizer = fileSystemVirtualizer;
            this.indexParser = new GitIndexParser(this);

            this.projectionReadWriteLock = new ReaderWriterLockSlim();
            this.projectionParseComplete = new ManualResetEventSlim(initialState: false);
            this.wakeUpIndexParsingThread = new AutoResetEvent(initialState: false);
            this.projectionIndexBackupPath = Path.Combine(this.context.Enlistment.DotGVFSRoot, ProjectionIndexBackupName);
            this.indexPath = Path.Combine(this.context.Enlistment.WorkingDirectoryBackingRoot, GVFSConstants.DotGit.Index);
            this.placeholderList = placeholderList;
            this.modifiedPaths = modifiedPaths;
        }

        // For Unit Testing
        protected GitIndexProjection()
        {
        }

        public enum FileType : short
        {
            Invalid,

            Regular,
            SymLink,
            GitLink,
        }

        public int PlaceholderCount
        {
            get
            {
                return this.placeholderList.Count;
            }
        }

        public static void ReadIndex(ITracer tracer, string indexPath)
        {
            using (FileStream indexStream = new FileStream(indexPath, FileMode.Open, FileAccess.ReadWrite, FileShare.Read, IndexFileStreamBufferSize))
            {
                GitIndexParser.ValidateIndex(tracer, indexStream);
            }
        }

        /// <summary>
        /// Force the index file to be parsed and a new projection collection to be built.
        /// This method should only be used to measure index parsing performance.
        /// </summary>
        void IProfilerOnlyIndexProjection.ForceRebuildProjection()
        {
            this.CopyIndexFileAndBuildProjection();
        }

        /// <summary>
        /// Force the index file to be parsed to add missing paths to the modified paths database.
        /// This method should only be used to measure index parsing performance.
        /// </summary>
        void IProfilerOnlyIndexProjection.ForceAddMissingModifiedPaths(ITracer tracer)
        {
            using (FileStream indexStream = new FileStream(this.indexPath, FileMode.Open, FileAccess.ReadWrite, FileShare.Read, IndexFileStreamBufferSize))
            {
                // Not checking the FileSystemTaskResult here because this is only for profiling
                this.indexParser.AddMissingModifiedFilesAndRemoveThemFromPlaceholderList(tracer, indexStream);
            }
        }

        public void BuildProjectionFromPath(ITracer tracer, string indexPath)
        {
            using (FileStream indexStream = new FileStream(indexPath, FileMode.Open, FileAccess.ReadWrite, FileShare.Read, IndexFileStreamBufferSize))
            {
                this.indexParser.RebuildProjection(tracer, indexStream);
            }
        }

        public virtual void Initialize(BackgroundFileSystemTaskRunner backgroundFileSystemTaskRunner)
        {
            if (!File.Exists(this.indexPath))
            {
                string message = "GVFS requires the .git\\index to exist";
                EventMetadata metadata = CreateEventMetadata();
                this.context.Tracer.RelatedError(metadata, message);
                throw new FileNotFoundException(message);
            }

            this.backgroundFileSystemTaskRunner = backgroundFileSystemTaskRunner;

            this.projectionReadWriteLock.EnterWriteLock();
            try
            {
                this.projectionInvalid = this.repoMetadata.GetProjectionInvalid();

                if (!this.context.FileSystem.FileExists(this.projectionIndexBackupPath) || this.projectionInvalid)
                {
                    this.CopyIndexFileAndBuildProjection();
                }
                else
                {
                    this.BuildProjection();
                }
            }
            finally
            {
                this.projectionReadWriteLock.ExitWriteLock();
            }

            this.ClearUpdatePlaceholderErrors();
            if (this.repoMetadata.GetPlaceholdersNeedUpdate())
            {
                this.UpdatePlaceholders();
            }

            // If somehow something invalidated the projection while we were initializing, the parsing thread will
            // pick it up and parse again
            if (!this.projectionInvalid)
            {
                this.projectionParseComplete.Set();
            }

            this.indexParsingThread = Task.Factory.StartNew(this.ParseIndexThreadMain, TaskCreationOptions.LongRunning);
        }

        public virtual void Shutdown()
        {
            this.isStopping = true;
            this.wakeUpIndexParsingThread.Set();
            this.indexParsingThread.Wait();
        }

        public void WaitForProjectionUpdate()
        {
            this.projectionParseComplete.Wait();
        }

        public NamedPipeMessages.ReleaseLock.Response TryReleaseExternalLock(int pid)
        {
            NamedPipeMessages.LockData externalHolder = this.context.Repository.GVFSLock.GetExternalHolder();
            if (externalHolder != null &&
                externalHolder.PID == pid)
            {
                // We MUST NOT release the lock until all processing has been completed, so that once
                // control returns to the user, the projection is in a consistent state

                this.context.Tracer.RelatedEvent(EventLevel.Informational, "ReleaseExternalLockRequested", null);
                this.context.Repository.GVFSLock.Stats.RecordReleaseExternalLockRequested();

                this.ClearNegativePathCacheIfPollutedByGit();

                ConcurrentHashSet<string> updateFailures = this.updatePlaceholderFailures;
                ConcurrentHashSet<string> deleteFailures = this.deletePlaceholderFailures;
                this.ClearUpdatePlaceholderErrors();

                if (this.context.Repository.GVFSLock.ReleaseLockHeldByExternalProcess(pid))
                {
                    if (updateFailures.Count > 0 || deleteFailures.Count > 0)
                    {
                        return new NamedPipeMessages.ReleaseLock.Response(
                            NamedPipeMessages.ReleaseLock.SuccessResult,
                            new NamedPipeMessages.ReleaseLock.ReleaseLockData(
                                new List<string>(updateFailures),
                                new List<string>(deleteFailures)));
                    }

                    return new NamedPipeMessages.ReleaseLock.Response(NamedPipeMessages.ReleaseLock.SuccessResult);
                }
            }

            this.context.Tracer.RelatedError("GitIndexProjection: Received a release request from a process that does not own the lock (PID={0})", pid);
            return new NamedPipeMessages.ReleaseLock.Response(NamedPipeMessages.ReleaseLock.FailureResult);
        }

        public virtual bool IsProjectionParseComplete()
        {
            return this.projectionParseComplete.IsSet;
        }

        public virtual void InvalidateProjection()
        {
            this.context.Tracer.RelatedEvent(EventLevel.Informational, "InvalidateProjection", null);

            this.projectionParseComplete.Reset();

            try
            {
                // Because the projection is now invalid, attempt to delete the projection file.  If this delete fails
                // replacing the projection will be handled by the parsing thread
                this.context.FileSystem.DeleteFile(this.projectionIndexBackupPath);
            }
            catch (Exception e)
            {
                EventMetadata metadata = CreateEventMetadata(e);
                metadata.Add(TracingConstants.MessageKey.InfoMessage, nameof(this.InvalidateProjection) + ": Failed to delete GVFS_Projection file");
                this.context.Tracer.RelatedEvent(EventLevel.Informational, nameof(this.InvalidateProjection) + "_FailedToDeleteProjection", metadata);
            }

            this.SetProjectionAndPlaceholdersAsInvalid();
            this.wakeUpIndexParsingThread.Set();
        }

        public void InvalidateModifiedFiles()
        {
            this.context.Tracer.RelatedEvent(EventLevel.Informational, "ModifiedFilesInvalid", null);
            this.modifiedFilesInvalid = true;
        }

        public void OnPlaceholderCreateBlockedForGit()
        {
            int count = Interlocked.Increment(ref this.negativePathCacheUpdatedForGitCount);
            if (count == 1)
            {
                // If placeholder creation is blocked multiple times, only queue a single background task
                this.backgroundFileSystemTaskRunner.Enqueue(FileSystemTask.OnPlaceholderCreationsBlockedForGit());
            }
        }

        public void ClearNegativePathCacheIfPollutedByGit()
        {
            int count = Interlocked.Exchange(ref this.negativePathCacheUpdatedForGitCount, 0);
            if (count > 0)
            {
                this.ClearNegativePathCache();
            }
        }

        public void OnPlaceholderFolderCreated(string virtualPath)
        {
            this.placeholderList.AddPartialFolder(virtualPath);
        }

        public void OnPossibleTombstoneFolderCreated(string virtualPath)
        {
            this.placeholderList.AddPossibleTombstoneFolder(virtualPath);
        }

        public virtual void OnPlaceholderFolderExpanded(string relativePath)
        {
            this.placeholderList.AddExpandedFolder(relativePath);
        }

        public virtual void OnPlaceholderFileCreated(string virtualPath, string sha)
        {
            this.placeholderList.AddFile(virtualPath, sha);
        }

        public virtual bool TryGetProjectedItemsFromMemory(string folderPath, out List<ProjectedFileInfo> projectedItems)
        {
            projectedItems = null;

            this.projectionReadWriteLock.EnterReadLock();

            try
            {
                FolderData folderData;
                if (this.TryGetOrAddFolderDataFromCache(folderPath, out folderData))
                {
                    if (folderData.ChildrenHaveSizes)
                    {
                        projectedItems = ConvertToProjectedFileInfos(folderData.ChildEntries);
                        return true;
                    }
                }

                return false;
            }
            finally
            {
                this.projectionReadWriteLock.ExitReadLock();
            }
        }

        public virtual void GetFileTypeAndMode(string filePath, out FileType fileType, out ushort fileMode)
        {
            if (!GVFSPlatform.Instance.FileSystem.SupportsFileMode)
            {
                throw new InvalidOperationException($"{nameof(this.GetFileTypeAndMode)} is only supported on GVFSPlatforms that support file mode");
            }

            fileType = FileType.Regular;
            fileMode = FileMode644;

            this.projectionReadWriteLock.EnterReadLock();

            try
            {
                FileTypeAndMode fileTypeAndMode;
                if (this.nonDefaultFileTypesAndModes.TryGetValue(filePath, out fileTypeAndMode))
                {
                    fileType = fileTypeAndMode.Type;
                    fileMode = fileTypeAndMode.Mode;
                }
            }
            finally
            {
                this.projectionReadWriteLock.ExitReadLock();
            }
        }

        public virtual List<ProjectedFileInfo> GetProjectedItems(
            CancellationToken cancellationToken,
            BlobSizes.BlobSizesConnection blobSizesConnection,
            string folderPath)
        {
            this.projectionReadWriteLock.EnterReadLock();

            try
            {
                FolderData folderData;
                if (this.TryGetOrAddFolderDataFromCache(folderPath, out folderData))
                {
                    folderData.PopulateSizes(
                        this.context.Tracer,
                        this.gitObjects,
                        blobSizesConnection,
                        availableSizes: null,
                        cancellationToken: cancellationToken);

                    return ConvertToProjectedFileInfos(folderData.ChildEntries);
                }

                return new List<ProjectedFileInfo>();
            }
            finally
            {
                this.projectionReadWriteLock.ExitReadLock();
            }
        }

        public virtual bool IsPathProjected(string virtualPath, out string fileName, out bool isFolder)
        {
            isFolder = false;
            string parentKey;
            this.GetChildNameAndParentKey(virtualPath, out fileName, out parentKey);
            FolderEntryData data = this.GetProjectedFolderEntryData(
                blobSizesConnection: null,
                childName: fileName,
                parentKey: parentKey);

            if (data != null)
            {
                isFolder = data.IsFolder;
                return true;
            }

            return false;
        }

        public virtual ProjectedFileInfo GetProjectedFileInfo(
            CancellationToken cancellationToken,
            BlobSizes.BlobSizesConnection blobSizesConnection,
            string virtualPath,
            out string parentFolderPath)
        {
            string childName;
            string parentKey;
            this.GetChildNameAndParentKey(virtualPath, out childName, out parentKey);
            parentFolderPath = parentKey;
            string gitCasedChildName;
            FolderEntryData data = this.GetProjectedFolderEntryData(
                cancellationToken,
                blobSizesConnection,
                availableSizes: null,
                childName: childName,
                parentKey: parentKey,
                gitCasedChildName: out gitCasedChildName);

            if (data != null)
            {
                if (data.IsFolder)
                {
                    return new ProjectedFileInfo(gitCasedChildName, size: 0, isFolder: true, sha: Sha1Id.None);
                }
                else
                {
                    FileData fileData = (FileData)data;
                    return new ProjectedFileInfo(gitCasedChildName, fileData.Size, isFolder: false, sha: fileData.Sha);
                }
            }

            return null;
        }

        public virtual FileSystemTaskResult OpenIndexForRead()
        {
            if (!File.Exists(this.indexPath))
            {
                EventMetadata metadata = CreateEventMetadata();
                this.context.Tracer.RelatedError(metadata, "AcquireIndexLockAndOpenForWrites: Can't open the index because it doesn't exist");

                return FileSystemTaskResult.FatalError;
            }

            this.projectionParseComplete.Wait();

            FileSystemTaskResult result = FileSystemTaskResult.FatalError;
            try
            {
                this.indexFileStream = new FileStream(this.indexPath, FileMode.Open, FileAccess.Read, FileShare.Read, IndexFileStreamBufferSize);
                result = FileSystemTaskResult.Success;
            }
            catch (IOException e)
            {
                EventMetadata metadata = CreateEventMetadata(e);
                this.context.Tracer.RelatedWarning(metadata, "IOException in AcquireIndexLockAndOpenForWrites (Retryable)");
                result = FileSystemTaskResult.RetryableError;
            }
            catch (Exception e)
            {
                EventMetadata metadata = CreateEventMetadata(e);
                this.context.Tracer.RelatedError(metadata, "Exception in AcquireIndexLockAndOpenForWrites (FatalError)");
                result = FileSystemTaskResult.FatalError;
            }

            return result;
        }

        public FileSystemTaskResult CloseIndex()
        {
            if (this.indexFileStream != null)
            {
                this.indexFileStream.Dispose();
                this.indexFileStream = null;
            }

            return FileSystemTaskResult.Success;
        }

        public FileSystemTaskResult AddMissingModifiedFiles()
        {
            try
            {
                if (this.modifiedFilesInvalid)
                {
                    using (ITracer activity = this.context.Tracer.StartActivity(
                        nameof(this.indexParser.AddMissingModifiedFilesAndRemoveThemFromPlaceholderList),
                        EventLevel.Informational))
                    {
                        FileSystemTaskResult result = this.indexParser.AddMissingModifiedFilesAndRemoveThemFromPlaceholderList(
                            activity,
                            this.indexFileStream);

                        if (result == FileSystemTaskResult.Success)
                        {
                            this.modifiedFilesInvalid = false;
                        }

                        return result;
                    }
                }
            }
            catch (IOException e)
            {
                EventMetadata metadata = CreateEventMetadata(e);
                this.context.Tracer.RelatedWarning(metadata, "IOException in " + nameof(this.AddMissingModifiedFiles) + " (Retryable)");

                return FileSystemTaskResult.RetryableError;
            }
            catch (Exception e)
            {
                EventMetadata metadata = CreateEventMetadata(e);
                this.context.Tracer.RelatedError(metadata, "Exception in " + nameof(this.AddMissingModifiedFiles) + " (FatalError)");

                return FileSystemTaskResult.FatalError;
            }

            return FileSystemTaskResult.Success;
        }

        public void RemoveFromPlaceholderList(string fileOrFolderPath)
        {
            this.placeholderList.Remove(fileOrFolderPath);
        }

        public void Dispose()
        {
            this.Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (this.projectionReadWriteLock != null)
                {
                    this.projectionReadWriteLock.Dispose();
                    this.projectionReadWriteLock = null;
                }

                if (this.projectionParseComplete != null)
                {
                    this.projectionParseComplete.Dispose();
                    this.projectionParseComplete = null;
                }

                if (this.wakeUpIndexParsingThread != null)
                {
                    this.wakeUpIndexParsingThread.Dispose();
                    this.wakeUpIndexParsingThread = null;
                }

                if (this.indexParsingThread != null)
                {
                    this.indexParsingThread.Dispose();
                    this.indexParsingThread = null;
                }

                if (this.placeholderList != null)
                {
                    this.placeholderList.Dispose();
                    this.placeholderList = null;
                }
            }
        }

        protected void GetChildNameAndParentKey(string virtualPath, out string childName, out string parentKey)
        {
            parentKey = string.Empty;

            int separatorIndex = virtualPath.LastIndexOf(Path.DirectorySeparatorChar);
            if (separatorIndex < 0)
            {
                childName = virtualPath;
                return;
            }

            childName = virtualPath.Substring(separatorIndex + 1);
            parentKey = virtualPath.Substring(0, separatorIndex);
        }

        private static EventMetadata CreateEventMetadata(Exception e = null)
        {
            EventMetadata metadata = new EventMetadata();
            metadata.Add("Area", EtwArea);
            if (e != null)
            {
                metadata.Add("Exception", e.ToString());
            }

            return metadata;
        }

        private static List<ProjectedFileInfo> ConvertToProjectedFileInfos(SortedFolderEntries sortedFolderEntries)
        {
            List<ProjectedFileInfo> childItems = new List<ProjectedFileInfo>(sortedFolderEntries.Count);
            for (int i = 0; i < sortedFolderEntries.Count; i++)
            {
                FolderEntryData childEntry = sortedFolderEntries[i];

                if (childEntry.IsFolder)
                {
                    childItems.Add(new ProjectedFileInfo(childEntry.Name.GetString(), size: 0, isFolder: true, sha: Sha1Id.None));
                }
                else
                {
                    FileData fileData = (FileData)childEntry;
                    childItems.Add(new ProjectedFileInfo(fileData.Name.GetString(), fileData.Size, isFolder: false, sha: fileData.Sha));
                }
            }

            return childItems;
        }

        private void AddItemFromIndexEntry(GitIndexEntry indexEntry)
        {
            if (indexEntry.BuildingProjection_HasSameParentAsLastEntry)
            {
                indexEntry.BuildingProjection_LastParent.AddChildFile(indexEntry.BuildingProjection_GetChildName(), indexEntry.Sha);
            }
            else
            {
                if (indexEntry.BuildingProjection_NumParts == 1)
                {
                    indexEntry.BuildingProjection_LastParent = this.rootFolderData;
                    indexEntry.BuildingProjection_LastParent.AddChildFile(indexEntry.BuildingProjection_GetChildName(), indexEntry.Sha);
                }
                else
                {
                    indexEntry.BuildingProjection_LastParent = this.AddFileToTree(indexEntry);
                }
            }

            if (GVFSPlatform.Instance.FileSystem.SupportsFileMode)
            {
                // TODO(Mac): Test if performance could be improved by eliminating the SupportsFileMode check
                // (e.g. by defaulting FileMode to Regular 644 and eliminating the SupportsFileMode check)
                if (indexEntry.TypeAndMode.Type != FileType.Regular ||
                    indexEntry.TypeAndMode.Mode != FileMode644)
                {
                    // TODO(Mac): The line below causes a conversion from LazyUTF8String to .NET string.
                    // Measure the perf and memory overhead of performing this conversion, and determine if we need
                    // a way to keep the path as LazyUTF8String
                    this.nonDefaultFileTypesAndModes.Add(indexEntry.BuildingProjection_GetGitRelativePath(), indexEntry.TypeAndMode);
                }
            }
        }

        private FileSystemTaskResult AddModifiedPath(string path)
        {
            bool wasAdded = this.modifiedPaths.TryAdd(path, isFolder: false, isRetryable: out bool isRetryable);
            if (!wasAdded)
            {
                return isRetryable ? FileSystemTaskResult.RetryableError : FileSystemTaskResult.FatalError;
            }

            return FileSystemTaskResult.Success;
        }

        private void ClearProjectionCaches()
        {
            SortedFolderEntries.FreePool();
            LazyUTF8String.FreePool();
            this.projectionFolderCache.Clear();
            this.nonDefaultFileTypesAndModes.Clear();
            this.rootFolderData.ResetData(new LazyUTF8String("<root>"));
        }

        private bool TryGetSha(string childName, string parentKey, out string sha)
        {
            sha = string.Empty;
            FileData data = this.GetProjectedFolderEntryData(
                blobSizesConnection: null,
                childName: childName,
                parentKey: parentKey) as FileData;

            if (data != null && !data.IsFolder)
            {
                sha = data.ConvertShaToString();
                return true;
            }

            return false;
        }

        private void SetProjectionInvalid(bool isInvalid)
        {
            this.projectionInvalid = isInvalid;
            this.repoMetadata.SetProjectionInvalid(isInvalid);
        }

        private void SetProjectionAndPlaceholdersAsInvalid()
        {
            this.projectionInvalid = true;
            this.repoMetadata.SetProjectionInvalidAndPlaceholdersNeedUpdate();
        }

        private string GetParentKey(string gitPath, out int pathSeparatorIndex)
        {
            string parentKey = string.Empty;
            pathSeparatorIndex = gitPath.LastIndexOf(GVFSConstants.GitPathSeparator);
            if (pathSeparatorIndex >= 0)
            {
                parentKey = gitPath.Substring(0, pathSeparatorIndex);
            }

            return parentKey;
        }

        private string GetChildName(string gitPath, int pathSeparatorIndex)
        {
            if (pathSeparatorIndex < 0)
            {
                return gitPath;
            }

            return gitPath.Substring(pathSeparatorIndex + 1);
        }

        /// <summary>
        /// Add FolderData and FileData objects to the tree needed for the current index entry
        /// </summary>
        /// <param name="indexEntry">GitIndexEntry used to create the child's path</param>
        /// <returns>The FolderData for childData's parent</returns>
        /// <remarks>This method will create and add any intermediate FolderDatas that are
        /// required but not already in the tree.  For example, if the tree was completely empty
        /// and AddFileToTree was called for the path \A\B\C.txt:
        ///
        ///    pathParts -> { "A", "B", "C.txt"}
        ///
        ///    AddFileToTree would create new FolderData entries in the tree for "A" and "B"
        ///    and return the FolderData entry for "B"
        /// </remarks>
        private FolderData AddFileToTree(GitIndexEntry indexEntry)
        {
            FolderData parentFolder = this.rootFolderData;
            for (int pathIndex = 0; pathIndex < indexEntry.BuildingProjection_NumParts - 1; ++pathIndex)
            {
                if (parentFolder == null)
                {
                    string parentFolderName;
                    if (pathIndex > 0)
                    {
                        parentFolderName = indexEntry.BuildingProjection_PathParts[pathIndex - 1].GetString();
                    }
                    else
                    {
                        parentFolderName = this.rootFolderData.Name.GetString();
                    }

                    string gitPath = indexEntry.BuildingProjection_GetGitRelativePath();

                    EventMetadata metadata = CreateEventMetadata();
                    metadata.Add("gitPath", gitPath);
                    metadata.Add("parentFolder", parentFolderName);
                    this.context.Tracer.RelatedError(metadata, "AddFileToTree: Found a file where a folder was expected");

                    throw new InvalidDataException("Found a file (" + parentFolderName + ") where a folder was expected: " + gitPath);
                }

                parentFolder = parentFolder.ChildEntries.GetOrAddFolder(indexEntry.BuildingProjection_PathParts[pathIndex]);
            }

            parentFolder.AddChildFile(indexEntry.BuildingProjection_PathParts[indexEntry.BuildingProjection_NumParts - 1], indexEntry.Sha);

            return parentFolder;
        }

        private FolderEntryData GetProjectedFolderEntryData(
            CancellationToken cancellationToken,
            BlobSizes.BlobSizesConnection blobSizesConnection,
            Dictionary<string, long> availableSizes,
            string childName,
            string parentKey,
            out string gitCasedChildName)
        {
            this.projectionReadWriteLock.EnterReadLock();
            try
            {
                FolderData parentFolderData;
                if (this.TryGetOrAddFolderDataFromCache(parentKey, out parentFolderData))
                {
                    LazyUTF8String child = new LazyUTF8String(childName);
                    FolderEntryData childData;
                    if (parentFolderData.ChildEntries.TryGetValue(child, out childData))
                    {
                        gitCasedChildName = childData.Name.GetString();

                        if (blobSizesConnection != null && !childData.IsFolder)
                        {
                            FileData fileData = (FileData)childData;
                            if (!fileData.IsSizeSet() && !fileData.TryPopulateSizeLocally(this.context.Tracer, this.gitObjects, blobSizesConnection, availableSizes, out string _))
                            {
                                Stopwatch queryTime = Stopwatch.StartNew();
                                parentFolderData.PopulateSizes(this.context.Tracer, this.gitObjects, blobSizesConnection, availableSizes, cancellationToken);
                                this.context.Repository.GVFSLock.Stats.RecordSizeQuery(queryTime.ElapsedMilliseconds);
                            }
                        }

                        return childData;
                    }
                }

                gitCasedChildName = string.Empty;
                return null;
            }
            finally
            {
                this.projectionReadWriteLock.ExitReadLock();
            }
        }

        /// <summary>
        /// Get the FolderEntryData for the specified child name and parent key.
        /// </summary>
        /// <param name="blobSizesConnection">
        /// BlobSizesConnection used to lookup the size for the FolderEntryData.  If null, size will not be populated.
        /// </param>
        /// <param name="childName">Child name (i.e. file name)</param>
        /// <param name="parentKey">Parent key (parent folder path)</param>
        /// <returns>FolderEntryData for the specified childName and parentKey or null if no FolderEntryData exists for them in the projection</returns>
        /// <remarks><see cref="GetChildNameAndParentKey"/> can be used for getting child name and parent key from a file path</remarks>
        private FolderEntryData GetProjectedFolderEntryData(
            BlobSizes.BlobSizesConnection blobSizesConnection,
            string childName,
            string parentKey)
        {
            string casedChildName;
            return this.GetProjectedFolderEntryData(
                CancellationToken.None,
                blobSizesConnection,
                availableSizes: null,
                childName: childName,
                parentKey: parentKey,
                gitCasedChildName: out casedChildName);
        }

        /// <summary>
        /// Try to get the FolderData for the specified folder path from the projectionFolderCache
        /// cache.  If the  folder is not already in projectionFolderCache, search for it in the tree and
        /// then add it to projectionData
        /// </summary>
        /// <returns>True if the folder could be found, and false otherwise</returns>
        private bool TryGetOrAddFolderDataFromCache(
            string folderPath,
            out FolderData folderData)
        {
            if (!this.projectionFolderCache.TryGetValue(folderPath, out folderData))
            {
                LazyUTF8String[] pathParts = folderPath
                    .Split(new char[] { Path.DirectorySeparatorChar }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => new LazyUTF8String(x))
                    .ToArray();

                FolderEntryData data;
                if (!this.TryGetFolderEntryDataFromTree(pathParts, folderEntryData: out data))
                {
                    folderPath = null;
                    return false;
                }

                if (data.IsFolder)
                {
                    folderData = (FolderData)data;
                    this.projectionFolderCache.TryAdd(folderPath, folderData);
                }
                else
                {
                    EventMetadata metadata = CreateEventMetadata();
                    metadata.Add("folderPath", folderPath);
                    metadata.Add(TracingConstants.MessageKey.InfoMessage, "Found file at path");
                    this.context.Tracer.RelatedEvent(
                        EventLevel.Informational,
                        $"{nameof(this.TryGetOrAddFolderDataFromCache)}_FileAtPath",
                        metadata);

                    folderPath = null;
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Finds the FolderEntryData for the path provided
        /// </summary>
        /// <param name="pathParts">Path to desired entry</param>
        /// <param name="folderEntryData">Out: FolderEntryData for pathParts</param>
        /// <returns>True if the path could be found in the tree, and false otherwise</returns>
        /// <remarks>If the root folder is desired, the pathParts should be an empty array</remarks>
        private bool TryGetFolderEntryDataFromTree(LazyUTF8String[] pathParts, out FolderEntryData folderEntryData)
        {
            return this.TryGetFolderEntryDataFromTree(pathParts, pathParts.Length, out folderEntryData);
        }

        /// <summary>
        /// Finds the FolderEntryData at the specified depth of the path provided.  If depth == pathParts.Length
        /// TryGetFolderEntryDataFromTree will find the FolderEntryData specified in pathParts.  If
        /// depth is less than pathParts.Length, then TryGetFolderEntryDataFromTree will return an ancestor folder of
        /// childPathParts.
        /// </summary>
        /// <param name="pathParts">Path</param>
        /// <param name="depth">Desired path depth, if depth is > pathParts.Length, pathParts.Length will be used</param>
        /// <param name="folderEntryData">Out: FolderEntryData for pathParts at the specified depth.  For example,
        /// if pathParts were { "A", "B", "C" } and depth was 2, FolderEntryData for "B" would be returned.</param>
        /// <returns>True if the specified path\depth could be found in the tree, and false otherwise</returns>
        private bool TryGetFolderEntryDataFromTree(LazyUTF8String[] pathParts, int depth, out FolderEntryData folderEntryData)
        {
            folderEntryData = null;
            depth = Math.Min(depth, pathParts.Length);
            FolderEntryData currentEntry = this.rootFolderData;
            for (int pathIndex = 0; pathIndex < depth; ++pathIndex)
            {
                if (!currentEntry.IsFolder)
                {
                    return false;
                }

                FolderData folderData = (FolderData)currentEntry;
                if (!folderData.ChildEntries.TryGetValue(pathParts[pathIndex], out currentEntry))
                {
                    return false;
                }
            }

            folderEntryData = currentEntry;
            return folderEntryData != null;
        }

        private void ParseIndexThreadMain()
        {
            try
            {
                while (true)
                {
                    this.wakeUpIndexParsingThread.WaitOne();

                    if (this.isStopping)
                    {
                        return;
                    }

                    Stopwatch stopwatch = Stopwatch.StartNew();
                    this.projectionReadWriteLock.EnterWriteLock();

                    // Record if the projection needed to be updated to ensure that placeholders and the negative cache
                    // are only updated when required (i.e. only updated when the projection was updated)
                    bool updatedProjection = this.projectionInvalid;

                    try
                    {
                        while (this.projectionInvalid)
                        {
                            try
                            {
                                this.CopyIndexFileAndBuildProjection();
                            }
                            catch (Win32Exception e)
                            {
                                this.SetProjectionAndPlaceholdersAsInvalid();

                                EventMetadata metadata = CreateEventMetadata(e);
                                this.context.Tracer.RelatedWarning(metadata, "Win32Exception when reparsing index for projection");
                            }
                            catch (IOException e)
                            {
                                this.SetProjectionAndPlaceholdersAsInvalid();

                                EventMetadata metadata = CreateEventMetadata(e);
                                this.context.Tracer.RelatedWarning(metadata, "IOException when reparsing index for projection");
                            }
                            catch (UnauthorizedAccessException e)
                            {
                                this.SetProjectionAndPlaceholdersAsInvalid();

                                EventMetadata metadata = CreateEventMetadata(e);
                                this.context.Tracer.RelatedWarning(metadata, "UnauthorizedAccessException when reparsing index for projection");
                            }

                            if (this.isStopping)
                            {
                                return;
                            }
                        }
                    }
                    finally
                    {
                        this.projectionReadWriteLock.ExitWriteLock();
                    }

                    stopwatch.Stop();
                    this.context.Repository.GVFSLock.Stats.RecordProjectionWriteLockHeld(stopwatch.ElapsedMilliseconds);

                    if (this.isStopping)
                    {
                        return;
                    }

                    // Avoid unnecessary updates by checking if the projection actually changed.  Some git commands (e.g. cherry-pick)
                    // update the index multiple times which can result in the outer 'while (true)' loop executing twice.  This happens
                    // because this.wakeUpThread is set again after this thread has woken up but before it's done any processing (because it's
                    // still waiting for the git command to complete).  If the projection is still valid during the second execution of
                    // the loop there's no need to clear the negative cache or update placeholders a second time.
                    if (updatedProjection)
                    {
                        this.ClearNegativePathCache();
                        this.UpdatePlaceholders();
                    }

                    this.projectionParseComplete.Set();

                    if (this.isStopping)
                    {
                        return;
                    }
                }
            }
            catch (Exception e)
            {
                this.LogErrorAndExit("ParseIndexThreadMain caught unhandled exception, exiting process", e);
            }
        }

        private void ClearNegativePathCache()
        {
            uint totalEntryCount;
            FileSystemResult clearCacheResult = this.fileSystemVirtualizer.ClearNegativePathCache(out totalEntryCount);
            int gitCount = Interlocked.Exchange(ref this.negativePathCacheUpdatedForGitCount, 0);

            EventMetadata clearCacheMetadata = CreateEventMetadata();
            clearCacheMetadata.Add(TracingConstants.MessageKey.InfoMessage, $"{nameof(this.ClearNegativePathCache)}: Cleared negative path cache");
            clearCacheMetadata.Add(nameof(totalEntryCount), totalEntryCount);
            clearCacheMetadata.Add("negativePathCacheUpdatedForGitCount", gitCount);
            clearCacheMetadata.Add("clearCacheResult.Result", clearCacheResult.ToString());
            clearCacheMetadata.Add("clearCacheResult.RawResult", clearCacheResult.RawResult);
            this.context.Tracer.RelatedEvent(EventLevel.Informational, $"{nameof(this.ClearNegativePathCache)}_ClearedCache", clearCacheMetadata);

            if (clearCacheResult.Result != FSResult.Ok)
            {
                this.LogErrorAndExit("ClearNegativePathCache failed, exiting process. ClearNegativePathCache result: " + clearCacheResult.ToString());
            }
        }

        private void ClearUpdatePlaceholderErrors()
        {
            this.updatePlaceholderFailures = new ConcurrentHashSet<string>();
            this.deletePlaceholderFailures = new ConcurrentHashSet<string>();
        }

        private void UpdatePlaceholders()
        {
            Stopwatch stopwatch = new Stopwatch();
            List<IPlaceholderData> placeholderFilesListCopy;
            List<IPlaceholderData> placeholderFoldersListCopy;
            this.placeholderList.GetAllEntries(out placeholderFilesListCopy, out placeholderFoldersListCopy);

            EventMetadata metadata = new EventMetadata();
            metadata.Add("File placeholder count", placeholderFilesListCopy.Count);
            metadata.Add("Folder placeholders count", placeholderFoldersListCopy.Count);

            using (ITracer activity = this.context.Tracer.StartActivity("UpdatePlaceholders", EventLevel.Informational, metadata))
            {
                int minItemsPerThread = 10;
                int numThreads = Math.Max(8, Environment.ProcessorCount);
                numThreads = Math.Min(numThreads, placeholderFilesListCopy.Count / minItemsPerThread);
                numThreads = Math.Max(numThreads, 1);

                // folderPlaceholdersToKeep always contains the empty path so as to avoid unnecessary attempts
                // to remove the repository's root folder.
                ConcurrentHashSet<string> folderPlaceholdersToKeep = new ConcurrentHashSet<string>();
                folderPlaceholdersToKeep.Add(string.Empty);

                // updatedPlaceholderDictionary and updatedPlaceholderBag are mutually exclusive.
                //  - On platforms that expand on enumeration: updatedPlaceholderDictionary is used (required for ReExpandFolder)
                //  - On platforms that do not expand on enumeration: updatedPlaceholderBag is used (for speed)
                ConcurrentDictionary<string, IPlaceholderData> updatedPlaceholderDictionary;
                ConcurrentBag<IPlaceholderData> updatedPlaceholderBag;
                Action<IPlaceholderData> addPlaceholderToUpdatedPlaceholders;
                if (GVFSPlatform.Instance.KernelDriver.EnumerationExpandsDirectories)
                {
                    updatedPlaceholderDictionary = new ConcurrentDictionary<string, IPlaceholderData>(
                        concurrencyLevel: numThreads,
                        capacity: placeholderFilesListCopy.Count + placeholderFoldersListCopy.Count,
                        comparer: StringComparer.Ordinal);
                    updatedPlaceholderBag = null;
                    addPlaceholderToUpdatedPlaceholders = (data) => updatedPlaceholderDictionary.TryAdd(data.Path, data);
                }
                else
                {
                    updatedPlaceholderDictionary = null;
                    updatedPlaceholderBag = new ConcurrentBag<IPlaceholderData>();
                    addPlaceholderToUpdatedPlaceholders = (data) => updatedPlaceholderBag.Add(data);
                }

                stopwatch.Restart();
                this.ProcessListOnThreads(
                    numThreads,
                    placeholderFilesListCopy,
                    (placeholderBatch, start, end, blobSizesConnection, availableSizes) =>
                        this.BatchPopulateMissingSizesFromRemote(blobSizesConnection, placeholderBatch, start, end, availableSizes),
                    (placeholder, blobSizesConnection, availableSizes) =>
                        this.UpdateOrDeleteFilePlaceholder(blobSizesConnection, placeholder, addPlaceholderToUpdatedPlaceholders, folderPlaceholdersToKeep, availableSizes));
                stopwatch.Stop();
                long millisecondsUpdatingFilePlaceholders = stopwatch.ElapsedMilliseconds;

                stopwatch.Restart();
                this.blobSizes.Flush();

                int deleteFolderPlaceholderAttempted = 0;
                using (BlobSizes.BlobSizesConnection blobSizesConnection = this.blobSizes.CreateConnection())
                {
                    // A hash of the folder placeholders is only required if the platform expands directories
                    HashSet<string> folderPlaceholders =
                        GVFSPlatform.Instance.KernelDriver.EnumerationExpandsDirectories ?
                        new HashSet<string>(placeholderFoldersListCopy.Select(x => x.Path), GVFSPlatform.Instance.Constants.PathComparer) :
                        null;

                    // Order the folders in decscending order so that we walk the tree from bottom up.
                    // Traversing the folders in this order:
                    //  1. Ensures child folders are deleted before their parents
                    //  2. Ensures that folders that have been deleted by git (but are still in the projection) are found before their
                    //     parent folder is re-expanded (only applies on platforms where EnumerationExpandsDirectories is true)
                    foreach (IPlaceholderData folderPlaceholder in placeholderFoldersListCopy.OrderByDescending(x => x.Path))
                    {
                        bool keepFolder = true;
                        if (!folderPlaceholdersToKeep.Contains(folderPlaceholder.Path))
                        {
                            bool isProjected = this.IsPathProjected(folderPlaceholder.Path, out string fileName, out bool isFolder);

                            // Check the projection for the folder to determine if the folder needs to be deleted
                            // The delete will be attempted if one of the following is true
                            // 1. not in the projection anymore
                            // 2. in the projection but is not a folder in the projection
                            // 3. Folder placeholder is a possible tombstone
                            if (!isProjected ||
                                !isFolder ||
                                folderPlaceholder.IsPossibleTombstoneFolder)
                            {
                                keepFolder = !this.RemoveFolderPlaceholderIfEmpty(folderPlaceholder);
                                ++deleteFolderPlaceholderAttempted;
                            }

                            if (keepFolder)
                            {
                                this.AddParentFoldersToListToKeep(folderPlaceholder.Path, folderPlaceholdersToKeep);
                            }
                        }

                        if (keepFolder)
                        {
                            addPlaceholderToUpdatedPlaceholders(folderPlaceholder);

                            // Remove folder placeholders before re-expansion to ensure that projection changes that convert a folder to a file work
                            // properly
                            if (GVFSPlatform.Instance.KernelDriver.EnumerationExpandsDirectories && folderPlaceholder.IsExpandedFolder)
                            {
                                if (updatedPlaceholderDictionary == null)
                                {
                                    throw new InvalidOperationException(
                                        $"{nameof(updatedPlaceholderDictionary)} must be used when enumeration expands directories");
                                }

                                this.ReExpandFolder(blobSizesConnection, folderPlaceholder.Path, updatedPlaceholderDictionary, folderPlaceholders);
                            }
                        }
                    }
                }

                stopwatch.Stop();
                long millisecondsUpdatingFolderPlaceholders = stopwatch.ElapsedMilliseconds;

                stopwatch.Restart();
                if (GVFSPlatform.Instance.KernelDriver.EnumerationExpandsDirectories)
                {
                    if (updatedPlaceholderBag != null)
                    {
                        throw new InvalidOperationException(
                            $"{nameof(updatedPlaceholderBag)} should only be used when enumeration does not expand directories");
                    }

                    this.placeholderList.WriteAllEntriesAndFlush(updatedPlaceholderDictionary.Values);
                }
                else
                {
                    if (updatedPlaceholderDictionary != null)
                    {
                        throw new InvalidOperationException(
                            $"{nameof(updatedPlaceholderDictionary)} should only be used when enumeration expands directories");
                    }

                    this.placeholderList.WriteAllEntriesAndFlush(updatedPlaceholderBag);
                }

                this.repoMetadata.SetPlaceholdersNeedUpdate(false);

                stopwatch.Stop();
                long millisecondsWriteAndFlush = stopwatch.ElapsedMilliseconds;

                TimeSpan duration = activity.Stop(null);
                this.context.Repository.GVFSLock.Stats.RecordUpdatePlaceholders(
                    (long)duration.TotalMilliseconds,
                    millisecondsUpdatingFilePlaceholders,
                    millisecondsUpdatingFolderPlaceholders,
                    millisecondsWriteAndFlush,
                    deleteFolderPlaceholderAttempted);
            }
        }

        private void ProcessListOnThreads<T>(
            int numThreads,
            List<T> list,
            Action<List<T>, int, int, BlobSizes.BlobSizesConnection, Dictionary<string, long>> preProcessBatch,
            Action<T, BlobSizes.BlobSizesConnection, Dictionary<string, long>> processItem)
        {
            if (numThreads > 1)
            {
                Thread[] processThreads = new Thread[numThreads];
                int itemsPerThread = list.Count / numThreads;

                for (int i = 0; i < numThreads; i++)
                {
                    int start = i * itemsPerThread;
                    int end = (i + 1) == numThreads ? list.Count : (i + 1) * itemsPerThread;

                    processThreads[i] = new Thread(
                        () =>
                        {
                            // We have a top-level try\catch for any unhandled exceptions thrown in the newly created thread
                            try
                            {
                                this.ProcessListThreadCallback(preProcessBatch, processItem, list, start, end);
                            }
                            catch (Exception e)
                            {
                                this.LogErrorAndExit(nameof(this.ProcessListOnThreads) + " background thread caught unhandled exception, exiting process", e);
                            }
                        });

                    processThreads[i].Start();
                }

                for (int i = 0; i < processThreads.Length; i++)
                {
                    processThreads[i].Join();
                }
            }
            else
            {
                this.ProcessListThreadCallback(preProcessBatch, processItem, list, 0, list.Count);
            }
        }

        private void ProcessListThreadCallback<T>(
            Action<List<T>, int, int, BlobSizes.BlobSizesConnection, Dictionary<string, long>> preProcessBatch,
            Action<T, BlobSizes.BlobSizesConnection, Dictionary<string, long>> processItem,
            List<T> placeholderList,
            int start,
            int end)
        {
            using (BlobSizes.BlobSizesConnection blobSizesConnection = this.blobSizes.CreateConnection())
            {
                Dictionary<string, long> availableSizes = new Dictionary<string, long>();

                if (preProcessBatch != null)
                {
                    preProcessBatch(placeholderList, start, end, blobSizesConnection, availableSizes);
                }

                for (int j = start; j < end; ++j)
                {
                    processItem(placeholderList[j], blobSizesConnection, availableSizes);
                }
            }
        }

        private void BatchPopulateMissingSizesFromRemote(
            BlobSizes.BlobSizesConnection blobSizesConnection,
            List<IPlaceholderData> placeholderList,
            int start,
            int end,
            Dictionary<string, long> availableSizes)
        {
            int maxObjectsInHTTPRequest = 2000;

            for (int index = start; index < end; index += maxObjectsInHTTPRequest)
            {
                int count = Math.Min(maxObjectsInHTTPRequest, end - index);
                IEnumerable<string> nextBatch = this.GetShasWithoutSizeAndNeedingUpdate(blobSizesConnection, availableSizes, placeholderList, index, index + count);

                if (nextBatch.Any())
                {
                    Stopwatch queryTime = Stopwatch.StartNew();
                    List<GitObjectsHttpRequestor.GitObjectSize> fileSizes = this.gitObjects.GetFileSizes(nextBatch, CancellationToken.None);
                    this.context.Repository.GVFSLock.Stats.RecordSizeQuery(queryTime.ElapsedMilliseconds);

                    foreach (GitObjectsHttpRequestor.GitObjectSize downloadedSize in fileSizes)
                    {
                        string downloadedSizeId = downloadedSize.Id.ToUpper();
                        Sha1Id sha1Id = new Sha1Id(downloadedSizeId);
                        blobSizesConnection.BlobSizesDatabase.AddSize(sha1Id, downloadedSize.Size);
                        availableSizes[downloadedSizeId] = downloadedSize.Size;
                    }
                }
            }
        }

        private IEnumerable<string> GetShasWithoutSizeAndNeedingUpdate(BlobSizes.BlobSizesConnection blobSizesConnection, Dictionary<string, long> availableSizes, List<IPlaceholderData> placeholders, int start, int end)
        {
            for (int index = start; index < end; index++)
            {
                string projectedSha = this.GetNewProjectedShaForPlaceholder(placeholders[index].Path);

                if (!string.IsNullOrEmpty(projectedSha))
                {
                    long blobSize = 0;
                    string shaOnDisk = placeholders[index].Sha;

                    if (shaOnDisk.Equals(projectedSha))
                    {
                        continue;
                    }

                    if (blobSizesConnection.TryGetSize(new Sha1Id(projectedSha), out blobSize))
                    {
                        availableSizes[projectedSha] = blobSize;
                        continue;
                    }

                    if (this.gitObjects.TryGetBlobSizeLocally(projectedSha, out blobSize))
                    {
                        availableSizes[projectedSha] = blobSize;
                        continue;
                    }

                    yield return projectedSha;
                }
            }
        }

        private string GetNewProjectedShaForPlaceholder(string path)
        {
            string childName;
            string parentKey;
            this.GetChildNameAndParentKey(path, out childName, out parentKey);

            string projectedSha;
            if (this.TryGetSha(childName, parentKey, out projectedSha))
            {
                return projectedSha;
            }

            return null;
        }

        private void ReExpandFolder(
            BlobSizes.BlobSizesConnection blobSizesConnection,
            string relativeFolderPath,
            ConcurrentDictionary<string, IPlaceholderData> updatedPlaceholderList,
            HashSet<string> existingFolderPlaceholders)
        {
            FolderData folderData;
            if (!this.TryGetOrAddFolderDataFromCache(relativeFolderPath, out folderData))
            {
                // Folder is no longer in the projection
                return;
            }

            // TODO(Mac): Issue #255, batch file sizes up-front for the new placeholders written by ReExpandFolder
            folderData.PopulateSizes(
                this.context.Tracer,
                this.gitObjects,
                blobSizesConnection,
                availableSizes: null,
                cancellationToken: CancellationToken.None);

            for (int i = 0; i < folderData.ChildEntries.Count; i++)
            {
                FolderEntryData childEntry = folderData.ChildEntries[i];
                string childRelativePath;
                if (relativeFolderPath.Length == 0)
                {
                    childRelativePath = childEntry.Name.GetString();
                }
                else
                {
                    childRelativePath = relativeFolderPath + Path.DirectorySeparatorChar + childEntry.Name.GetString();
                }

                bool newChild = childEntry.IsFolder ? !existingFolderPlaceholders.Contains(childRelativePath) : !updatedPlaceholderList.ContainsKey(childRelativePath);

                if (newChild)
                {
                    FileSystemResult result;
                    string fileShaOrFolderValue;
                    if (childEntry.IsFolder)
                    {
                        fileShaOrFolderValue = PlaceholderListDatabase.PartialFolderValue;
                        result = this.fileSystemVirtualizer.WritePlaceholderDirectory(childRelativePath);
                    }
                    else
                    {
                        FileData childFileData = childEntry as FileData;
                        fileShaOrFolderValue = childFileData.Sha.ToString();
                        result = this.fileSystemVirtualizer.WritePlaceholderFile(childRelativePath, childFileData.Size, fileShaOrFolderValue);
                    }

                    switch (result.Result)
                    {
                        case FSResult.Ok:
                            updatedPlaceholderList.TryAdd(
                                childRelativePath,
                                new PlaceholderListDatabase.PlaceholderData(childRelativePath, fileShaOrFolderValue));
                            break;

                        case FSResult.FileOrPathNotFound:
                            // Git command must have removed the folder being re-expanded (relativeFolderPath)
                            // Remove the folder from existingFolderPlaceholders so that its parent will create
                            // it again (when it's re-expanded)
                            existingFolderPlaceholders.Remove(relativeFolderPath);
                            return;

                        default:
                            // TODO(Mac): Issue #245, handle failures of WritePlaceholderDirectory and WritePlaceholderFile
                            break;
                    }
                }
            }
        }

        /// <summary>
        /// Removes the folder placeholder from disk if it's empty.
        /// </summary>
        /// <returns>
        /// <c>true</c>If the folder placeholder was deleted
        /// <c>false</c>If RemoveFolderPlaceholderIfEmpty failed attempting to remove the folder placeholder
        /// </returns>
        private bool RemoveFolderPlaceholderIfEmpty(IPlaceholderData placeholder)
        {
            UpdateFailureReason failureReason = UpdateFailureReason.NoFailure;
            FileSystemResult result = this.fileSystemVirtualizer.DeleteFile(placeholder.Path, FolderPlaceholderDeleteFlags, out failureReason);
            switch (result.Result)
            {
                case FSResult.Ok:
                case FSResult.FileOrPathNotFound:
                    return true;

                case FSResult.DirectoryNotEmpty:
                    return false;

                default:
                    EventMetadata metadata = CreateEventMetadata();
                    metadata.Add("Folder Path", placeholder.Path);
                    metadata.Add("result.Result", result.Result.ToString());
                    metadata.Add("result.RawResult", result.RawResult);
                    metadata.Add("UpdateFailureCause", failureReason.ToString());
                    this.context.Tracer.RelatedEvent(EventLevel.Informational, nameof(this.RemoveFolderPlaceholderIfEmpty) + "_DeleteFileFailure", metadata);
                    return false;
            }
        }

        private void UpdateOrDeleteFilePlaceholder(
            BlobSizes.BlobSizesConnection blobSizesConnection,
            IPlaceholderData placeholder,
            Action<IPlaceholderData> addPlaceholderToUpdatedPlaceholders,
            ConcurrentHashSet<string> folderPlaceholdersToKeep,
            Dictionary<string, long> availableSizes)
        {
            string childName;
            string parentKey;
            this.GetChildNameAndParentKey(placeholder.Path, out childName, out parentKey);

            string projectedSha;
            if (!this.TryGetSha(childName, parentKey, out projectedSha))
            {
                UpdateFailureReason failureReason = UpdateFailureReason.NoFailure;
                FileSystemResult result = this.fileSystemVirtualizer.DeleteFile(placeholder.Path, FilePlaceholderUpdateFlags, out failureReason);
                this.ProcessGvUpdateDeletePlaceholderResult(
                    placeholder,
                    string.Empty,
                    result,
                    addPlaceholderToUpdatedPlaceholders,
                    failureReason,
                    parentKey,
                    folderPlaceholdersToKeep,
                    deleteOperation: true);
            }
            else
            {
                string onDiskSha = placeholder.Sha;
                if (!onDiskSha.Equals(projectedSha))
                {
                    DateTime now = DateTime.UtcNow;
                    UpdateFailureReason failureReason = UpdateFailureReason.NoFailure;
                    FileSystemResult result;

                    try
                    {
                        FileData data = (FileData)this.GetProjectedFolderEntryData(CancellationToken.None, blobSizesConnection, availableSizes, childName, parentKey, out string _);
                        result = this.fileSystemVirtualizer.UpdatePlaceholderIfNeeded(
                            placeholder.Path,
                            creationTime: now,
                            lastAccessTime: now,
                            lastWriteTime: now,
                            changeTime: now,
                            fileAttributes: FileAttributes.Archive,
                            endOfFile: data.Size,
                            shaContentId: projectedSha,
                            updateFlags: FilePlaceholderUpdateFlags,
                            failureReason: out failureReason);
                    }
                    catch (Exception e)
                    {
                        result = new FileSystemResult(FSResult.IOError, rawResult: -1);

                        EventMetadata metadata = CreateEventMetadata(e);
                        metadata.Add("virtualPath", placeholder.Path);
                        this.context.Tracer.RelatedWarning(metadata, "UpdateOrDeletePlaceholder: Exception while trying to update placeholder");
                    }

                    this.ProcessGvUpdateDeletePlaceholderResult(
                        placeholder,
                        projectedSha,
                        result,
                        addPlaceholderToUpdatedPlaceholders,
                        failureReason,
                        parentKey,
                        folderPlaceholdersToKeep,
                        deleteOperation: false);
                }
                else
                {
                    addPlaceholderToUpdatedPlaceholders(placeholder);
                    this.AddParentFoldersToListToKeep(parentKey, folderPlaceholdersToKeep);
                }
            }
        }

        private void ProcessGvUpdateDeletePlaceholderResult(
            IPlaceholderData placeholder,
            string projectedSha,
            FileSystemResult result,
            Action<IPlaceholderData> addPlaceholderToUpdatedPlaceholders,
            UpdateFailureReason failureReason,
            string parentKey,
            ConcurrentHashSet<string> folderPlaceholdersToKeep,
            bool deleteOperation)
        {
            EventMetadata metadata;
            switch (result.Result)
            {
                case FSResult.Ok:
                    if (!deleteOperation)
                    {
                        addPlaceholderToUpdatedPlaceholders(new PlaceholderListDatabase.PlaceholderData(placeholder.Path, projectedSha));
                        this.AddParentFoldersToListToKeep(parentKey, folderPlaceholdersToKeep);
                    }

                    break;

                case FSResult.IoReparseTagNotHandled:
                    // Attempted to update\delete a file that has a non-ProjFS reparse point

                    this.ScheduleBackgroundTaskForFailedUpdateDeletePlaceholder(placeholder, deleteOperation);
                    this.AddParentFoldersToListToKeep(parentKey, folderPlaceholdersToKeep);

                    metadata = CreateEventMetadata();
                    metadata.Add("deleteOperation", deleteOperation);
                    metadata.Add("virtualPath", placeholder.Path);
                    metadata.Add("result.Result", result.ToString());
                    metadata.Add("result.RawResult", result.RawResult);
                    metadata.Add(TracingConstants.MessageKey.InfoMessage, "UpdateOrDeletePlaceholder: StatusIoReparseTagNotHandled");
                    this.context.Tracer.RelatedEvent(EventLevel.Informational, "UpdatePlaceholders_StatusIoReparseTagNotHandled", metadata);

                    break;

                case FSResult.VirtualizationInvalidOperation:
                    // GVFS attempted to update\delete a file that is no longer partial.
                    // This can occur if:
                    //
                    //    - A file is converted from partial to full (or tombstone) while a git command is running.
                    //      Any tasks scheduled during the git command to update the placeholder list have not yet
                    //      completed at this point.
                    //
                    //    - A placeholder file was converted to full without being in the index.  This can happen if 'git update-index --remove'
                    //      is used to remove a file from the index before converting the file to full.  Because a skip-worktree bit
                    //      is not cleared when this file file is converted to full, FileSystemCallbacks assumes that there is no placeholder
                    //      that needs to be removed removed from the list
                    //
                    //    - When there is a merge conflict the conflicting file will get the skip worktree bit removed. In some cases git
                    //      will not make any changes to the file in the working directory. In order to handle this case we have to check
                    //      the merge stage in the index and add that entry to the placeholder list. In doing so the files that git did
                    //      update in the working directory will be full files but we will have a placeholder entry for them as well.

                    // There have been reports of FileSystemVirtualizationInvalidOperation getting hit without a corresponding background
                    // task having been scheduled (to add the file to the modified paths).
                    // Schedule OnFailedPlaceholderUpdate\OnFailedPlaceholderDelete to be sure that Git starts managing this
                    // file.  Currently the only known way that this can happen is deleting a partial file and putting a full
                    // file in its place while GVFS is unmounted.
                    this.ScheduleBackgroundTaskForFailedUpdateDeletePlaceholder(placeholder, deleteOperation);
                    this.AddParentFoldersToListToKeep(parentKey, folderPlaceholdersToKeep);

                    metadata = CreateEventMetadata();
                    metadata.Add("deleteOperation", deleteOperation);
                    metadata.Add("virtualPath", placeholder.Path);
                    metadata.Add("result.Result", result.ToString());
                    metadata.Add("result.RawResult", result.RawResult);
                    metadata.Add("failureReason", failureReason.ToString());
                    metadata.Add("backgroundCount", this.backgroundFileSystemTaskRunner.Count);
                    metadata.Add(TracingConstants.MessageKey.InfoMessage, "UpdateOrDeletePlaceholder: attempted an invalid operation");
                    this.context.Tracer.RelatedEvent(EventLevel.Informational, "UpdatePlaceholders_InvalidOperation", metadata);

                    break;

                case FSResult.FileOrPathNotFound:
                    break;

                default:
                    {
                        string gitPath;
                        this.AddFileToUpdateDeletePlaceholderFailureReport(deleteOperation, placeholder, out gitPath);
                        this.ScheduleBackgroundTaskForFailedUpdateDeletePlaceholder(placeholder, deleteOperation);
                        this.AddParentFoldersToListToKeep(parentKey, folderPlaceholdersToKeep);

                        metadata = CreateEventMetadata();
                        metadata.Add("deleteOperation", deleteOperation);
                        metadata.Add("virtualPath", placeholder.Path);
                        metadata.Add("gitPath", gitPath);
                        metadata.Add("result.Result", result.ToString());
                        metadata.Add("result.RawResult", result.RawResult);
                        metadata.Add("failureReason", failureReason.ToString());
                        this.context.Tracer.RelatedWarning(metadata, "UpdateOrDeletePlaceholder: did not succeed");
                    }

                    break;
            }
        }

        private void AddParentFoldersToListToKeep(string parentKey, ConcurrentHashSet<string> folderPlaceholdersToKeep)
        {
            string folder = parentKey;
            while (!string.IsNullOrEmpty(folder))
            {
                folderPlaceholdersToKeep.Add(folder);
                this.GetChildNameAndParentKey(folder, out string _, out string parentFolder);
                folder = parentFolder;
            }
        }

        private void AddFileToUpdateDeletePlaceholderFailureReport(
            bool deleteOperation,
            IPlaceholderData placeholder,
            out string gitPath)
        {
            gitPath = placeholder.Path.TrimStart(Path.DirectorySeparatorChar).Replace(Path.DirectorySeparatorChar, GVFSConstants.GitPathSeparator);
            if (deleteOperation)
            {
                this.deletePlaceholderFailures.Add(gitPath);
            }
            else
            {
                this.updatePlaceholderFailures.Add(gitPath);
            }
        }

        private void ScheduleBackgroundTaskForFailedUpdateDeletePlaceholder(IPlaceholderData placeholder, bool deleteOperation)
        {
            if (deleteOperation)
            {
                this.backgroundFileSystemTaskRunner.Enqueue(FileSystemTask.OnFailedPlaceholderDelete(placeholder.Path));
            }
            else
            {
                this.backgroundFileSystemTaskRunner.Enqueue(FileSystemTask.OnFailedPlaceholderUpdate(placeholder.Path));
            }
        }

        private void LogErrorAndExit(string message, Exception e = null)
        {
            EventMetadata metadata = CreateEventMetadata(e);
            this.context.Tracer.RelatedError(metadata, message);
            Environment.Exit(1);
        }

        private void CopyIndexFileAndBuildProjection()
        {
            this.context.FileSystem.CopyFile(this.indexPath, this.projectionIndexBackupPath, overwrite: true);
            this.BuildProjection();
        }

        private void BuildProjection()
        {
            this.SetProjectionInvalid(false);

            using (ITracer tracer = this.context.Tracer.StartActivity("ParseGitIndex", EventLevel.Informational))
            {
                using (FileStream indexStream = new FileStream(this.projectionIndexBackupPath, FileMode.Open, FileAccess.Read, FileShare.Read, IndexFileStreamBufferSize))
                {
                    try
                    {
                        this.indexParser.RebuildProjection(tracer, indexStream);
                    }
                    catch (Exception e)
                    {
                        EventMetadata metadata = CreateEventMetadata(e);
                        this.context.Tracer.RelatedWarning(metadata, $"{nameof(this.BuildProjection)}: Exception thrown by {nameof(GitIndexParser.RebuildProjection)}");

                        this.SetProjectionInvalid(true);
                        throw;
                    }
                }

                SortedFolderEntries.ShrinkPool();
                LazyUTF8String.ShrinkPool();

                EventMetadata poolMetadata = CreateEventMetadata();
                poolMetadata.Add($"{nameof(SortedFolderEntries)}_{nameof(SortedFolderEntries.FolderPoolSize)}", SortedFolderEntries.FolderPoolSize());
                poolMetadata.Add($"{nameof(SortedFolderEntries)}_{nameof(SortedFolderEntries.FilePoolSize)}", SortedFolderEntries.FilePoolSize());
                poolMetadata.Add($"{nameof(LazyUTF8String)}_{nameof(LazyUTF8String.StringPoolSize)}", LazyUTF8String.StringPoolSize());
                poolMetadata.Add($"{nameof(LazyUTF8String)}_{nameof(LazyUTF8String.BytePoolSize)}", LazyUTF8String.BytePoolSize());
                TimeSpan duration = tracer.Stop(poolMetadata);
                this.context.Repository.GVFSLock.Stats.RecordParseGitIndex((long)duration.TotalMilliseconds);
            }
        }
    }
}
