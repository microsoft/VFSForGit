using GVFS.Common;
using GVFS.Common.NamedPipes;
using GVFS.Common.Physical;
using GVFS.Common.Physical.FileSystem;
using GVFS.Common.Physical.Git;
using GVFS.Common.Tracing;
using GVFS.GVFlt.DotGit;
using GVFSGvFltWrapper;
using Microsoft.Database.Isam.Config;
using Microsoft.Diagnostics.Tracing;
using Microsoft.Isam.Esent.Collections.Generic;
using Newtonsoft.Json;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;

namespace GVFS.GVFlt
{
    public class GVFltCallbacks : IDisposable, IHeartBeatMetadataProvider
    {
        private const int MaxBlobStreamBufferSize = 64 * 1024;
        private const string RefMarker = "ref:";
        private const string EtwArea = "GVFltCallbacks";
        private const int BlockSize = 64 * 1024;
        private const int AcquireGVFSLockRetries = 50;
        private const int AcquireGVFSLockWaitPerTryMillis = 600;

        private const int MinGvFltThreads = 5;

        private static readonly string RefsHeadsPath = GVFSConstants.DotGit.Refs.Heads.Root + GVFSConstants.PathSeparator;
        private readonly string logsHeadPath;

        private GvFltWrapper gvflt;
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
            this.gvflt = new GvFltWrapper();
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

            this.gitIndexProjection = new GitIndexProjection(context, gitObjects, this.blobSizes, this.repoMetadata, this.gvflt);

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
            HResult result = GvFltWrapper.GvConvertDirectoryToVirtualizationRoot(virtualizationInstanceGuid, folderPath);
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
            this.gvflt.OnGetPlaceHolderInformation = this.GVFltGetPlaceHolderInformationHandler;
            this.gvflt.OnGetFileStream = this.GVFltGetFileStreamHandler;
            this.gvflt.OnNotifyFirstWrite = this.GVFltNotifyFirstWriteHandler;

            this.gvflt.OnNotifyCreate = this.GVFltNotifyCreateHandler;
            this.gvflt.OnNotifyPreDelete = this.GVFltNotifyPreDeleteHandler;
            this.gvflt.OnNotifyPreRename = this.GvFltNotifyPreRenameHandler;
            this.gvflt.OnNotifyPreSetHardlink = null;
            this.gvflt.OnNotifyFileRenamed = this.GVFltNotifyFileRenamedHandler;
            this.gvflt.OnNotifyHardlinkCreated = null;
            this.gvflt.OnNotifyFileHandleClosed = this.GVFltNotifyFileHandleClosedHandler;

            uint threadCount = (uint)Math.Max(MinGvFltThreads, Environment.ProcessorCount * 2);

            // We currently use twice as many threads as connections to allow for 
            // non-network operations to possibly succeed despite the connection limit
            HResult result = this.gvflt.GvStartVirtualizationInstance(
                this.context.Tracer,
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
                    this.gvflt.GvStopVirtualizationInstance();
                    this.gvflt.GvDetachDriver();
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

        private static EventMetadata CreatePathEventMetadata(string area, string relativeFilePath)
        {
            EventMetadata metadata = new EventMetadata();
            metadata.Add("Area", area);
            metadata.Add("relativeFilePath", relativeFilePath);

            return metadata;
        }

        private void OnIndexFileChange()
        {
            string lockedGitCommand = this.context.Repository.GVFSLock.GetLockedGitCommand();
            if (string.IsNullOrEmpty(lockedGitCommand))
            {
                if (!this.gitIndexProjection.IsIndexBeingUpdatedByGVFS())
                {
                    EventMetadata metadata = new EventMetadata();
                    metadata.Add("Area", EtwArea);
                    metadata.Add("Message", "Index modified without git holding GVFS lock");
                    this.context.Tracer.RelatedEvent(EventLevel.Warning, "OnIndexFileChange", metadata);

                    // TODO 935249: Investigate if index should have its offsets or projection invalidated 
                    // if the GVFS lock is not held when the index is written to
                    this.gitIndexProjection.InvalidateOffsets();
                }
            }
            else if (this.GitCommandLeavesProjectionUnchanged(lockedGitCommand))
            {
                bool canSkipInvalidation = GitHelper.IsVerb(lockedGitCommand, "status") && lockedGitCommand.Contains("--no-lock-index");
                if (!canSkipInvalidation)
                {
                    this.gitIndexProjection.InvalidateOffsets();
                }
            }
            else
            {
                this.gitIndexProjection.InvalidateProjection();
            }
        }

        private bool GitCommandLeavesProjectionUnchanged(string lockedGitCommand)
        {            
            if (GitHelper.IsVerb(lockedGitCommand, "add") ||
                GitHelper.IsVerb(lockedGitCommand, "branch") ||
                GitHelper.IsVerb(lockedGitCommand, "commit") ||
                GitHelper.IsVerb(lockedGitCommand, "status") ||
                GitHelper.IsVerb(lockedGitCommand, "update-index") ||
                GitHelper.IsVerb(lockedGitCommand, "update-ref") ||
                this.GitCommandIsResetLeavingProjectionUnchanged(lockedGitCommand))
            {
                return true;
            }

            return false;
        }

        private bool GitCommandIsResetLeavingProjectionUnchanged(string gitCommand)
        {
            // TODO 940173: Be more robust when parsing git arguments
            if (!GitHelper.IsVerb(gitCommand, "reset"))
            {
                return false;
            }

            if (gitCommand.Contains(" --hard ") || gitCommand.EndsWith(" --hard"))
            {
                return false;
            }

            if (gitCommand.Contains(" --keep ") || gitCommand.EndsWith(" --keep"))
            {
                return false;
            }

            if (gitCommand.Contains(" --merge ") || gitCommand.EndsWith(" --merge"))
            {
                return false;
            }

            return true;
        }

        private bool GitCommandIsResetHard(string gitCommand)
        {
            // TODO 940173: Be more robust when parsing git arguments
            if (GitHelper.IsVerb(gitCommand, "reset") &&
                (gitCommand.Contains(" --hard ") || gitCommand.EndsWith(" --hard")))
            {
                return true;
            }

            return false;
        }        

        private void OnLogsHeadChange()
        {
            // Don't open the .git\logs\HEAD file here to check its attributes as we're in a callback for the .git folder
            this.logsHeadFileProperties = null;
        }

        private StatusCode GVFltStartDirectoryEnumerationHandler(Guid enumerationId, string virtualPath)
        {
            virtualPath = PathUtil.RemoveTrailingSlashIfPresent(virtualPath);

            if (!this.isMountComplete)
            {
                EventMetadata metadata = this.CreateEventMetadata(
                    "GVFltStartDirectoryEnumerationHandler: Failed to start enumeration, mount has not yet completed",
                    virtualPath);
                metadata.Add("enumerationId", enumerationId);
                this.context.Tracer.RelatedEvent(EventLevel.Informational, "StartDirectoryEnum_MountNotComplete", metadata);

                return StatusCode.StatusDeviceNotReady;
            }

            GVFltActiveEnumeration activeEnumeration;
            try
            {
                activeEnumeration = new GVFltActiveEnumeration(this.gitIndexProjection.GetProjectedItems_CanTimeout(virtualPath));
            }
            catch (TimeoutException e)
            {
                EventMetadata metadata = this.CreateEventMetadata(
                    "GVFltStartDirectoryEnumerationHandler: Timeout while creating GVFltFolder",
                    virtualPath,
                    e,
                    errorMessage: true);
                metadata.Add("enumerationId", enumerationId);
                this.context.Tracer.RelatedError(metadata);

                return StatusCode.StatusTimeout;
            }

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
                return StatusCode.StatusInvalidParameter;
            }

            return StatusCode.StatusSucccess;
        }

        private StatusCode GVFltEndDirectoryEnumerationHandler(Guid enumerationId)
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
                return StatusCode.StatusInvalidParameter;
            }

            return StatusCode.StatusSucccess;
        }

        private StatusCode GVFltGetDirectoryEnumerationHandler(
            Guid enumerationId,
            string filterFileName,
            bool restartScan,
            GvDirectoryEnumerationResult result)
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

                return StatusCode.StatusInternalError;
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
                    // Only advance the enumeration if the file name fit in the GvDirectoryEnumerationResult
                    activeEnumeration.MoveNext();
                    return StatusCode.StatusSucccess;
                }
                else
                {
                    // Return StatusBufferOverflow to indicate that the file name had to be truncated
                    return StatusCode.StatusBufferOverflow;
                }
            }

            // TODO 636568: Confirm return code values/behavior with GVFlt team
            StatusCode statusCode = (initialRequest && PathUtil.IsEnumerationFilterSet(filterFileName)) ? StatusCode.StatusNoSuchFile : StatusCode.StatusNoMoreFiles;
            return statusCode;
        }

        /// <summary>
        /// GVFltQueryFileNameHandler is called by GVFlt when a file is being deleted or renamed.  It is an optimiation so that GVFlt
        /// can avoid calling Start\Get\End enumeration to check if GVFS is still projecting a file.  This method uses the same
        /// rules for deciding what is projected as the enumeration callbacks.
        /// </summary>
        private StatusCode GVFltQueryFileNameHandler(string virtualPath)
        {
            if (PathUtil.IsPathInsideDotGit(virtualPath))
            {
                return StatusCode.StatusObjectNameNotFound;
            }

            virtualPath = PathUtil.RemoveTrailingSlashIfPresent(virtualPath);

            if (!this.isMountComplete)
            {
                EventMetadata metadata = this.CreateEventMetadata("GVFltQueryFileNameHandler: Mount has not yet completed", virtualPath);
                this.context.Tracer.RelatedEvent(EventLevel.Informational, "QueryFileName_MountNotComplete", metadata);
                return StatusCode.StatusDeviceNotReady;
            }

            bool isFolder;
            if (!this.gitIndexProjection.IsPathProjected(virtualPath, out isFolder))
            {
                return StatusCode.StatusObjectNameNotFound;
            }

            return StatusCode.StatusSucccess;
        }

        private StatusCode GVFltGetPlaceHolderInformationHandler(
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
                EventMetadata metadata = this.CreateEventMetadata("GVFltGetPlaceHolderInformationHandler: Mount has not yet completed", virtualPath);
                metadata.Add("desiredAccess", desiredAccess);
                metadata.Add("shareMode", shareMode);
                metadata.Add("createDisposition", createDisposition);
                metadata.Add("createOptions", createOptions);
                metadata.Add("triggeringProcessId", triggeringProcessId);
                metadata.Add("triggeringProcessImageFileName", triggeringProcessImageFileName);
                this.context.Tracer.RelatedEvent(EventLevel.Informational, "GetPlaceHolder_MountNotComplete", metadata);

                return StatusCode.StatusDeviceNotReady;
            }

            GVFltFileInfo fileInfo;
            string sha;
            try
            {
                fileInfo = this.gitIndexProjection.GetProjectedGVFltFileInfoAndSha_CanTimeout(virtualPath, out sha);
            }
            catch (TimeoutException e)
            {
                EventMetadata metadata = this.CreateEventMetadata("GVFltGetPlaceHolderInformationHandler: Timeout while getting GVFltFileInfo", virtualPath, e, errorMessage: true);
                metadata.Add("desiredAccess", desiredAccess);
                metadata.Add("shareMode", shareMode);
                metadata.Add("createDisposition", createDisposition);
                metadata.Add("createOptions", createOptions);
                metadata.Add("triggeringProcessId", triggeringProcessId);
                metadata.Add("triggeringProcessImageFileName", triggeringProcessImageFileName);
                this.context.Tracer.RelatedError(metadata);

                return StatusCode.StatusTimeout;
            }

            if (fileInfo == null)
            {
                return StatusCode.StatusObjectNameNotFound;
            }

            try
            {
                if (!fileInfo.IsFolder &&
                    !this.IsSpecialGitFile(fileInfo) &&
                    !this.CanDeferGitLockAcquisition() &&
                    !this.TryAcquireGVFSLock())
                {
                    EventMetadata metadata = this.CreateEventMetadata("GVFltGetPlaceHolderInformationHandler: Failed to acquire lock for placeholder creation", virtualPath);
                    metadata.Add("desiredAccess", desiredAccess);
                    metadata.Add("shareMode", shareMode);
                    metadata.Add("createDisposition", createDisposition);
                    metadata.Add("createOptions", createOptions);
                    metadata.Add("triggeringProcessId", triggeringProcessId);
                    metadata.Add("triggeringProcessImageFileName", triggeringProcessImageFileName);
                    this.context.Tracer.RelatedEvent(EventLevel.Verbose, nameof(this.GVFltGetPlaceHolderInformationHandler), metadata);

                    // Another process is modifying the working directory so we cannot modify it
                    // until they are done.
                    return StatusCode.StatusObjectNameNotFound;
                }

                // The file name case in the virtualPath parameter might be different than the file name case in the repo.
                // Build a new virtualPath that preserves the case in the repo so that the placeholder file is created
                // with proper case.
                string gitCaseVirtualPath = Path.Combine(Path.GetDirectoryName(virtualPath), fileInfo.Name);
                
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
                StatusCode result = this.gvflt.GvWritePlaceholderInformation(
                    gitCaseVirtualPath,
                    properties.CreationTimeUTC,
                    properties.LastAccessTimeUTC,
                    properties.LastWriteTimeUTC,
                    changeTime: properties.LastWriteTimeUTC,
                    fileAttributes: fileAttributes,
                    endOfFile: fileInfo.Size,
                    directory: fileInfo.IsFolder,
                    contentId: sha,
                    epochId: null);

                if (result != StatusCode.StatusSucccess)
                {
                    EventMetadata metadata = this.CreateEventMetadata("GVFltGetPlaceHolderInformationHandler: GvWritePlaceholderInformation failed", virtualPath, exception: null, errorMessage: true);
                    metadata.Add("gitCaseVirtualPath", gitCaseVirtualPath);
                    metadata.Add("desiredAccess", desiredAccess);
                    metadata.Add("shareMode", shareMode);
                    metadata.Add("createDisposition", createDisposition);
                    metadata.Add("createOptions", createOptions);
                    metadata.Add("triggeringProcessId", triggeringProcessId);
                    metadata.Add("triggeringProcessImageFileName", triggeringProcessImageFileName);
                    metadata.Add("FileName", fileInfo.Name);
                    metadata.Add("IsFolder", fileInfo.IsFolder);
                    metadata.Add("StatusCode", result.ToString("X") + "(" + result.ToString("G") + ")");
                    this.context.Tracer.RelatedError(metadata);
                }
                else
                {
                    if (!fileInfo.IsFolder)
                    {
                        this.gitIndexProjection.OnPlaceholderFileCreated(gitCaseVirtualPath, sha);

                        // Note: Because GetPlaceHolderInformationHandler is not synchronized it is possible that GVFS will double count
                        // the creation of file placeholders if multiple requests for the same file are received at the same time on different
                        // threads.                         
                        this.placeHolderCreationCount.AddOrUpdate(
                            triggeringProcessImageFileName, 
                            new PlaceHolderCreateCounter(), 
                            (key, oldCount) => { oldCount.Increment(); return oldCount; });                             
                    }
                }

                return result;
            }
            finally
            {
                this.background.ReleaseAcquisitionLock();
            }
        }

        private StatusCode GVFltGetFileStreamHandler(
            string virtualPath,
            long byteOffset,
            uint length,
            Guid streamGuid,
            string contentId,
            uint triggeringProcessId,
            string triggeringProcessImageFileName,
            GVFltWriteBuffer targetBuffer)
        {
            string sha = contentId;

            EventMetadata metadata = new EventMetadata();
            metadata.Add("originalVirtualPath", virtualPath);
            metadata.Add("byteOffset", byteOffset);
            metadata.Add("length", length);
            metadata.Add("streamGuid", streamGuid);
            metadata.Add("triggeringProcessId", triggeringProcessId);
            metadata.Add("triggeringProcessImageFileName", triggeringProcessImageFileName);
            metadata.Add("sha", sha);
            using (ITracer activity = this.context.Tracer.StartActivity("GetFileStream", EventLevel.Verbose, Keywords.Telemetry, metadata))
            {
                if (!this.isMountComplete)
                {
                    metadata.Add("Message", "GVFltGetFileStreamHandler failed, mount has not yet completed");
                    activity.RelatedEvent(EventLevel.Informational, "GetFileStream_MountNotComplete", metadata);
                    return StatusCode.StatusDeviceNotReady;
                }

                if (byteOffset != 0)
                {
                    metadata.Add("ErrorMessage", "Invalid Parameter: byteOffset must be 0");
                    activity.RelatedError(metadata);
                    return StatusCode.StatusInvalidParameter;
                }

                try
                {
                    if (!this.gvfsGitObjects.TryCopyBlobContentStream_CanTimeout(
                        sha,
                        (reader, blobLength) =>
                    {
                        if (blobLength != length)
                        {
                            metadata.Add("blobLength", blobLength);
                            metadata.Add("ErrorMessage", "Actual file length (blobLength) does not match requested length");
                            activity.RelatedError(metadata);

                            // Clear out the stream to leave it in a good state.
                            reader.CopyBlockTo<CopyBlobContentTimeoutException>(StreamWriter.Null, blobLength);

                            throw new GvFltException(StatusCode.StatusInvalidParameter);
                        }

                        using (StreamWriter writer = new StreamWriter(targetBuffer.Stream, reader.CurrentEncoding, (int)targetBuffer.Length, leaveOpen: true))
                        {
                            writer.AutoFlush = true;

                            long remainingData = blobLength;
                            while (remainingData > 0)
                            {
                                uint bytesToCopy = (uint)Math.Min(remainingData, targetBuffer.Length);
                                writer.BaseStream.Seek(0, SeekOrigin.Begin);
                                reader.CopyBlockTo<CopyBlobContentTimeoutException>(writer, bytesToCopy);
                                long writeOffset = length - remainingData;

                                StatusCode writeResult = this.gvflt.GvWriteFile(streamGuid, targetBuffer, (ulong)writeOffset, bytesToCopy);
                                remainingData -= bytesToCopy;

                                if (writeResult != StatusCode.StatusSucccess)
                                {
                                    switch (writeResult)
                                    {
                                        case StatusCode.StatusFileClosed:
                                            // StatusFileClosed is expected, and occurs when an application closes a file handle before OnGetFileStream
                                            // is complete
                                            break;

                                        case StatusCode.StatusObjectNameNotFound:
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

                                    // Clear out the stream to leave it in a good state.
                                    if (remainingData > 0)
                                    {
                                        reader.CopyBlockTo<CopyBlobContentTimeoutException>(StreamWriter.Null, remainingData);
                                    }

                                    throw new GvFltException(writeResult);
                                }
                            }
                        }
                    }))
                    {
                        metadata.Add("ErrorMessage", "TryCopyBlobContentStream failed");
                        activity.RelatedError(metadata);
                        return StatusCode.StatusFileNotAvailable;
                    }
                }
                catch (TimeoutException e)
                {
                    metadata.Add("Message", "GVFltGetFileStreamHandler: Timeout while getting file stream");
                    metadata.Add("Exception", e.ToString());
                    activity.RelatedEvent(EventLevel.Warning, "Warning", metadata);
                    return StatusCode.StatusTimeout;
                }

                return StatusCode.StatusSucccess;
            }
        }

        private StatusCode GVFltNotifyFirstWriteHandler(string virtualPath)
        {
            virtualPath = PathUtil.RemoveTrailingSlashIfPresent(virtualPath);

            if (!this.isMountComplete)
            {
                EventMetadata metadata = this.CreateEventMetadata("GVFltNotifyFirstWriteHandler: Mount has not yet completed", virtualPath);
                this.context.Tracer.RelatedEvent(EventLevel.Informational, "NotifyFirstWrite_MountNotComplete", metadata);
                return StatusCode.StatusDeviceNotReady;
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

            return StatusCode.StatusSucccess;
        }

        private void GVFltNotifyCreateHandler(
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

        private StatusCode GvFltNotifyPreRenameHandler(string relativePath, string destinationPath)
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

                    return StatusCode.StatusAccessDenied;
                }
            }

            return StatusCode.StatusSucccess;
        }

        private StatusCode GVFltNotifyPreDeleteHandler(string virtualPath, bool isDirectory)
        {
            if (PathUtil.IsPathInsideDotGit(virtualPath))
            {
                virtualPath = PathUtil.RemoveTrailingSlashIfPresent(virtualPath);
                if (!DoesPathAllowDelete(virtualPath))
                {
                    return StatusCode.StatusAccessDenied;
                }
            }
            else if (isDirectory)
            {
                try
                {
                    // Block directory deletes during git commands for directories not in the sparse-checkout 
                    // git-clean and git-reset --hard are excluded from this restriction.
                    if (!this.sparseCheckout.HasEntry(virtualPath, isFolder: true) &&
                        !this.CanDeleteDirectory())
                    {
                        // Respond with something that Git expects, StatusAccessDenied will lock up Git. 
                        // The directory is not exactly not-empty but it’s potentially not-empty 
                        // within the timeline of the current git command which is the reason for us blocking the delete.
                        return StatusCode.StatusDirectoryNotEmpty;
                    }
                }
                finally
                {
                    this.background.ReleaseAcquisitionLock();
                }
            }

            return StatusCode.StatusSucccess;
        }

        private bool CanDeleteDirectory()
        {
            string lockedGitCommand = this.context.Repository.GVFSLock.GetLockedGitCommand();
            return 
                string.IsNullOrEmpty(lockedGitCommand) || 
                GitHelper.IsVerb(lockedGitCommand, "clean") ||
                this.GitCommandIsResetHard(lockedGitCommand);
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
            uint notificationMask = (uint)GvNotificationType.NotificationFileRenamed;

            if (!DoesPathAllowDelete(virtualPath))
            {
                notificationMask |= (uint)GvNotificationType.NotificationPreDelete;
            }

            if (IsPathMonitoredForWrites(virtualPath))
            {
                notificationMask |= (uint)GvNotificationType.NotificationFileHandleClosed;
            }

            if (virtualPath.Equals(GVFSConstants.DotGit.IndexLock, StringComparison.OrdinalIgnoreCase))
            {
                notificationMask |= (uint)GvNotificationType.NotificationPreRename;
            }

            return notificationMask;
        }

        private uint GetWorkingDirectoryNotificationMask(bool isDirectory)
        {
            uint notificationMask = (uint)GvNotificationType.NotificationFileRenamed;

            if (isDirectory)
            {
                notificationMask |= (uint)GvNotificationType.NotificationPreDelete;
            }

            notificationMask |= (uint)GvNotificationType.NotificationFileHandleClosed;

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

        /// <summary>
        /// Try to acquire the global lock. Retry but ensure that we don't reach the GVFlt callback timeout./>
        /// </summary>
        /// <returns>True if the lock was acquired, false otherwise.</returns>
        private bool TryAcquireGVFSLock()
        {
            this.background.ObtainAcquisitionLock();
            int numAttempts = 0;

            int maxGVFSLockAttempts = this.GetMaxGVFSLockAttempts();

            while (numAttempts < maxGVFSLockAttempts)
            {
                if (this.context.Repository.GVFSLock.TryAcquireLock())
                {
                    return true;
                }

                numAttempts++;

                // If we are about to attempt again, wait.
                if (numAttempts < maxGVFSLockAttempts)
                {
                    Thread.Sleep(AcquireGVFSLockWaitPerTryMillis);
                }
            }

            return false;
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

        private int GetMaxGVFSLockAttempts()
        {
            if (this.context.Repository.GVFSLock.IsLockedByGitVerb("commit"))
            {
                return AcquireGVFSLockRetries;
            }
            else
            {
                return 1;
            }
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
        private bool CanDeferGitLockAcquisition()
        {
            return this.context.Repository.GVFSLock.IsLockedByGitVerb("status", "add", "mv");
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
    }
}
