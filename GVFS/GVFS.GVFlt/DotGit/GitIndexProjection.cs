using GVFS.Common;
using GVFS.Common.Physical;
using GVFS.Common.Physical.Git;
using GVFS.Common.Tracing;
using GVFSGvFltWrapper;
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

        private const string ProjectionIndexBackupName = "GVFS_projection";
        private const string EtwArea = "GitIndexProjection";

        private const int ExternalLockReleaseTimeoutMs = 50;

        private static readonly DateTime UnixEpoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        private char[] gitPathSeparatorCharArray = new char[] { GVFSConstants.GitPathSeparator };

        private GVFSContext context;
        private RepoMetadata repoMetadata;

        private FileOrFolderData rootFolderData;
        
        // Cache of folder paths (in Windows format) to folder data
        private ConcurrentDictionary<string, FileOrFolderData> projectionFolderCache;

        private PersistentDictionary<string, long> blobSizes;
        private SparseCheckoutAndDoNotProject sparseCheckoutAndDoNotProject;
        private GVFSGitObjects gitObjects;
        private ReaderWriterLockSlim projectionReadWriteLock;
        private ManualResetEventSlim projectionParseComplete;
        private ManualResetEventSlim externalLockReleaseHandlingComplete;

        private bool offsetsInvalid;
        private bool projectionInvalid;

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
            SparseCheckoutAndDoNotProject sparseCheckoutAndDoNotProject,
            GVFSGitObjects gitObjects,
            PersistentDictionary<string, long> blobSizes,
            RepoMetadata repoMetadata)
        {
            this.context = context;
            this.repoMetadata = repoMetadata;
            this.blobSizes = blobSizes;
            this.sparseCheckoutAndDoNotProject = sparseCheckoutAndDoNotProject;
            this.gitObjects = gitObjects;
            this.projectionReadWriteLock = new ReaderWriterLockSlim();
            this.projectionParseComplete = new ManualResetEventSlim(false);
            this.wakeUpThread = new AutoResetEvent(false);
            this.projectionIndexBackupPath = Path.Combine(this.context.Enlistment.DotGVFSRoot, ProjectionIndexBackupName);
            this.indexPath = Path.Combine(this.context.Enlistment.WorkingDirectoryRoot, GVFSConstants.DotGit.Index);
            this.externalLockReleaseHandlingComplete = new ManualResetEventSlim(false);
        }

        /// <summary>
        /// Force the index file to be parsed and a new projection collection to be built.  
        /// This method should only be used to measure index parsing performance.
        /// </summary>
        void IProfilerOnlyIndexProjection.ForceParseIndexFileForNewProjection()
        {
            this.CopyIndexFileAndParse();
        }

        /// <summary>
        /// Force the index file to be parsed to update offsets.  
        /// This method should only be used to measure index parsing performance.
        /// </summary>
        void IProfilerOnlyIndexProjection.ForceParseIndexToUpdateOffsets()
        {
            using (FileStream indexStream = new FileStream(this.indexPath, FileMode.Open, FileAccess.ReadWrite, FileShare.Read, IndexFileStreamBufferSize))
            {
                this.ParseIndex(indexStream, updateOffsetsOnly: true);
            }
        }

        public void Initialize()
        {
            this.gitIndexLock = new FileBasedLock(
                this.context.FileSystem,
                this.context.Tracer,
                Path.Combine(this.context.Enlistment.WorkingDirectoryRoot, GVFSConstants.DotGit.Index + GVFSConstants.DotGit.LockExtension),
                "GVFS",
                FileBasedLock.ExistingLockCleanup.DeleteExisting);

            this.projectionReadWriteLock.EnterWriteLock();

            this.projectionInvalid = this.repoMetadata.GetProjectionInvalid();

            try
            {
                if (!this.context.FileSystem.FileExists(this.projectionIndexBackupPath) || this.projectionInvalid)
                {
                    this.CopyIndexFileAndParse();
                }
                else
                {
                    // offsetsInvalid is true because we're projecting something other than the current index
                    this.offsetsInvalid = true;
                    this.ParseProjection();
                }
            }
            finally
            {
                this.projectionReadWriteLock.ExitWriteLock();
            }

            // If somehow something invalidated the projection while we were initializing, the parsing thread will
            // pick it up and parse again
            if (!this.projectionInvalid)
            {
                this.projectionParseComplete.Set();
            }

            this.backgroundThread = Task.Factory.StartNew((Action)this.ParseIndexThreadMain, TaskCreationOptions.LongRunning);            
        }

        public void Shutdown()
        {
            this.isStopping = true;
            this.wakeUpThread.Set();
            this.backgroundThread.Wait();
        }

        public bool TryReleaseExternalLock(int pid)
        {
            // GitIndexProjection must make sure that nothing can read from its projection between
            // git releasing its lock and the parsing thread acquring its writer lock.
            //
            // Example:
            //
            //    git checkout master
            //    type Readme.md  <-- This must use the new projection
            //
            // To ensure this happens, this method will not return until the parsing thread has singaled that it
            // has acquired its writer lock

            this.externalLockReleaseHandlingComplete.Reset();
            if (this.context.Repository.GVFSLock.ReleaseExternalLock(pid))
            {
                // If the parsing thread is not waiting to parse the index, then there is no need to wait for it.
                if (!this.IsProjectionParseComplete())
                {
                    // Wait for the parsing thread to acknowledge that the GVFS lock has been released (and that it has
                    // acquired its writer lock)
                    this.externalLockReleaseHandlingComplete.Wait();
                }

                return true;
            }

            return false;
        }

        public bool IsProjectionParseComplete()
        {
            return this.projectionParseComplete.IsSet;
        }

        public void InvalidateProjection()
        {
            this.SetProjectionAndOffsetsAsInvalid();
            this.projectionParseComplete.Reset();
            this.wakeUpThread.Set();
        }

        public bool IsIndexBeingUpdatedByGVFS()
        {
            return this.gitIndexLock.IsOpen();
        }

        public void InvalidateOffsets()
        {
            if (!this.IsIndexBeingUpdatedByGVFS())
            {
                this.offsetsInvalid = true;
            }
        }

        public IEnumerable<GVFltFileInfo> GetProjectedItems_CanTimeout(string folderPath)
        {
            this.projectionReadWriteLock.EnterReadLock();

            try
            {
                FileOrFolderData folderData;
                if (this.TryGetOrAddFolderDataFromCache(folderPath, out folderData))
                {
                    this.PopulateSizes_CanTimeout(folderData);
                    List<GVFltFileInfo> childItems = new List<GVFltFileInfo>();
                    foreach (KeyValuePair<string, FileOrFolderData> childEntry in folderData.ChildEntries)
                    {
                        string childPath;
                        if (folderPath.Length > 0)
                        {
                            childPath = string.Concat(folderPath, GVFSConstants.PathSeparatorString, childEntry.Key);
                        }
                        else
                        {
                            childPath = childEntry.Key;
                        }

                        if (this.sparseCheckoutAndDoNotProject.ShouldPathBeProjected(childPath, childEntry.Value.IsFolder))
                        {
                            childItems.Add(new GVFltFileInfo(
                                childEntry.Key,
                                childEntry.Value.IsFolder ? 0 : childEntry.Value.Size,
                                childEntry.Value.IsFolder));
                        }
                    }

                    childItems.Sort(GVFltFileInfo.SortAlphabeticallyIgnoreCase());
                    return childItems;
                }

                return new List<GVFltFileInfo>();
            }
            finally
            {
                this.projectionReadWriteLock.ExitReadLock();
            }
        }

        public bool IsPathProjected(string virtualPath, out bool isFolder)
        {
            isFolder = false;
            FileOrFolderData data = this.GetProjectedFileOrFolderData(virtualPath, populateSize: false);
            if (data != null)
            {
                isFolder = data.IsFolder;
                return true;
            }

            return false;
        }

        public GVFltFileInfo GetProjectedGVFltFileInfoAndSha_CanTimeout(string virtualPath, out string sha)
        {
            sha = string.Empty;
            FileOrFolderData data = this.GetProjectedFileOrFolderData(virtualPath, populateSize: true);
            if (data != null)
            {
                if (data.IsFolder)
                {
                    return new GVFltFileInfo(data.Name, size: 0, isFolder: true);
                }

                sha = data.ConvertShaToString();
                return new GVFltFileInfo(data.Name, data.Size, isFolder: false);
            }

            return null;
        }

        public void StopProjecting(string virtualPath, bool isFolder)
        {
            this.sparseCheckoutAndDoNotProject.StopProjecting(virtualPath, isFolder);
        }

        public CallbackResult AcquireIndexLockAndOpenForWrites()
        {
            if (!File.Exists(this.indexPath))
            {
                EventMetadata metadata = CreateErrorMetadata("Can't open the index because it doesn't exist");
                this.context.Tracer.RelatedError(metadata);

                return CallbackResult.FatalError;
            }

            if (!this.gitIndexLock.TryAcquireLockAndDeleteOnClose())
            {
                EventMetadata metadata = CreateEventMetadata("Can't aquire index lock");
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
                EventMetadata metadata = CreateErrorMetadata("IOException in AcquireIndexLockAndOpenForWrites (RetryableError)", e);
                this.context.Tracer.RelatedError(metadata);
                result = CallbackResult.RetryableError;
            }
            catch (Exception e)
            {
                EventMetadata metadata = CreateErrorMetadata("Exception in AcquireIndexLockAndOpenForWrites (FatalError)", e);
                this.context.Tracer.RelatedError(metadata);
                result = CallbackResult.FatalError;
            }
            finally
            {
                if (result != CallbackResult.Success)
                {
                    if (!this.gitIndexLock.TryReleaseLock())
                    {
                        EventMetadata metadata = CreateErrorMetadata("Unable to release index.lock in AcquireIndexLockAndOpenForWrites (FatalError)");
                        this.context.Tracer.RelatedError(metadata);
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
                EventMetadata metadata = CreateErrorMetadata("Fatal Exception in ReleaseLockAndClose", e);
                this.context.Tracer.RelatedError(metadata);
            }

            return CallbackResult.FatalError;
        }

        public CallbackResult ClearSkipWorktreeAndUpdateEntry(string filePath, DateTime createTimeUtc, DateTime lastWriteTimeUtc, uint fileSize)
        {
            try
            {
                if (this.offsetsInvalid)
                {
                    this.offsetsInvalid = false;

                    if (this.lastUpdateTime == uint.MaxValue)
                    {
                        this.lastUpdateTime = 0;
                    }
                    else
                    {
                        ++this.lastUpdateTime;
                    }

                    this.ParseIndex(this.indexFileStream, updateOffsetsOnly: true);

                    if (this.offsetsInvalid)
                    {
                        return CallbackResult.RetryableError;
                    }
                }

                long offset;
                if (this.TryGetIndexPathOffset(filePath, out offset))
                {
                    if (createTimeUtc == DateTime.MinValue ||
                        lastWriteTimeUtc == DateTime.MinValue ||
                        fileSize == 0)
                    {
                        createTimeUtc = DateTime.MinValue;
                        lastWriteTimeUtc = DateTime.MinValue;
                        fileSize = 0;
                    }

                    uint ctimeSeconds = ToUnixEpochSeconds(createTimeUtc);
                    uint ctimeNanosecondFraction = ToUnixNanosecondFraction(createTimeUtc);
                    uint mtimeSeconds = ToUnixEpochSeconds(lastWriteTimeUtc);
                    uint mtimeNanosecondFraction = ToUnixNanosecondFraction(lastWriteTimeUtc);

                    this.indexFileStream.Seek(offset, SeekOrigin.Begin);

                    this.indexFileStream.Write(BitConverter.GetBytes(EndianHelper.Swap(ctimeSeconds)), 0, 4);            // ctime seconds
                    this.indexFileStream.Write(BitConverter.GetBytes(EndianHelper.Swap(ctimeNanosecondFraction)), 0, 4); // ctime nanosecond fractions
                    this.indexFileStream.Write(BitConverter.GetBytes(EndianHelper.Swap(mtimeSeconds)), 0, 4);            // mtime seconds
                    this.indexFileStream.Write(BitConverter.GetBytes(EndianHelper.Swap(mtimeNanosecondFraction)), 0, 4); // mtime nanosecond fractions
                    this.indexFileStream.Seek(20, SeekOrigin.Current);                                                   // dev + ino + mode + uid + gid
                    this.indexFileStream.Write(BitConverter.GetBytes(EndianHelper.Swap(fileSize)), 0, 4);                // size
                    this.indexFileStream.Seek(22, SeekOrigin.Current);                                                   // sha + flags
                    this.indexFileStream.Write(new byte[2] { 0, 0 }, 0, 2);                                              // extended flags
                    this.indexFileStream.Flush();
                }
            }
            catch (IOException e)
            {
                EventMetadata metadata = CreateErrorMetadata("IOException in ClearSkipWorktreeAndUpdateEntry (RetryableError)", e);
                this.context.Tracer.RelatedError(metadata);

                return CallbackResult.RetryableError;
            }
            catch (Exception e)
            {
                EventMetadata metadata = CreateErrorMetadata("Exception in ClearSkipWorktreeAndUpdateEntry (FatalError)", e);
                this.context.Tracer.RelatedError(metadata);

                return CallbackResult.FatalError;
            }

            return CallbackResult.Success;
        }

        public void Dispose()
        {
            this.Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected void Dispose(bool disposing)
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
            }
        }

        private static int ReadReplaceLength(Stream stream)
        {
            int headerByte = stream.ReadByte();
            int offset = headerByte & 0x7f;

            // Terminate the loop when the high bit is no longer set.
            for (int i = 0; (headerByte & 0x80) != 0; i++)
            {
                headerByte = stream.ReadByte();

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

        private static EventMetadata CreateEventMetadata(string message, Exception e = null)
        {
            EventMetadata metadata = new EventMetadata();
            metadata.Add("Area", EtwArea);
            metadata.Add("Message", message);
            if (e != null)
            {
                metadata.Add("Exception", e.ToString());
            }

            return metadata;
        }

        private static EventMetadata CreateErrorMetadata(string message, Exception e = null)
        {
            EventMetadata metadata = new EventMetadata();
            metadata.Add("Area", EtwArea);
            if (e != null)
            {
                metadata.Add("Exception", e.ToString());
            }

            metadata.Add("ErrorMessage", message);
            return metadata;
        }

        private void SetProjectionInvalid(bool isInvalid)
        {
            this.projectionInvalid = isInvalid;
            this.repoMetadata.SetProjectionInvalid(isInvalid);
        }

        private void SetProjectionAndOffsetsAsInvalid()
        {
            this.SetProjectionInvalid(true);
            this.offsetsInvalid = true;
        }

        private void AddItem(string gitPath, byte[] shaBytes, long offset, ref FileOrFolderData lastParent, ref string lastParentKey)
        {
            int separatorIndex;
            string parentKey = this.GetParentKey(gitPath, out separatorIndex);
            if (parentKey.Equals(lastParentKey))
            {
                FileOrFolderData newFileData = new FileOrFolderData(this.GetChildName(gitPath, separatorIndex), shaBytes, offset);
                lastParent.AddChild(this.context.Tracer, newFileData.Name, newFileData);
            }
            else
            {
                lastParentKey = parentKey;                
                if (separatorIndex < 0)
                {
                    lastParent = this.rootFolderData;
                    FileOrFolderData newFileData = new FileOrFolderData(gitPath, shaBytes, offset);                    
                    lastParent.AddChild(this.context.Tracer, newFileData.Name, newFileData);
                }
                else
                {
                    // NOTE: Testing has revealed that if the call to gitPath.Split is moved to the start of 
                    // this method memory usage will be noticably lower.  However, performance will also be 
                    // worse and so the call to Split is being left here.
                    string[] pathParts = gitPath.Split(this.gitPathSeparatorCharArray);
                    FileOrFolderData newFileData = new FileOrFolderData(pathParts[pathParts.Length - 1], shaBytes, offset);
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
                        EventMetadata metadata = CreateErrorMetadata("UpdateFileOffset: Failed to find parentKey in projectionFolderCache");
                        metadata.Add("gitPath", gitPath);
                        metadata.Add("parentKey", parentKey);
                        metadata.Add("offset", offset);
                        this.context.Tracer.RelatedError(metadata);
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
                if (!fileData.IsFolder && fileData.LastUpdateTime == this.lastUpdateTime)
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
                    EventMetadata metadata = CreateErrorMetadata("AddFileToTree: Found a folder where a file was expected");
                    metadata.Add("gitPath", string.Join(GVFSConstants.GitPathSeparatorString, pathParts));
                    metadata.Add("parentFolder", parentFolder.Name);
                    this.context.Tracer.RelatedError(metadata);
                    return null;
                }

                if (!parentFolder.ChildEntries.TryGetValue(pathParts[pathIndex], out childEntry))
                {
                    childEntry = new FileOrFolderData(pathParts[pathIndex]);
                    parentFolder.AddChild(this.context.Tracer, childEntry.Name, childEntry);
                }
                
                parentFolder = childEntry;
            }

            parentFolder.AddChild(this.context.Tracer, childData.Name, childData);

            return parentFolder;
        }

        private FileOrFolderData GetProjectedFileOrFolderData(string virtualPath, bool populateSize)
        {
            this.projectionReadWriteLock.EnterReadLock();
            try
            {
                FileOrFolderData parentFolderData;
                string childName;
                string parentKey;
                this.GetChildNameAndParentKey(virtualPath, out childName, out parentKey);
                if (this.TryGetOrAddFolderDataFromCache(parentKey, out parentFolderData))
                {
                    FileOrFolderData childData;
                    if (parentFolderData.ChildEntries.TryGetValue(childName, out childData))
                    {
                        bool returnChildData = this.sparseCheckoutAndDoNotProject.ShouldPathBeProjected(virtualPath, isFolder: childData.IsFolder);
                        if (returnChildData)
                        {
                            if (populateSize && !childData.IsFolder && !childData.IsSizeSet())
                            {
                                // If we need the size for a single file, batch the request and get sizes for all files in the folder
                                this.PopulateSizes_CanTimeout(parentFolderData);
                            }

                            return childData;
                        }
                    }
                }

                return null;
            }
            finally
            {
                this.projectionReadWriteLock.ExitReadLock();
            }
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
                    EventMetadata metadata = CreateEventMetadata("TryGetOrAddFolderDataFromCache: Found a file when expecting a folder");
                    metadata.Add("folderPath", folderPath);
                    this.context.Tracer.RelatedEvent(EventLevel.Warning, "GitIndexProjection_TryGetOrAddFolderDataFromCacheFoundFile", metadata);

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

        private void PopulateSizes_CanTimeout(FileOrFolderData parentFolderData)
        {
            if (parentFolderData.ChildrenHaveSizes)
            {
                return;
            }

            bool blobSizeUpdated = false;
            List<FileMissingSize> childrenMissingSizes = new List<FileMissingSize>();
            foreach (KeyValuePair<string, FileOrFolderData> childEntry in parentFolderData.ChildEntries)
            {
                if (!childEntry.Value.IsFolder && !childEntry.Value.IsSizeSet())
                {
                    string sha = childEntry.Value.ConvertShaToString();
                    long blobLength = 0;
                    if (this.blobSizes.TryGetValue(sha, out blobLength))
                    {
                        childEntry.Value.Size = blobLength;
                    }
                    else if (this.gitObjects.TryGetBlobSizeLocally_CanTimeout(sha, out blobLength))
                    {
                        childEntry.Value.Size = blobLength;
                        this.blobSizes[sha] = blobLength;
                        blobSizeUpdated = true;
                    }
                    else
                    {
                        childrenMissingSizes.Add(new FileMissingSize(childEntry.Value, sha));
                    }
                }
            }

            // Anything remaining should come from the remote.
            if (childrenMissingSizes.Count > 0)
            {
                // Lock parentFolderData to avoid multiple requests to the server for the same size list
                lock (parentFolderData)
                {
                    // Opening multiple files in the same folder at the same time could trigger multiple calls to PopulateSizes_CanTimeout
                    // at the same time.  Confirm once more that GVFS needs to download sizes object sizes
                    if (!parentFolderData.ChildrenHaveSizes)
                    {
                        Dictionary<string, long> objectLengths = this.gitObjects.GetFileSizes(childrenMissingSizes.Select(e => e.Sha).Distinct()).ToDictionary(s => s.Id, s => s.Size, StringComparer.OrdinalIgnoreCase);
                        foreach (FileMissingSize childNeedingSize in childrenMissingSizes)
                        {
                            long blobLength = 0;
                            if (objectLengths.TryGetValue(childNeedingSize.Sha, out blobLength))
                            {
                                childNeedingSize.Data.Size = blobLength;
                                this.blobSizes[childNeedingSize.Sha] = blobLength;
                                blobSizeUpdated = true;
                            }
                            else
                            {
                                EventMetadata metadata = CreateErrorMetadata("PopulateSizes_CanTimeout: Failed to download size");
                                metadata.Add("SHA", childNeedingSize.Sha);
                                this.context.Tracer.RelatedError(metadata, Keywords.Network);
                                throw new GvFltException(StatusCode.StatusFileNotAvailable);
                            }
                        }
                    }

                    parentFolderData.ChildrenHaveSizes = true;
                }
            }

            if (blobSizeUpdated)
            {
                this.blobSizes.Flush();
            }
        }

        private void GetChildNameAndParentKey(string virtualPath, out string childName, out string parentKey)
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

                    // Notify the WaitForExternalReleaseLockProcessingToComplete thread that this thread has received the lock release notification
                    // Wait until after we have acquired the write lock to ensure that nothing can access the projection between
                    // the git command ending and this thread reparsing the index
                    this.externalLockReleaseHandlingComplete.Set();

                    try
                    {
                        while (this.projectionInvalid)
                        {
                            try
                            {
                                this.lastUpdateTime = 0;
                                this.CopyIndexFileAndParse();
                            }
                            catch (Win32Exception e)
                            {
                                this.SetProjectionAndOffsetsAsInvalid();

                                EventMetadata metadata = CreateEventMetadata("Win32Exception when reparsing index for projection", e);
                                this.context.Tracer.RelatedEvent(EventLevel.Warning, "GitIndexProjection_ReprojectWin32Exception", metadata);
                            }
                            catch (IOException e)
                            {
                                this.SetProjectionAndOffsetsAsInvalid();

                                EventMetadata metadata = CreateEventMetadata("IOException when reparsing index for projection", e);
                                this.context.Tracer.RelatedEvent(EventLevel.Warning, "GitIndexProjection_ReprojectIOException", metadata);
                            }
                            catch (UnauthorizedAccessException e)
                            {
                                this.SetProjectionAndOffsetsAsInvalid();

                                EventMetadata metadata = CreateEventMetadata("UnauthorizedAccessException when reparsing index for projection", e);
                                this.context.Tracer.RelatedEvent(EventLevel.Warning, "GitIndexProjection_ReprojectUnauthorizedAccessException", metadata);
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

        private void LogErrorAndExit(string message, Exception e)
        {
            EventMetadata metadata = new EventMetadata();
            metadata.Add("Area", EtwArea);
            if (e != null)
            {
                metadata.Add("Exception", e.ToString());
            }

            metadata.Add("ErrorMessage", message);
            this.context.Tracer.RelatedError(metadata);
            Environment.Exit(1);
        }

        private void CopyIndexFileAndParse()
        {
            this.context.FileSystem.CopyFile(this.indexPath, this.projectionIndexBackupPath, overwrite: true);
            this.SetProjectionInvalid(false);
            this.offsetsInvalid = false;
            this.ParseProjection();
        }

        private void ParseProjection()
        {
            using (FileStream indexStream = new FileStream(this.projectionIndexBackupPath, FileMode.Open, FileAccess.Read, FileShare.Read, IndexFileStreamBufferSize))
            {
                this.ParseIndex(indexStream, updateOffsetsOnly: false);
            }
        }

        private void ParseIndex(FileStream indexFileStream, bool updateOffsetsOnly)
        {
            byte[] buffer = new byte[40];
            indexFileStream.Position = 0;

            indexFileStream.Read(buffer, 0, 8);
            uint entryCount = ReadUInt32(buffer, indexFileStream);
            
            if (!updateOffsetsOnly)
            {
                this.projectionFolderCache = new ConcurrentDictionary<string, FileOrFolderData>(StringComparer.OrdinalIgnoreCase);
                this.rootFolderData = new FileOrFolderData(string.Empty);
            }

            FileOrFolderData lastParent = null;
            string lastParentPath = null;

            int previousPathLength = 0;
            byte[] pathBuffer = new byte[MaxPathBufferSize];
            for (int i = 0; i < entryCount; i++)
            {
                // If the projection or offsets get set as invalid while being parsed we can bail
                // since the index will have to be reparsed
                if ((updateOffsetsOnly && this.offsetsInvalid) || (!updateOffsetsOnly && this.projectionInvalid))
                {
                    return;
                }

                long entryOffset = indexFileStream.Position;
                indexFileStream.Read(buffer, 0, 40);

                // Note: sha must be allocated inside the loop because we save a reference to it in AddItem below
                // Optimization TODO: if we're parsing for offsets only, we don't need to allocate anything here
                byte[] sha = new byte[20];
                indexFileStream.Read(sha, 0, 20);

                ushort flags = ReadUInt16(buffer, indexFileStream);
                bool isExtended = (flags & ExtendedBit) == ExtendedBit;
                int pathLength = (ushort)(((flags << 20) >> 20) & 4095);

                bool skipWorktree = false;
                if (isExtended)
                {
                    ushort extendedFlags = ReadUInt16(buffer, indexFileStream);
                    skipWorktree = (extendedFlags & SkipWorktreeBit) == SkipWorktreeBit;
                }

                int replaceLength = ReadReplaceLength(indexFileStream);
                int replaceIndex = previousPathLength - replaceLength;
                indexFileStream.Read(pathBuffer, replaceIndex, pathLength - replaceIndex + 1);
                previousPathLength = pathLength;
                if (skipWorktree)
                {
                    // Potential Future Perf Optimization: Perform this work on multiple threads.  If we take the first byte and % by number of threads,
                    // we can ensure that all entries for a given folder end up in the same dictionary              
                    string path = Encoding.UTF8.GetString(pathBuffer, 0, pathLength);
                    if (updateOffsetsOnly)
                    {
                        this.UpdateFileOffset(path, entryOffset, ref lastParent, ref lastParentPath);
                    }
                    else
                    {
                        this.AddItem(path, sha, entryOffset, ref lastParent, ref lastParentPath);
                    }
                }
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
            private const long FolderWithoutChildSizesSize = -2; // Size of -2 indicates that entry is a folder whose children do not have their sizes
            private const long FolderWithChildSizesSize = -3; // Size of -3 indicates that entry is a folder whose children do have their sizes
            private byte[] shaBytes;

            /// <summary>
            /// Create a FileOrFolderData for a folder
            /// </summary>
            public FileOrFolderData(string folderName)
            {
                this.Name = folderName;
                this.Size = FolderWithoutChildSizesSize;
                this.ChildEntries = new Dictionary<string, FileOrFolderData>(StringComparer.OrdinalIgnoreCase);
                this.Offset = InvalidOffset;
            }

            /// <summary>
            /// Create a FileOrFolderData for a file
            /// </summary>
            public FileOrFolderData(string fileName, byte[] shaBytes, long offset)    
            {
                this.Name = fileName;
                this.shaBytes = shaBytes;
                this.Size = InvalidSize;
                this.Offset = offset;
                this.LastUpdateTime = 0;
            }

            public bool IsFolder
            {
                get { return this.Size == FolderWithoutChildSizesSize || this.Size == FolderWithChildSizesSize; }
            }

            public bool ChildrenHaveSizes
            {
                get
                {
                    return this.Size == FolderWithChildSizesSize;
                }

                set
                {
                    if (value)
                    {
                        this.Size = FolderWithChildSizesSize;
                    }
                    else
                    {
                        this.Size = FolderWithoutChildSizesSize;
                    }
                }
            }

            public string Name { get; }
            public long Size { get; set; }
            public long Offset { get; private set; }
            public uint LastUpdateTime { get; private set; }

            // Potential Future Memory Optimization: Switch ChildEntries to HashSet so that 
            // we can avoid having to store another string reference
            public Dictionary<string, FileOrFolderData> ChildEntries { get; }

            public bool IsSizeSet()
            {
                return this.Size >= MinValidSize;
            }

            public string ConvertShaToString()
            {
                return BitConverter.ToString(this.shaBytes).Replace("-", string.Empty);
            }

            public void AddChild(ITracer tracer, string childName, FileOrFolderData data)
            {
                try
                {
                    this.ChildEntries.Add(childName, data);
                }
                catch (ArgumentException e)
                {
                    EventMetadata metadata = this.CreateErrorMetadata("Skipping addition of child, entry already exists in collection", e);
                    metadata.Add("childName", childName);
                    tracer.RelatedEvent(EventLevel.Warning, "AddChild_DuplicateEntry", metadata);
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
                        EventMetadata metadata = this.CreateErrorMetadata("UpdateChildOffset: Failed to find childName in ChildEntries");
                        metadata.Add("childName", childName);
                        metadata.Add("offset", offset);
                        tracer.RelatedError(metadata);
                    }
                }
                else
                {
                    EventMetadata metadata = this.CreateEventMetadata("UpdateChildOffset: Skipping update of child, this FileOrFolderData is a file");
                    metadata.Add("childName", childName);
                    metadata.Add("offset", offset);
                    tracer.RelatedEvent(EventLevel.Warning, "UpdateChildOffset_ParentFileOrFolderDataIsFile", metadata);
                }
            }

            private void SetOffset(ITracer tracer, long offset, uint updateTime)
            {
                if (this.IsFolder)
                {
                    EventMetadata metadata = this.CreateEventMetadata("SetOffset: Skipping update of file offset, this FileOrFolderData is a folder");
                    metadata.Add("offset", offset);
                    metadata.Add("updateTime", updateTime);
                    tracer.RelatedEvent(EventLevel.Warning, "SetOffset_FileOrFolderDataIsFolder", metadata);
                }
                else
                {
                    this.Offset = offset;
                    this.LastUpdateTime = updateTime;
                }
            }

            private EventMetadata CreateEventMetadata(string message, Exception e = null)
            {
                EventMetadata metadata = GitIndexProjection.CreateEventMetadata(message, e);
                metadata.Add("Name", this.Name);
                return metadata;
            }

            private EventMetadata CreateErrorMetadata(string message, Exception e = null)
            {
                EventMetadata metadata = GitIndexProjection.CreateErrorMetadata(message, e);
                metadata.Add("Name", this.Name);
                return metadata;
            }
        }
    }
}
