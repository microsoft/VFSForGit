using GvFlt;
using GVFS.Common;
using GVFS.Common.FileSystem;
using GVFS.Common.Git;
using GVFS.Common.NamedPipes;
using GVFS.Common.NetworkStreams;
using GVFS.Common.Tracing;
using GVFS.GVFlt.DotGit;
using Microsoft.Database.Isam.Config;
using Microsoft.Diagnostics.Tracing;
using Microsoft.Isam.Esent.Collections.Generic;
using Newtonsoft.Json;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;

using ITracer = GVFS.Common.Tracing.ITracer;

namespace GVFS.GVFlt
{
    public class GVFltCallbacks : IDisposable, IHeartBeatMetadataProvider
    {
        public const byte PlaceholderVersion = 1;

        private const int MaxBlobStreamBufferSize = 64 * 1024;
        private const string RefMarker = "ref:";
        private const string EtwArea = "GVFltCallbacks";
        private const int BlockSize = 64 * 1024;

        private const int MinGvFltThreads = 5;

        private static readonly string RefsHeadsPath = GVFSConstants.DotGit.Refs.Heads.Root + GVFSConstants.PathSeparator;
        private readonly string logsHeadPath;

        private VirtualizationInstance gvflt;
        private object stopLock = new object();
        private bool gvfltIsStarted = false;
        private bool isMountComplete = false;
        private bool placeholderListUpdatedByBackgroundThread = false;
        private ConcurrentDictionary<Guid, GVFltActiveEnumeration> activeEnumerations;
        private ConcurrentDictionary<string, PlaceHolderCreateCounter> placeHolderCreationCount;
        private GVFSGitObjects gvfsGitObjects;
        private SparseCheckout sparseCheckout;
        private GitIndexProjection gitIndexProjection;
        private AlwaysExcludeFile alwaysExcludeFile;
        private PersistentDictionary<string, long> blobSizes;

        private ReliableBackgroundOperations<BackgroundGitUpdate> background;
        private GVFSContext context;
        private RepoMetadata repoMetadata;
        private FileProperties logsHeadFileProperties;

        public GVFltCallbacks(GVFSContext context, GVFSGitObjects gitObjects, RepoMetadata repoMetadata)
        {
            this.context = context;
            this.repoMetadata = repoMetadata;
            this.logsHeadFileProperties = null;
            this.gvflt = new VirtualizationInstance();
            this.activeEnumerations = new ConcurrentDictionary<Guid, GVFltActiveEnumeration>();
            this.sparseCheckout = new SparseCheckout(
                this.context,
                Path.Combine(this.context.Enlistment.WorkingDirectoryRoot, GVFSConstants.DotGit.Info.SparseCheckoutPath));
            this.alwaysExcludeFile = new AlwaysExcludeFile(this.context, Path.Combine(this.context.Enlistment.WorkingDirectoryRoot, GVFSConstants.DotGit.Info.AlwaysExcludePath));
            this.blobSizes = new PersistentDictionary<string, long>(
                Path.Combine(this.context.Enlistment.DotGVFSRoot, GVFSConstants.DatabaseNames.BlobSizes),
                new DatabaseConfig()
                {
                    CacheSizeMax = 500 * 1024 * 1024, // 500 MB
                });
            this.gvfsGitObjects = gitObjects;

            this.gitIndexProjection = new GitIndexProjection(
                context, 
                gitObjects, 
                this.blobSizes, 
                this.repoMetadata, 
                this.gvflt,
                new PersistentDictionary<string, string>(Path.Combine(this.context.Enlistment.DotGVFSRoot, GVFSConstants.DatabaseNames.PlaceholderList)),
                this.sparseCheckout);

            this.background = new ReliableBackgroundOperations<BackgroundGitUpdate>(
                this.context,
                this.PreBackgroundOperation,
                this.ExecuteBackgroundOperation,
                this.PostBackgroundOperation,
                GVFSConstants.DatabaseNames.BackgroundGitUpdates);

            this.logsHeadPath = Path.Combine(this.context.Enlistment.WorkingDirectoryRoot, GVFSConstants.DotGit.Logs.Head);
            this.placeHolderCreationCount = new ConcurrentDictionary<string, PlaceHolderCreateCounter>(StringComparer.OrdinalIgnoreCase);
        }
        
        public IProfilerOnlyIndexProjection GitIndexProjectionProfiler
        {
            get { return this.gitIndexProjection; }
        }

        public static bool TryPrepareFolderForGVFltCallbacks(string folderPath, out string error)
        {
            error = string.Empty;
            Guid virtualizationInstanceGuid = Guid.NewGuid();
            HResult result = VirtualizationInstance.ConvertDirectoryToVirtualizationRoot(virtualizationInstanceGuid, folderPath);
            if (result != HResult.Ok)
            {
                error = "Failed to prepare \"" + folderPath + "\" for callbacks, error: " + result.ToString("F");
                return false;
            }

            return true;
        }

        public static bool DoesPathAllowDelete(string virtualPath)
        {
            if (virtualPath.Equals(GVFSConstants.DotGit.Index, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            return true;
        }

        public static bool IsPathMonitoredForWrites(string virtualPath)
        {
            if (virtualPath.Equals(GVFSConstants.DotGit.Index, StringComparison.OrdinalIgnoreCase) ||
                virtualPath.Equals(GVFSConstants.DotGit.Logs.Head, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return false;
        }

        public static string GetShaFromContentId(byte[] contentId)
        {
            return Encoding.Unicode.GetString(contentId, 0, GVFSConstants.ShaStringLength * sizeof(char));
        }

        public static byte GetPlaceholderVersionFromEpochId(byte[] epochId)
        {
            return epochId[0];
        }        

        public static byte[] ConvertShaToContentId(string sha)
        {
            return Encoding.Unicode.GetBytes(sha);
        }

        public static byte[] GetEpochId()
        {
            return new byte[] { PlaceholderVersion };
        }

        public NamedPipeMessages.ReleaseLock.Response TryReleaseExternalLock(int pid)
        {
            return this.gitIndexProjection.TryReleaseExternalLock(pid);
        }

        public int GetBackgroundOperationCount()
        {
            return this.background.Count;
        }

        public bool IsReadyForExternalAcquireLockRequests()
        {
            return this.isMountComplete && this.GetBackgroundOperationCount() == 0 && this.gitIndexProjection.IsProjectionParseComplete();
        }

        public bool TryStart(out string error)
        {
            error = string.Empty;

            this.sparseCheckout.LoadOrCreate();
            this.alwaysExcludeFile.LoadOrCreate();

            // Callbacks
            this.gvflt.OnStartDirectoryEnumeration = this.GVFltStartDirectoryEnumerationHandler;
            this.gvflt.OnEndDirectoryEnumeration = this.GVFltEndDirectoryEnumerationHandler;
            this.gvflt.OnGetDirectoryEnumeration = this.GVFltGetDirectoryEnumerationHandler;
            this.gvflt.OnQueryFileName = this.GVFltQueryFileNameHandler;
            this.gvflt.OnGetPlaceholderInformation = this.GVFltGetPlaceholderInformationHandler;
            this.gvflt.OnGetFileStream = this.GVFltGetFileStreamHandler;
            this.gvflt.OnNotifyFirstWrite = this.GVFltNotifyFirstWriteHandler;

            this.gvflt.OnNotifyFileHandleCreated = this.GVFltNotifyFileHandleCreatedHandler;
            this.gvflt.OnNotifyPreDelete = this.GVFltNotifyPreDeleteHandler;
            this.gvflt.OnNotifyPreRename = this.GvFltNotifyPreRenameHandler;
            this.gvflt.OnNotifyPreSetHardlink = null;
            this.gvflt.OnNotifyFileRenamed = this.GVFltNotifyFileRenamedHandler;
            this.gvflt.OnNotifyHardlinkCreated = null;
            this.gvflt.OnNotifyFileHandleClosed = this.GVFltNotifyFileHandleClosedHandler;

            uint threadCount = (uint)Math.Max(MinGvFltThreads, Environment.ProcessorCount * 2);

            // We currently use twice as many threads as connections to allow for 
            // non-network operations to possibly succeed despite the connection limit
            HResult result = this.gvflt.StartVirtualizationInstance(
                new GvFltTracer(this.context.Tracer),
                this.context.Enlistment.WorkingDirectoryRoot,
                poolThreadCount: threadCount,
                concurrentThreadCount: threadCount);

            if (result != HResult.Ok)
            {
                this.context.Tracer.RelatedError("GvStartVirtualizationInstance failed: " + result.ToString("X") + "(" + result.ToString("G") + ")");
                error = "Failed to start virtualization instance (" + result.ToString() + ")";
                return false;
            }

            using (ITracer activity = this.context.Tracer.StartActivity("InitialProjectionParse", EventLevel.Informational))
            {
                this.gitIndexProjection.Initialize(this.background);
            }

            this.gvfltIsStarted = true;
            this.background.Start();
            this.isMountComplete = true;

            return true;
        }

        public void Stop()
        {
            lock (this.stopLock)
            {
                // Stop the background thread first since some of its operations might require that the GVFlt
                // Virtualization Instance still be present
                this.background.Shutdown();
                this.gitIndexProjection.Shutdown();

                if (this.gvfltIsStarted)
                {
                    this.gvflt.StopVirtualizationInstance();
                    this.gvflt.DetachDriver();
                    Console.WriteLine("GVFlt callbacks stopped");
                    this.gvfltIsStarted = false;
                }
            }
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

            metadata.Add("SparseCheckoutCount", this.sparseCheckout.EntryCount);
            metadata.Add("PlaceholderCount", this.gitIndexProjection.PlaceholderCount);

            return metadata;
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
                if (this.blobSizes != null)
                {
                    this.blobSizes.Dispose();
                    this.blobSizes = null;
                }

                if (this.gitIndexProjection != null)
                {
                    this.gitIndexProjection.Dispose();
                    this.gitIndexProjection = null;
                }

                if (this.repoMetadata != null)
                {
                    this.repoMetadata.Dispose();
                    this.repoMetadata = null;
                }

                if (this.background != null)
                {
                    this.background.Dispose();
                    this.background = null;
                }

                if (this.context != null)
                {
                    this.context.Dispose();
                    this.context = null;
                }
            }
        }

        private void OnIndexFileChange()
        {
            string lockedGitCommand = this.context.Repository.GVFSLock.GetLockedGitCommand();
            GitCommandLineParser gitCommand = new GitCommandLineParser(lockedGitCommand);
            if (this.gitIndexProjection.IsIndexBeingUpdatedByGVFS())
            {
                // No need to invalidate anything, because this event came from our own background thread writing to the index

                if (gitCommand.IsValidGitCommand)
                {
                    // But there should never be a case where GVFS is writing to the index while Git is holding the lock
                    EventMetadata metadata = new EventMetadata
                    {
                        { "Area", EtwArea },
                        { "Message", "GVFS wrote to the index while git was holding the GVFS lock" },
                        { "GitCommand", lockedGitCommand },
                    };

                    this.context.Tracer.RelatedEvent(EventLevel.Warning, "OnIndexFileChange_LockCollision", metadata);
                }
            }
            else if (!gitCommand.IsValidGitCommand)
            {
                // Something wrote to the index without holding the GVFS lock, so we invalidate the projection
                this.gitIndexProjection.InvalidateProjection();

                // But this isn't something we expect to see, so log a warning
                EventMetadata metadata = new EventMetadata
                {
                    { "Area", EtwArea },
                    { "Message", "Index modified without git holding GVFS lock" },
                };

                this.context.Tracer.RelatedEvent(EventLevel.Warning, "OnIndexFileChange_NoLock", metadata);
            }
            else if (this.GitCommandLeavesProjectionUnchanged(gitCommand))
            {
                this.gitIndexProjection.InvalidateOffsetsAndSparseCheckout();
                this.background.Enqueue(BackgroundGitUpdate.OnIndexWriteWithoutProjectionChange());
            }
            else
            {
                this.gitIndexProjection.InvalidateProjection();
            }
        }

        private bool GitCommandLeavesProjectionUnchanged(GitCommandLineParser gitCommand)
        {
            return
                gitCommand.IsVerb("add", "commit", "status", "update-index") ||
                gitCommand.IsResetSoftOrMixed() ||
                gitCommand.IsCheckoutWithFilePaths();
        }    

        private void OnLogsHeadChange()
        {
            // Don't open the .git\logs\HEAD file here to check its attributes as we're in a callback for the .git folder
            this.logsHeadFileProperties = null;
        }

        private NtStatus GVFltStartDirectoryEnumerationHandler(Guid enumerationId, string virtualPath)
        {
            virtualPath = PathUtil.RemoveTrailingSlashIfPresent(virtualPath);

            if (!this.isMountComplete)
            {
                EventMetadata metadata = this.CreateEventMetadata(
                    "GVFltStartDirectoryEnumerationHandler: Failed to start enumeration, mount has not yet completed",
                    virtualPath);
                metadata.Add("enumerationId", enumerationId);
                this.context.Tracer.RelatedEvent(EventLevel.Informational, "StartDirectoryEnum_MountNotComplete", metadata);

                return NtStatus.DeviceNotReady;
            }

            GVFltActiveEnumeration activeEnumeration = new GVFltActiveEnumeration(this.gitIndexProjection.GetProjectedItems(virtualPath));            
            if (!this.activeEnumerations.TryAdd(enumerationId, activeEnumeration))
            {
                EventMetadata metadata = this.CreateEventMetadata(
                    "GVFltStartDirectoryEnumerationHandler: Failed to add enumeration ID to active collection",
                    virtualPath,
                    exception: null,
                    errorMessage: true);
                metadata.Add("enumerationId", enumerationId);
                this.context.Tracer.RelatedError(metadata);

                activeEnumeration.Dispose();
                return NtStatus.InvalidParameter;
            }

            return NtStatus.Succcess;
        }

        private NtStatus GVFltEndDirectoryEnumerationHandler(Guid enumerationId)
        {
            GVFltActiveEnumeration activeEnumeration;
            if (this.activeEnumerations.TryRemove(enumerationId, out activeEnumeration))
            {
                activeEnumeration.Dispose();
            }
            else
            {
                EventMetadata metadata = this.CreateEventMetadata(
                    "GVFltEndDirectoryEnumerationHandler: Failed to remove enumeration ID from active collection",
                    virtualPath: null,
                    exception: null,
                    errorMessage: true);

                metadata.Add("enumerationId", enumerationId);
                this.context.Tracer.RelatedError(metadata);
                return NtStatus.InvalidParameter;
            }

            return NtStatus.Succcess;
        }

        private NtStatus GVFltGetDirectoryEnumerationHandler(
            Guid enumerationId,
            string filterFileName,
            bool restartScan,
            DirectoryEnumerationResult result)
        {
            GVFltActiveEnumeration activeEnumeration = null;
            if (!this.activeEnumerations.TryGetValue(enumerationId, out activeEnumeration))
            {
                EventMetadata metadata = this.CreateEventMetadata(
                    "GVFltGetDirectoryEnumerationHandler: Failed to find active enumeration ID",
                    virtualPath: null,
                    exception: null,
                    errorMessage: true);
                metadata.Add("filterFileName", filterFileName);
                metadata.Add("enumerationId", enumerationId);
                metadata.Add("restartScan", restartScan);
                this.context.Tracer.RelatedError(metadata);

                return NtStatus.InternalError;
            }

            bool initialRequest;
            if (restartScan)
            {
                activeEnumeration.RestartEnumeration(filterFileName);
                initialRequest = true;
            }
            else
            {
                initialRequest = activeEnumeration.TrySaveFilterString(filterFileName);
            }

            if (activeEnumeration.IsCurrentValid)
            {
                GVFltFileInfo fileInfo = activeEnumeration.Current;
                FileProperties properties = this.GetLogsHeadFileProperties();

                result.ChangeTime = properties.LastWriteTimeUTC;
                result.CreationTime = properties.CreationTimeUTC;
                result.LastAccessTime = properties.LastAccessTimeUTC;
                result.LastWriteTime = properties.LastWriteTimeUTC;

                if (fileInfo.IsFolder)
                {
                    result.EndOfFile = 0;
                    result.FileAttributes = (uint)NativeMethods.FileAttributes.FILE_ATTRIBUTE_DIRECTORY;
                }
                else
                {
                    result.EndOfFile = fileInfo.Size;
                    result.FileAttributes = (uint)NativeMethods.FileAttributes.FILE_ATTRIBUTE_ARCHIVE;
                }

                if (result.TrySetFileName(fileInfo.Name))
                {
                    // Only advance the enumeration if the file name fit in the DirectoryEnumerationResult
                    activeEnumeration.MoveNext();
                    return NtStatus.Succcess;
                }
                else
                {
                    // Return StatusBufferOverflow to indicate that the file name had to be truncated
                    return NtStatus.BufferOverflow;
                }
            }

            // TODO 636568: Confirm return code values/behavior with GVFlt team
            NtStatus statusCode = (initialRequest && PathUtil.IsEnumerationFilterSet(filterFileName)) ? NtStatus.NoSuchFile : NtStatus.NoMoreFiles;
            return statusCode;
        }

        /// <summary>
        /// GVFltQueryFileNameHandler is called by GVFlt when a file is being deleted or renamed.  It is an optimization so that GVFlt
        /// can avoid calling Start\Get\End enumeration to check if GVFS is still projecting a file.  This method uses the same
        /// rules for deciding what is projected as the enumeration callbacks.
        /// </summary>
        private NtStatus GVFltQueryFileNameHandler(string virtualPath)
        {
            if (PathUtil.IsPathInsideDotGit(virtualPath))
            {
                return NtStatus.ObjectNameNotFound;
            }

            virtualPath = PathUtil.RemoveTrailingSlashIfPresent(virtualPath);

            if (!this.isMountComplete)
            {
                EventMetadata metadata = this.CreateEventMetadata("GVFltQueryFileNameHandler: Mount has not yet completed", virtualPath);
                this.context.Tracer.RelatedEvent(EventLevel.Informational, "QueryFileName_MountNotComplete", metadata);
                return NtStatus.DeviceNotReady;
            }

            bool isFolder;
            if (!this.gitIndexProjection.IsPathProjected(virtualPath, out isFolder))
            {
                return NtStatus.ObjectNameNotFound;
            }

            return NtStatus.Succcess;
        }

        private NtStatus GVFltGetPlaceholderInformationHandler(
            string virtualPath,
            uint desiredAccess,
            uint shareMode,
            uint createDisposition,
            uint createOptions,
            uint triggeringProcessId,
            string triggeringProcessImageFileName)
        {
            virtualPath = PathUtil.RemoveTrailingSlashIfPresent(virtualPath);

            if (!this.isMountComplete)
            {
                EventMetadata metadata = this.CreateEventMetadata("GVFltGetPlaceholderInformationHandler: Mount has not yet completed", virtualPath);
                metadata.Add("desiredAccess", desiredAccess);
                metadata.Add("shareMode", shareMode);
                metadata.Add("createDisposition", createDisposition);
                metadata.Add("createOptions", createOptions);
                metadata.Add("triggeringProcessId", triggeringProcessId);
                metadata.Add("triggeringProcessImageFileName", triggeringProcessImageFileName);
                this.context.Tracer.RelatedEvent(EventLevel.Informational, "GetPlaceHolder_MountNotComplete", metadata);

                return NtStatus.DeviceNotReady;
            }
            
            string sha;
            string parentFolderPath;
            GVFltFileInfo fileInfo = this.gitIndexProjection.GetProjectedGVFltFileInfoAndSha(virtualPath, out parentFolderPath, out sha);
            if (fileInfo == null)
            {
                return NtStatus.ObjectNameNotFound;
            }

            if (!fileInfo.IsFolder &&
                !this.IsSpecialGitFile(fileInfo) &&
                !this.CanCreatePlaceholder())
            {
                EventMetadata metadata = this.CreateEventMetadata("GVFltGetPlaceholderInformationHandler: Not allowed to create placeholder", virtualPath);
                metadata.Add("desiredAccess", desiredAccess);
                metadata.Add("shareMode", shareMode);
                metadata.Add("createDisposition", createDisposition);
                metadata.Add("createOptions", createOptions);
                metadata.Add("triggeringProcessId", triggeringProcessId);
                metadata.Add("triggeringProcessImageFileName", triggeringProcessImageFileName);
                this.context.Tracer.RelatedEvent(EventLevel.Verbose, nameof(this.GVFltGetPlaceholderInformationHandler), metadata);

                // Another process is modifying the working directory so we cannot modify it
                // until they are done.
                return NtStatus.ObjectNameNotFound;
            }

            // The file name case in the virtualPath parameter might be different than the file name case in the repo.
            // Build a new virtualPath that preserves the case in the repo so that the placeholder file is created
            // with proper case.
            string gitCaseVirtualPath = Path.Combine(parentFolderPath, fileInfo.Name);

            uint fileAttributes;
            if (fileInfo.IsFolder)
            {
                fileAttributes = (uint)NativeMethods.FileAttributes.FILE_ATTRIBUTE_DIRECTORY;
            }
            else
            {
                fileAttributes = (uint)NativeMethods.FileAttributes.FILE_ATTRIBUTE_ARCHIVE;
            }

            FileProperties properties = this.GetLogsHeadFileProperties();
            NtStatus result = this.gvflt.WritePlaceholderInformation(
                gitCaseVirtualPath,
                properties.CreationTimeUTC,
                properties.LastAccessTimeUTC,
                properties.LastWriteTimeUTC,
                changeTime: properties.LastWriteTimeUTC,
                fileAttributes: fileAttributes,
                endOfFile: fileInfo.Size,
                directory: fileInfo.IsFolder,
                contentId: ConvertShaToContentId(sha),
                epochId: GVFltCallbacks.GetEpochId());

            if (result != NtStatus.Succcess)
            {
                EventMetadata metadata = this.CreateEventMetadata("GVFltGetPlaceholderInformationHandler: GvWritePlaceholderInformation failed", virtualPath, exception: null, errorMessage: true);
                metadata.Add("gitCaseVirtualPath", gitCaseVirtualPath);
                metadata.Add("desiredAccess", desiredAccess);
                metadata.Add("shareMode", shareMode);
                metadata.Add("createDisposition", createDisposition);
                metadata.Add("createOptions", createOptions);
                metadata.Add("triggeringProcessId", triggeringProcessId);
                metadata.Add("triggeringProcessImageFileName", triggeringProcessImageFileName);
                metadata.Add("FileName", fileInfo.Name);
                metadata.Add("IsFolder", fileInfo.IsFolder);
                metadata.Add("NtStatus", result.ToString("X") + "(" + result.ToString("G") + ")");
                this.context.Tracer.RelatedError(metadata);
            }
            else
            {
                if (!fileInfo.IsFolder)
                {
                    this.gitIndexProjection.OnPlaceholderFileCreated(gitCaseVirtualPath, sha);

                    // Note: Because GVFltGetPlaceholderInformationHandler is not synchronized it is possible that GVFS will double count
                    // the creation of file placeholders if multiple requests for the same file are received at the same time on different
                    // threads.                         
                    this.placeHolderCreationCount.AddOrUpdate(
                        triggeringProcessImageFileName,
                        (imageName) => { return new PlaceHolderCreateCounter(); },
                        (key, oldCount) => { oldCount.Increment(); return oldCount; });
                }
            }

            return result;
        }

        private NtStatus GVFltGetFileStreamHandler(
            string virtualPath,
            long byteOffset,
            uint length,
            Guid streamGuid,
            byte[] contentId, 
            byte[] epochId,            
            uint triggeringProcessId,
            string triggeringProcessImageFileName)
        {
            string sha = GetShaFromContentId(contentId);
            byte placeholderVersion = GetPlaceholderVersionFromEpochId(epochId);

            EventMetadata metadata = new EventMetadata();
            metadata.Add("originalVirtualPath", virtualPath);
            metadata.Add("byteOffset", byteOffset);
            metadata.Add("length", length);
            metadata.Add("streamGuid", streamGuid);
            metadata.Add("triggeringProcessId", triggeringProcessId);
            metadata.Add("triggeringProcessImageFileName", triggeringProcessImageFileName);
            metadata.Add("sha", sha);
            metadata.Add("placeholderVersion", placeholderVersion);
            using (ITracer activity = this.context.Tracer.StartActivity("GetFileStream", EventLevel.Verbose, Keywords.Telemetry, metadata))
            {
                if (!this.isMountComplete)
                {
                    metadata.Add("Message", "GVFltGetFileStreamHandler failed, mount has not yet completed");
                    activity.RelatedEvent(EventLevel.Informational, "GetFileStream_MountNotComplete", metadata);
                    return NtStatus.DeviceNotReady;
                }

                if (byteOffset != 0)
                {
                    metadata.Add("ErrorMessage", "Invalid Parameter: byteOffset must be 0");
                    activity.RelatedError(metadata);
                    return NtStatus.InvalidParameter;
                }

                if (placeholderVersion != PlaceholderVersion)
                {
                    metadata.Add("ErrorMessage", "GVFltGetFileStreamHandler: Unexpected placeholder version");
                    activity.RelatedError(metadata);
                    return NtStatus.InternalError;
                }

                try
                {
                    if (!this.gvfsGitObjects.TryCopyBlobContentStream(
                        sha,
                        (stream, blobLength) =>
                    {
                        if (blobLength != length)
                        {
                            metadata.Add("blobLength", blobLength);
                            metadata.Add("ErrorMessage", "Actual file length (blobLength) does not match requested length");
                            activity.RelatedError(metadata);

                            throw new GvFltException(NtStatus.InvalidParameter);
                        }

                        byte[] buffer = new byte[Math.Min(MaxBlobStreamBufferSize, blobLength)];
                        long remainingData = blobLength;

                        using (WriteBuffer targetBuffer = gvflt.CreateWriteBuffer())
                        {
                            while (remainingData > 0)
                            {
                                uint bytesToCopy = (uint)Math.Min(remainingData, targetBuffer.Length);

                                try
                                {
                                    targetBuffer.Stream.Seek(0, SeekOrigin.Begin);
                                    stream.CopyBlockTo(targetBuffer.Stream, bytesToCopy, buffer);
                                }
                                catch (IOException e)
                                {
                                    metadata.Add("Exception", e.ToString());
                                    metadata.Add("ErrorMessage", "IOException while copying to unmanaged buffer.");
                                    activity.RelatedError(metadata);
                                    throw new GvFltException("IOException while copying to unmanaged buffer: " + e.Message, NtStatus.FileNotAvailable);
                                }

                                long writeOffset = length - remainingData;

                                NtStatus writeResult = this.gvflt.WriteFile(streamGuid, targetBuffer, (ulong)writeOffset, bytesToCopy);
                                remainingData -= bytesToCopy;

                                if (writeResult != NtStatus.Succcess)
                                {
                                    switch (writeResult)
                                    {
                                        case NtStatus.FileClosed:
                                            // StatusFileClosed is expected, and occurs when an application closes a file handle before OnGetFileStream
                                            // is complete
                                            break;

                                        case NtStatus.ObjectNameNotFound:
                                            // GvWriteFile may return STATUS_OBJECT_NAME_NOT_FOUND if the stream guid provided is not valid (doesn’t exist in the stream table).
                                            // For each file expansion, GVFlt creates a new get stream session with a new stream guid, the session starts at the beginning of the 
                                            // file expansion, and ends after the GetFileStream command returns or times out.
                                            //
                                            // If we hit this in GVFS, the most common explanation is that we're calling GvWriteFile after the GVFlt thread waiting on the respose
                                            // from GetFileStream has already timed out
                                            metadata.Add("Message", "GvWriteFile returned StatusObjectNameNotFound");
                                            activity.RelatedEvent(EventLevel.Informational, "GetFileStream_ObjectNameNotFound", metadata);
                                            break;

                                        default:
                                            metadata.Add("ErrorMessage", "GvWriteFile failed, error: " + writeResult.ToString("X") + "(" + writeResult.ToString("G") + ")");
                                            activity.RelatedError(metadata);
                                            break;
                                    }

                                    throw new GvFltException(writeResult);
                                }
                            }
                        }
                    }))
                    {
                        metadata.Add("ErrorMessage", "TryCopyBlobContentStream failed");
                        activity.RelatedError(metadata);
                        return NtStatus.FileNotAvailable;
                    }
                }
                catch (GvFltException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    metadata.Add("ErrorMessage", "TryCopyBlobContentStream failed");
                    metadata.Add("Exception", ex.ToString());
                    activity.RelatedError(metadata);
                    return NtStatus.FileNotAvailable;
                }

                return NtStatus.Succcess;
            }
        }

        private NtStatus GVFltNotifyFirstWriteHandler(string virtualPath)
        {
            virtualPath = PathUtil.RemoveTrailingSlashIfPresent(virtualPath);

            if (!this.isMountComplete)
            {
                EventMetadata metadata = this.CreateEventMetadata("GVFltNotifyFirstWriteHandler: Mount has not yet completed", virtualPath);
                this.context.Tracer.RelatedEvent(EventLevel.Informational, "NotifyFirstWrite_MountNotComplete", metadata);
                return NtStatus.DeviceNotReady;
            }

            if (string.Equals(virtualPath, string.Empty))
            {
                // Empty path is the root folder
                this.background.Enqueue(BackgroundGitUpdate.OnFolderFirstWrite(virtualPath));
            }
            else
            {
                bool isFolder;
                bool isPathProjected = this.gitIndexProjection.IsPathProjected(virtualPath, out isFolder);
                if (isPathProjected)
                {
                    if (isFolder)
                    {
                        this.background.Enqueue(BackgroundGitUpdate.OnFolderFirstWrite(virtualPath));
                    }
                    else
                    {
                        this.background.Enqueue(BackgroundGitUpdate.OnFileFirstWrite(virtualPath));
                    }
                }
            }

            return NtStatus.Succcess;
        }

        private void GVFltNotifyFileHandleCreatedHandler(
            string virtualPath,
            bool isDirectory,
            uint desiredAccess,
            uint shareMode,
            uint createDisposition,
            uint createOptions,
            IoStatusBlockValue iostatusBlock,
            ref uint notificationMask)
        {
            if (PathUtil.IsPathInsideDotGit(virtualPath))
            {
                notificationMask = this.GetDotGitNotificationMask(virtualPath);
            }
            else 
            {
                notificationMask = this.GetWorkingDirectoryNotificationMask(isDirectory);

                switch (iostatusBlock)
                {
                    case IoStatusBlockValue.FileCreated:
                        if (isDirectory)
                        {
                            this.background.Enqueue(BackgroundGitUpdate.OnFolderCreated(virtualPath));
                        }
                        else
                        {
                            this.background.Enqueue(BackgroundGitUpdate.OnFileCreated(virtualPath));
                        }

                        break;

                    case IoStatusBlockValue.FileOverwritten:
                        if (!isDirectory)
                        {
                            this.background.Enqueue(BackgroundGitUpdate.OnFileOverwritten(virtualPath));
                        }

                        break;

                    case IoStatusBlockValue.FileSuperseded:
                        if (!isDirectory)
                        {
                            this.background.Enqueue(BackgroundGitUpdate.OnFileSuperseded(virtualPath));
                        }

                        break;

                    default:
                        break;
                }
            }
        }

        private NtStatus GvFltNotifyPreRenameHandler(string relativePath, string destinationPath)
        {
            if (destinationPath.Equals(GVFSConstants.DotGit.Index, StringComparison.OrdinalIgnoreCase))
            {
                string lockedGitCommand = this.context.Repository.GVFSLock.GetLockedGitCommand();
                if (string.IsNullOrEmpty(lockedGitCommand))
                {
                    EventMetadata metadata = new EventMetadata();
                    metadata.Add("Area", EtwArea);
                    metadata.Add("Message", "Blocked index rename outside the lock");
                    this.context.Tracer.RelatedEvent(EventLevel.Warning, "GvFltNotifyPreRenameHandler", metadata);

                    return NtStatus.AccessDenied;
                }
            }

            return NtStatus.Succcess;
        }

        private NtStatus GVFltNotifyPreDeleteHandler(string virtualPath, bool isDirectory)
        {
            if (PathUtil.IsPathInsideDotGit(virtualPath))
            {
                virtualPath = PathUtil.RemoveTrailingSlashIfPresent(virtualPath);
                if (!DoesPathAllowDelete(virtualPath))
                {
                    return NtStatus.AccessDenied;
                }
            }
            else if (isDirectory)
            {
                // Block directory deletes during git commands for directories not in the sparse-checkout 
                // git-clean and git-reset --hard are excluded from this restriction.
                if (!this.sparseCheckout.HasEntry(virtualPath, isFolder: true) &&
                    !this.CanDeleteDirectory())
                {
                    // Respond with something that Git expects, StatusAccessDenied will lock up Git. 
                    // The directory is not exactly not-empty but it’s potentially not-empty 
                    // within the timeline of the current git command which is the reason for us blocking the delete.
                    return NtStatus.DirectoryNotEmpty;
                }
            }

            return NtStatus.Succcess;
        }

        private bool CanDeleteDirectory()
        {
            GitCommandLineParser gitCommand = new GitCommandLineParser(this.context.Repository.GVFSLock.GetLockedGitCommand());
            return 
                !gitCommand.IsValidGitCommand ||
                gitCommand.IsVerb("clean") ||
                gitCommand.IsResetHard();
        }

        private void GVFltNotifyFileRenamedHandler(
            string virtualPath,
            string destinationPath,
            bool isDirectory,
            ref uint notificationMask)
        {
            if (PathUtil.IsPathInsideDotGit(destinationPath))
            {
                notificationMask = this.GetDotGitNotificationMask(destinationPath);
                this.OnDotGitFileChanged(destinationPath);
            }
            else
            {
                notificationMask = this.GetWorkingDirectoryNotificationMask(isDirectory);
                if (isDirectory)
                {
                    this.background.Enqueue(BackgroundGitUpdate.OnFolderRenamed(virtualPath, destinationPath));
                }
                else
                {
                    this.background.Enqueue(BackgroundGitUpdate.OnFileRenamed(virtualPath, destinationPath));
                }
            }
        }

        private void GVFltNotifyFileHandleClosedHandler(
            string virtualPath,
            bool isDirectory,
            bool fileModified,
            bool fileDeleted)
        {
            bool pathInsideDotGit = false;

            if (fileModified || fileDeleted)
            {
                pathInsideDotGit = PathUtil.IsPathInsideDotGit(virtualPath);
            }

            if (fileModified && pathInsideDotGit)
            {
                // TODO 876861: See if GVFlt can provide process ID\name in this callback
                this.OnDotGitFileChanged(virtualPath);
            }
            else if (fileDeleted && !pathInsideDotGit)
            {
                if (isDirectory)
                {
                    this.background.Enqueue(BackgroundGitUpdate.OnFolderDeleted(virtualPath));
                }
                else
                {
                    this.background.Enqueue(BackgroundGitUpdate.OnFileDeleted(virtualPath));
                }
            }
        }

        private void OnDotGitFileChanged(string virtualPath)
        {
            if (virtualPath.Equals(GVFSConstants.DotGit.Index, StringComparison.OrdinalIgnoreCase))
            {
                this.OnIndexFileChange();
            }
            else if (virtualPath.Equals(GVFSConstants.DotGit.Logs.Head, StringComparison.OrdinalIgnoreCase))
            {
                this.OnLogsHeadChange();
            }
        }

        private uint GetDotGitNotificationMask(string virtualPath)
        {
            uint notificationMask = (uint)NotificationType.FileRenamed;

            if (!DoesPathAllowDelete(virtualPath))
            {
                notificationMask |= (uint)NotificationType.PreDelete;
            }

            if (IsPathMonitoredForWrites(virtualPath))
            {
                notificationMask |= (uint)NotificationType.FileHandleClosed;
            }

            if (virtualPath.Equals(GVFSConstants.DotGit.IndexLock, StringComparison.OrdinalIgnoreCase))
            {
                notificationMask |= (uint)NotificationType.PreRename;
            }

            return notificationMask;
        }

        private uint GetWorkingDirectoryNotificationMask(bool isDirectory)
        {
            uint notificationMask = (uint)NotificationType.FileRenamed;

            if (isDirectory)
            {
                notificationMask |= (uint)NotificationType.PreDelete;
            }

            notificationMask |= (uint)NotificationType.FileHandleClosed;

            return notificationMask;
        }

        private CallbackResult PreBackgroundOperation()
        {
            this.placeholderListUpdatedByBackgroundThread = false;
            return this.gitIndexProjection.AcquireIndexLockAndOpenForWrites();
        }

        private CallbackResult ExecuteBackgroundOperation(BackgroundGitUpdate gitUpdate)
        {
            EventMetadata metadata = new EventMetadata();
            CallbackResult result;

            switch (gitUpdate.Operation)
            {
                case BackgroundGitUpdate.OperationType.OnFileCreated:
                case BackgroundGitUpdate.OperationType.OnFailedPlaceholderDelete:
                    metadata.Add("virtualPath", gitUpdate.VirtualPath);
                    result = this.AddFileToSparseCheckoutAndClearSkipWorktreeBit(gitUpdate.VirtualPath);

                    if (result == CallbackResult.Success)
                    {
                        result = this.alwaysExcludeFile.AddEntriesForFileOrFolder(gitUpdate.VirtualPath, isFolder: false);
                    }

                    break;

                case BackgroundGitUpdate.OperationType.OnFileRenamed:
                    metadata.Add("oldVirtualPath", gitUpdate.OldVirtualPath);
                    metadata.Add("virtualPath", gitUpdate.VirtualPath);
                    result = CallbackResult.Success;
                    if (!string.IsNullOrEmpty(gitUpdate.OldVirtualPath) && !PathUtil.IsPathInsideDotGit(gitUpdate.OldVirtualPath))
                    {
                        result = this.AddFileToSparseCheckoutAndClearSkipWorktreeBit(gitUpdate.OldVirtualPath);
                    }
                    
                    if (result == CallbackResult.Success && !string.IsNullOrEmpty(gitUpdate.VirtualPath))
                    {
                        // No need to check if gitUpdate.VirtualPath is inside the .git folder as OnFileRenamed is not scheduled
                        // when a file destination is in inside the .git folder

                        result = this.AddFileToSparseCheckoutAndClearSkipWorktreeBit(gitUpdate.VirtualPath);
                        if (result == CallbackResult.Success)
                        {
                            result = this.alwaysExcludeFile.AddEntriesForFileOrFolder(gitUpdate.VirtualPath, isFolder: false);
                        }
                    }

                    break;

                case BackgroundGitUpdate.OperationType.OnFileDeleted:
                case BackgroundGitUpdate.OperationType.OnFileOverwritten:
                case BackgroundGitUpdate.OperationType.OnFileSuperseded:
                case BackgroundGitUpdate.OperationType.OnFileFirstWrite:
                case BackgroundGitUpdate.OperationType.OnFailedPlaceholderUpdate:
                    metadata.Add("virtualPath", gitUpdate.VirtualPath);
                    result = this.AddFileToSparseCheckoutAndClearSkipWorktreeBit(gitUpdate.VirtualPath);
                    break;

                case BackgroundGitUpdate.OperationType.OnFolderCreated:
                    metadata.Add("virtualPath", gitUpdate.VirtualPath);
                    result = this.sparseCheckout.AddFolderEntry(gitUpdate.VirtualPath);                    
                    if (result == CallbackResult.Success)
                    {
                        result = this.alwaysExcludeFile.AddEntriesForFileOrFolder(gitUpdate.VirtualPath, isFolder: true);
                    }

                    break;

                case BackgroundGitUpdate.OperationType.OnFolderRenamed:
                    result = CallbackResult.Success;
                    metadata.Add("oldVirtualPath", gitUpdate.OldVirtualPath);
                    metadata.Add("virtualPath", gitUpdate.VirtualPath);

                    // An empty destination path means the folder was renamed to somewhere outside of the repo
                    // Note that only full folders can be moved\renamed, and so there will already be a recursive
                    // sparse-checkout entry for the virtualPath of the folder being moved (meaning that no 
                    // additional work is needed for any files\folders inside the folder being moved)
                    if (!string.IsNullOrEmpty(gitUpdate.VirtualPath))
                    {
                        result = this.sparseCheckout.AddFolderEntry(gitUpdate.VirtualPath);
                        if (result == CallbackResult.Success)
                        {
                            Queue<string> relativeFolderPaths = new Queue<string>();
                            relativeFolderPaths.Enqueue(gitUpdate.VirtualPath);

                            // Add the renamed folder and all of its subfolders to the always_exclude file
                            while (relativeFolderPaths.Count > 0)
                            {
                                string folderPath = relativeFolderPaths.Dequeue();
                                result = this.alwaysExcludeFile.AddEntriesForFileOrFolder(folderPath, isFolder: true);
                                if (result == CallbackResult.Success)
                                {
                                    try
                                    {
                                        foreach (DirectoryItemInfo itemInfo in this.context.FileSystem.ItemsInDirectory(Path.Combine(this.context.Enlistment.WorkingDirectoryRoot, folderPath)))
                                        {
                                            if (itemInfo.IsDirectory)
                                            {
                                                string itemVirtualPath = Path.Combine(folderPath, itemInfo.Name);
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
                                        exceptionMetadata.Add("Message", "DirectoryNotFoundException while traversing folder path");
                                        exceptionMetadata.Add("folderPath", folderPath);
                                        this.context.Tracer.RelatedEvent(EventLevel.Informational, "DirectoryNotFoundWhileUpdatingAlwaysExclude", exceptionMetadata);
                                    }
                                    catch (IOException e)
                                    {
                                        metadata.Add("Details", "IOException while traversing folder path");
                                        metadata.Add("folderPath", folderPath);
                                        metadata.Add("Exception", e.ToString());
                                        result = CallbackResult.RetryableError;
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

                case BackgroundGitUpdate.OperationType.OnFolderDeleted:
                    metadata.Add("virtualPath", gitUpdate.VirtualPath);
                    result = this.sparseCheckout.AddFolderEntry(gitUpdate.VirtualPath);
                    break;

                case BackgroundGitUpdate.OperationType.OnFolderFirstWrite:
                    metadata.Add("virtualPath", gitUpdate.VirtualPath);
                    result = this.alwaysExcludeFile.AddEntriesForFileOrFolder(gitUpdate.VirtualPath, isFolder: true);
                    break;

                case BackgroundGitUpdate.OperationType.OnIndexWriteWithoutProjectionChange:
                    result = this.gitIndexProjection.ValidateSparseCheckout();
                    break;

                default:
                    throw new InvalidOperationException("Invalid background operation");
            }

            if (result != CallbackResult.Success)
            {
                metadata.Add("Area", "ExecuteBackgroundOperation");
                metadata.Add("Operation", gitUpdate.Operation.ToString());
                metadata.Add("Message", "Background operation failed");
                metadata.Add("result", result.ToString());
                this.context.Tracer.RelatedEvent(EventLevel.Warning, "FailedBackgroundOperation", metadata);
            }

            return result;
        }

        private CallbackResult AddFileToSparseCheckoutAndClearSkipWorktreeBit(string virtualPath)
        {
            CallbackResult result = this.sparseCheckout.AddFileEntry(virtualPath);
            if (result != CallbackResult.Success)
            {
                return result;
            }

            result = this.gitIndexProjection.ClearSkipWorktreeBit(virtualPath);
            if (result == CallbackResult.Success)
            {
                if (this.gitIndexProjection.RemoveFromPlaceholderList(virtualPath))
                {
                    this.placeholderListUpdatedByBackgroundThread = true;
                }
            }

            return result;
        }

        private CallbackResult PostBackgroundOperation()
        {
            if (this.placeholderListUpdatedByBackgroundThread)
            {
                this.gitIndexProjection.FlushPlaceholderList();
            }

            this.sparseCheckout.Close();
            this.alwaysExcludeFile.Close();
            return this.gitIndexProjection.ReleaseLockAndClose();
        }
        
        private bool IsSpecialGitFile(GVFltFileInfo fileInfo)
        {
            if (fileInfo.IsFolder)
            {
                return false;
            }

            return
                fileInfo.Name.Equals(GVFSConstants.SpecialGitFiles.GitAttributes, StringComparison.OrdinalIgnoreCase) ||
                fileInfo.Name.Equals(GVFSConstants.SpecialGitFiles.GitIgnore, StringComparison.OrdinalIgnoreCase);
        }

        private EventMetadata CreateEventMetadata(
            string message = null,
            string virtualPath = null,
            Exception exception = null,
            bool errorMessage = false)
        {
            EventMetadata metadata = new EventMetadata();
            metadata.Add("Area", EtwArea);

            if (virtualPath != null)
            {
                metadata.Add("virtualPath", virtualPath);
            }

            if (message != null)
            {
                metadata.Add(errorMessage ? "ErrorMessage" : "Message", message);
            }

            if (exception != null)
            {
                metadata.Add("Exception", exception.ToString());
            }

            return metadata;
        }

        private FileProperties GetLogsHeadFileProperties()
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
                    EventMetadata metadata = new EventMetadata();
                    metadata.Add("Area", EtwArea);
                    metadata.Add("Exception", e.ToString());
                    metadata.Add("ErrorMessage", "GetLogsHeadFileProperties: Exception thrown from GetFileProperties");
                    this.context.Tracer.RelatedError("GetLogsHeadFileProperties_GetFilePropertiesException", metadata);

                    properties = FileProperties.DefaultFile;

                    // Leave logsHeadFileProperties null to indicate that it is still needs to be refreshed
                    this.logsHeadFileProperties = null;
                }
            }

            return properties;
        }

        /// <remarks>
        /// If a git-status or git-add is running, we don't want to fail placeholder creation because users will
        /// want to be able to run those commands during long running builds. Allow lock acquisition to be deferred
        /// until background thread actually needs it.
        /// 
        /// git-mv is also allowed to defer since it needs to create the files it moves.
        /// </remarks>
        private bool CanCreatePlaceholder()
        {
            GitCommandLineParser gitCommand = new GitCommandLineParser(this.context.Repository.GVFSLock.GetLockedGitCommand());
            return
                !gitCommand.IsValidGitCommand ||
                gitCommand.IsVerb("status", "add", "mv");
        }

        [Serializable]
        public struct BackgroundGitUpdate : IBackgroundOperation
        {
            public BackgroundGitUpdate(OperationType operation, string virtualPath, string oldVirtualPath)
            {
                this.Id = ReliableBackgroundOperations<BackgroundGitUpdate>.UnassignedOperationId;
                this.Operation = operation;
                this.VirtualPath = virtualPath;
                this.OldVirtualPath = oldVirtualPath;
            }

            public enum OperationType
            {
                Invalid = 0,

                OnFileCreated,
                OnFileRenamed,
                OnFileDeleted,
                OnFileOverwritten,
                OnFileSuperseded,
                OnFileFirstWrite,
                OnFailedPlaceholderDelete,
                OnFailedPlaceholderUpdate,
                OnFolderCreated,
                OnFolderRenamed,
                OnFolderDeleted,
                OnFolderFirstWrite,
                OnIndexWriteWithoutProjectionChange,
            }

            public OperationType Operation { get; set; }

            public string VirtualPath { get; set; }
            public string OldVirtualPath { get; set; }

            public long Id { get; set; }

            public static BackgroundGitUpdate OnFileCreated(string virtualPath)
            {
                return new BackgroundGitUpdate(OperationType.OnFileCreated, virtualPath, oldVirtualPath: null);
            }

            public static BackgroundGitUpdate OnFileRenamed(string oldVirtualPath, string newVirtualPath)
            {
                return new BackgroundGitUpdate(OperationType.OnFileRenamed, newVirtualPath, oldVirtualPath);
            }

            public static BackgroundGitUpdate OnFileDeleted(string virtualPath)
            {
                return new BackgroundGitUpdate(OperationType.OnFileDeleted, virtualPath, oldVirtualPath: null);
            }

            public static BackgroundGitUpdate OnFileOverwritten(string virtualPath)
            {
                return new BackgroundGitUpdate(OperationType.OnFileOverwritten, virtualPath, oldVirtualPath: null);
            }

            public static BackgroundGitUpdate OnFileSuperseded(string virtualPath)
            {
                return new BackgroundGitUpdate(OperationType.OnFileSuperseded, virtualPath, oldVirtualPath: null);
            }

            public static BackgroundGitUpdate OnFileFirstWrite(string virtualPath)
            {
                return new BackgroundGitUpdate(OperationType.OnFileFirstWrite, virtualPath, oldVirtualPath: null);
            }

            public static BackgroundGitUpdate OnFailedPlaceholderDelete(string virtualPath)
            {
                return new BackgroundGitUpdate(OperationType.OnFailedPlaceholderDelete, virtualPath, oldVirtualPath: null);
            }

            public static BackgroundGitUpdate OnFailedPlaceholderUpdate(string virtualPath)
            {
                return new BackgroundGitUpdate(OperationType.OnFailedPlaceholderUpdate, virtualPath, oldVirtualPath: null);
            }

            public static BackgroundGitUpdate OnFolderCreated(string virtualPath)
            {
                return new BackgroundGitUpdate(OperationType.OnFolderCreated, virtualPath, oldVirtualPath: null);
            }

            public static BackgroundGitUpdate OnFolderRenamed(string oldVirtualPath, string newVirtualPath)
            {
                return new BackgroundGitUpdate(OperationType.OnFolderRenamed, newVirtualPath, oldVirtualPath);
            }

            public static BackgroundGitUpdate OnFolderDeleted(string virtualPath)
            {
                return new BackgroundGitUpdate(OperationType.OnFolderDeleted, virtualPath, oldVirtualPath: null);
            }

            public static BackgroundGitUpdate OnFolderFirstWrite(string virtualPath)
            {
                return new BackgroundGitUpdate(OperationType.OnFolderFirstWrite, virtualPath, oldVirtualPath: null);
            }

            public static BackgroundGitUpdate OnIndexWriteWithoutProjectionChange()
            {
                return new BackgroundGitUpdate(OperationType.OnIndexWriteWithoutProjectionChange, virtualPath: null, oldVirtualPath: null);
            }

            public override string ToString()
            {
                return JsonConvert.SerializeObject(this);
            }
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

        private class GvFltTracer : GvFlt.ITracer
        {
            private ITracer tracer;

            public GvFltTracer(ITracer tracer)
            {
                this.tracer = tracer;
            }

            public void TraceError(string message)
            {
                this.tracer.RelatedError(message);
            }

            public void TraceError(Dictionary<string, object> metadata)
            {
                this.tracer.RelatedError(new EventMetadata(metadata));
            }

            public void TraceEvent(EventLevel level, string eventName, Dictionary<string, object> metadata)
            {
                this.tracer.RelatedEvent(level, eventName, new EventMetadata(metadata));
            }
        }
    }
}
