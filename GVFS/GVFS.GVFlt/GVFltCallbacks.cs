using GVFS.Common;
using GVFS.Common.Git;
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
using System.Threading;

namespace GVFS.GVFlt
{
    public class GVFltCallbacks : IDisposable
    {
        private const string RefMarker = "ref:";
        private const int BlockSize = 64 * 1024;
        private const long AllocationSize = 1L << 10;
        private const int AcquireGitLockRetries = 50;
        private const int AcquireGitLockWaitPerTryMillis = 600;

        private const int MinGvFltThreads = 3;

        private static readonly string RefsHeadsPath = GVFSConstants.DotGit.Refs.Heads.Root + GVFSConstants.PathSeparator;
        private readonly string logsHeadPath;

        private GvFltWrapper gvflt;
        private object stopLock = new object();
        private bool gvfltIsStarted = false;
        private bool isMountComplete = false;
        private ConcurrentDictionary<Guid, GVFltActiveEnumeration> activeEnumerations;
        private ConcurrentDictionary<string, GVFltFolder> workingDirectoryFolders;
        private GVFSGitObjects gvfsGitObjects;
        private SparseCheckoutAndDoNotProject sparseCheckoutAndDoNotProject;
        private ExcludeFile excludeFile;
        private PersistentDictionary<string, long> blobSizes;
        private string projectedCommitId = null;
        private IDisposable folderCreateWatcher;
        private IDisposable fileCreateWatcher;
        
        private ConcurrentHashSet<string> createdByGVFS = new ConcurrentHashSet<string>(StringComparer.OrdinalIgnoreCase);

        private ReliableBackgroundOperations<BackgroundGitUpdate> background;
        private GVFSContext context;
        private FileProperties logsHeadFileProperties;

        public GVFltCallbacks(GVFSContext context, GVFSGitObjects gitObjects)
        {
            this.context = context;
            this.logsHeadFileProperties = null;
            this.gvflt = new GvFltWrapper();
            this.activeEnumerations = new ConcurrentDictionary<Guid, GVFltActiveEnumeration>();
            this.workingDirectoryFolders = new ConcurrentDictionary<string, GVFltFolder>(StringComparer.OrdinalIgnoreCase);
            this.sparseCheckoutAndDoNotProject = new SparseCheckoutAndDoNotProject(
                this.context,
                Path.Combine(this.context.Enlistment.WorkingDirectoryRoot, GVFSConstants.DotGit.Info.SparseCheckoutPath),
                GVFSConstants.DatabaseNames.DoNotProject);
            this.excludeFile = new ExcludeFile(this.context, Path.Combine(this.context.Enlistment.WorkingDirectoryRoot, GVFSConstants.DotGit.Info.ExcludePath));
            this.blobSizes = new PersistentDictionary<string, long>(
                Path.Combine(this.context.Enlistment.DotGVFSRoot, GVFSConstants.DatabaseNames.BlobSizes),
                new DatabaseConfig()
                {
                    CacheSizeMax = 500 * 1024 * 1024, // 500 MB
                });
            this.gvfsGitObjects = gitObjects;

            this.background = new ReliableBackgroundOperations<BackgroundGitUpdate>(
                this.context,
                this.PreBackgroundOperation,
                this.ExecuteBackgroundOperation,
                this.PostBackgroundOperation,
                GVFSConstants.DatabaseNames.BackgroundGitUpdates);

            this.logsHeadPath = Path.Combine(this.context.Enlistment.WorkingDirectoryRoot, GVFSConstants.DotGit.Logs.Head);
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
                virtualPath.Equals(GVFSConstants.DotGit.Head, StringComparison.OrdinalIgnoreCase) ||
                virtualPath.Equals(GVFSConstants.DotGit.Logs.Head, StringComparison.OrdinalIgnoreCase) ||
                virtualPath.StartsWith(RefsHeadsPath, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return false;
        }

        public int GetBackgroundOperationCount()
        {
            return this.background.Count;
        }

        public bool TryStart(out string error)
        {
            error = string.Empty;

            this.sparseCheckoutAndDoNotProject.LoadOrCreate();
            this.excludeFile.LoadOrCreate();
            this.context.Repository.Initialize();

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
            this.gvflt.OnNotifyPreRename = null;
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

            bool gvfsHeadFileFound;
            string parseGVFSHeadFileErrors;
            if (!this.context.Enlistment.TryParseGVFSHeadFile(out gvfsHeadFileFound, out parseGVFSHeadFileErrors, out this.projectedCommitId))
            {
                if (gvfsHeadFileFound)
                {
                    EventMetadata metadata = new EventMetadata();
                    metadata.Add("parseGVFSHeadFileErrors", parseGVFSHeadFileErrors);
                    metadata.Add("ErrorMessage", "TryStart: Failed to parse GVFSHeadFile");
                    this.context.Tracer.RelatedError(metadata);
                }
            }

            if (string.IsNullOrEmpty(this.projectedCommitId))
            {
                this.UpdateGVFSHead(this.GetHeadCommitId());
            }

            if (this.projectedCommitId == null)
            {
                throw new GvFltException("Failed to start virtualiation instance, error: Failed to retreive projected commit ID");
            }

            // TODO 694569: Replace file system watcher with GVFlt callbacks
            this.folderCreateWatcher = this.context.FileSystem.MonitorChanges(
                this.context.Enlistment.WorkingDirectoryRoot,
                notifyFilter: NotifyFilters.DirectoryName,
                onCreate: e =>
                {
                    if (!PathUtil.IsPathInsideDotGit(e.Name) && !this.createdByGVFS.Contains(e.Name))
                    {
                        this.StopProjecting(e.Name, isFolder: true);
                        this.background.Enqueue(BackgroundGitUpdate.OnFolderCreated(e.Name));
                    }
                },
                onRename: e =>
                {
                    if (!PathUtil.IsPathInsideDotGit(e.Name))
                    {
                        this.background.Enqueue(BackgroundGitUpdate.OnFolderRenamed(e.OldName, e.Name));
                    }
                },
                onDelete: e =>
                {
                    if (!PathUtil.IsPathInsideDotGit(e.Name))
                    {
                        this.background.Enqueue(BackgroundGitUpdate.OnFolderDeleted(e.Name));
                    }
                });

            this.fileCreateWatcher = this.context.FileSystem.MonitorChanges(
                this.context.Enlistment.WorkingDirectoryRoot,
                notifyFilter: NotifyFilters.FileName,
                onCreate: e =>
                {
                    if (!PathUtil.IsPathInsideDotGit(e.Name) && !this.createdByGVFS.Contains(e.Name))
                    {
                        this.StopProjecting(e.Name, isFolder: false);
                        this.background.Enqueue(BackgroundGitUpdate.OnFileCreated(e.Name));
                    }
                },
                onRename: e =>
                {
                    if (!PathUtil.IsPathInsideDotGit(e.Name))
                    {
                        this.background.Enqueue(BackgroundGitUpdate.OnFileRenamed(e.OldName, e.Name));
                    }
                },
                onDelete: null);

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

                if (this.gvfltIsStarted)
                {
                    this.gvflt.GvStopVirtualizationInstance();
                    this.gvflt.GvDetachDriver();
                    Console.WriteLine("GVFlt callbacks stopped");
                    this.gvfltIsStarted = false;
                }
            }
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
                if (this.folderCreateWatcher != null)
                {
                    this.folderCreateWatcher.Dispose();
                    this.folderCreateWatcher = null;
                }

                if (this.fileCreateWatcher != null)
                {
                    this.fileCreateWatcher.Dispose();
                    this.fileCreateWatcher = null;
                }

                if (this.sparseCheckoutAndDoNotProject != null)
                {
                    this.sparseCheckoutAndDoNotProject.Dispose();
                    this.sparseCheckoutAndDoNotProject = null;
                }

                if (this.blobSizes != null)
                {
                    this.blobSizes.Dispose();
                    this.blobSizes = null;
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

        private void UpdateGVFSHead(string commitId)
        {
            this.projectedCommitId = commitId;
            string gvfsHeadFile = this.context.Enlistment.GVFSHeadFile;
            this.context.FileSystem.WriteAllText(gvfsHeadFile, this.projectedCommitId);
        }

        private void OnIndexFileChange()
        {
            this.context.Repository.Index.Invalidate();
        }

        private void OnHeadChange()
        {
            string repoHeadCommitId = this.GetHeadCommitId();

            if (repoHeadCommitId == null)
            {
                // This will happen if the ref mentioned in .git\HEAD does not exist.  This happens during "git branch -m",
                // because deletes the old ref before creating the new one.  It can also happen if a user simply deletes
                // the ref that they're currently on.

                // In this situation, we will continue projecting the last commit we were at until HEAD changes again.
                return;
            }

            if (!this.projectedCommitId.Equals(repoHeadCommitId))
            {
                // We need to capture what the git command is when the HEAD is changed 
                // so that some other command doesn't run and change it before we have a chance to read it
                string lockedGitCommand = this.context.Repository.GVFSLock.GetLockedGitCommand();
                if (string.IsNullOrEmpty(lockedGitCommand))
                {
                    EventMetadata metadata = new EventMetadata();
                    metadata.Add("Area", "WorkingDirectoryCallbacks");
                    metadata.Add("Message", "gvfs lock not held.");
                    this.context.Tracer.RelatedEvent(EventLevel.Warning, "OnHeadChange", metadata);
                }

                if (string.IsNullOrEmpty(lockedGitCommand) ||
                    (GitHelper.IsVerb(lockedGitCommand, "reset") &&
                     !lockedGitCommand.Contains("--hard") &&
                     !lockedGitCommand.Contains("--merge") &&
                     !lockedGitCommand.Contains("--keep")))
                {
                    // If there were any files that were added, the paths need to be in the
                    // exclude file so they will show up as untracked
                    this.background.Enqueue(BackgroundGitUpdate.OnHeadChangeForNonHardReset(repoHeadCommitId, this.projectedCommitId));
                }
                else if (GitHelper.IsVerb(lockedGitCommand, "commit"))
                {
                    this.UpdateGVFSHead(repoHeadCommitId);
                }
                else
                {
                    this.UpdateGVFSHead(repoHeadCommitId);
                    this.workingDirectoryFolders.Clear();
                }
            }
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

            GVFltFolder folder;
            try
            {
                folder = this.workingDirectoryFolders.GetOrAdd(
                    virtualPath,
                    path => new GVFltFolder(this.context, this.gvfsGitObjects, this.sparseCheckoutAndDoNotProject, this.blobSizes, path, this.projectedCommitId));
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

            GVFltActiveEnumeration activeEnumeration = new GVFltActiveEnumeration(folder.GetItems());
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
                result.AllocationSize = AllocationSize;

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

            GVFltFileInfo fileInfo;
            try
            {
                fileInfo = this.GetGVFltFileInfo(virtualPath);
            }
            catch (TimeoutException e)
            {
                EventMetadata metadata = this.CreateEventMetadata("GVFltQueryFileNameHandler: Timeout while getting GVFltFileInfo", virtualPath, e, errorMessage: true);
                this.context.Tracer.RelatedError(metadata);
                return StatusCode.StatusTimeout;
            }

            if (fileInfo == null || !fileInfo.IsProjected)
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
            try
            {
                fileInfo = this.GetGVFltFileInfo(virtualPath);
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

            if (fileInfo == null || !fileInfo.IsProjected)
            {
                return StatusCode.StatusObjectNameNotFound;
            }

            try
            {
                if (!fileInfo.IsFolder &&
                    !this.IsSpecialGitFile(fileInfo) &&
                    !this.CanDeferGitLockAcquisition() &&
                    !this.TryAcquireGitLock())
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

                string sha = string.Empty;
                uint fileAttributes;
                if (fileInfo.IsFolder)
                {
                    fileAttributes = (uint)NativeMethods.FileAttributes.FILE_ATTRIBUTE_DIRECTORY;
                }
                else
                {
                    if (!this.context.Repository.TryGetFileSha(this.projectedCommitId, gitCaseVirtualPath, out sha))
                    {
                        EventMetadata metadata = this.CreateEventMetadata("GVFltGetPlaceHolderInformationHandler: TryGetFileSha failed", virtualPath, exception: null, errorMessage: true);
                        metadata.Add("gitCaseVirtualPath", gitCaseVirtualPath);
                        metadata.Add("desiredAccess", desiredAccess);
                        metadata.Add("shareMode", shareMode);
                        metadata.Add("createDisposition", createDisposition);
                        metadata.Add("createOptions", createOptions);
                        metadata.Add("triggeringProcessId", triggeringProcessId);
                        metadata.Add("triggeringProcessImageFileName", triggeringProcessImageFileName);
                        this.context.Tracer.RelatedError(metadata);
                        return StatusCode.StatusFileNotAvailable;
                    }

                    fileAttributes = (uint)NativeMethods.FileAttributes.FILE_ATTRIBUTE_ARCHIVE;
                }

                FileProperties properties = this.GetLogsHeadFileProperties();
                this.createdByGVFS.Add(gitCaseVirtualPath);
                StatusCode result = this.gvflt.GvWritePlaceholderInformation(
                    gitCaseVirtualPath,
                    properties.CreationTimeUTC,
                    properties.LastAccessTimeUTC,
                    properties.LastWriteTimeUTC,
                    changeTime: properties.LastWriteTimeUTC,
                    fileAttributes: fileAttributes,
                    allocationSize: AllocationSize,
                    endOfFile: fileInfo.Size,
                    directory: fileInfo.IsFolder,
                    contentId: sha,
                    epochId: this.projectedCommitId);

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
                    this.background.Enqueue(BackgroundGitUpdate.OnPlaceholderCreated(gitCaseVirtualPath, fileInfo.IsFolder));

                    if (!fileInfo.IsFolder)
                    {
                        // Note: Folder will have IsProjected set to false in GVFltNotifyFirstWriteHandler.  We can't update folders
                        // here because GVFltGetPlaceHolderInformationHandler is not synchronized across threads and it is common for
                        // multiple threads of a build to open handles to the same folder in parallel
                        fileInfo.IsProjected = false;
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
            using (ITracer activity = this.context.Tracer.StartActivity("GetFileStream", EventLevel.Verbose, metadata))
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
                    if (!this.gvfsGitObjects.TryCopyBlobContentStream(
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
                catch (TimeoutException)
                {
                    metadata.Add("Message", "GVFltGetFileStreamHandler: Timeout while getting file stream");
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
                this.background.Enqueue(BackgroundGitUpdate.OnFolderFirstWrite(virtualPath, isFolder: true));
            }
            else
            {
                GVFltFileInfo fileInfo = this.GetGVFltFileInfo(virtualPath, readOnly: true);
                if (fileInfo == null)
                {
                    this.background.Enqueue(BackgroundGitUpdate.OnFolderFirstWrite(virtualPath, isFolder: false));
                }
                else if (fileInfo.IsFolder)
                {
                    fileInfo.IsProjected = false;
                    this.background.Enqueue(BackgroundGitUpdate.OnFolderFirstWrite(virtualPath, isFolder: true));
                }
            }

            return StatusCode.StatusSucccess;
        }

        private void GVFltNotifyCreateHandler(
            string virtualPath,
            uint desiredAccess,
            uint shareMode,
            uint createDisposition,
            uint createOptions,
            uint iostatusBlock,
            ref uint notificationMask)
        {
            if (PathUtil.IsPathInsideDotGit(virtualPath))
            {
                notificationMask = this.GetDotGitNotificationMask(virtualPath);
            }
        }

        private StatusCode GVFltNotifyPreDeleteHandler(string virtualPath)
        {
            if (PathUtil.IsPathInsideDotGit(virtualPath))
            {
                virtualPath = PathUtil.RemoveTrailingSlashIfPresent(virtualPath);
                if (!DoesPathAllowDelete(virtualPath))
                {
                    return StatusCode.StatusAccessDenied;
                }
            }

            return StatusCode.StatusSucccess;
        }

        private void GVFltNotifyFileRenamedHandler(
            string virtualPath,
            string destinationPath,
            ref uint notificationMask)
        {
            if (PathUtil.IsPathInsideDotGit(virtualPath))
            {
                notificationMask = this.GetDotGitNotificationMask(destinationPath);
                this.OnDotGitFileChanged(destinationPath);
            }
        }

        private void GVFltNotifyFileHandleClosedHandler(
            string virtualPath,
            bool fileModified,
            bool fileDeleted)
        {
            if (fileModified)
            {
                if (PathUtil.IsPathInsideDotGit(virtualPath))
                {
                    this.OnDotGitFileChanged(virtualPath);
                }
            }
        }

        private void OnDotGitFileChanged(string virtualPath)
        {
            if (virtualPath.Equals(GVFSConstants.DotGit.Index, StringComparison.OrdinalIgnoreCase))
            {
                this.OnIndexFileChange();
            }
            else if (virtualPath.Equals(GVFSConstants.DotGit.Head, StringComparison.OrdinalIgnoreCase) ||
                     virtualPath.StartsWith(RefsHeadsPath, StringComparison.OrdinalIgnoreCase))
            {
                this.OnHeadChange();
            }
            else if (virtualPath.Equals(GVFSConstants.DotGit.Logs.Head, StringComparison.OrdinalIgnoreCase))
            {
                this.OnLogsHeadChange();
            }
        }

        /// <param name="readOnly">If true, GetGVFltFileInfo will only check the entries 
        /// already present in GVFS's collection.  If false, GetGVFltFileInfo will create a 
        /// new GVFltFolder for virtualPath's parent (if there's not already an entry in GVFS's collection).</param>
        private GVFltFileInfo GetGVFltFileInfo(string virtualPath, bool readOnly = false)
        {
            string parentFolderVirtualPath;
            try
            {
                parentFolderVirtualPath = Path.GetDirectoryName(virtualPath);
            }
            catch (ArgumentException)
            {
                EventMetadata metadata = new EventMetadata();
                metadata.Add("virtualPath", virtualPath);
                metadata.Add("ErrorMessage", "GetGVFltFileInfo: file name contains illegal characters");
                this.context.Tracer.RelatedError(metadata);

                throw new GvFltException(StatusCode.StatusObjectNameInvalid);
            }
            catch (PathTooLongException)
            {
                EventMetadata metadata = new EventMetadata();
                metadata.Add("virtualPath", virtualPath);
                metadata.Add("ErrorMessage", "GetGVFltFileInfo: PathTooLongException, virtualPath is too long for GetDirectoryName");
                this.context.Tracer.RelatedError(metadata);
                return null;
            }

            string fileName = Path.GetFileName(virtualPath);

            GVFltFileInfo fileInfo = null;
            if (readOnly)
            {
                GVFltFolder folder;
                if (this.workingDirectoryFolders.TryGetValue(parentFolderVirtualPath, out folder))
                {
                    fileInfo = folder.GetFileInfo(fileName);
                }
            }
            else
            {
                GVFltFolder folder = this.workingDirectoryFolders.GetOrAdd(
                    parentFolderVirtualPath,
                    path => new GVFltFolder(this.context, this.gvfsGitObjects, this.sparseCheckoutAndDoNotProject, this.blobSizes, parentFolderVirtualPath, this.projectedCommitId));
                fileInfo = folder.GetFileInfo(fileName);
            }

            return fileInfo;
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

            return notificationMask;
        }

        private CallbackResult PreBackgroundOperation()
        {
            return this.context.Repository.Index.Open();
        }

        private CallbackResult ExecuteBackgroundOperation(BackgroundGitUpdate gitUpdate)
        {
            EventMetadata metadata = new EventMetadata();
            CallbackResult result;

            switch (gitUpdate.Operation)
            {
                case BackgroundGitUpdate.OperationType.OnPlaceholderCreated:
                    result = CallbackResult.Success;
                    metadata.Add("virtualPath", gitUpdate.VirtualPath);
                    metadata.Add("IsFolder", gitUpdate.IsFolder);
                    if (gitUpdate.IsFolder)
                    {
                        if (gitUpdate.VirtualPath != string.Empty)
                        {
                            result = this.sparseCheckoutAndDoNotProject.OnPartialPlaceholderFolderCreated(gitUpdate.VirtualPath);
                        }
                    }
                    else
                    {
                        long fileSize = 0;
                        GVFltFileInfo fileInfo = null;
                        try
                        {
                            fileInfo = this.GetGVFltFileInfo(gitUpdate.VirtualPath);
                        }
                        catch (TimeoutException e)
                        {
                            EventMetadata exceptionMetadata = new EventMetadata();
                            exceptionMetadata.Add("Area", "ExecuteBackgroundOperation");
                            exceptionMetadata.Add("Operation", gitUpdate.Operation.ToString());
                            exceptionMetadata.Add("virtualPath", gitUpdate.VirtualPath);
                            exceptionMetadata.Add("Message", "ExecuteBackgroundOperation: Timeout while getting GVFltFileInfo for index update.");
                            exceptionMetadata.Add("Exception", e.ToString());
                            this.context.Tracer.RelatedError(exceptionMetadata);
                        }

                        if (fileInfo != null)
                        {
                            fileSize = fileInfo.Size;
                        }

                        FileProperties properties = this.GetLogsHeadFileProperties();
                        result = this.sparseCheckoutAndDoNotProject.OnPlaceholderFileCreated(gitUpdate.VirtualPath, properties.CreationTimeUTC, properties.LastAccessTimeUTC, fileSize);
                    }

                    break;

                case BackgroundGitUpdate.OperationType.OnFolderFirstWrite:
                    metadata.Add("virtualPath", gitUpdate.VirtualPath);
                    metadata.Add("isFolder", gitUpdate.IsFolder);
                    result = CallbackResult.Success;

                    // For OnFolderFirstWrite:
                    // (gitUpdate.IsFolder == true)  => The first write callback confirmed that gitUpdate.VirtualPath is a folder path
                    // (gitUpdate.IsFolder == false) => The first write callback was unable to confirm that gitUpdate.VirtualPath is a folder path,
                    //                                  the background thread needs to check if the path is for a folder
                    bool confirmedFolder = gitUpdate.IsFolder;

                    if (confirmedFolder)
                    {
                        result = this.excludeFile.FolderChanged(gitUpdate.VirtualPath);
                    }
                    else
                    {
                        // If, when the first write callback was received, the file info for this path was not in workingDirectoryFolders the background
                        // thread needs to check if a placeholder has been created for a folder at this path (and if so, the exclude file needs to be updated)
                        if (!this.sparseCheckoutAndDoNotProject.ShouldPathBeProjected(gitUpdate.VirtualPath, isFolder: true))
                        {
                            result = this.excludeFile.FolderChanged(gitUpdate.VirtualPath);
                        }
                    }

                    break;

                case BackgroundGitUpdate.OperationType.OnFileCreated:
                    metadata.Add("virtualPath", gitUpdate.VirtualPath);
                    result = this.sparseCheckoutAndDoNotProject.OnFileCreated(gitUpdate.VirtualPath);

                    break;

                case BackgroundGitUpdate.OperationType.OnFileRenamed:
                    metadata.Add("oldVirtualPath", gitUpdate.OldVirtualPath);
                    metadata.Add("virtualPath", gitUpdate.VirtualPath);
                    result = this.sparseCheckoutAndDoNotProject.OnFileRenamed(gitUpdate.VirtualPath);

                    break;

                case BackgroundGitUpdate.OperationType.OnFolderCreated:
                    metadata.Add("virtualPath", gitUpdate.VirtualPath);
                    result = this.excludeFile.FolderChanged(gitUpdate.VirtualPath);
                    if (result == CallbackResult.Success)
                    {
                        result = this.sparseCheckoutAndDoNotProject.OnFolderCreated(gitUpdate.VirtualPath);
                    }

                    break;

                case BackgroundGitUpdate.OperationType.OnFolderRenamed:
                    result = CallbackResult.Success;
                    metadata.Add("oldVirtualPath", gitUpdate.OldVirtualPath);
                    metadata.Add("virtualPath", gitUpdate.VirtualPath);

                    Queue<string> relativeFolderPaths = new Queue<string>();
                    relativeFolderPaths.Enqueue(gitUpdate.VirtualPath);
                    result = CallbackResult.Success;

                    // Add the renamed folder and all of its subfolders to the exclude file
                    while (relativeFolderPaths.Count > 0)
                    {
                        string folderPath = relativeFolderPaths.Dequeue();
                        result = this.excludeFile.FolderChanged(folderPath);
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
                                this.context.Tracer.RelatedEvent(EventLevel.Informational, "DirectoryNotFoundWhileUpdatingExclude", exceptionMetadata);
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

                    if (result == CallbackResult.Success)
                    {
                        result = this.sparseCheckoutAndDoNotProject.OnFolderRenamed(gitUpdate.VirtualPath);
                    }

                    break;

                case BackgroundGitUpdate.OperationType.OnFolderDeleted:
                    metadata.Add("virtualPath", gitUpdate.VirtualPath);
                    result = this.sparseCheckoutAndDoNotProject.OnFolderDeleted(gitUpdate.VirtualPath);

                    break;

                // This case is only for a HEAD change after a non-hard reset
                case BackgroundGitUpdate.OperationType.OnHeadChange:
                    result = CallbackResult.Success;
                    GitProcess.Result gitResult = new GitProcess(this.context.Enlistment).DiffWithNameOnlyAndFilterForAddedAndReanamedFiles(gitUpdate.NewCommitId, gitUpdate.OldCommitId);
                    if (!gitResult.HasErrors && !string.IsNullOrWhiteSpace(gitResult.Output))
                    {
                        string[] addedFiles = gitResult.Output.Split(new char[] { '\n' }, StringSplitOptions.RemoveEmptyEntries);
                        foreach (string addedFile in addedFiles)
                        {
                            // Convert octets that git uses to display paths with unicode characters
                            string cleanedFilePath = GitPathConverter.ConvertPathOctetsToUtf8(addedFile.Trim('"')).Replace(GVFSConstants.GitPathSeparator, GVFSConstants.PathSeparator);

                            int lastSlash = cleanedFilePath.LastIndexOf(GVFSConstants.PathSeparator);
                            string folderToAdd = string.Empty;
                            if (lastSlash != -1)
                            {
                                folderToAdd = cleanedFilePath.Substring(0, lastSlash);
                            }

                            this.excludeFile.FolderChanged(folderToAdd);

                            string fullPath = Path.Combine(this.context.Enlistment.WorkingDirectoryRoot, cleanedFilePath);

                            try
                            {
                                using (FileStream forceHydrate = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read))
                                {
                                    // We have to open the file and force it to get hydrated, otherwise a subsequent reset --hard
                                    // will lose these new files because it will no longer know which commit to project them from

                                    // In the future, we can simply lay down a placeholder with the right blob id, and ensure that the
                                    // index has the correct mtime and size, so that the files will be present without necessarily being hydrated

                                    forceHydrate.ReadByte();
                                }
                            }
                            catch (FileNotFoundException)
                            {
                                // FileNotFoundException can occur when addedFile was deleted prior to HEAD changes
                            }
                            catch (DirectoryNotFoundException)
                            {
                                // DirectoryNotFoundException can occur when addedFile's parent folder was deleted prior to HEAD changes
                            }
                            catch (IOException e)
                            {
                                metadata.Add("Exception", e.ToString());
                                result = CallbackResult.RetryableError;
                                break;
                            }
                        }
                    }
                    else if (gitResult.HasErrors)
                    {
                        metadata.Add("gitResult.Errors", gitResult.Errors);
                        result = CallbackResult.RetryableError;
                    }

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

        private CallbackResult PostBackgroundOperation()
        {
            this.sparseCheckoutAndDoNotProject.Close();
            this.excludeFile.Close();
            return this.context.Repository.Index.Close();
        }

        private string GetFullFileContents(string relativeFilePath)
        {
            string fileContents = string.Empty;
            try
            {
                GvFltWrapper.OnDiskStatus fileStatus = this.gvflt.GetFileOnDiskStatus(relativeFilePath);
                switch (fileStatus)
                {
                    case GvFltWrapper.OnDiskStatus.Full:
                        fileContents = this.gvflt.ReadFullFileContents(relativeFilePath);
                        break;
                    case GvFltWrapper.OnDiskStatus.Partial:
                        EventMetadata metadata = new EventMetadata();
                        metadata.Add("Area", "GetFullFileContents");
                        metadata.Add("relativeFilePath", relativeFilePath);
                        metadata.Add("ErrorMessage", "GetFullFileContents: Attempted to read file contents for partial file");
                        this.context.Tracer.RelatedError(metadata);

                        fileContents = null;
                        break;
                    case GvFltWrapper.OnDiskStatus.NotOnDisk:
                        break;
                }
            }
            catch (GvFltException e)
            {
                EventMetadata metadata = new EventMetadata();
                metadata.Add("Area", "GetFullFileContents");
                metadata.Add("relativeFilePath", relativeFilePath);
                metadata.Add("Exception", e.ToString());
                metadata.Add("ErrorMessage", "GvFltException caught while trying to read file");
                this.context.Tracer.RelatedError(metadata);

                return null;
            }

            return fileContents;
        }

        private string GetHeadCommitId()
        {
            string headFileContents = this.GetFullFileContents(GVFSConstants.DotGit.Head);
            if (string.IsNullOrEmpty(headFileContents))
            {
                EventMetadata metadata = new EventMetadata();
                metadata.Add("Area", "GetHeadCommitId");
                metadata.Add("headFileContents", headFileContents);
                metadata.Add("ErrorMessage", "HEAD file is missing or empty");
                this.context.Tracer.RelatedError(metadata);
            
                return null;
            }

            headFileContents = headFileContents.Trim();

            if (GitHelper.IsValidFullSHA(headFileContents))
            {
                return headFileContents;
            }

            if (!headFileContents.StartsWith(RefMarker, StringComparison.OrdinalIgnoreCase))
            {
                EventMetadata metadata = new EventMetadata();
                metadata.Add("Area", "GetHeadCommitId");
                metadata.Add("headFileContents", headFileContents);
                metadata.Add("ErrorMessage", "headContents does not contain SHA or ref marker");
                this.context.Tracer.RelatedError(metadata);

                return null;
            }

            string symRef = headFileContents.Substring(RefMarker.Length).Trim();
            string refFilePath = Path.Combine(GVFSConstants.DotGit.Root, symRef.Replace('/', '\\'));
            string commitId = this.GetFullFileContents(refFilePath);
            if (commitId == null)
            {
                return null;
            }

            if (commitId.Length > 0)
            {
                commitId = commitId.Trim();
                if (GitHelper.IsValidFullSHA(commitId))
                {
                    return commitId;
                }

                EventMetadata metadata = new EventMetadata();
                metadata.Add("Area", "GetHeadCommitId");
                metadata.Add("symRef", symRef);
                metadata.Add("commitId", commitId);
                metadata.Add("ErrorMessage", "commitId in sym ref file is not a valid commit ID");
                this.context.Tracer.RelatedError(metadata);

                return null;
            }

            try
            {
                string packedRefFileContents = this.GetFullFileContents(GVFSConstants.DotGit.PackedRefs);
                if (packedRefFileContents == null)
                {
                    return null;
                }

                foreach (string packedRefLine in packedRefFileContents.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    if (packedRefLine.Contains(symRef))
                    {
                        commitId = packedRefLine.Substring(0, 40);

                        if (GitHelper.IsValidFullSHA(commitId))
                        {
                            return commitId;
                        }
                        else
                        {
                            EventMetadata metadata = new EventMetadata();
                            metadata.Add("Area", "GetHeadCommitId");
                            metadata.Add("symRef", symRef);
                            metadata.Add("commitId", commitId);
                            metadata.Add("Message", "Commit ID found in packed-refs that is not a valid hex string");
                            this.context.Tracer.RelatedEvent(EventLevel.Warning, "GetHeadCommitId_BadPackedRefsCommit", metadata);
                        }
                    }
                }
            }
            catch (Exception e)
            {
                EventMetadata metadata = new EventMetadata();
                metadata.Add("Area", "GetHeadCommitId");
                metadata.Add("Exception", e.ToString());
                metadata.Add("ErrorMessage", "Exception caught while trying to parse packed-refs file");

                return null;
            }

            return null;
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

        private void StopProjecting(string virtualPath, bool isFolder)
        {
            this.sparseCheckoutAndDoNotProject.StopProjecting(virtualPath, isFolder);
            GVFltFileInfo fileInfo = this.GetGVFltFileInfo(virtualPath);
            if (fileInfo != null)
            {
                fileInfo.IsProjected = false;
            }
        }

        /// <summary>
        /// Try to acquire the global lock. Retry but ensure that we don't reach the GVFlt callback timeout./>
        /// </summary>
        /// <returns>True if the lock was acquired, false otherwise.</returns>
        private bool TryAcquireGitLock()
        {
            this.background.ObtainAcquisitionLock();
            int numRetries = 0;

            int maxGitLockRetries = this.GetMaxGitLockRetries();

            while (numRetries < maxGitLockRetries)
            {
                if (this.context.Repository.GVFSLock.TryAcquireLock())
                {
                    return true;
                }
                else
                {
                    Thread.Sleep(AcquireGitLockWaitPerTryMillis);
                    numRetries++;
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
            metadata.Add("Area", "GVFltCallbacks");

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

        private int GetMaxGitLockRetries()
        {
            if (this.context.Repository.GVFSLock.IsLockedByGitVerb("commit"))
            {
                return AcquireGitLockRetries;
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
                    metadata.Add("Area", "GVFltCallbacks");
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
        /// </remarks>
        private bool CanDeferGitLockAcquisition()
        {
            return this.context.Repository.GVFSLock.IsLockedByGitVerb("status", "add");
        }

        [Serializable]
        public struct BackgroundGitUpdate : IBackgroundOperation
        {
            public BackgroundGitUpdate(OperationType operation, string virtualPath, string oldVirtualPath, bool isFolder)
            {
                this.Id = Guid.NewGuid();
                this.Operation = operation;
                this.VirtualPath = virtualPath;
                this.OldVirtualPath = oldVirtualPath;
                this.IsFolder = isFolder;
                this.NewCommitId = null;
                this.OldCommitId = null;
            }

            public BackgroundGitUpdate(OperationType operation, string newCommitId, string oldCommitId)
            {
                this.Id = Guid.NewGuid();
                this.VirtualPath = null;
                this.OldVirtualPath = null;
                this.IsFolder = false;
                this.Operation = operation;
                this.NewCommitId = newCommitId;
                this.OldCommitId = oldCommitId;
            }

            public enum OperationType
            {
                Invalid = 0,

                OnHeadChange,

                OnPlaceholderCreated,
                OnFolderFirstWrite,

                OnFileCreated,
                OnFileRenamed,
                OnFolderCreated,
                OnFolderRenamed,
                OnFolderDeleted,
            }

            public OperationType Operation { get; set; }

            public string VirtualPath { get; set; }
            public string OldVirtualPath { get; set; }
            public bool IsFolder { get; set; }
            public string NewCommitId { get; set; }
            public string OldCommitId { get; set; }
            public Guid Id { get; set; }

            public static BackgroundGitUpdate OnHeadChangeForNonHardReset(string newCommitId, string oldCommitId)
            {
                return new BackgroundGitUpdate(OperationType.OnHeadChange, newCommitId, oldCommitId);
            }

            public static BackgroundGitUpdate OnPlaceholderCreated(string virtualPath, bool isFolder)
            {
                return new BackgroundGitUpdate(OperationType.OnPlaceholderCreated, virtualPath, null, isFolder);
            }

            public static BackgroundGitUpdate OnFolderFirstWrite(string virtualPath, bool isFolder)
            {
                return new BackgroundGitUpdate(OperationType.OnFolderFirstWrite, virtualPath, null, isFolder);
            }

            public static BackgroundGitUpdate OnFileCreated(string virtualPath)
            {
                return new BackgroundGitUpdate(OperationType.OnFileCreated, virtualPath, null, false);
            }

            public static BackgroundGitUpdate OnFileRenamed(string oldVirtualPath, string newVirtualPath)
            {
                return new BackgroundGitUpdate(OperationType.OnFileRenamed, newVirtualPath, oldVirtualPath, false);
            }

            public static BackgroundGitUpdate OnFolderCreated(string virtualPath)
            {
                return new BackgroundGitUpdate(OperationType.OnFolderCreated, virtualPath, null, true);
            }

            public static BackgroundGitUpdate OnFolderRenamed(string oldVirtualPath, string newVirtualPath)
            {
                return new BackgroundGitUpdate(OperationType.OnFolderRenamed, newVirtualPath, oldVirtualPath, true);
            }

            public static BackgroundGitUpdate OnFolderDeleted(string virtualPath)
            {
                return new BackgroundGitUpdate(OperationType.OnFolderDeleted, virtualPath, null, true);
            }

            public override string ToString()
            {
                return JsonConvert.SerializeObject(this);
            }
        }
    }
}
