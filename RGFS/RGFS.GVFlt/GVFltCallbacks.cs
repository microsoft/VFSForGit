using RGFS.Common;
using RGFS.Common.FileSystem;
using RGFS.Common.Git;
using RGFS.Common.NamedPipes;
using RGFS.Common.NetworkStreams;
using RGFS.Common.Tracing;
using RGFS.GVFlt.DotGit;
using GvLib;
using Microsoft.Database.Isam.Config;
using Microsoft.Diagnostics.Tracing;
using Microsoft.Isam.Esent.Collections.Generic;
using Newtonsoft.Json;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace RGFS.GVFlt
{
    public class GVFltCallbacks : IDisposable, IHeartBeatMetadataProvider
    {
        public const byte PlaceholderVersion = 1;

        private const int MaxBlobStreamBufferSize = 64 * 1024;
        private const string EtwArea = "GVFltCallbacks";
        private const int MinGvFltThreads = 5;

        private static readonly GitCommandLineParser.Verbs CanCreatePlaceholderVerbs =
            GitCommandLineParser.Verbs.AddOrStage | GitCommandLineParser.Verbs.Move | GitCommandLineParser.Verbs.Status;
        private static readonly GitCommandLineParser.Verbs LeavesProjectionUnchangedVerbs =
            GitCommandLineParser.Verbs.AddOrStage | GitCommandLineParser.Verbs.Commit | GitCommandLineParser.Verbs.Status | GitCommandLineParser.Verbs.UpdateIndex;

        private readonly string logsHeadPath;

        private IVirtualizationInstance gvflt;
        private object stopLock = new object();
        private bool gvfltIsStarted = false;
        private bool isMountComplete = false;
        private ConcurrentDictionary<Guid, GVFltActiveEnumeration> activeEnumerations;
        private ConcurrentDictionary<string, PlaceHolderCreateCounter> placeHolderCreationCount;
        private ConcurrentDictionary<int, CancellationTokenSource> activeCommands;
        private RGFSGitObjects rgfsGitObjects;
        private SparseCheckout sparseCheckout;
        private GitIndexProjection gitIndexProjection;
        private AlwaysExcludeFile alwaysExcludeFile;
        private PersistentDictionary<string, long> blobSizes;

        private ReliableBackgroundOperations background;
        private RGFSContext context;
        private RepoMetadata repoMetadata;
        private FileProperties logsHeadFileProperties;

        public GVFltCallbacks(RGFSContext context, RGFSGitObjects gitObjects, RepoMetadata repoMetadata)
            : this(
                  context, 
                  gitObjects, 
                  repoMetadata,
                  new PersistentDictionary<string, long>(
                      Path.Combine(context.Enlistment.DotRGFSRoot, RGFSConstants.DotRGFS.BlobSizesName),
                      new DatabaseConfig()
                      {
                          CacheSizeMax = 500 * 1024 * 1024, // 500 MB
                      }),
                  gvflt: null, 
                  gitIndexProjection: null,
                  reliableBackgroundOperations: null)
        {
        }
        
        public GVFltCallbacks(
            RGFSContext context, 
            RGFSGitObjects gitObjects, 
            RepoMetadata repoMetadata,
            PersistentDictionary<string, long> blobSizes,
            IVirtualizationInstance gvflt, 
            GitIndexProjection gitIndexProjection,
            ReliableBackgroundOperations reliableBackgroundOperations)
        {
            this.context = context;
            this.repoMetadata = repoMetadata;
            this.logsHeadFileProperties = null;
            this.gvflt = gvflt ?? new VirtualizationInstance();
            this.activeEnumerations = new ConcurrentDictionary<Guid, GVFltActiveEnumeration>();
            this.sparseCheckout = new SparseCheckout(
                this.context,
                Path.Combine(this.context.Enlistment.WorkingDirectoryRoot, RGFSConstants.DotGit.Info.SparseCheckoutPath));
            this.alwaysExcludeFile = new AlwaysExcludeFile(this.context, Path.Combine(this.context.Enlistment.WorkingDirectoryRoot, RGFSConstants.DotGit.Info.AlwaysExcludePath));

            this.blobSizes = blobSizes;

            this.rgfsGitObjects = gitObjects;

            string error;
            PlaceholderListDatabase placeholders;
            if (!PlaceholderListDatabase.TryCreate(
                this.context.Tracer,
                Path.Combine(this.context.Enlistment.DotRGFSRoot, RGFSConstants.DotRGFS.Databases.PlaceholderList),
                this.context.FileSystem,
                out placeholders,
                out error))
            {
                throw new InvalidRepoException(error);
            }

            this.gitIndexProjection = gitIndexProjection ?? new GitIndexProjection(
                context, 
                gitObjects, 
                this.blobSizes, 
                this.repoMetadata, 
                this.gvflt,
                placeholders,
                this.sparseCheckout);
            
            this.background = reliableBackgroundOperations ?? new ReliableBackgroundOperations(
                this.context,
                this.PreBackgroundOperation,
                this.ExecuteBackgroundOperation,
                this.PostBackgroundOperation,
                Path.Combine(context.Enlistment.DotRGFSRoot, RGFSConstants.DotRGFS.Databases.BackgroundGitOperations));

            this.logsHeadPath = Path.Combine(this.context.Enlistment.WorkingDirectoryRoot, RGFSConstants.DotGit.Logs.Head);
            this.placeHolderCreationCount = new ConcurrentDictionary<string, PlaceHolderCreateCounter>(StringComparer.OrdinalIgnoreCase);
            this.activeCommands = new ConcurrentDictionary<int, CancellationTokenSource>();

            EventMetadata metadata = new EventMetadata();
            metadata.Add("placeholders.Count", placeholders.EstimatedCount);
            metadata.Add("background.Count", this.background.Count);
            metadata.Add(TracingConstants.MessageKey.InfoMessage, "GVFltCallbacks created");
            this.context.Tracer.RelatedEvent(EventLevel.Informational, "GVFltCallbacks_Constructor", metadata);
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
            if (virtualPath.Equals(RGFSConstants.DotGit.Index, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            return true;
        }

        public static string GetShaFromContentId(byte[] contentId)
        {
            return Encoding.Unicode.GetString(contentId, 0, RGFSConstants.ShaStringLength * sizeof(char));
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

            this.gvflt.OnNotifyPostCreateHandleOnly = null;
            this.gvflt.OnNotifyPostCreateNewFile = this.GVFltNotifyPostCreateNewFileHandler;
            this.gvflt.OnNotifyPostCreateOverwrittenOrSuperseded = this.GVFltNotifyPostCreateOverwrittenOrSupersededHandler;
            this.gvflt.OnNotifyPreDelete = this.GVFltNotifyPreDeleteHandler;
            this.gvflt.OnNotifyPreRename = this.GvFltNotifyPreRenameHandler;
            this.gvflt.OnNotifyPreSetHardlink = null;
            this.gvflt.OnNotifyFileRenamed = this.GVFltNotifyFileRenamedHandler;
            this.gvflt.OnNotifyHardlinkCreated = null;
            this.gvflt.OnNotifyFileHandleClosedOnly = null;
            this.gvflt.OnNotifyFileHandleClosedModifiedOrDeleted = this.GVFltNotifyFileHandleClosedModifiedOrDeletedHandler;

            this.gvflt.OnCancelCommand = this.GVFltCancelCommandHandler;

            uint threadCount = (uint)Math.Max(MinGvFltThreads, Environment.ProcessorCount * 2);

            uint logicalBytesPerSector = 0;
            uint writeBufferAlignment = 0;

            NotificationType globalNotificationMask =
                Notifications.DotGitFolder |
                Notifications.IndexFile |
                Notifications.IndexLockFile |
                Notifications.LogsHeadFile |
                Notifications.FilesInWorkingFolder |
                Notifications.FoldersInWorkingFolder;

            // We currently use twice as many threads as connections to allow for 
            // non-network operations to possibly succeed despite the connection limit
            HResult result = this.gvflt.StartVirtualizationInstance(
                this.context.Enlistment.WorkingDirectoryRoot,
                poolThreadCount: threadCount,
                concurrentThreadCount: threadCount,
                enableNegativePathCache: true,
                globalNotificationMask: globalNotificationMask,
                logicalBytesPerSector: ref logicalBytesPerSector,
                writeBufferAlignment: ref writeBufferAlignment);

            if (result != HResult.Ok)
            {
                this.context.Tracer.RelatedError("GvStartVirtualizationInstance failed: " + result.ToString("X") + "(" + result.ToString("G") + ")");
                error = "Failed to start virtualization instance (" + result.ToString() + ")";
                return false;
            }
            else
            {
                EventMetadata metadata = this.CreateEventMetadata();
                metadata.Add("logicalBytesPerSector", logicalBytesPerSector);
                metadata.Add("writeBufferAlignment", writeBufferAlignment);
                this.context.Tracer.RelatedEvent(EventLevel.Informational, "BytesPerSectorAndAlignment", metadata);
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
            metadata.Add("PlaceholderCount", this.gitIndexProjection.EstimatedPlaceholderCount);

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
            string lockedGitCommand = this.context.Repository.RGFSLock.GetLockedGitCommand();
            GitCommandLineParser gitCommand = new GitCommandLineParser(lockedGitCommand);
            if (this.gitIndexProjection.IsIndexBeingUpdatedByRGFS())
            {
                // No need to invalidate anything, because this event came from our own background thread writing to the index

                if (gitCommand.IsValidGitCommand)
                {
                    // But there should never be a case where RGFS is writing to the index while Git is holding the lock
                    EventMetadata metadata = new EventMetadata
                    {
                        { "Area", EtwArea },
                        { TracingConstants.MessageKey.WarningMessage, "RGFS wrote to the index while git was holding the RGFS lock" },
                        { "GitCommand", lockedGitCommand },
                    };

                    this.context.Tracer.RelatedEvent(EventLevel.Warning, "OnIndexFileChange_LockCollision", metadata);
                }
            }
            else if (!gitCommand.IsValidGitCommand)
            {
                // Something wrote to the index without holding the RGFS lock, so we invalidate the projection
                this.gitIndexProjection.InvalidateProjection();

                // But this isn't something we expect to see, so log a warning
                EventMetadata metadata = new EventMetadata
                {
                    { "Area", EtwArea },
                    { TracingConstants.MessageKey.WarningMessage, "Index modified without git holding RGFS lock" },
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
                gitCommand.IsVerb(LeavesProjectionUnchangedVerbs) ||
                gitCommand.IsResetSoftOrMixed() ||
                gitCommand.IsCheckoutWithFilePaths();
        }    

        private void OnLogsHeadChange()
        {
            // Don't open the .git\logs\HEAD file here to check its attributes as we're in a callback for the .git folder
            this.logsHeadFileProperties = null;
        }

        private bool TryRegisterCommand(int commandId, out CancellationTokenSource cancellationSource)
        {
            cancellationSource = new CancellationTokenSource();
            return this.activeCommands.TryAdd(commandId, cancellationSource);
        }

        private bool TryCompleteCommand(int commandId, NtStatus result)
        {
            CancellationTokenSource cancellationSource;
            if (this.activeCommands.TryRemove(commandId, out cancellationSource))
            {
                this.gvflt.CompleteCommand(commandId, result);
                return true;
            }

            return false;
        }

        // TODO: Need GvFlt 13150199 to be fixed so that RGFS doesn't leak memory if the enumeration
        // cancelled.  Currently GVFltEndDirectoryEnumerationHandler must be called to remove the
        // GVFltActiveEnumeration from this.activeEnumerations
        private NtStatus GVFltStartDirectoryEnumerationHandler(int commandId, Guid enumerationId, string virtualPath)
        {
            try
            {
                virtualPath = PathUtil.RemoveTrailingSlashIfPresent(virtualPath);

                if (!this.isMountComplete)
                {
                    EventMetadata metadata = this.CreateEventMetadata(enumerationId, virtualPath);
                    metadata.Add(TracingConstants.MessageKey.InfoMessage, nameof(this.GVFltStartDirectoryEnumerationHandler) + ": Failed to start enumeration, mount has not yet completed");
                    this.context.Tracer.RelatedEvent(EventLevel.Informational, "StartDirectoryEnum_MountNotComplete", metadata);

                    return NtStatus.DeviceNotReady;
                }

                IEnumerable<GVFltFileInfo> projectedItems;
                if (this.gitIndexProjection.TryGetProjectedItemsFromMemory(virtualPath, out projectedItems))
                {
                    GVFltActiveEnumeration activeEnumeration = new GVFltActiveEnumeration(projectedItems);
                    if (!this.activeEnumerations.TryAdd(enumerationId, activeEnumeration))
                    {
                        this.context.Tracer.RelatedError(
                            this.CreateEventMetadata(enumerationId, virtualPath), 
                            nameof(this.GVFltStartDirectoryEnumerationHandler) + ": Failed to add enumeration ID to active collection");

                        activeEnumeration.Dispose();
                        return NtStatus.InvalidParameter;
                    }

                    return NtStatus.Success;
                }

                CancellationTokenSource cancellationSource;
                if (!this.TryRegisterCommand(commandId, out cancellationSource))
                {
                    EventMetadata metadata = this.CreateEventMetadata(enumerationId, virtualPath);
                    metadata.Add("commandId", commandId);
                    this.context.Tracer.RelatedWarning(metadata, nameof(this.GVFltStartDirectoryEnumerationHandler) + ": Failed to register command");
                }

                Task.Run(
                    () => this.GVFltStartDirectoryEnumerationAsyncHandler(
                        cancellationSource.Token,
                        commandId,
                        enumerationId,
                        virtualPath),
                    cancellationSource.Token).ContinueWith((result) =>
                    {
                        cancellationSource.Dispose();
                    });
            }
            catch (Exception e)
            {
                EventMetadata metadata = this.CreateEventMetadata(enumerationId, virtualPath, e);
                metadata.Add("commandId", commandId);
                this.LogUnhandledExceptionAndExit(nameof(this.GVFltStartDirectoryEnumerationHandler), metadata);
            }

            return NtStatus.Pending;
        }

        private void GVFltStartDirectoryEnumerationAsyncHandler(
            CancellationToken cancellationToken,
            int commandId,
            Guid enumerationId, 
            string virtualPath)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return;
            }

            NtStatus result;
            try
            {
                GVFltActiveEnumeration activeEnumeration = new GVFltActiveEnumeration(this.gitIndexProjection.GetProjectedItems(virtualPath, cancellationToken));

                if (!this.activeEnumerations.TryAdd(enumerationId, activeEnumeration))
                {
                    this.context.Tracer.RelatedError(
                        this.CreateEventMetadata(enumerationId, virtualPath), 
                        nameof(this.GVFltStartDirectoryEnumerationAsyncHandler) + ": Failed to add enumeration ID to active collection");

                    activeEnumeration.Dispose();
                    result = NtStatus.InvalidParameter;
                }
                else
                {
                    result = NtStatus.Success;
                }
            }
            catch (OperationCanceledException)
            {
                EventMetadata metadata = this.CreateEventMetadata(virtualPath);
                metadata.Add(TracingConstants.MessageKey.InfoMessage, nameof(this.GVFltStartDirectoryEnumerationAsyncHandler) + ": Operation cancelled");
                this.context.Tracer.RelatedEvent(
                    EventLevel.Informational,
                    nameof(this.GVFltStartDirectoryEnumerationAsyncHandler) + "_Cancelled",
                    metadata);

                return;
            }
            catch (GitIndexProjection.SizesUnavailableException e)
            {
                result = NtStatus.FileNotAvailable;

                EventMetadata metadata = this.CreateEventMetadata(enumerationId, virtualPath, e);
                metadata.Add("commandId", commandId);
                metadata.Add("result", result.ToString("X") + "(" + result.ToString("G") + ")");
                this.context.Tracer.RelatedError(metadata, nameof(this.GVFltStartDirectoryEnumerationAsyncHandler) + ": caught GitIndexProjection.SizesUnavailableException");
            }
            catch (Exception e)
            {
                result = NtStatus.InternalError;

                EventMetadata metadata = this.CreateEventMetadata(enumerationId, virtualPath, e);
                metadata.Add("commandId", commandId);
                this.LogUnhandledExceptionAndExit(nameof(this.GVFltStartDirectoryEnumerationAsyncHandler), metadata);
            }

            if (!this.TryCompleteCommand(commandId, result))
            {
                // Command has already been canceled, and no EndDirectoryEnumeration callback will be received

                EventMetadata metadata = this.CreateEventMetadata(virtualPath);
                metadata.Add(TracingConstants.MessageKey.InfoMessage, "GVFltStartDirectoryEnumerationAsyncHandler: TryCompleteCommand returned false, command already canceled");
                metadata.Add("commandId", commandId);
                metadata.Add("enumerationId", enumerationId);
                metadata.Add("result", result.ToString("X") + "(" + result.ToString("G") + ")");

                GVFltActiveEnumeration activeEnumeration;
                bool activeEnumerationsUpdated = this.activeEnumerations.TryRemove(enumerationId, out activeEnumeration);
                if (activeEnumerationsUpdated)
                {
                    activeEnumeration.Dispose();
                }

                metadata.Add("activeEnumerationsUpdated", activeEnumerationsUpdated);                
                this.context.Tracer.RelatedEvent(EventLevel.Informational, "GVFltStartDirectoryEnumerationAsyncHandler_CommandAlreadyCanceled", metadata);                
            }
        }

        private NtStatus GVFltEndDirectoryEnumerationHandler(Guid enumerationId)
        {
            try
            {
                GVFltActiveEnumeration activeEnumeration;
                if (this.activeEnumerations.TryRemove(enumerationId, out activeEnumeration))
                {
                    activeEnumeration.Dispose();
                }
                else
                {
                    this.context.Tracer.RelatedWarning(
                        this.CreateEventMetadata(enumerationId), 
                        nameof(this.GVFltEndDirectoryEnumerationHandler) + ": Failed to remove enumeration ID from active collection", 
                        Keywords.Telemetry);

                    return NtStatus.InvalidParameter;
                }                
            }
            catch (Exception e)
            {
                this.LogUnhandledExceptionAndExit(
                    nameof(this.GVFltEndDirectoryEnumerationHandler),
                    this.CreateEventMetadata(enumerationId, virtualPath: null, exception: e));
            }

            return NtStatus.Success;
        }

        private NtStatus GVFltGetDirectoryEnumerationHandler(
            Guid enumerationId,
            string filterFileName,
            bool restartScan,
            DirectoryEnumerationResult result)
        {
            try
            {
                GVFltActiveEnumeration activeEnumeration = null;
                if (!this.activeEnumerations.TryGetValue(enumerationId, out activeEnumeration))
                {
                    EventMetadata metadata = this.CreateEventMetadata(enumerationId);
                    metadata.Add("filterFileName", filterFileName);
                    metadata.Add("restartScan", restartScan);
                    this.context.Tracer.RelatedError(metadata, nameof(this.GVFltGetDirectoryEnumerationHandler) + ": Failed to find active enumeration ID");

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
                        return NtStatus.Success;
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
            catch (Win32Exception e)
            {
                this.context.Tracer.RelatedWarning(
                    this.CreateEventMetadata(enumerationId, virtualPath: null, exception: e), 
                    nameof(this.GVFltGetDirectoryEnumerationHandler) + " caught Win32Exception");

                return Utils.Win32ErrorToNtStatus(e.NativeErrorCode);
            }
            catch (Exception e)
            {
                this.LogUnhandledExceptionAndExit(
                    nameof(this.GVFltGetDirectoryEnumerationHandler),
                    this.CreateEventMetadata(enumerationId, virtualPath: null, exception: e));

                return NtStatus.InternalError;
            }
        }

        /// <summary>
        /// GVFltQueryFileNameHandler is called by GVFlt when a file is being deleted or renamed.  It is an optimization so that GVFlt
        /// can avoid calling Start\Get\End enumeration to check if RGFS is still projecting a file.  This method uses the same
        /// rules for deciding what is projected as the enumeration callbacks.
        /// </summary>
        private NtStatus GVFltQueryFileNameHandler(string virtualPath)
        {
            try
            {
                if (PathUtil.IsPathInsideDotGit(virtualPath))
                {
                    return NtStatus.ObjectNameNotFound;
                }

                virtualPath = PathUtil.RemoveTrailingSlashIfPresent(virtualPath);

                if (!this.isMountComplete)
                {
                    EventMetadata metadata = this.CreateEventMetadata(virtualPath);
                    metadata.Add(TracingConstants.MessageKey.InfoMessage, "GVFltQueryFileNameHandler: Mount has not yet completed");
                    this.context.Tracer.RelatedEvent(EventLevel.Informational, "QueryFileName_MountNotComplete", metadata);
                    return NtStatus.DeviceNotReady;
                }

                bool isFolder;
                string fileName;
                if (!this.gitIndexProjection.IsPathProjected(virtualPath, out fileName, out isFolder))
                {
                    return NtStatus.ObjectNameNotFound;
                }
            }
            catch (Exception e)
            {
                this.LogUnhandledExceptionAndExit(nameof(this.GVFltQueryFileNameHandler), this.CreateEventMetadata(virtualPath, e));
            }

            return NtStatus.Success;
        }

        private NtStatus GVFltGetPlaceholderInformationHandler(
            int commandId,
            string virtualPath,
            uint desiredAccess,
            uint shareMode,
            uint createDisposition,
            uint createOptions,
            uint triggeringProcessId,
            string triggeringProcessImageFileName)
        {
            try
            {
                virtualPath = PathUtil.RemoveTrailingSlashIfPresent(virtualPath);

                if (!this.isMountComplete)
                {
                    EventMetadata metadata = this.CreateEventMetadata(virtualPath);
                    metadata.Add("commandId", commandId);
                    metadata.Add("desiredAccess", desiredAccess);
                    metadata.Add("shareMode", shareMode);
                    metadata.Add("createDisposition", createDisposition);
                    metadata.Add("createOptions", createOptions);
                    metadata.Add("triggeringProcessId", triggeringProcessId);
                    metadata.Add("triggeringProcessImageFileName", triggeringProcessImageFileName);
                    metadata.Add(TracingConstants.MessageKey.InfoMessage, "GVFltGetPlaceholderInformationHandler: Mount has not yet completed");
                    this.context.Tracer.RelatedEvent(EventLevel.Informational, "GetPlaceHolder_MountNotComplete", metadata);

                    return NtStatus.DeviceNotReady;
                }

                bool isFolder;
                string fileName;
                if (!this.gitIndexProjection.IsPathProjected(virtualPath, out fileName, out isFolder))
                {
                    return NtStatus.ObjectNameNotFound;
                }

                if (!isFolder &&
                    !this.IsSpecialGitFile(fileName) &&
                    !this.CanCreatePlaceholder())
                {
                    EventMetadata metadata = this.CreateEventMetadata(virtualPath);
                    metadata.Add("commandId", commandId);
                    metadata.Add("desiredAccess", desiredAccess);
                    metadata.Add("shareMode", shareMode);
                    metadata.Add("createDisposition", createDisposition);
                    metadata.Add("createOptions", createOptions);
                    metadata.Add("triggeringProcessId", triggeringProcessId);
                    metadata.Add("triggeringProcessImageFileName", triggeringProcessImageFileName);
                    metadata.Add(TracingConstants.MessageKey.VerboseMessage, "GVFltGetPlaceholderInformationHandler: Not allowed to create placeholder");
                    this.context.Tracer.RelatedEvent(EventLevel.Verbose, nameof(this.GVFltGetPlaceholderInformationHandler), metadata);

                    this.gitIndexProjection.OnPlaceholderCreateBlockedForGit();

                    // Another process is modifying the working directory so we cannot modify it
                    // until they are done.
                    return NtStatus.ObjectNameNotFound;
                }

                CancellationTokenSource cancellationSource;
                if (!this.TryRegisterCommand(commandId, out cancellationSource))
                {
                    EventMetadata metadata = this.CreateEventMetadata(virtualPath);
                    metadata.Add("commandId", commandId);
                    metadata.Add("desiredAccess", desiredAccess);
                    metadata.Add("shareMode", shareMode);
                    metadata.Add("createDisposition", createDisposition);
                    metadata.Add("createOptions", createOptions);
                    metadata.Add("triggeringProcessId", triggeringProcessId);
                    metadata.Add("triggeringProcessImageFileName", triggeringProcessImageFileName);
                    this.context.Tracer.RelatedWarning(metadata, nameof(this.GVFltGetPlaceholderInformationHandler) + ": Failed to register command");
                }

                Task.Run(
                    () => this.GVFltGetPlaceholderInformationAsyncHandler(
                        cancellationSource.Token,
                        commandId,
                        virtualPath,
                        desiredAccess,
                        shareMode,
                        createDisposition,
                        createOptions,
                        triggeringProcessId,
                        triggeringProcessImageFileName),
                    cancellationSource.Token).ContinueWith((result) =>
                    {
                        cancellationSource.Dispose();
                    });
            }
            catch (Exception e)
            {
                EventMetadata metadata = this.CreateEventMetadata(virtualPath, e);
                metadata.Add("commandId", commandId);
                metadata.Add("desiredAccess", desiredAccess);
                metadata.Add("shareMode", shareMode);
                metadata.Add("createDisposition", createDisposition);
                metadata.Add("createOptions", createOptions);
                metadata.Add("triggeringProcessId", triggeringProcessId);
                metadata.Add("triggeringProcessImageFileName", triggeringProcessImageFileName);
                this.LogUnhandledExceptionAndExit(nameof(this.GVFltGetPlaceholderInformationHandler), metadata);
            }

            return NtStatus.Pending;
        }

        private void GVFltGetPlaceholderInformationAsyncHandler(
            CancellationToken cancellationToken,
            int commandId,
            string virtualPath,
            uint desiredAccess,
            uint shareMode,
            uint createDisposition,
            uint createOptions,
            uint triggeringProcessId,
            string triggeringProcessImageFileName)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return;
            }

            NtStatus result = NtStatus.Success;

            try
            {
                GVFltFileInfo fileInfo;
                string sha;
                string parentFolderPath;
                try
                {                    
                    fileInfo = this.gitIndexProjection.GetProjectedGVFltFileInfoAndSha(cancellationToken, virtualPath, out parentFolderPath, out sha);
                    if (fileInfo == null)
                    {
                        this.TryCompleteCommand(commandId, NtStatus.ObjectNameNotFound);
                        return;
                    }
                }
                catch (OperationCanceledException)
                {
                    EventMetadata metadata = this.CreateEventMetadata(virtualPath);
                    metadata.Add(TracingConstants.MessageKey.InfoMessage, nameof(this.GVFltGetPlaceholderInformationAsyncHandler) + ": Operation cancelled");
                    this.context.Tracer.RelatedEvent(
                        EventLevel.Informational, 
                        nameof(this.GVFltGetPlaceholderInformationAsyncHandler) + "_GetProjectedGVFltFileInfoAndShaCancelled", 
                        metadata);
                    return;
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
                result = this.gvflt.WritePlaceholderInformation(
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

                if (result != NtStatus.Success)
                {
                    EventMetadata metadata = this.CreateEventMetadata(virtualPath);
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
                    this.context.Tracer.RelatedError(metadata, nameof(this.GVFltGetPlaceholderInformationAsyncHandler) + ": GvWritePlaceholderInformation failed");
                }
                else
                {
                    if (!fileInfo.IsFolder)
                    {
                        this.gitIndexProjection.OnPlaceholderFileCreated(gitCaseVirtualPath, sha);

                        // Note: Because GVFltGetPlaceholderInformationHandler is not synchronized it is possible that RGFS will double count
                        // the creation of file placeholders if multiple requests for the same file are received at the same time on different
                        // threads.                         
                        this.placeHolderCreationCount.AddOrUpdate(
                            triggeringProcessImageFileName,
                            (imageName) => { return new PlaceHolderCreateCounter(); },
                            (key, oldCount) => { oldCount.Increment(); return oldCount; });
                    }
                }
            }
            catch (GitIndexProjection.SizesUnavailableException e)
            {
                result = NtStatus.FileNotAvailable;

                EventMetadata metadata = this.CreateEventMetadata(virtualPath, e);
                metadata.Add("commandId", commandId);
                metadata.Add("desiredAccess", desiredAccess);
                metadata.Add("shareMode", shareMode);
                metadata.Add("createDisposition", createDisposition);
                metadata.Add("createOptions", createOptions);
                metadata.Add("triggeringProcessId", triggeringProcessId);
                metadata.Add("triggeringProcessImageFileName", triggeringProcessImageFileName);
                metadata.Add("result", result.ToString("X") + "(" + result.ToString("G") + ")");
                this.context.Tracer.RelatedError(metadata, nameof(this.GVFltGetPlaceholderInformationAsyncHandler) + ": caught GitIndexProjection.SizesUnavailableException");
            }
            catch (Win32Exception e)
            {
                result = Utils.Win32ErrorToNtStatus(e.NativeErrorCode);

                EventMetadata metadata = this.CreateEventMetadata(virtualPath, e);
                metadata.Add("commandId", commandId);
                metadata.Add("desiredAccess", desiredAccess);
                metadata.Add("shareMode", shareMode);
                metadata.Add("createDisposition", createDisposition);
                metadata.Add("createOptions", createOptions);
                metadata.Add("triggeringProcessId", triggeringProcessId);
                metadata.Add("triggeringProcessImageFileName", triggeringProcessImageFileName);
                metadata.Add("result", result.ToString("X") + "(" + result.ToString("G") + ")");
                this.context.Tracer.RelatedWarning(metadata, nameof(this.GVFltGetPlaceholderInformationAsyncHandler) + ": caught Win32Exception");
            }
            catch (Exception e)
            {
                EventMetadata metadata = this.CreateEventMetadata(virtualPath, e);
                metadata.Add("commandId", commandId);
                metadata.Add("desiredAccess", desiredAccess);
                metadata.Add("shareMode", shareMode);
                metadata.Add("createDisposition", createDisposition);
                metadata.Add("createOptions", createOptions);
                metadata.Add("triggeringProcessId", triggeringProcessId);
                metadata.Add("triggeringProcessImageFileName", triggeringProcessImageFileName);
                this.LogUnhandledExceptionAndExit(nameof(this.GVFltGetPlaceholderInformationAsyncHandler), metadata);
            }

            this.TryCompleteCommand(commandId, result);
        }

        private NtStatus GVFltGetFileStreamHandler(
            int commandId,
            string virtualPath,
            long byteOffset,
            uint length,
            Guid streamGuid,
            byte[] contentId, 
            byte[] epochId,            
            uint triggeringProcessId,
            string triggeringProcessImageFileName)
        {
            try
            {
                if (contentId == null)
                {
                    this.context.Tracer.RelatedError("GVFltGetFileStreamHandler called with null contentId, path: " + virtualPath);
                    return NtStatus.InternalError;
                }

                if (epochId == null)
                {
                    this.context.Tracer.RelatedError("GVFltGetFileStreamHandler called with null epochId, path: " + virtualPath);
                    return NtStatus.InternalError;
                }

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
                metadata.Add("commandId", commandId);
                ITracer activity = this.context.Tracer.StartActivity("GetFileStream", EventLevel.Verbose, Keywords.Telemetry, metadata);

                if (!this.isMountComplete)
                {
                    metadata.Add(TracingConstants.MessageKey.InfoMessage, "GVFltGetFileStreamHandler failed, mount has not yet completed");
                    activity.RelatedEvent(EventLevel.Informational, "GetFileStream_MountNotComplete", metadata);
                    activity.Dispose();
                    return NtStatus.DeviceNotReady;
                }

                if (byteOffset != 0)
                {
                    activity.RelatedError(metadata, "Invalid Parameter: byteOffset must be 0");
                    activity.Dispose();
                    return NtStatus.InvalidParameter;
                }

                if (placeholderVersion != PlaceholderVersion)
                {
                    activity.RelatedError(metadata, nameof(this.GVFltGetFileStreamHandler) + ": Unexpected placeholder version");
                    activity.Dispose();
                    return NtStatus.InternalError;
                }

                CancellationTokenSource cancellationSource;
                if (!this.TryRegisterCommand(commandId, out cancellationSource))
                {
                    metadata.Add(TracingConstants.MessageKey.WarningMessage, nameof(this.GVFltGetFileStreamHandler) + ": Failed to register command");
                    this.context.Tracer.RelatedEvent(EventLevel.Warning, nameof(this.GVFltGetFileStreamHandler) + "_FailedToRegisterCommand", metadata);
                }

                Task.Run(
                    () => this.GVFltGetFileStreamHandlerAsyncHandler(
                        cancellationSource.Token,
                        commandId,
                        length,
                        streamGuid,
                        sha,
                        metadata,
                        activity),
                    cancellationSource.Token).ContinueWith((result) =>
                    {
                        activity.Dispose();
                        cancellationSource.Dispose();
                    });
            }
            catch (Exception e)
            {
                EventMetadata metadata = this.CreateEventMetadata(virtualPath, e);
                metadata.Add("originalVirtualPath", virtualPath);
                metadata.Add("byteOffset", byteOffset);
                metadata.Add("length", length);
                metadata.Add("streamGuid", streamGuid);
                metadata.Add("triggeringProcessId", triggeringProcessId);
                metadata.Add("triggeringProcessImageFileName", triggeringProcessImageFileName);
                metadata.Add("commandId", commandId);
                this.LogUnhandledExceptionAndExit(nameof(this.GVFltGetFileStreamHandler), metadata);
            }

            return NtStatus.Pending;
        }

        private void GVFltGetFileStreamHandlerAsyncHandler(
            CancellationToken cancellationToken,
            int commandId,
            uint length,
            Guid streamGuid,
            string sha,
            EventMetadata requestMetadata,
            ITracer activity)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return;
            }

            try
            {
                if (!this.rgfsGitObjects.TryCopyBlobContentStream(
                    sha,
                    cancellationToken,
                    (stream, blobLength) =>
                    {
                        if (blobLength != length)
                        {
                            requestMetadata.Add("blobLength", blobLength);
                            activity.RelatedError(requestMetadata, "Actual file length (blobLength) does not match requested length");

                            throw new GetFileStreamException(NtStatus.InvalidParameter);
                        }

                        byte[] buffer = new byte[Math.Min(MaxBlobStreamBufferSize, blobLength)];
                        long remainingData = blobLength;

                        using (WriteBuffer targetBuffer = gvflt.CreateWriteBuffer((uint)buffer.Length))
                        {
                            while (remainingData > 0)
                            {
                                cancellationToken.ThrowIfCancellationRequested();

                                uint bytesToCopy = (uint)Math.Min(remainingData, targetBuffer.Length);

                                try
                                {
                                    targetBuffer.Stream.Seek(0, SeekOrigin.Begin);
                                    stream.CopyBlockTo(targetBuffer.Stream, bytesToCopy, buffer);
                                }
                                catch (IOException e)
                                {
                                    requestMetadata.Add("Exception", e.ToString());
                                    activity.RelatedError(requestMetadata, "IOException while copying to unmanaged buffer.");

                                    throw new GetFileStreamException("IOException while copying to unmanaged buffer: " + e.Message, NtStatus.FileNotAvailable);
                                }

                                long writeOffset = length - remainingData;

                                NtStatus writeResult = this.gvflt.WriteFile(streamGuid, targetBuffer, (ulong)writeOffset, bytesToCopy);
                                remainingData -= bytesToCopy;

                                if (writeResult != NtStatus.Success)
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
                                            // If we hit this in RGFS, the most common explanation is that we're calling GvWriteFile after the GVFlt thread waiting on the respose
                                            // from GetFileStream has already timed out
                                            {
                                                requestMetadata.Add(TracingConstants.MessageKey.InfoMessage, "GvWriteFile returned StatusObjectNameNotFound");
                                                activity.RelatedEvent(EventLevel.Informational, "WriteFile_ObjectNameNotFound", requestMetadata);
                                            }

                                            break;

                                        default:
                                            {
                                                activity.RelatedError(requestMetadata, "GvWriteFile failed, error: " + writeResult.ToString("X") + "(" + writeResult.ToString("G") + ")");
                                            }

                                            break;
                                    }

                                    throw new GetFileStreamException(writeResult);
                                }
                            }
                        }
                    }))
                {
                    activity.RelatedError(requestMetadata, "TryCopyBlobContentStream failed");

                    this.TryCompleteCommand(commandId, NtStatus.FileNotAvailable);
                    return;
                }
            }
            catch (OperationCanceledException)
            {
                requestMetadata.Add(TracingConstants.MessageKey.InfoMessage, "GVFltGetFileStreamHandlerAsyncHandler: Operation cancelled");
                this.context.Tracer.RelatedEvent(
                    EventLevel.Informational, 
                    nameof(this.GVFltGetFileStreamHandlerAsyncHandler) + "_OperationCancelled",
                    requestMetadata);

                return;
            }
            catch (GetFileStreamException e)
            {
                this.TryCompleteCommand(commandId, e.ErrorCode);
                return;
            }
            catch (Exception e)
            {
                requestMetadata.Add("Exception", e.ToString());
                activity.RelatedError(requestMetadata, "TryCopyBlobContentStream failed");

                this.TryCompleteCommand(commandId, NtStatus.FileNotAvailable);
                return;
            }

            this.TryCompleteCommand(commandId, NtStatus.Success);
        }

        private NtStatus GVFltNotifyFirstWriteHandler(string virtualPath)
        {
            try
            {
                virtualPath = PathUtil.RemoveTrailingSlashIfPresent(virtualPath);

                if (!this.isMountComplete)
                {
                    EventMetadata metadata = this.CreateEventMetadata(virtualPath);
                    metadata.Add(TracingConstants.MessageKey.InfoMessage, nameof(this.GVFltNotifyFirstWriteHandler) + ": Mount has not yet completed");
                    this.context.Tracer.RelatedEvent(EventLevel.Informational, "NotifyFirstWrite_MountNotComplete", metadata);
                    return NtStatus.DeviceNotReady;
                }

                if (!string.Equals(virtualPath, string.Empty))
                {
                    bool isFolder;
                    string fileName;
                    bool isPathProjected = this.gitIndexProjection.IsPathProjected(virtualPath, out fileName, out isFolder);
                    if (isPathProjected && !isFolder)
                    {
                        this.background.Enqueue(BackgroundGitUpdate.OnFileFirstWrite(virtualPath));
                    }
                }
            }
            catch (Exception e)
            {
                this.LogUnhandledExceptionAndExit(nameof(this.GVFltNotifyFirstWriteHandler), this.CreateEventMetadata(virtualPath, e));
            }

            return NtStatus.Success;
        }

        private void GVFltNotifyPostCreateNewFileHandler(
            string virtualPath,
            bool isDirectory,
            uint desiredAccess,
            uint shareMode,
            uint createDisposition,
            uint createOptions,
            ref NotificationType notificationMask)
        {
            try
            {
                if (!PathUtil.IsPathInsideDotGit(virtualPath))
                {
                    if (isDirectory)
                    {
                        this.background.Enqueue(BackgroundGitUpdate.OnFolderCreated(virtualPath));
                    }
                    else
                    {
                        this.background.Enqueue(BackgroundGitUpdate.OnFileCreated(virtualPath));
                    }
                }
            }
            catch (Exception e)
            {
                EventMetadata metadata = this.CreateEventMetadata(virtualPath, e);
                metadata.Add("isDirectory", isDirectory);
                metadata.Add("desiredAccess", desiredAccess);
                metadata.Add("shareMode", shareMode);
                metadata.Add("createDisposition", createDisposition);
                metadata.Add("createOptions", createOptions);
                this.LogUnhandledExceptionAndExit(nameof(this.GVFltNotifyPostCreateNewFileHandler), metadata);
            }
        }

        private void GVFltNotifyPostCreateOverwrittenOrSupersededHandler(
            string virtualPath,
            bool isDirectory,
            uint desiredAccess,
            uint shareMode,
            uint createDisposition,
            uint createOptions,
            IoStatusBlockValue iostatusBlock,
            ref NotificationType notificationMask)
        {
            try
            {
                if (!PathUtil.IsPathInsideDotGit(virtualPath))
                {
                    switch (iostatusBlock)
                    {
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
            catch (Exception e)
            {
                EventMetadata metadata = this.CreateEventMetadata(virtualPath, e);
                metadata.Add("isDirectory", isDirectory);
                metadata.Add("desiredAccess", desiredAccess);
                metadata.Add("shareMode", shareMode);
                metadata.Add("createDisposition", createDisposition);
                metadata.Add("createOptions", createOptions);
                metadata.Add("iostatusBlock", iostatusBlock);
                this.LogUnhandledExceptionAndExit(nameof(this.GVFltNotifyPostCreateOverwrittenOrSupersededHandler), metadata);
            }
        }

        private NtStatus GvFltNotifyPreRenameHandler(string relativePath, string destinationPath)
        {
            try
            {
                if (destinationPath.Equals(RGFSConstants.DotGit.Index, StringComparison.OrdinalIgnoreCase))
                {
                    string lockedGitCommand = this.context.Repository.RGFSLock.GetLockedGitCommand();
                    if (string.IsNullOrEmpty(lockedGitCommand))
                    {
                        EventMetadata metadata = new EventMetadata();
                        metadata.Add("Area", EtwArea);
                        metadata.Add(TracingConstants.MessageKey.WarningMessage, "Blocked index rename outside the lock");
                        this.context.Tracer.RelatedEvent(EventLevel.Warning, "GvFltNotifyPreRenameHandler", metadata);

                        return NtStatus.AccessDenied;
                    }
                }
            }
            catch (Exception e)
            {
                EventMetadata metadata = this.CreateEventMetadata(relativePath, e);
                metadata.Add("destinationPath", destinationPath);
                this.LogUnhandledExceptionAndExit(nameof(this.GvFltNotifyPreRenameHandler), metadata);
            }

            return NtStatus.Success;
        }

        private NtStatus GVFltNotifyPreDeleteHandler(string virtualPath, bool isDirectory)
        {
            try
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
            }
            catch (Exception e)
            {
                EventMetadata metadata = this.CreateEventMetadata(virtualPath, e);
                metadata.Add("isDirectory", isDirectory);
                this.LogUnhandledExceptionAndExit(nameof(this.GVFltNotifyPreDeleteHandler), metadata);
            }

            return NtStatus.Success;
        }

        private bool CanDeleteDirectory()
        {
            GitCommandLineParser gitCommand = new GitCommandLineParser(this.context.Repository.RGFSLock.GetLockedGitCommand());
            return 
                !gitCommand.IsValidGitCommand ||
                gitCommand.IsVerb(GitCommandLineParser.Verbs.Clean) ||
                gitCommand.IsResetHard();
        }

        private void GVFltNotifyFileRenamedHandler(
            string virtualPath,
            string destinationPath,
            bool isDirectory,
            ref NotificationType notificationMask)
        {
            try
            {
                if (PathUtil.IsPathInsideDotGit(destinationPath))
                {
                    this.OnDotGitFileChanged(destinationPath);
                }
                else
                {
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
            catch (Exception e)
            {
                EventMetadata metadata = this.CreateEventMetadata(virtualPath, e);
                metadata.Add("destinationPath", destinationPath);
                metadata.Add("isDirectory", isDirectory);
                this.LogUnhandledExceptionAndExit(nameof(this.GVFltNotifyFileRenamedHandler), metadata);
            }
        }

        private void GVFltNotifyFileHandleClosedModifiedOrDeletedHandler(
            string virtualPath, 
            bool isDirectory,
            bool isFileModified,
            bool isFileDeleted)
        {
            try
            {
                bool pathInsideDotGit = PathUtil.IsPathInsideDotGit(virtualPath);

                if (isFileModified && pathInsideDotGit)
                {
                    // TODO 876861: See if GVFlt can provide process ID\name in this callback
                    this.OnDotGitFileChanged(virtualPath);
                }
                else if (isFileDeleted && !pathInsideDotGit)
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
            catch (Exception e)
            {
                EventMetadata metadata = this.CreateEventMetadata(virtualPath, e);
                metadata.Add("isDirectory", isDirectory);
                metadata.Add("isFileModified", isFileModified);
                metadata.Add("isFileDeleted", isFileDeleted);
                this.LogUnhandledExceptionAndExit(nameof(this.GVFltNotifyFileHandleClosedModifiedOrDeletedHandler), metadata);
            }
        }        

        private void GVFltCancelCommandHandler(int commandId)
        {
            try
            {
                CancellationTokenSource cancellationSource;
                if (this.activeCommands.TryRemove(commandId, out cancellationSource))
                {
                    try
                    {
                        cancellationSource.Cancel();
                    }
                    catch (ObjectDisposedException)
                    {
                        // Task already completed
                    }
                    catch (AggregateException e)
                    {
                        // An aggregate exception containing all the exceptions thrown by 
                        // the registered callbacks on the associated CancellationToken

                        foreach (Exception innerException in e.Flatten().InnerExceptions)
                        {
                            if (!(innerException is OperationCanceledException) && !(innerException is TaskCanceledException))
                            {
                                EventMetadata metadata = this.CreateEventMetadata(virtualPath: null, exception: innerException);
                                metadata.Add("commandId", commandId);
                                this.context.Tracer.RelatedError(metadata, "GVFltCancelCommandHandler: AggregateException while requesting cancellation");
                            }
                        }
                    }
                }
            }
            catch (Exception e)
            {
                EventMetadata metadata = this.CreateEventMetadata(virtualPath: null, exception: e);
                metadata.Add("commandId", commandId);
                this.LogUnhandledExceptionAndExit(nameof(this.GVFltCancelCommandHandler), metadata);
            }
        }

        private void OnDotGitFileChanged(string virtualPath)
        {
            if (virtualPath.Equals(RGFSConstants.DotGit.Index, StringComparison.OrdinalIgnoreCase))
            {
                this.OnIndexFileChange();
            }
            else if (virtualPath.Equals(RGFSConstants.DotGit.Logs.Head, StringComparison.OrdinalIgnoreCase))
            {
                this.OnLogsHeadChange();
            }
        }

        private CallbackResult PreBackgroundOperation()
        {
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
                        result = this.alwaysExcludeFile.AddEntriesForFile(gitUpdate.VirtualPath);
                    }

                    break;

                case BackgroundGitUpdate.OperationType.OnFileRenamed:
                    metadata.Add("oldVirtualPath", gitUpdate.OldVirtualPath);
                    metadata.Add("virtualPath", gitUpdate.VirtualPath);
                    result = CallbackResult.Success;
                    if (!string.IsNullOrEmpty(gitUpdate.OldVirtualPath) && !PathUtil.IsPathInsideDotGit(gitUpdate.OldVirtualPath))
                    {
                        result = this.AddFileToSparseCheckoutAndClearSkipWorktreeBit(gitUpdate.OldVirtualPath);
                        if (result == CallbackResult.Success)
                        {
                            result = this.alwaysExcludeFile.RemoveEntriesForFiles(new List<string> { gitUpdate.OldVirtualPath });
                        }
                    }

                    if (result == CallbackResult.Success && !string.IsNullOrEmpty(gitUpdate.VirtualPath))
                    {
                        // No need to check if gitUpdate.VirtualPath is inside the .git folder as OnFileRenamed is not scheduled
                        // when a file destination is inside the .git folder

                        result = this.AddFileToSparseCheckoutAndClearSkipWorktreeBit(gitUpdate.VirtualPath);
                        if (result == CallbackResult.Success)
                        {
                            result = this.alwaysExcludeFile.AddEntriesForFile(gitUpdate.VirtualPath);
                        }
                    }

                    break;

                case BackgroundGitUpdate.OperationType.OnFileDeleted:
                    metadata.Add("virtualPath", gitUpdate.VirtualPath);
                    result = this.AddFileToSparseCheckoutAndClearSkipWorktreeBit(gitUpdate.VirtualPath);
                    if (result == CallbackResult.Success)
                    {
                        result = this.alwaysExcludeFile.RemoveEntriesForFiles(new List<string> { gitUpdate.VirtualPath });
                    }

                    break;

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
                            List<string> virtualPathsToRemove = new List<string> { };
                            Queue<string> relativeFolderPaths = new Queue<string>();
                            relativeFolderPaths.Enqueue(gitUpdate.VirtualPath);

                            // Add all the files in the renamed folder to the always_exclude file
                            while (relativeFolderPaths.Count > 0)
                            {
                                string folderPath = relativeFolderPaths.Dequeue();
                                if (result == CallbackResult.Success)
                                {
                                    try
                                    {
                                        foreach (DirectoryItemInfo itemInfo in this.context.FileSystem.ItemsInDirectory(Path.Combine(this.context.Enlistment.WorkingDirectoryRoot, folderPath)))
                                        {
                                            string itemVirtualPath = Path.Combine(folderPath, itemInfo.Name);
                                            if (itemInfo.IsDirectory)
                                            {
                                                relativeFolderPaths.Enqueue(itemVirtualPath);
                                            }
                                            else
                                            {
                                                string oldItemVirtualPath = gitUpdate.OldVirtualPath + itemVirtualPath.Substring(gitUpdate.VirtualPath.Length);
                                                virtualPathsToRemove.Add(oldItemVirtualPath);
                                                result = this.alwaysExcludeFile.AddEntriesForFile(itemVirtualPath);
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

                            if (result == CallbackResult.Success)
                            {
                                result = this.alwaysExcludeFile.RemoveEntriesForFiles(virtualPathsToRemove);
                            }
                        }
                    }

                    break;

                case BackgroundGitUpdate.OperationType.OnFolderDeleted:
                    metadata.Add("virtualPath", gitUpdate.VirtualPath);
                    result = this.sparseCheckout.AddFolderEntry(gitUpdate.VirtualPath);
                    break;

                case BackgroundGitUpdate.OperationType.OnFolderFirstWrite:
                    result = CallbackResult.Success;
                    break;

                case BackgroundGitUpdate.OperationType.OnIndexWriteWithoutProjectionChange:
                    result = this.gitIndexProjection.ValidateSparseCheckout();
                    break;

                case BackgroundGitUpdate.OperationType.OnPlaceholderCreationsBlockedForGit:
                    this.gitIndexProjection.ValidateNegativePathCache();
                    result = CallbackResult.Success;
                    break;

                default:
                    throw new InvalidOperationException("Invalid background operation");
            }

            if (result != CallbackResult.Success)
            {
                metadata.Add("Area", "ExecuteBackgroundOperation");
                metadata.Add("Operation", gitUpdate.Operation.ToString());
                metadata.Add(TracingConstants.MessageKey.WarningMessage, "Background operation failed");
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

            bool skipWorktreeBitCleared;
            result = this.gitIndexProjection.ClearSkipWorktreeBit(virtualPath, out skipWorktreeBitCleared);
            if (result == CallbackResult.Success && skipWorktreeBitCleared)
            {
                this.gitIndexProjection.RemoveFromPlaceholderList(virtualPath);
            }

            return result;
        }

        private CallbackResult PostBackgroundOperation()
        {
            this.sparseCheckout.Close();

            CallbackResult alwaysExcludeResult = this.alwaysExcludeFile.FlushAndClose();
            if (alwaysExcludeResult != CallbackResult.Success)
            {
                return alwaysExcludeResult;
            }

            return this.gitIndexProjection.ReleaseLockAndClose();
        }
        
        private bool IsSpecialGitFile(string fileName)
        {
            return
                fileName.Equals(RGFSConstants.SpecialGitFiles.GitAttributes, StringComparison.OrdinalIgnoreCase) ||
                fileName.Equals(RGFSConstants.SpecialGitFiles.GitIgnore, StringComparison.OrdinalIgnoreCase);
        }

        private EventMetadata CreateEventMetadata(
            Guid enumerationId,
            string virtualPath = null,
            Exception exception = null)
        {
            EventMetadata metadata = this.CreateEventMetadata(virtualPath, exception);
            metadata.Add("enumerationId", enumerationId);
            return metadata;
        }

        private EventMetadata CreateEventMetadata(
            string virtualPath = null,
            Exception exception = null)
        {
            EventMetadata metadata = new EventMetadata();
            metadata.Add("Area", EtwArea);

            if (virtualPath != null)
            {
                metadata.Add("virtualPath", virtualPath);
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
                    EventMetadata metadata = this.CreateEventMetadata(virtualPath: null, exception: e);
                    this.context.Tracer.RelatedWarning(metadata, "GetLogsHeadFileProperties: Exception thrown from GetFileProperties", Keywords.Telemetry);

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
            GitCommandLineParser gitCommand = new GitCommandLineParser(this.context.Repository.RGFSLock.GetLockedGitCommand());
            return
                !gitCommand.IsValidGitCommand ||
                gitCommand.IsVerb(CanCreatePlaceholderVerbs);
        }

        private void LogUnhandledExceptionAndExit(string methodName, EventMetadata metadata)
        {
            this.context.Tracer.RelatedError(metadata, methodName + " caught unhandled exception, exiting process");
            Environment.Exit(1);
        }

        [Serializable]
        public struct BackgroundGitUpdate
        {
            public BackgroundGitUpdate(OperationType operation, string virtualPath, string oldVirtualPath)
            {
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
                OnPlaceholderCreationsBlockedForGit
            }

            public OperationType Operation { get; set; }

            public string VirtualPath { get; set; }
            public string OldVirtualPath { get; set; }

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

            public static BackgroundGitUpdate OnIndexWriteWithoutProjectionChange()
            {
                return new BackgroundGitUpdate(OperationType.OnIndexWriteWithoutProjectionChange, virtualPath: null, oldVirtualPath: null);
            }

            public static BackgroundGitUpdate OnPlaceholderCreationsBlockedForGit()
            {
                return new BackgroundGitUpdate(OperationType.OnPlaceholderCreationsBlockedForGit, virtualPath: null, oldVirtualPath: null);
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

        private class GetFileStreamException : Exception
        {
            public GetFileStreamException(NtStatus errorCode)
                : this("GetFileStreamException exception, error: " + errorCode.ToString(), errorCode)
            {                
            }

            public GetFileStreamException(string message, NtStatus errorCode)
                : base(message)
            {
                this.ErrorCode = errorCode;
            }

            public NtStatus ErrorCode { get; private set; }
        }

        private class Notifications
        {
            public const NotificationType DotGitFolder = NotificationType.FileRenamed;

            public const NotificationType IndexFile =
                NotificationType.PreDelete |
                NotificationType.FileRenamed |
                NotificationType.FileHandleClosedModified;

            public const NotificationType IndexLockFile =
                NotificationType.PreRename |
                NotificationType.FileRenamed;

            public const NotificationType LogsHeadFile = 
                NotificationType.FileRenamed | 
                NotificationType.FileHandleClosedModified;

            public const NotificationType FilesInWorkingFolder =
                NotificationType.PostCreateNewFile |
                NotificationType.PostCreateOverwrittenOrSuperseded |
                NotificationType.FileRenamed |
                NotificationType.FileHandleClosedDeleted;

            public const NotificationType FoldersInWorkingFolder =
                NotificationType.PostCreateNewFile |
                NotificationType.PreDelete |
                NotificationType.FileRenamed |
                NotificationType.FileHandleClosedDeleted;
        }
    }
}
