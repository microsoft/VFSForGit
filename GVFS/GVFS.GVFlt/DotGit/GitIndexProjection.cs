using GVFS.Common;
using GVFS.Common.Git;
using GVFS.Common.NamedPipes;
using GVFS.Common.Tracing;
using GvLib;
using Microsoft.Diagnostics.Tracing;
using Microsoft.Isam.Esent.Collections.Generic;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace GVFS.GVFlt.DotGit
{
    public class GitIndexProjection : IDisposable, IProfilerOnlyIndexProjection
    {
        public const ushort ExtendedBit = 0x4000;
        public const ushort SkipWorktreeBit = 0x4000;
        public const int BaseEntryLength = 62;
        public const int MaxPathBufferSize = 4096;
        public const int IndexFileStreamBufferSize = 4096 * 10;

        public const string ProjectionIndexBackupName = "GVFS_projection";
        
        private const UpdateType PlaceholderUpdateFlags = UpdateType.AllowDirtyMetadata | UpdateType.AllowReadOnly;

        private const string EtwArea = "GitIndexProjection";

        private const int ExternalLockReleaseTimeoutMs = 50;

        private static readonly DateTime UnixEpoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        private char[] gitPathSeparatorCharArray = new char[] { GVFSConstants.GitPathSeparator };

        private GVFSContext context;
        private RepoMetadata repoMetadata;
        private IVirtualizationInstance gvflt;
        private SparseCheckout sparseCheckout;

        private FileOrFolderData rootFolderData;
        
        // Cache of folder paths (in Windows format) to folder data
        private ConcurrentDictionary<string, FileOrFolderData> projectionFolderCache;

        private PersistentDictionary<string, long> blobSizes;
        private PlaceholderListDatabase placeholderList;
        private GVFSGitObjects gitObjects;
        private ReliableBackgroundOperations background;
        private ReaderWriterLockSlim projectionReadWriteLock;
        private ManualResetEventSlim projectionParseComplete;
        private ManualResetEventSlim externalLockReleaseHandlingComplete;

        private volatile bool offsetsInvalid;        
        private volatile bool projectionInvalid;

        // Number of times that the negative path cache has (potentially) been updated by GVFS preventing
        // git from creating a placeholder (since the last time the cache was cleared)
        private int negativePathCacheUpdatedForGitCount;

        // sparseCheckoutInvalid: If true, a change to the index that did not trigger a new projection 
        // has been made and GVFS has not yet validated that all entries whose skip-worktree bit is  
        // cleared are in the sparse-checkout
        private volatile bool sparseCheckoutInvalid; 

        private ConcurrentHashSet<string> updatePlaceholderFailures;
        private ConcurrentHashSet<string> deletePlaceholderFailures;

        // lastUpdateTime is used by GitIndexProjection to know if offsets in rootFolderData (and projectionFolderCache) are still valid.  Each time
        // the offsets are reparsed, lastUpdateTime is increased by one.  When GitIndexProjection looks up an offset it
        // will only treat that offset as valid if its LastUpdateTime matches this.lastUpdateTime
        private uint lastUpdateTime;

        private string projectionIndexBackupPath;
        private string indexPath;

        private FileStream indexFileStream;
        private FileBasedLock gitIndexLock;

        private AutoResetEvent wakeUpThread;
        private Task backgroundThread;
        private bool isStopping;

        public GitIndexProjection(
            GVFSContext context,
            GVFSGitObjects gitObjects,
            PersistentDictionary<string, long> blobSizes,
            RepoMetadata repoMetadata,
            IVirtualizationInstance gvflt,
            PlaceholderListDatabase placeholderList,
            SparseCheckout sparseCheckout)
        {
            this.context = context;
            this.gitObjects = gitObjects;
            this.blobSizes = blobSizes;
            this.repoMetadata = repoMetadata;
            this.gvflt = gvflt;

            this.projectionReadWriteLock = new ReaderWriterLockSlim();
            this.projectionParseComplete = new ManualResetEventSlim(false);
            this.wakeUpThread = new AutoResetEvent(false);
            this.projectionIndexBackupPath = Path.Combine(this.context.Enlistment.DotGVFSRoot, ProjectionIndexBackupName);
            this.indexPath = Path.Combine(this.context.Enlistment.WorkingDirectoryRoot, GVFSConstants.DotGit.Index);
            this.externalLockReleaseHandlingComplete = new ManualResetEventSlim(false);
            this.placeholderList = placeholderList;
            this.sparseCheckout = sparseCheckout;
        }

        // For Unit Testing
        protected GitIndexProjection()
        {
        }

        private enum MergeStage : byte
        {
            NoConflicts = 0,
            CommonAncestor = 1,
            Yours = 2,
            Theirs = 3
        }

        private enum IndexAction
        {
            RebuildProjection,
            UpdateOffsets,
            ValidateSparseCheckout,
            UpdateOffsetsAndValidateSparseCheckout,
            ValidateIndex,
        }

        public int EstimatedPlaceholderCount
        {
            get
            {
                return this.placeholderList.EstimatedCount;
            }
        }

        public static void ReadIndex(string indexPath)
        {
            using (FileStream indexStream = new FileStream(indexPath, FileMode.Open, FileAccess.ReadWrite, FileShare.Read, IndexFileStreamBufferSize))
            {
                CallbackResult result = PerformIndexAction(
                    projection: null,
                    indexFileStream: indexStream,
                    action: IndexAction.ValidateIndex);

                if (result != CallbackResult.Success)
                {
                    // ValidateIndex should always result in CallbackResult.Success (or a thrown exception)
                    throw new InvalidOperationException("ParseIndexAndBuildProjection: PerformIndexAction failed to validate index");
                }
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
        /// Force the index file to be parsed to update offsets and validate the sparse checkout.  
        /// This method should only be used to measure index parsing performance.
        /// </summary>
        void IProfilerOnlyIndexProjection.ForceUpdateOffsetsAndValidateSparseCheckout()
        {
            using (FileStream indexStream = new FileStream(this.indexPath, FileMode.Open, FileAccess.ReadWrite, FileShare.Read, IndexFileStreamBufferSize))
            {
                PerformIndexAction(this, indexStream, IndexAction.UpdateOffsetsAndValidateSparseCheckout);
            }
        }

        /// <summary>
        /// Force the index file to be parsed to validate the sparse-checkout.  
        /// This method should only be used to measure index parsing performance.
        /// </summary>
        void IProfilerOnlyIndexProjection.ForceValidateSparseCheckout()
        {
            using (FileStream indexStream = new FileStream(this.indexPath, FileMode.Open, FileAccess.ReadWrite, FileShare.Read, IndexFileStreamBufferSize))
            {
                PerformIndexAction(this, indexStream, IndexAction.ValidateSparseCheckout);
            }
        }

        public void BuildProjectionFromPath(string indexPath)
        {
            using (FileStream indexStream = new FileStream(indexPath, FileMode.Open, FileAccess.ReadWrite, FileShare.Read, IndexFileStreamBufferSize))
            {
                this.ParseIndexAndBuildProjection(indexStream);
            }
        }

        public virtual void Initialize(ReliableBackgroundOperations backgroundQueue)
        {
            if (!File.Exists(this.indexPath))
            {
                string message = "GVFS requires the .git\\index to exist";
                EventMetadata metadata = CreateEventMetadata();
                this.context.Tracer.RelatedError(metadata, message);
                throw new FileNotFoundException(message);
            }

            this.gitIndexLock = new FileBasedLock(
                this.context.FileSystem,
                this.context.Tracer,
                Path.Combine(this.context.Enlistment.WorkingDirectoryRoot, GVFSConstants.DotGit.Index + GVFSConstants.DotGit.LockExtension),
                "GVFS",
                cleanupStaleLock: true,
                overwriteExistingLock: false);

            this.background = backgroundQueue;

            this.projectionReadWriteLock.EnterWriteLock();

            this.projectionInvalid = this.repoMetadata.GetProjectionInvalid();

            try
            {
                if (!this.context.FileSystem.FileExists(this.projectionIndexBackupPath) || this.projectionInvalid)
                {
                    this.CopyIndexFileAndBuildProjection();
                }
                else
                {                    
                    this.BuildProjection();

                    // Set offsetsInvalid to true because we're projecting something other than the current index
                    // (and so whatever offsets were just loaded into the projection are no longer up-to-date)
                    this.offsetsInvalid = true;
                }
            }
            finally
            {
                this.projectionReadWriteLock.ExitWriteLock();
            }

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

            this.backgroundThread = Task.Factory.StartNew((Action)this.ParseIndexThreadMain, TaskCreationOptions.LongRunning);            
        }

        public virtual void Shutdown()
        {
            this.isStopping = true;
            this.wakeUpThread.Set();
            this.backgroundThread.Wait();
        }

        public NamedPipeMessages.ReleaseLock.Response TryReleaseExternalLock(int pid)
        {
            // GitIndexProjection must make sure that nothing can read from its projection between
            // git releasing its lock and the parsing thread parsing the index and updating all placeholders.
            //
            // Example:
            //
            //    git checkout master
            //    type Readme.md  <-- This must use the new projection
            //
            // To ensure this happens, this method will not return until the parsing thread has signaled that it
            // has completed its processing
            this.externalLockReleaseHandlingComplete.Reset();
            if (this.context.Repository.GVFSLock.ReleaseExternalLock(pid))
            {
                this.ValidateNegativePathCache();

                // If the parsing thread is not waiting to parse the index, then there is no need to wait for it.
                if (!this.IsProjectionParseComplete())
                {
                    // Wait for the parsing thread to complete its work
                    this.externalLockReleaseHandlingComplete.Wait();

                    ConcurrentHashSet<string> updateFailures = this.updatePlaceholderFailures;
                    this.updatePlaceholderFailures = new ConcurrentHashSet<string>();

                    ConcurrentHashSet<string> deleteFailures = this.deletePlaceholderFailures;
                    this.deletePlaceholderFailures = new ConcurrentHashSet<string>();

                    if (updateFailures.Count != 0 || deleteFailures.Count != 0)
                    {
                        // Return SuccessResult as the lock was successfully released
                        return new NamedPipeMessages.ReleaseLock.Response(
                            NamedPipeMessages.ReleaseLock.SuccessResult,
                            new NamedPipeMessages.ReleaseLock.ReleaseLockData(
                                new List<string>(updateFailures),
                                new List<string>(deleteFailures)));
                    }
                }

                return new NamedPipeMessages.ReleaseLock.Response(NamedPipeMessages.ReleaseLock.SuccessResult);
            }

            return new NamedPipeMessages.ReleaseLock.Response(NamedPipeMessages.ReleaseLock.FailureResult);
        }

        public bool IsProjectionParseComplete()
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

            this.SetProjectionAndPlaceholdersAndOffsetsAsInvalid();
            this.wakeUpThread.Set();
        }

        public bool IsIndexBeingUpdatedByGVFS()
        {
            return this.gitIndexLock.IsOpen();
        }

        public void InvalidateOffsetsAndSparseCheckout()
        {
            this.context.Tracer.RelatedEvent(EventLevel.Informational, "InvalidateOffsetsAndSparseCheckout", null);

            this.offsetsInvalid = true;
            this.sparseCheckoutInvalid = true;            
        }
        
        public void OnPlaceholderCreateBlockedForGit()
        {
            int count = Interlocked.Increment(ref this.negativePathCacheUpdatedForGitCount);
            if (count == 1)
            {
                // If placeholder creation is blocked multiple times, only queue a single background task
                this.background.Enqueue(GVFltCallbacks.BackgroundGitUpdate.OnPlaceholderCreationsBlockedForGit());
            }
        }

        public void ValidateNegativePathCache()
        {
            int count = Interlocked.Exchange(ref this.negativePathCacheUpdatedForGitCount, 0);
            if (count > 0)
            {
                this.ClearGvFltNegativePathCache();
            }
        }

        public virtual void OnPlaceholderFileCreated(string virtualPath, string sha)
        {
            this.placeholderList.AddAndFlush(virtualPath, sha);
        }

        public virtual bool TryGetProjectedItemsFromMemory(string folderPath, out IEnumerable<GVFltFileInfo> projectedItems)
        {
            projectedItems = null;

            this.projectionReadWriteLock.EnterReadLock();

            try
            {
                FileOrFolderData folderData;
                if (this.TryGetOrAddFolderDataFromCache(folderPath, out folderData))
                {
                    if (folderData.ChildrenHaveSizes)
                    {
                        List<GVFltFileInfo> childItems = new List<GVFltFileInfo>(folderData.ChildEntries.Count);
                        foreach (KeyValuePair<string, FileOrFolderData> childEntry in folderData.ChildEntries)
                        {
                            childItems.Add(new GVFltFileInfo(
                                childEntry.Key,
                                childEntry.Value.IsFolder ? 0 : childEntry.Value.Size,
                                childEntry.Value.IsFolder));
                        }

                        projectedItems = childItems;
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

        public virtual IEnumerable<GVFltFileInfo> GetProjectedItems(string folderPath, CancellationToken cancellationToken)
        {
            this.projectionReadWriteLock.EnterReadLock();

            try
            {
                FileOrFolderData folderData;
                if (this.TryGetOrAddFolderDataFromCache(folderPath, out folderData))
                {
                    folderData.FolderOnly_PopulateSizes(this.context.Tracer, this.gitObjects, this.blobSizes, cancellationToken);
                    List<GVFltFileInfo> childItems = new List<GVFltFileInfo>(folderData.ChildEntries.Count);
                    foreach (KeyValuePair<string, FileOrFolderData> childEntry in folderData.ChildEntries)
                    {
                        childItems.Add(new GVFltFileInfo(
                            childEntry.Key,
                            childEntry.Value.IsFolder ? 0 : childEntry.Value.Size,
                            childEntry.Value.IsFolder));
                    }

                    return childItems;
                }

                return new List<GVFltFileInfo>();
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
            FileOrFolderData data = this.GetProjectedFileOrFolderData(fileName, parentKey, populateSize: false);
            if (data != null)
            {
                isFolder = data.IsFolder;
                return true;
            }

            return false;
        }

        public virtual GVFltFileInfo GetProjectedGVFltFileInfoAndSha(CancellationToken cancellationToken, string virtualPath, out string parentFolderPath, out string sha)
        {
            sha = string.Empty;
            string childName;
            string parentKey;
            this.GetChildNameAndParentKey(virtualPath, out childName, out parentKey);
            parentFolderPath = parentKey;
            string gitCasedChildName;
            FileOrFolderData data = this.GetProjectedFileOrFolderData(
                childName, 
                parentKey, 
                populateSize: true, 
                cancellationToken: cancellationToken, 
                gitCasedChildName: out gitCasedChildName);

            if (data != null)
            {
                if (data.IsFolder)
                {
                    return new GVFltFileInfo(gitCasedChildName, size: 0, isFolder: true);
                }

                sha = data.ConvertShaToString();
                return new GVFltFileInfo(gitCasedChildName, data.Size, isFolder: false);
            }

            return null;
        }

        public CallbackResult AcquireIndexLockAndOpenForWrites()
        {
            if (!File.Exists(this.indexPath))
            {
                EventMetadata metadata = CreateEventMetadata();
                this.context.Tracer.RelatedError(metadata, "AcquireIndexLockAndOpenForWrites: Can't open the index because it doesn't exist");

                return CallbackResult.FatalError;
            }

            if (!this.gitIndexLock.TryAcquireLockAndDeleteOnClose())
            {
                EventMetadata metadata = CreateEventMetadata();
                metadata.Add(TracingConstants.MessageKey.VerboseMessage, "AcquireIndexLockAndOpenForWrites: Can't aquire index lock");
                this.context.Tracer.RelatedEvent(EventLevel.Verbose, "OpenCantAcquireIndexLock", metadata);

                return CallbackResult.RetryableError;
            }

            this.projectionParseComplete.Wait();

            CallbackResult result = CallbackResult.FatalError;
            try
            {
                this.indexFileStream = new FileStream(this.indexPath, FileMode.Open, FileAccess.ReadWrite, FileShare.Read, IndexFileStreamBufferSize);
                result = CallbackResult.Success;
            }
            catch (IOException e)
            {
                EventMetadata metadata = CreateEventMetadata(e);
                this.context.Tracer.RelatedWarning(metadata, "IOException in AcquireIndexLockAndOpenForWrites (Retryable)");
                result = CallbackResult.RetryableError;
            }
            catch (Exception e)
            {
                EventMetadata metadata = CreateEventMetadata(e);
                this.context.Tracer.RelatedError(metadata, "Exception in AcquireIndexLockAndOpenForWrites (FatalError)");
                result = CallbackResult.FatalError;
            }
            finally
            {
                if (result != CallbackResult.Success)
                {
                    if (!this.gitIndexLock.TryReleaseLock())
                    {
                        EventMetadata metadata = CreateEventMetadata();
                        this.context.Tracer.RelatedError(metadata, "Unable to release index.lock in AcquireIndexLockAndOpenForWrites (FatalError)");
                        result = CallbackResult.FatalError;
                    }
                }
            }

            return result;
        }

        public CallbackResult ReleaseLockAndClose()
        {
            if (this.indexFileStream != null)
            {
                this.indexFileStream.Dispose();
                this.indexFileStream = null;
            }

            try
            {
                if (!this.gitIndexLock.IsOpen() ||
                    this.gitIndexLock.TryReleaseLock())
                {
                    return CallbackResult.Success;
                }
            }
            catch (Exception e)
            {
                EventMetadata metadata = CreateEventMetadata(e);
                this.context.Tracer.RelatedError(metadata, "Fatal Exception in ReleaseLockAndClose");
            }

            return CallbackResult.FatalError;
        }

        public CallbackResult ValidateSparseCheckout()
        {
            try
            {
                if (this.sparseCheckoutInvalid)
                {
                    CallbackResult result = PerformIndexAction(this, this.indexFileStream, IndexAction.ValidateSparseCheckout);
                    if (result == CallbackResult.Success)
                    {
                        this.sparseCheckoutInvalid = false;
                    }

                    return result;
                }
            }
            catch (IOException e)
            {
                EventMetadata metadata = CreateEventMetadata(e);
                this.context.Tracer.RelatedWarning(metadata, "IOException in ValidateSparseCheckout (Retryable)");

                return CallbackResult.RetryableError;
            }
            catch (Exception e)
            {
                EventMetadata metadata = CreateEventMetadata(e);
                this.context.Tracer.RelatedError(metadata, "Exception in ValidateSparseCheckout (FatalError)");

                return CallbackResult.FatalError;
            }

            return CallbackResult.Success;
        }

        /// <summary>
        /// Update the index file offsets that GVFS has cached in memory.
        /// </summary>
        /// <remarks>
        /// As a performance optimization, UpdateOffsets will also validate the sparse-checkout file (if it needs validation).
        /// </remarks>
        public CallbackResult UpdateOffsets()
        {
            try
            {
                if (this.offsetsInvalid)
                {                    
                    if (this.lastUpdateTime == uint.MaxValue)
                    {
                        this.lastUpdateTime = 0;
                    }
                    else
                    {
                        ++this.lastUpdateTime;
                    }

                    // Performance optimization: If sparseCheckoutInvalid is true, save GVFS from reading the index a second time by 
                    // updating offsets and validating the sparse-checkout in a single pass
                    CallbackResult result = PerformIndexAction(
                        this,
                        this.indexFileStream, 
                        this.sparseCheckoutInvalid ? IndexAction.UpdateOffsetsAndValidateSparseCheckout : IndexAction.UpdateOffsets);
                    if (result == CallbackResult.Success)
                    {
                        this.sparseCheckoutInvalid = false;
                        this.offsetsInvalid = false;
                    }

                    return result;
                }
            }
            catch (IOException e)
            {
                EventMetadata metadata = CreateEventMetadata(e);
                this.context.Tracer.RelatedWarning(metadata, "IOException in UpdateOffsets (Retryable)");

                return CallbackResult.RetryableError;
            }
            catch (Exception e)
            {
                EventMetadata metadata = CreateEventMetadata(e);
                this.context.Tracer.RelatedError(metadata, "Exception in UpdateOffsets (FatalError)");

                return CallbackResult.FatalError;
            }

            return CallbackResult.Success;
        }

        public CallbackResult ClearSkipWorktreeBit(string filePath, out bool skipWorktreeBitCleared)
        {
            skipWorktreeBitCleared = false;

            try
            {
                CallbackResult updateOffsetsResult = this.UpdateOffsets();
                if (updateOffsetsResult != CallbackResult.Success)
                {
                    return updateOffsetsResult;
                }                

                long offset;
                if (this.TryGetIndexPathOffset(filePath, out offset))
                {
                    skipWorktreeBitCleared = true;
                    this.indexFileStream.Seek(offset + 62, SeekOrigin.Begin);  // + 62 for: ctime + mtime + dev + ino + mode + uid + gid + size + sha + flags
                    this.indexFileStream.Write(new byte[2] { 0, 0 }, 0, 2);    // extended flags
                    this.indexFileStream.Flush();
                }
            }
            catch (IOException e)
            {
                EventMetadata metadata = CreateEventMetadata(e);
                this.context.Tracer.RelatedWarning(metadata, "IOException in ClearSkipWorktreeBit (Retryable)");

                return CallbackResult.RetryableError;
            }
            catch (Exception e)
            {
                EventMetadata metadata = CreateEventMetadata(e);
                this.context.Tracer.RelatedError(metadata, "Exception in ClearSkipWorktreeBit (FatalError)");

                return CallbackResult.FatalError;
            }

            return CallbackResult.Success;
        }

        public void RemoveFromPlaceholderList(string filePath)
        {
            this.placeholderList.RemoveAndFlush(filePath);
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

                if (this.wakeUpThread != null)
                {
                    this.wakeUpThread.Dispose();
                    this.wakeUpThread = null;
                }

                if (this.externalLockReleaseHandlingComplete != null)
                {
                    this.externalLockReleaseHandlingComplete.Dispose();
                    this.externalLockReleaseHandlingComplete = null;
                }

                if (this.backgroundThread != null)
                {
                    this.backgroundThread.Dispose();
                    this.backgroundThread = null;
                }

                if (this.gitIndexLock != null)
                {
                    this.gitIndexLock.Dispose();
                    this.gitIndexLock = null;
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

            int separatorIndex = virtualPath.LastIndexOf(GVFSConstants.PathSeparator);
            if (separatorIndex < 0)
            {
                childName = virtualPath;
                return;
            }

            childName = virtualPath.Substring(separatorIndex + 1);
            parentKey = virtualPath.Substring(0, separatorIndex);
        }

        private static int ReadReplaceLength(Stream stream)
        {
            int headerByte = stream.ReadByte();
            int offset = headerByte & 0x7f;

            // Terminate the loop when the high bit is no longer set.
            for (int i = 0; (headerByte & 0x80) != 0; i++)
            {
                headerByte = stream.ReadByte();
                if (headerByte < 0)
                {
                    throw new EndOfStreamException("Unexpected end of stream while reading git index.");
                }

                offset += 1;
                offset = (offset << 7) + (headerByte & 0x7f);
            }

            return offset;
        }

        private static uint ReadUInt32(byte[] buffer, Stream stream)
        {
            buffer[3] = (byte)stream.ReadByte();
            buffer[2] = (byte)stream.ReadByte();
            buffer[1] = (byte)stream.ReadByte();
            buffer[0] = (byte)stream.ReadByte();

            return BitConverter.ToUInt32(buffer, 0);
        }

        private static ushort ReadUInt16(byte[] buffer, Stream stream)
        {
            buffer[1] = (byte)stream.ReadByte();
            buffer[0] = (byte)stream.ReadByte();

            // (ushort)BitConverter.ToInt16 avoids the running the duplicated checks in ToUInt16
            return (ushort)BitConverter.ToInt16(buffer, 0);
        }

        private static uint ToUnixNanosecondFraction(DateTime datetime)
        {
            if (datetime > UnixEpoch)
            {
                TimeSpan timediff = datetime - UnixEpoch;
                double nanoseconds = (timediff.TotalSeconds - Math.Truncate(timediff.TotalSeconds)) * 1000000000;
                return Convert.ToUInt32(nanoseconds);
            }
            else
            {
                return 0;
            }
        }

        private static uint ToUnixEpochSeconds(DateTime datetime)
        {
            if (datetime > UnixEpoch)
            {
                return Convert.ToUInt32(Math.Truncate((datetime - UnixEpoch).TotalSeconds));
            }
            else
            {
                return 0;
            }
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

        /// <summary>
        /// Takes an action using the index in indexFileStream
        /// </summary>
        /// <param name="projection">The GitIndexProjection to use for actions other than IndexAction.ValidateIndex</param>
        /// <param name="indexFileStream">FileStream for a git index file</param>
        /// <param name="action">Action to take using the specified index</param>
        /// <returns>
        /// CallbackResult indicating success or failure of the specified action
        /// </returns>
        /// <remarks>
        /// Only actions that update the sparse-checkout file (ValidateSparseCheckout, UpdateOffsetsAndValidateSparseCheckout) can result
        /// in TryIndexAction returning a CallbackResult other than Success.  All other actions result in success (or an exception in the
        /// case of a corrupt index)
        /// </remarks>
        private static CallbackResult PerformIndexAction(GitIndexProjection projection, FileStream indexFileStream, IndexAction action)
        {
            if (projection == null && action != IndexAction.ValidateIndex)
            {
                throw new ArgumentNullException("projection", "projection cannot be null for actions other than IndexAction.ValidateIndex");
            }

            byte[] buffer = new byte[40];
            indexFileStream.Position = 0;

            indexFileStream.Read(buffer, 0, 4);
            if (buffer[0] != 'D' ||
                buffer[1] != 'I' ||
                buffer[2] != 'R' ||
                buffer[3] != 'C')
            {
                throw new InvalidDataException("Incorrect magic signature for index: " + string.Join(string.Empty, buffer.Take(4).Select(c => (char)c)));
            }

            uint indexVersion = ReadUInt32(buffer, indexFileStream);
            if (indexVersion != 4)
            {
                throw new InvalidDataException("Unsupported index version: " + indexVersion);
            }

            uint entryCount = ReadUInt32(buffer, indexFileStream);

            if (action == IndexAction.RebuildProjection)
            {
                projection.projectionFolderCache = new ConcurrentDictionary<string, FileOrFolderData>(StringComparer.OrdinalIgnoreCase);
                projection.rootFolderData = new FileOrFolderData();
            }

            FileOrFolderData lastParent = null;
            string lastParentPath = null;

            int previousPathLength = 0;
            byte[] pathBuffer = new byte[MaxPathBufferSize];
            byte[] sha = new byte[20];
            for (int i = 0; i < entryCount; i++)
            {
                long entryOffset = indexFileStream.Position;
                indexFileStream.Read(buffer, 0, 40);
                indexFileStream.Read(sha, 0, 20);

                ushort flags = ReadUInt16(buffer, indexFileStream);
                MergeStage mergeStage = (MergeStage)((flags >> 12) & 3);
                bool isExtended = (flags & ExtendedBit) == ExtendedBit;
                int pathLength = (ushort)(flags & 0xFFF);

                bool skipWorktree = false;
                if (isExtended)
                {
                    ushort extendedFlags = ReadUInt16(buffer, indexFileStream);
                    skipWorktree = (extendedFlags & SkipWorktreeBit) == SkipWorktreeBit;
                }

                int replaceLength = ReadReplaceLength(indexFileStream);
                int replaceIndex = previousPathLength - replaceLength;
                int value = pathLength - replaceIndex + 1;
                indexFileStream.Read(pathBuffer, replaceIndex, value);
                previousPathLength = pathLength;

                switch (action)
                {
                    case IndexAction.RebuildProjection:
                        if (skipWorktree)
                        {
                            string path = Encoding.UTF8.GetString(pathBuffer, 0, pathLength);
                            projection.AddItem(path, sha, entryOffset, ref lastParent, ref lastParentPath);
                        }
                        else if (mergeStage == MergeStage.Yours)
                        {
                            string path = Encoding.UTF8.GetString(pathBuffer, 0, pathLength);
                            projection.AddItem(path, sha, FileOrFolderData.InvalidOffset, ref lastParent, ref lastParentPath);
                        }

                        break;

                    case IndexAction.UpdateOffsets:
                        if (skipWorktree)
                        {
                            string path = Encoding.UTF8.GetString(pathBuffer, 0, pathLength);
                            projection.UpdateFileOffset(path, entryOffset, ref lastParent, ref lastParentPath);
                        }

                        break;

                    case IndexAction.ValidateSparseCheckout:
                        if (!skipWorktree)
                        {
                            // A git command (e.g. 'git reset --mixed') may have cleared a file's skip worktree bit without
                            // updating the sparse-checkout file.  Ensure this file is in the sparse-checkout file
                            string path = Encoding.UTF8.GetString(pathBuffer, 0, pathLength);
                            CallbackResult updateSparseCheckoutResult = projection.sparseCheckout.AddFileEntryFromIndex(path);
                            if (updateSparseCheckoutResult != CallbackResult.Success)
                            {
                                return updateSparseCheckoutResult;
                            }
                        }

                        break;

                    case IndexAction.UpdateOffsetsAndValidateSparseCheckout:
                        {
                            string path = Encoding.UTF8.GetString(pathBuffer, 0, pathLength);
                            if (skipWorktree)
                            {
                                projection.UpdateFileOffset(path, entryOffset, ref lastParent, ref lastParentPath);
                            }
                            else
                            {
                                CallbackResult updateSparseCheckoutResult = projection.sparseCheckout.AddFileEntryFromIndex(path);
                                if (updateSparseCheckoutResult != CallbackResult.Success)
                                {
                                    return updateSparseCheckoutResult;
                                }
                            }
                        }

                        break;

                    case IndexAction.ValidateIndex:
                        {
                            // Validate that pathBuffer can be converted to a string
                            string path = Encoding.UTF8.GetString(pathBuffer, 0, pathLength);
                            if (path.Length == 0)
                            {
                                throw new InvalidDataException("Zero-length path found in index");
                            }
                        }

                        break;

                    default:
                        throw new ArgumentOutOfRangeException(nameof(action));
                }
            }

            return CallbackResult.Success;
        }

        private bool TryGetSha(string childName, string parentKey, out string sha)
        {
            sha = string.Empty;
            FileOrFolderData data = this.GetProjectedFileOrFolderData(childName, parentKey, populateSize: false);
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

        private void SetProjectionAndPlaceholdersAndOffsetsAsInvalid()
        {
            this.projectionInvalid = true;
            this.repoMetadata.SetProjectionInvalidAndPlaceholdersNeedUpdate();

            this.offsetsInvalid = true;
        }

        private void AddItem(string gitPath, byte[] shaBytes, long offset, ref FileOrFolderData lastParent, ref string lastParentKey)
        {
            int separatorIndex;
            string parentKey = this.GetParentKey(gitPath, out separatorIndex);
            if (parentKey.Equals(lastParentKey))
            {
                FileOrFolderData newFileData = new FileOrFolderData(shaBytes, offset);
                lastParent.AddChild(this.context.Tracer, this.GetChildName(gitPath, separatorIndex), newFileData);
            }
            else
            {
                lastParentKey = parentKey;                
                if (separatorIndex < 0)
                {
                    lastParent = this.rootFolderData;
                    FileOrFolderData newFileData = new FileOrFolderData(shaBytes, offset);                    
                    lastParent.AddChild(this.context.Tracer, gitPath, newFileData);
                }
                else
                {
                    // NOTE: Testing has revealed that if the call to gitPath.Split is moved to the start of 
                    // this method memory usage will be noticably lower.  However, performance will also be 
                    // worse and so the call to Split is being left here.
                    string[] pathParts = gitPath.Split(this.gitPathSeparatorCharArray);
                    FileOrFolderData newFileData = new FileOrFolderData(shaBytes, offset);
                    lastParent = this.AddFileToTree(pathParts, newFileData);
                }
            }
        }

        private void UpdateFileOffset(string gitPath, long offset, ref FileOrFolderData lastParent, ref string lastParentKey)
        {
            int separatorIndex;
            string parentKey = this.GetParentKey(gitPath, out separatorIndex);
            if (parentKey.Equals(lastParentKey))
            {
                lastParent.UpdateChildOffset(
                    this.context.Tracer, 
                    this.GetChildName(gitPath, separatorIndex), 
                    offset, 
                    this.lastUpdateTime);
            }
            else
            {
                lastParentKey = parentKey;               
                if (separatorIndex < 0)
                {
                    lastParent = this.rootFolderData;
                    lastParent.UpdateChildOffset(this.context.Tracer, gitPath, offset, this.lastUpdateTime);
                }
                else
                {
                    string[] pathParts = gitPath.Split(this.gitPathSeparatorCharArray);
                    string childName = pathParts[pathParts.Length - 1];
                    if (this.TryGetParentFolderDataFromTree(pathParts, fileOrFolderData: out lastParent))
                    {
                        lastParent.UpdateChildOffset(this.context.Tracer, childName, offset, this.lastUpdateTime);
                    }
                    else
                    {
                        // TODO 1083624: Improve GVFS's detection of this scenario

                        EventMetadata metadata = CreateEventMetadata();
                        metadata.Add("gitPath", gitPath);
                        metadata.Add("parentKey", parentKey);
                        metadata.Add("offset", offset);
                        this.context.Tracer.RelatedWarning(metadata, "UpdateFileOffset: Failed to find parentKey", Keywords.Telemetry);
                    }
                }
            }
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

        private bool TryGetIndexPathOffset(string virtualPath, out long offset)
        {
            FileOrFolderData parentFolderData;
            string childName;
            string parentKey;
            this.GetChildNameAndParentKey(virtualPath, out childName, out parentKey);
            if (!this.TryGetOrAddFolderDataFromCache(parentKey, out parentFolderData))
            {
                offset = FileOrFolderData.InvalidOffset;
                return false;
            }

            FileOrFolderData fileData;
            if (parentFolderData.ChildEntries.TryGetValue(childName, out fileData))
            {
                if (!fileData.IsFolder && fileData.LastUpdateTime == this.lastUpdateTime && fileData.Offset >= 0)
                {
                    offset = fileData.Offset;
                    return true;
                }
            }

            offset = FileOrFolderData.InvalidOffset;
            return false;
        }

        /// <summary>
        /// Add a FileOrFolderData to the tree
        /// </summary>
        /// <param name="pathParts">childData's path</param>
        /// <param name="childData">FileOrFolderData to add to the tree</param>
        /// <returns>The FileOrFolderData for childData's parent</returns>
        /// <remarks>This method will create and add any intermediate FileOrFolderDatas that are
        /// required but not already in the tree.  For example, if the tree was completely empty
        /// and AddFileToTree was called for the path \A\B\C.txt:
        /// 
        ///    pathParts -> { "A", "B", "C.txt"}
        /// 
        ///    AddFileToTree would create new FileOrFolderData entries in the tree for "A" and "B"
        ///    and return the FileOrFolderData entry for "B"
        /// </remarks>
        private FileOrFolderData AddFileToTree(string[] pathParts, FileOrFolderData childData)
        {            
            FileOrFolderData parentFolder = this.rootFolderData;
            FileOrFolderData childEntry = null;
            for (int pathIndex = 0; pathIndex < pathParts.Length - 1; ++pathIndex)
            {
                if (!parentFolder.IsFolder)
                {
                    string parentFolderName = pathIndex > 0 ? pathParts[pathIndex - 1] : "<root>";
                    string gitPath = string.Join(GVFSConstants.GitPathSeparatorString, pathParts);

                    EventMetadata metadata = CreateEventMetadata();
                    metadata.Add("gitPath", gitPath);
                    metadata.Add("parentFolder", parentFolderName);
                    this.context.Tracer.RelatedError(metadata, "AddFileToTree: Found a file where a folder was expected");

                    throw new InvalidDataException("Found a file (" + parentFolderName + ") where a folder was expected: " + gitPath);
                }

                if (!parentFolder.ChildEntries.TryGetValue(pathParts[pathIndex], out childEntry))
                {
                    childEntry = new FileOrFolderData();
                    parentFolder.AddChild(this.context.Tracer, pathParts[pathIndex], childEntry);
                }
                
                parentFolder = childEntry;
            }

            parentFolder.AddChild(this.context.Tracer, pathParts[pathParts.Length - 1], childData);

            return parentFolder;
        }
        
        private FileOrFolderData GetProjectedFileOrFolderData(string childName, string parentKey, bool populateSize, CancellationToken cancellationToken, out string gitCasedChildName)
        {
            this.projectionReadWriteLock.EnterReadLock();
            try
            {
                FileOrFolderData parentFolderData;
                if (this.TryGetOrAddFolderDataFromCache(parentKey, out parentFolderData))
                {
                    int childIndex = parentFolderData.ChildEntries.IndexOfKey(childName);                    
                    if (childIndex >= 0)
                    {
                        gitCasedChildName = parentFolderData.ChildEntries.Keys[childIndex];
                        FileOrFolderData childData = parentFolderData.ChildEntries.Values[childIndex];
                        if (populateSize && !childData.IsFolder && !childData.IsSizeSet())
                        {
                            string sha;
                            if (!childData.FileOnly_TryPopulateSizeLocally(this.gitObjects, this.blobSizes, out sha))
                            {
                                parentFolderData.FolderOnly_PopulateSizes(this.context.Tracer, this.gitObjects, this.blobSizes, cancellationToken);
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
        
        private FileOrFolderData GetProjectedFileOrFolderData(string childName, string parentKey, bool populateSize)
        {
            string gitCasedChildName;
            return this.GetProjectedFileOrFolderData(childName, parentKey, populateSize, CancellationToken.None, out gitCasedChildName);
        }

        /// <summary>
        /// Try to get the FileOrFolderData for the specified folder path from the projectionFolderCache
        /// cache.  If the  folder is not already in projectionFolderCache, search for it in the tree and
        /// then add it to projectionData
        /// </summary>        
        /// <returns>True if the folder could be found, and false otherwise</returns>
        private bool TryGetOrAddFolderDataFromCache(
            string folderPath, 
            out FileOrFolderData folderData)
        {
            if (!this.projectionFolderCache.TryGetValue(folderPath, out folderData))
            {
                string[] pathParts = folderPath.Split(new char[] { GVFSConstants.PathSeparator }, StringSplitOptions.RemoveEmptyEntries);
                if (!this.TryGetFileOrFolderDataFromTree(pathParts, fileOrFolderData: out folderData))
                {
                    folderPath = null;
                    return false;
                }

                if (folderData.IsFolder)
                {
                    this.projectionFolderCache.TryAdd(folderPath, folderData);
                }
                else
                {
                    EventMetadata metadata = CreateEventMetadata();
                    metadata.Add("folderPath", folderPath);
                    this.context.Tracer.RelatedWarning(metadata, "GitIndexProjection_TryGetOrAddFolderDataFromCacheFoundFile: Found a file when expecting a folder");

                    folderPath = null;
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Finds the FileOrFolderData for the parent of the path provided.
        /// </summary>
        /// <param name="childPathParts">Path to child entry</param>
        /// <param name="fileOrFolderData">Out: FileOrFolderData for childPathParts's parent</param>
        /// <returns>True if the parent could be found in the tree, and false otherwise</returns>
        /// <remarks>This method does not check if childPathParts is in the tree, it only looks up its parent.
        /// If childPathParts is empty (i.e. the root folder), the root folder will be returned.</remarks>
        private bool TryGetParentFolderDataFromTree(string[] childPathParts, out FileOrFolderData fileOrFolderData)
        {
            fileOrFolderData = null;
            FileOrFolderData parentFolder;
            if (this.TryGetFileOrFolderDataFromTree(childPathParts, childPathParts.Length - 1, out parentFolder))
            {
                if (parentFolder.IsFolder)
                {
                    fileOrFolderData = parentFolder;
                    return true;
                }
            }

            return false;            
        }

        /// <summary>
        /// Finds the FileOrFolderData for the path provided
        /// </summary>
        /// <param name="pathParts">Path to desired entry</param>
        /// <param name="fileOrFolderData">Out: FileOrFolderData for pathParts</param>
        /// <returns>True if the path could be found in the tree, and false otherwise</returns>
        /// <remarks>If the root folder is desired, the pathParts should be an empty array</remarks>
        private bool TryGetFileOrFolderDataFromTree(string[] pathParts, out FileOrFolderData fileOrFolderData)
        {
            return this.TryGetFileOrFolderDataFromTree(pathParts, pathParts.Length, out fileOrFolderData);
        }

        /// <summary>
        /// Finds the FileOrFolderData at the specified depth of the path provided.  If depth == pathParts.Length
        /// TryGetFileOrFolderDataFromTree will find the FileOrFolderData specified in pathParts.  If 
        /// depth is less than pathParts.Length, then TryGetFileOrFolderDataFromTree will return an ancestor folder of
        /// childPathParts.
        /// </summary>
        /// <param name="pathParts">Path</param>
        /// <param name="depth">Desired path depth, if depth is > pathParts.Length, pathParts.Length will be used</param>
        /// <param name="fileOrFolderData">Out: FileOrFolderData for pathParts at the specified depth.  For example,
        /// if pathParts were { "A", "B", "C" } and depth was 2, FileOrFolderData for "B" would be returned.</param>
        /// <returns>True if the specified path\depth could be found in the tree, and false otherwise</returns>
        private bool TryGetFileOrFolderDataFromTree(string[] pathParts, int depth, out FileOrFolderData fileOrFolderData)
        {
            fileOrFolderData = null;
            depth = Math.Min(depth, pathParts.Length);
            FileOrFolderData currentEntry = this.rootFolderData;
            for (int pathIndex = 0; pathIndex < depth; ++pathIndex)
            {
                if (!currentEntry.IsFolder)
                {
                    return false;
                }

                if (!currentEntry.ChildEntries.TryGetValue(pathParts[pathIndex], out currentEntry))
                {
                    return false;
                }
            }

            fileOrFolderData = currentEntry;
            return fileOrFolderData != null;
        }

        private void ParseIndexThreadMain()
        {
            try
            {
                while (true)
                {
                    this.wakeUpThread.WaitOne();

                    while (this.context.Repository.GVFSLock.IsExternalLockHolderAlive())
                    {
                        // Wait for a notification that the GVFS lock has been released (triggered by the post-command hook)
                        if (this.context.Repository.GVFSLock.WaitOnExternalLockRelease(ExternalLockReleaseTimeoutMs))
                        {
                            break;
                        }

                        if (this.isStopping)
                        {
                            return;
                        }
                    }

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
                                this.lastUpdateTime = 0;
                                this.CopyIndexFileAndBuildProjection();
                            }
                            catch (Win32Exception e)
                            {
                                this.SetProjectionAndPlaceholdersAndOffsetsAsInvalid();

                                EventMetadata metadata = CreateEventMetadata(e);
                                this.context.Tracer.RelatedWarning(metadata, "Win32Exception when reparsing index for projection");
                            }
                            catch (IOException e)
                            {
                                this.SetProjectionAndPlaceholdersAndOffsetsAsInvalid();

                                EventMetadata metadata = CreateEventMetadata(e);
                                this.context.Tracer.RelatedWarning(metadata, "IOException when reparsing index for projection");
                            }
                            catch (UnauthorizedAccessException e)
                            {
                                this.SetProjectionAndPlaceholdersAndOffsetsAsInvalid();

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
                        this.ClearGvFltNegativePathCache();
                        this.UpdatePlaceholders();
                    }

                    this.projectionParseComplete.Set();

                    // Notify the WaitForExternalReleaseLockProcessingToComplete thread that this thread has processed the lock release notification
                    // Wait until after we have finished parsing and updating placeholders to ensure that nothing can access the projection between
                    // the git command ending and this thread reparsing the index and updating placeholders
                    this.externalLockReleaseHandlingComplete.Set();

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

        private void ClearGvFltNegativePathCache()
        {
            uint totalEntryCount = 0;
            NtStatus clearCacheResult = this.gvflt.ClearNegativePathCache(ref totalEntryCount);
            int gitCount = Interlocked.Exchange(ref this.negativePathCacheUpdatedForGitCount, 0);

            EventMetadata clearCacheMetadata = CreateEventMetadata();
            clearCacheMetadata.Add(TracingConstants.MessageKey.InfoMessage, "ClearGvFltNegativePathCache: Cleared GvFlt negative path cache");
            clearCacheMetadata.Add("totalEntryCount", totalEntryCount);
            clearCacheMetadata.Add("negativePathCacheUpdatedForGitCount", gitCount);
            clearCacheMetadata.Add("clearCacheResult", clearCacheResult.ToString());
            this.context.Tracer.RelatedEvent(EventLevel.Informational, "ClearGvFltNegativePathCache", clearCacheMetadata);

            if (clearCacheResult != NtStatus.Success)
            {
                this.LogErrorAndExit("ClearNegativePathCache failed, exiting process. ClearNegativePathCache result: " + clearCacheResult.ToString());
            }
        }

        private void UpdatePlaceholders()
        {
            this.updatePlaceholderFailures = new ConcurrentHashSet<string>();
            this.deletePlaceholderFailures = new ConcurrentHashSet<string>();
            
            List<PlaceholderListDatabase.PlaceholderData> placeholderListCopy = this.placeholderList.GetAllEntries();
            EventMetadata metadata = new EventMetadata();
            metadata.Add("Count", placeholderListCopy.Count);
            using (ITracer activity = this.context.Tracer.StartActivity("UpdatePlaceholders", EventLevel.Informational, metadata))
            {
                int minFilesPerThread = 10;
                int numThreads = Math.Max(8, Environment.ProcessorCount);
                numThreads = Math.Min(numThreads, placeholderListCopy.Count / minFilesPerThread);

                ConcurrentBag<PlaceholderListDatabase.PlaceholderData> updatedPlaceholderList = new ConcurrentBag<PlaceholderListDatabase.PlaceholderData>();
                if (numThreads > 1)
                {
                    Thread[] placeholderUpdateThreads = new Thread[numThreads];
                    int filesPerThread = placeholderListCopy.Count / numThreads;
                    for (int i = 0; i < numThreads; i++)
                    {
                        int start = i * filesPerThread;
                        int end = (i + 1) == numThreads ? placeholderListCopy.Count : (i + 1) * filesPerThread;

                        placeholderUpdateThreads[i] = new Thread(
                            () =>
                            {
                                // We have a top-level try\catch for any unhandled exceptions thrown in the newly created thread
                                try
                                {
                                    for (int j = start; j < end; ++j)
                                    {
                                        this.UpdateOrDeletePlaceholder(placeholderListCopy[j], updatedPlaceholderList);
                                    }
                                }
                                catch (Exception e)
                                {
                                    this.LogErrorAndExit("UpdateOrDeletePlaceholder background thread caught unhandled exception, exiting process", e);
                                }
                            });

                        placeholderUpdateThreads[i].Start();
                    }

                    for (int i = 0; i < placeholderUpdateThreads.Length; i++)
                    {
                        placeholderUpdateThreads[i].Join();
                    }
                }
                else
                {
                    foreach (PlaceholderListDatabase.PlaceholderData placeholder in placeholderListCopy)
                    {
                        this.UpdateOrDeletePlaceholder(placeholder, updatedPlaceholderList);
                    }
                }

                this.placeholderList.WriteAllEntriesAndFlush(updatedPlaceholderList);
                this.repoMetadata.SetPlaceholdersNeedUpdate(false);
            }
        }

        private void UpdateOrDeletePlaceholder(
            PlaceholderListDatabase.PlaceholderData placeholder, 
            ConcurrentBag<PlaceholderListDatabase.PlaceholderData> updatedPlaceholderList)
        {
            string childName;
            string parentKey;
            this.GetChildNameAndParentKey(placeholder.Path, out childName, out parentKey);

            string projectedSha;
            if (!this.TryGetSha(childName, parentKey, out projectedSha))
            {
                UpdateFailureCause failureReason = UpdateFailureCause.NoFailure;
                NtStatus status = this.gvflt.DeleteFile(placeholder.Path, PlaceholderUpdateFlags, ref failureReason);
                this.ProcessGvUpdateDeletePlaceholderResult(placeholder, string.Empty, status, updatedPlaceholderList, failureReason, deleteOperation: true);
            }
            else
            {
                string onDiskSha = placeholder.Sha;
                if (!onDiskSha.Equals(projectedSha))
                {
                    DateTime now = DateTime.UtcNow;
                    UpdateFailureCause failureReason = UpdateFailureCause.NoFailure;
                    NtStatus status;

                    try
                    {
                        FileOrFolderData data = this.GetProjectedFileOrFolderData(childName, parentKey, populateSize: true);
                        status = this.gvflt.UpdatePlaceholderIfNeeded(
                            placeholder.Path,
                            creationTime: now,
                            lastAccessTime: now,
                            lastWriteTime: now,
                            changeTime: now,
                            fileAttributes: (uint)NativeMethods.FileAttributes.FILE_ATTRIBUTE_ARCHIVE,
                            endOfFile: data.Size,
                            contentId: GVFltCallbacks.ConvertShaToContentId(projectedSha),
                            epochId: GVFltCallbacks.GetEpochId(),
                            updateFlags: PlaceholderUpdateFlags,
                            failureReason: ref failureReason);
                    }
                    catch (Exception e)
                    {
                        status = NtStatus.Unsuccessful;
                        
                        EventMetadata metadata = CreateEventMetadata(e);
                        metadata.Add("virtualPath", placeholder.Path);
                        this.context.Tracer.RelatedWarning(metadata, "UpdateOrDeletePlaceholder: Exception while trying to update placeholder");
                    }

                    this.ProcessGvUpdateDeletePlaceholderResult(placeholder, projectedSha, status, updatedPlaceholderList, failureReason, deleteOperation: false);
                }
                else
                {
                    updatedPlaceholderList.Add(placeholder);
                }
            }
        }

        private void ProcessGvUpdateDeletePlaceholderResult(
            PlaceholderListDatabase.PlaceholderData placeholder,
            string projectedSha,
            NtStatus status,
            ConcurrentBag<PlaceholderListDatabase.PlaceholderData> updatedPlaceholderList,
            UpdateFailureCause failureReason,
            bool deleteOperation)
        {
            EventMetadata metadata;
            switch (status)
            {
                case NtStatus.Success:
                    if (!deleteOperation)
                    {
                        updatedPlaceholderList.Add(new PlaceholderListDatabase.PlaceholderData(placeholder.Path, projectedSha));
                    }

                    break;

                case NtStatus.IoReparseTagNotHandled:
                    // Attempted to update\delete a file that has a non-GvFlt reparse point

                    this.ScheduleBackgroundTaskForFailedUpdateDeletePlaceholder(placeholder, deleteOperation);

                    metadata = CreateEventMetadata();
                    metadata.Add("deleteOperation", deleteOperation);
                    metadata.Add("virtualPath", placeholder.Path);
                    metadata.Add("status", status.ToString());
                    metadata.Add(TracingConstants.MessageKey.InfoMessage, "UpdateOrDeletePlaceholder: StatusIoReparseTagNotHandled");
                    this.context.Tracer.RelatedEvent(EventLevel.Informational, "UpdatePlaceholders_StatusIoReparseTagNotHandled", metadata);

                    break;                

                case NtStatus.FileSystemVirtualizationInvalidOperation:
                    // GVFS attempted to update\delete a file that is no longer partial.  
                    // This can occur if:
                    //
                    //    - A file is converted from partial to full (or tombstone) while a git command is running.
                    //      Any tasks scheduled during the git command to update the placeholder list have not yet 
                    //      completed at this point.
                    //
                    //    - A placeholder file was converted to full without being in the index.  This can happen if 'git update-index --remove'
                    //      is used to remove a file from the index before converting the file to full.  Because a skip-worktree bit 
                    //      is not cleared when this file file is converted to full, GVFltCallbacks assumes that there is no placeholder
                    //      that needs to be removed removed from the list
                    //
                    //    - When there is a merge conflict the conflicting file will get the skip worktree bit removed. In some cases git 
                    //      will not make any changes to the file in the working directory. In order to handle this case we have to check
                    //      the merge stage in the index and add that entry to the placeholder list. In doing so the files that git did
                    //      update in the working directory will be full files but we will have a placeholder entry for them as well.

                    // There have been reports of FileSystemVirtualizationInvalidOperation getting hit without a corresponding background
                    // task having been scheduled (to add the file to the sparse-checkout and clear the skip-worktree bit).  
                    // Schedule OnFailedPlaceholderUpdate\OnFailedPlaceholderDelete to be sure that Git starts managing this
                    // file.  Currently the only known way that this can happen is deleting a partial file and putting a full
                    // file in its place while GVFS is unmounted.
                    this.ScheduleBackgroundTaskForFailedUpdateDeletePlaceholder(placeholder, deleteOperation);

                    metadata = CreateEventMetadata();
                    metadata.Add("deleteOperation", deleteOperation);
                    metadata.Add("virtualPath", placeholder.Path);
                    metadata.Add("status", status.ToString());
                    metadata.Add("failureReason", failureReason.ToString());
                    metadata.Add("backgroundCount", this.background.Count);
                    metadata.Add(TracingConstants.MessageKey.InfoMessage, "UpdateOrDeletePlaceholder: attempted an invalid operation");
                    this.context.Tracer.RelatedEvent(EventLevel.Informational, "UpdatePlaceholders_InvalidOperation", metadata);

                    break;

                case NtStatus.ObjectNameNotFound:
                    break;

                case NtStatus.ObjectPathNotFound:
                    break;

                default:
                    {
                        string gitPath;
                        this.AddFileToUpdateDeletePlaceholderFailureReport(deleteOperation, placeholder, out gitPath);
                        this.ScheduleBackgroundTaskForFailedUpdateDeletePlaceholder(placeholder, deleteOperation);

                        metadata = CreateEventMetadata();
                        metadata.Add("deleteOperation", deleteOperation);
                        metadata.Add("virtualPath", placeholder.Path);
                        metadata.Add("gitPath", gitPath);
                        metadata.Add("status", status.ToString());
                        metadata.Add("failureReason", failureReason.ToString());
                        this.context.Tracer.RelatedWarning(metadata, "UpdateOrDeletePlaceholder: did not succeed");
                    }       
                             
                    break;
            }
        }

        private void AddFileToUpdateDeletePlaceholderFailureReport(
            bool deleteOperation, 
            PlaceholderListDatabase.PlaceholderData placeholder, 
            out string gitPath)
        {
            gitPath = placeholder.Path.TrimStart(GVFSConstants.PathSeparator).Replace(GVFSConstants.PathSeparator, GVFSConstants.GitPathSeparator);
            if (deleteOperation)
            {
                this.deletePlaceholderFailures.Add(gitPath);
            }
            else
            {
                this.updatePlaceholderFailures.Add(gitPath);
            }
        }

        private void ScheduleBackgroundTaskForFailedUpdateDeletePlaceholder(PlaceholderListDatabase.PlaceholderData placeholder, bool deleteOperation)
        {
            if (deleteOperation)
            {
                this.background.Enqueue(GVFltCallbacks.BackgroundGitUpdate.OnFailedPlaceholderDelete(placeholder.Path));
            }
            else
            {
                this.background.Enqueue(GVFltCallbacks.BackgroundGitUpdate.OnFailedPlaceholderUpdate(placeholder.Path));
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
            this.offsetsInvalid = false;

            using (FileStream indexStream = new FileStream(this.projectionIndexBackupPath, FileMode.Open, FileAccess.Read, FileShare.Read, IndexFileStreamBufferSize))
            {
                try
                {
                    this.ParseIndexAndBuildProjection(indexStream);
                }
                catch (Exception e)
                {
                    EventMetadata metadata = CreateEventMetadata(e);
                    this.context.Tracer.RelatedWarning(metadata, "BuildProjection: Exception thrown by ParseIndexAndBuildProjection");

                    this.SetProjectionInvalid(true);
                    this.offsetsInvalid = true;
                    throw;
                }
            }
        }

        private void ParseIndexAndBuildProjection(FileStream indexFileStream)
        {
            CallbackResult result = PerformIndexAction(this, indexFileStream, IndexAction.RebuildProjection);

            if (result != CallbackResult.Success)
            {
                // RebuildProjection should always result in CallbackResult.Success (or a thrown exception)
                throw new InvalidOperationException("ParseIndexAndBuildProjection: PerformIndexAction failed to rebuild projection");
            }
        }

        public class SizesUnavailableException : Exception
        {
            public SizesUnavailableException(string message)
                : base(message)
            {
            }
        }

        // Wrapper for FileOrFolderData that allows for caching string SHAs
        private class FileMissingSize
        {
            public FileMissingSize(FileOrFolderData fileOrFolderData, string sha)
            {
                this.Data = fileOrFolderData;
                this.Sha = sha;
            }

            public FileOrFolderData Data { get; }

            public string Sha { get; }
        }        

        private class FileOrFolderData
        {
            public const long InvalidOffset = -1;

            // Special values that can be stored in Size
            // Use the Size field rather than additional fields to save on memory
            private const long MinValidSize = 0;
            private const long InvalidSize = -1;

            private ulong shaBytes1through8;
            private ulong shaBytes9Through16;
            private uint shaBytes17Through20;

            /// <summary>
            /// Create a FileOrFolderData for a folder
            /// </summary>
            public FileOrFolderData()
            {
                this.Size = FolderSizeMagicNumbers.ChildSizesNotSet;
                this.ChildEntries = new SortedList<string, FileOrFolderData>(StringComparer.OrdinalIgnoreCase);
                this.Offset = InvalidOffset;
            }

            /// <summary>
            /// Create a FileOrFolderData for a file
            /// </summary>
            public FileOrFolderData(byte[] shaBytes, long offset)    
            {
                this.Size = InvalidSize;
                this.Offset = offset;
                this.LastUpdateTime = 0;

                this.shaBytes1through8 =
                    shaBytes[0] |
                    ((ulong)shaBytes[1] << 8) |
                    ((ulong)shaBytes[2] << 16) |
                    ((ulong)shaBytes[3] << 24) |
                    ((ulong)shaBytes[4] << 32) |
                    ((ulong)shaBytes[5] << 40) |
                    ((ulong)shaBytes[6] << 48) |
                    ((ulong)shaBytes[7] << 56);

                this.shaBytes9Through16 =
                    shaBytes[8] |
                    ((ulong)shaBytes[9] << 8) |
                    ((ulong)shaBytes[10] << 16) |
                    ((ulong)shaBytes[11] << 24) |
                    ((ulong)shaBytes[12] << 32) |
                    ((ulong)shaBytes[13] << 40) |
                    ((ulong)shaBytes[14] << 48) |
                    ((ulong)shaBytes[15] << 56);

                this.shaBytes17Through20 =
                    shaBytes[16] |
                    ((uint)shaBytes[17] << 8) |
                    ((uint)shaBytes[18] << 16) |
                    ((uint)shaBytes[19] << 24);
            }

            public bool IsFolder
            {
                get { return this.Size <= FolderSizeMagicNumbers.MaxSizeForFolderIndication; }
            }

            public bool ChildrenHaveSizes
            {
                get
                {
                    return this.Size == FolderSizeMagicNumbers.ChildSizesSet;
                }
            }

            public long Size { get; private set; }
            public long Offset { get; private set; }
            public uint LastUpdateTime { get; private set; }

            public SortedList<string, FileOrFolderData> ChildEntries { get; private set; }            

            public bool IsSizeSet()
            {
                return this.Size >= MinValidSize;
            }

            public string ConvertShaToString()
            {
                char[] shaString = new char[40];
                BytesToCharArray(shaString, 0, this.shaBytes1through8, sizeof(ulong));
                BytesToCharArray(shaString, 16, this.shaBytes9Through16, sizeof(ulong));
                BytesToCharArray(shaString, 32, this.shaBytes17Through20, sizeof(uint));
                return new string(shaString, 0, shaString.Length);
            }

            public void AddChild(ITracer tracer, string childName, FileOrFolderData data)
            {
                try
                {
                    this.ChildEntries.Add(childName, data);
                }
                catch (ArgumentException e)
                {
                    EventMetadata metadata = this.CreateEventMetadata(e);
                    metadata.Add("childName", childName);
                    tracer.RelatedWarning(metadata, "AddChild: Skipping addition of child, entry already exists in collection");
                }
            }

            public void UpdateChildOffset(ITracer tracer, string childName, long offset, uint updateTime)
            {
                if (this.IsFolder)
                {
                    FileOrFolderData childData;
                    if (this.ChildEntries.TryGetValue(childName, out childData))
                    {
                        childData.SetOffset(tracer, offset, updateTime);
                    }
                    else
                    {
                        EventMetadata metadata = this.CreateEventMetadata();
                        metadata.Add("childName", childName);
                        metadata.Add("offset", offset);
                        tracer.RelatedWarning(metadata, "UpdateChildOffset: Failed to find childName in ChildEntries", Keywords.Telemetry);
                    }
                }
                else
                {
                    EventMetadata metadata = this.CreateEventMetadata();
                    metadata.Add("childName", childName);
                    metadata.Add("offset", offset);
                    tracer.RelatedWarning(metadata, "UpdateChildOffset: Skipping update of child, this FileOrFolderData is a file");
                }
            }

            public void FolderOnly_PopulateSizes(
                ITracer tracer,                
                GVFSGitObjects gitObjects,
                PersistentDictionary<string, long> blobSizes,
                CancellationToken cancellationToken)
            {
                if (this.ChildrenHaveSizes)
                {
                    return;
                }

                HashSet<string> missingShas;
                List<FileMissingSize> childrenMissingSizes;
                this.FolderOnly_PopulateSizesLocally(gitObjects, blobSizes, out missingShas, out childrenMissingSizes);

                lock (this)
                {
                    // Check ChildrenHaveSizes again in case another 
                    // thread has already done the work of setting the sizes
                    if (this.ChildrenHaveSizes)
                    {
                        return;
                    }
                    
                    this.PopulateSizesFromRemote(
                        tracer,
                        gitObjects,                        
                        blobSizes,
                        missingShas,
                        childrenMissingSizes,
                        cancellationToken);
                }
            }

            public bool FileOnly_TryPopulateSizeLocally(
                GVFSGitObjects gitObjects,
                PersistentDictionary<string, long> blobSizes,
                out string sha)
            {
                sha = this.ConvertShaToString();

                long blobLength = 0;
                if (blobSizes.TryGetValue(sha, out blobLength))
                {
                    this.Size = blobLength;
                    return true;
                }
                else if (gitObjects.TryGetBlobSizeLocally(sha, out blobLength))
                {
                    this.Size = blobLength;

                    // There is no flush for this value because It's already local, so there's little loss if it doesn't get persisted
                    // But it's faster to wait for some remote call to batch this value into a different flush
                    blobSizes[sha] = blobLength;
                    return true;
                }

                return false;
            }

            private static char GetHexValue(int i)
            {
                if (i < 10)
                {
                    return (char)(i + '0');
                }

                return (char)(i - 10 + 'A');
            }

            private static void BytesToCharArray(char[] shaString, int startIndex, ulong shaBytes, int numBytes)
            {
                byte b;
                int firstArrayIndex;
                for (int i = 0; i < numBytes; ++i)
                {
                    b = (byte)(shaBytes >> (i * 8));
                    firstArrayIndex = startIndex + (i * 2);
                    shaString[firstArrayIndex] = GetHexValue(b / 16);
                    shaString[firstArrayIndex + 1] = GetHexValue(b % 16);
                }
            }

            /// <summary>
            /// Populates the sizes of child entries in the folder using locally available data
            /// </summary>
            private void FolderOnly_PopulateSizesLocally(
                GVFSGitObjects gitObjects,
                PersistentDictionary<string, long> blobSizes,
                out HashSet<string> missingShas,
                out List<FileMissingSize> childrenMissingSizes)
            {
                if (this.ChildrenHaveSizes)
                {
                    missingShas = null;
                    childrenMissingSizes = null;
                    return;
                }
                
                missingShas = new HashSet<string>();
                childrenMissingSizes = new List<FileMissingSize>();
                foreach (KeyValuePair<string, FileOrFolderData> childEntry in this.ChildEntries)
                {
                    if (!childEntry.Value.IsFolder && !childEntry.Value.IsSizeSet())
                    {
                        string sha;
                        if (!childEntry.Value.FileOnly_TryPopulateSizeLocally(gitObjects, blobSizes, out sha))
                        {
                            childrenMissingSizes.Add(new FileMissingSize(childEntry.Value, sha));
                            missingShas.Add(sha);
                        }
                    }
                }

                if (childrenMissingSizes.Count == 0)
                {
                    this.Size = FolderSizeMagicNumbers.ChildSizesSet;
                }
            }

            /// <summary>
            /// Populate sizes using size data from the remote
            /// </summary>
            /// <param name="missingShas">Set of object shas whose sizes should be downloaded from the remote.  This set should contains all the distinct SHAs from
            /// in childrenMissingSizes.  PopulateSizesLocally can be used to generate this set</param>
            /// <param name="childrenMissingSizes">List of child entries whose sizes should be downloaded from the remote.  PopulateSizesLocally
            /// can be used to generate this list</param>
            private void PopulateSizesFromRemote(
                ITracer tracer,
                GVFSGitObjects gitObjects,
                PersistentDictionary<string, long> blobSizes,
                HashSet<string> missingShas,
                List<FileMissingSize> childrenMissingSizes,
                CancellationToken cancellationToken)
            {
                if (childrenMissingSizes != null && childrenMissingSizes.Count > 0)
                {
                    Dictionary<string, long> objectLengths = gitObjects.GetFileSizes(missingShas, cancellationToken).ToDictionary(s => s.Id, s => s.Size, StringComparer.OrdinalIgnoreCase);
                    foreach (FileMissingSize childNeedingSize in childrenMissingSizes)
                    {
                        long blobLength = 0;
                        if (objectLengths.TryGetValue(childNeedingSize.Sha, out blobLength))
                        {
                            childNeedingSize.Data.Size = blobLength;
                            blobSizes[childNeedingSize.Sha] = blobLength;
                        }
                        else
                        {
                            EventMetadata metadata = this.CreateEventMetadata();
                            metadata.Add("SHA", childNeedingSize.Sha);
                            tracer.RelatedError(metadata, "PopulateMissingSizesFromRemote: Failed to download size for child entry", Keywords.Network);
                            throw new SizesUnavailableException("Failed to download size for " + childNeedingSize.Sha);
                        }
                    }

                    blobSizes.Flush();
                }

                this.Size = FolderSizeMagicNumbers.ChildSizesSet;
            }

            private void SetOffset(ITracer tracer, long offset, uint updateTime)
            {
                if (this.IsFolder)
                {
                    EventMetadata metadata = this.CreateEventMetadata();
                    metadata.Add("offset", offset);
                    metadata.Add("updateTime", updateTime);
                    tracer.RelatedWarning(metadata, "SetOffset: Skipping update of file offset, this FileOrFolderData is a folder");
                }
                else
                {
                    this.Offset = offset;
                    this.LastUpdateTime = updateTime;
                }
            }

            private EventMetadata CreateEventMetadata(Exception e = null)
            {
                EventMetadata metadata = GitIndexProjection.CreateEventMetadata(e);
                return metadata;
            }

            // Special values that can be stored in Size to indicate that the FileOrFolderData is a folder, and to indicate that
            // folder's state
            // Use the Size field rather than additional fields to save on memory
            private static class FolderSizeMagicNumbers
            {
                public const long MaxSizeForFolderIndication = -2; // All values less than or equal to MaxSizeForFolderIndication indicate a folder
                public const long ChildSizesNotSet = -2;           // Size of -2 indicates that FileOrFolderData is a folder whose children do not have their sizes
                public const long ChildSizesSet = -3;              // Size of -3 indicates that FileOrFolderData is a folder whose children do have their sizes
            }
        }
    }
}
