using GVFS.Common;
using GVFS.Common.FileSystem;
using GVFS.Common.Git;
using GVFS.Common.Tracing;
using GVFS.Virtualization;
using GVFS.Virtualization.BlobSize;
using GVFS.Virtualization.FileSystem;
using GVFS.Virtualization.Projection;
using Microsoft.Windows.ProjFS;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace GVFS.Platform.Windows
{
    public class WindowsFileSystemVirtualizer : FileSystemVirtualizer, IRequiredCallbacks
    {
        /// <summary>
        /// GVFS uses the first byte of the providerId field of placeholders to version
        /// the data that it stores in the contentId (and providerId) fields of the placeholder
        /// </summary>
        public static readonly byte[] PlaceholderVersionId = new byte[] { PlaceholderVersion };

        private const string ClassName = nameof(WindowsFileSystemVirtualizer);
        private const int MaxBlobStreamBufferSize = 64 * 1024;
        private const int MinPrjLibThreads = 5;

        private IVirtualizationInstance virtualizationInstance;
        private ConcurrentDictionary<Guid, ActiveEnumeration> activeEnumerations;
        private ConcurrentDictionary<int, CancellationTokenSource> activeCommands;

        public WindowsFileSystemVirtualizer(GVFSContext context, GVFSGitObjects gitObjects)
            : this(
                  context,
                  gitObjects,
                  virtualizationInstance: null,
                  numWorkerThreads: FileSystemVirtualizer.DefaultNumWorkerThreads)
        {
        }

        public WindowsFileSystemVirtualizer(
            GVFSContext context,
            GVFSGitObjects gitObjects,
            IVirtualizationInstance virtualizationInstance,
            int numWorkerThreads)
            : base(context, gitObjects, numWorkerThreads)
        {
            List<NotificationMapping> notificationMappings = new List<NotificationMapping>()
            {
                new NotificationMapping(Notifications.FilesInWorkingFolder | Notifications.FoldersInWorkingFolder, string.Empty),
                new NotificationMapping(NotificationType.None, GVFSConstants.DotGit.Root),
                new NotificationMapping(Notifications.IndexFile, GVFSConstants.DotGit.Index),
                new NotificationMapping(Notifications.LogsHeadFile, GVFSConstants.DotGit.Logs.Head),
                new NotificationMapping(Notifications.ExcludeAndHeadFile, GVFSConstants.DotGit.Info.ExcludePath),
                new NotificationMapping(Notifications.ExcludeAndHeadFile, GVFSConstants.DotGit.Head),
                new NotificationMapping(Notifications.FilesAndFoldersInRefsHeads, GVFSConstants.DotGit.Refs.Heads.Root),
            };

            // We currently use twice as many threads as connections to allow for
            // non-network operations to possibly succeed despite the connection limit
            uint threadCount = (uint)Math.Max(MinPrjLibThreads, Environment.ProcessorCount * 2);
            this.virtualizationInstance = virtualizationInstance ?? new VirtualizationInstance(
                context.Enlistment.WorkingDirectoryRoot,
                poolThreadCount: threadCount,
                concurrentThreadCount: threadCount,
                enableNegativePathCache: true,
                notificationMappings: notificationMappings);

            this.activeEnumerations = new ConcurrentDictionary<Guid, ActiveEnumeration>();
            this.activeCommands = new ConcurrentDictionary<int, CancellationTokenSource>();
        }

        protected override string EtwArea => ClassName;

        /// <remarks>
        /// Public for unit testing
        /// </remarks>
        public static bool InternalFileNameMatchesFilter(string name, string filter)
        {
            return PatternMatcher.StrictMatchPattern(filter, name);
        }

        public static FSResult HResultToFSResult(HResult result)
        {
            switch (result)
            {
                case HResult.Ok:
                    return FSResult.Ok;

                case HResult.DirNotEmpty:
                    return FSResult.DirectoryNotEmpty;

                case HResult.FileNotFound:
                case HResult.PathNotFound:
                    return FSResult.FileOrPathNotFound;

                case (HResult)HResultExtensions.HResultFromNtStatus.IoReparseTagNotHandled:
                    return FSResult.IoReparseTagNotHandled;

                case HResult.VirtualizationInvalidOp:
                    return FSResult.VirtualizationInvalidOperation;

                case (HResult)HResultExtensions.GenericProjFSError:
                    return FSResult.GenericProjFSError;

                default:
                    return FSResult.IOError;
            }
        }

        public override void Stop()
        {
            this.virtualizationInstance.StopVirtualizing();
        }

        public override FileSystemResult ClearNegativePathCache(out uint totalEntryCount)
        {
            HResult result = this.virtualizationInstance.ClearNegativePathCache(out totalEntryCount);
            return new FileSystemResult(HResultToFSResult(result), unchecked((int)result));
        }

        public override FileSystemResult DeleteFile(string relativePath, UpdatePlaceholderType updateFlags, out UpdateFailureReason failureReason)
        {
            UpdateFailureCause failureCause = UpdateFailureCause.NoFailure;
            HResult result = this.virtualizationInstance.DeleteFile(relativePath, (UpdateType)updateFlags, out failureCause);
            failureReason = (UpdateFailureReason)failureCause;
            return new FileSystemResult(HResultToFSResult(result), unchecked((int)result));
        }

        public override FileSystemResult WritePlaceholderFile(
            string relativePath,
            long endOfFile,
            string sha)
        {
            FileProperties properties = this.FileSystemCallbacks.GetLogsHeadFileProperties();
            HResult result = this.virtualizationInstance.WritePlaceholderInfo(
                relativePath,
                properties.CreationTimeUTC,
                properties.LastAccessTimeUTC,
                properties.LastWriteTimeUTC,
                changeTime: properties.LastWriteTimeUTC,
                fileAttributes: FileAttributes.Archive,
                endOfFile: endOfFile,
                isDirectory: false,
                contentId: FileSystemVirtualizer.ConvertShaToContentId(sha),
                providerId: PlaceholderVersionId);

            return new FileSystemResult(HResultToFSResult(result), unchecked((int)result));
        }

        public override FileSystemResult WritePlaceholderDirectory(string relativePath)
        {
            FileProperties properties = this.FileSystemCallbacks.GetLogsHeadFileProperties();
            HResult result = this.virtualizationInstance.WritePlaceholderInfo(
                relativePath,
                properties.CreationTimeUTC,
                properties.LastAccessTimeUTC,
                properties.LastWriteTimeUTC,
                changeTime: properties.LastWriteTimeUTC,
                fileAttributes: FileAttributes.Directory,
                endOfFile: 0,
                isDirectory: true,
                contentId: FolderContentId,
                providerId: PlaceholderVersionId);

            return new FileSystemResult(HResultToFSResult(result), unchecked((int)result));
        }

        public override FileSystemResult UpdatePlaceholderIfNeeded(
            string relativePath,
            DateTime creationTime,
            DateTime lastAccessTime,
            DateTime lastWriteTime,
            DateTime changeTime,
            FileAttributes fileAttributes,
            long endOfFile,
            string shaContentId,
            UpdatePlaceholderType updateFlags,
            out UpdateFailureReason failureReason)
        {
            UpdateFailureCause failureCause = UpdateFailureCause.NoFailure;
            HResult result = this.virtualizationInstance.UpdateFileIfNeeded(
                relativePath,
                creationTime,
                lastAccessTime,
                lastWriteTime,
                changeTime,
                fileAttributes,
                endOfFile,
                ConvertShaToContentId(shaContentId),
                PlaceholderVersionId,
                (UpdateType)updateFlags,
                out failureCause);
            failureReason = (UpdateFailureReason)failureCause;
            return new FileSystemResult(HResultToFSResult(result), unchecked((int)result));
        }

        public override FileSystemResult DehydrateFolder(string relativePath)
        {
            // The folder should have already been deleted, but
            // its tombstone also needs to be deleted to allow reprojection.
            var result = this.virtualizationInstance.DeleteFile(
                relativePath,
                UpdateType.AllowTombstone,
                out UpdateFailureCause failureCause);

            return new FileSystemResult(HResultToFSResult(result), unchecked((int)result));
        }

        // TODO: Need ProjFS 13150199 to be fixed so that GVFS doesn't leak memory if the enumeration cancelled.
        // Currently EndDirectoryEnumerationHandler must be called to remove the ActiveEnumeration from this.activeEnumerations
        public HResult StartDirectoryEnumerationCallback(int commandId, Guid enumerationId, string virtualPath, uint triggeringProcessId, string triggeringProcessImageFileName)
        {
            try
            {
                List<ProjectedFileInfo> projectedItems;
                if (this.FileSystemCallbacks.GitIndexProjection.TryGetProjectedItemsFromMemory(virtualPath, out projectedItems))
                {
                    ActiveEnumeration activeEnumeration = new ActiveEnumeration(projectedItems);
                    if (!this.activeEnumerations.TryAdd(enumerationId, activeEnumeration))
                    {
                        this.Context.Tracer.RelatedError(
                            this.CreateEventMetadata(enumerationId, virtualPath),
                            nameof(this.StartDirectoryEnumerationCallback) + ": Failed to add enumeration ID to active collection");

                        return HResult.InternalError;
                    }

                    return HResult.Ok;
                }

                CancellationTokenSource cancellationSource;
                if (!this.TryRegisterCommand(commandId, out cancellationSource))
                {
                    EventMetadata metadata = this.CreateEventMetadata(enumerationId, virtualPath);
                    metadata.Add("commandId", commandId);
                    this.Context.Tracer.RelatedWarning(metadata, nameof(this.StartDirectoryEnumerationCallback) + ": Failed to register command");
                }

                FileOrNetworkRequest startDirectoryEnumerationHandler = new FileOrNetworkRequest(
                    (blobSizesConnection) => this.StartDirectoryEnumerationAsyncHandler(
                        cancellationSource.Token,
                        blobSizesConnection,
                        commandId,
                        enumerationId,
                        virtualPath),
                    () => cancellationSource.Dispose());

                Exception e;
                if (!this.TryScheduleFileOrNetworkRequest(startDirectoryEnumerationHandler, out e))
                {
                    EventMetadata metadata = this.CreateEventMetadata(virtualPath, e);
                    metadata.Add("commandId", commandId);
                    metadata.Add(TracingConstants.MessageKey.WarningMessage, nameof(this.StartDirectoryEnumerationCallback) + ": Failed to schedule async handler");
                    this.Context.Tracer.RelatedEvent(EventLevel.Warning, nameof(this.StartDirectoryEnumerationCallback) + "_FailedToScheduleAsyncHandler", metadata);

                    cancellationSource.Dispose();

                    return (HResult)HResultExtensions.HResultFromNtStatus.DeviceNotReady;
                }
            }
            catch (Exception e)
            {
                EventMetadata metadata = this.CreateEventMetadata(enumerationId, virtualPath, e);
                metadata.Add("commandId", commandId);
                this.LogUnhandledExceptionAndExit(nameof(this.StartDirectoryEnumerationCallback), metadata);
            }

            return HResult.Pending;
        }

        public HResult GetDirectoryEnumerationCallback(
        int commandId,
        Guid enumerationId,
        string filterFileName,
        bool restartScan,
        IDirectoryEnumerationResults results)
        {
            try
            {
                ActiveEnumeration activeEnumeration = null;
                if (!this.activeEnumerations.TryGetValue(enumerationId, out activeEnumeration))
                {
                    EventMetadata metadata = this.CreateEventMetadata(enumerationId);
                    metadata.Add("filterFileName", filterFileName);
                    metadata.Add("restartScan", restartScan);
                    this.Context.Tracer.RelatedError(metadata, nameof(this.GetDirectoryEnumerationCallback) + ": Failed to find active enumeration ID");

                    return HResult.InternalError;
                }

                if (restartScan)
                {
                    activeEnumeration.RestartEnumeration(filterFileName);
                }
                else
                {
                    activeEnumeration.TrySaveFilterString(filterFileName);
                }

                HResult result = HResult.Ok;
                bool entryAdded = false;
                while (activeEnumeration.IsCurrentValid)
                {
                    ProjectedFileInfo fileInfo = activeEnumeration.Current;
                    FileProperties properties = this.FileSystemCallbacks.GetLogsHeadFileProperties();

                    bool addResult = results.Add(
                        fileName: fileInfo.Name,
                        fileSize: fileInfo.IsFolder ? 0 : fileInfo.Size,
                        isDirectory: fileInfo.IsFolder,
                        fileAttributes: fileInfo.IsFolder ? FileAttributes.Directory : FileAttributes.Archive,
                        creationTime: properties.CreationTimeUTC,
                        lastAccessTime: properties.LastAccessTimeUTC,
                        lastWriteTime: properties.LastWriteTimeUTC,
                        changeTime: properties.LastWriteTimeUTC);

                    if (addResult == true)
                    {
                        entryAdded = true;
                        activeEnumeration.MoveNext();
                    }
                    else
                    {
                        if (entryAdded)
                        {
                            result = HResult.Ok;
                        }

                        break;
                    }
                }

                return result;
            }
            catch (Win32Exception e)
            {
                this.Context.Tracer.RelatedWarning(
                    this.CreateEventMetadata(enumerationId, relativePath: null, exception: e),
                    nameof(this.GetDirectoryEnumerationCallback) + " caught Win32Exception");

                return HResultExtensions.HResultFromWin32(e.NativeErrorCode);
            }
            catch (Exception e)
            {
                this.LogUnhandledExceptionAndExit(
                    nameof(this.GetDirectoryEnumerationCallback),
                    this.CreateEventMetadata(enumerationId, relativePath: null, exception: e));

                return HResult.InternalError;
            }
        }

        public HResult EndDirectoryEnumerationCallback(Guid enumerationId)
        {
            try
            {
                ActiveEnumeration activeEnumeration;
                if (!this.activeEnumerations.TryRemove(enumerationId, out activeEnumeration))
                {
                    this.Context.Tracer.RelatedWarning(
                        this.CreateEventMetadata(enumerationId),
                        nameof(this.EndDirectoryEnumerationCallback) + ": Failed to remove enumeration ID from active collection",
                        Keywords.Telemetry);

                    return HResult.InternalError;
                }
            }
            catch (Exception e)
            {
                this.LogUnhandledExceptionAndExit(
                    nameof(this.EndDirectoryEnumerationCallback),
                    this.CreateEventMetadata(enumerationId, relativePath: null, exception: e));
            }

            return HResult.Ok;
        }

        public HResult GetPlaceholderInfoCallback(
        int commandId,
        string virtualPath,
        uint triggeringProcessId,
        string triggeringProcessImageFileName)
        {
            try
            {
                bool isFolder;
                string fileName;
                if (!this.FileSystemCallbacks.GitIndexProjection.IsPathProjected(virtualPath, out fileName, out isFolder))
                {
                    return HResult.FileNotFound;
                }

                if (!isFolder &&
                    !this.IsSpecialGitFile(fileName) &&
                    !this.CanCreatePlaceholder())
                {
                    EventMetadata metadata = this.CreateEventMetadata(virtualPath);
                    metadata.Add("commandId", commandId);
                    metadata.Add("triggeringProcessId", triggeringProcessId);
                    metadata.Add("triggeringProcessImageFileName", triggeringProcessImageFileName);
                    metadata.Add(TracingConstants.MessageKey.VerboseMessage, $"{nameof(this.GetPlaceholderInfoCallback)}: Not allowed to create placeholder");
                    this.Context.Tracer.RelatedEvent(EventLevel.Verbose, nameof(this.GetPlaceholderInfoCallback), metadata);

                    this.FileSystemCallbacks.OnPlaceholderCreateBlockedForGit();

                    // Another process is modifying the working directory so we cannot modify it
                    // until they are done.
                    return HResult.FileNotFound;
                }

                CancellationTokenSource cancellationSource;
                if (!this.TryRegisterCommand(commandId, out cancellationSource))
                {
                    EventMetadata metadata = this.CreateEventMetadata(virtualPath);
                    metadata.Add("commandId", commandId);
                    metadata.Add("triggeringProcessId", triggeringProcessId);
                    metadata.Add("triggeringProcessImageFileName", triggeringProcessImageFileName);
                    this.Context.Tracer.RelatedWarning(metadata, nameof(this.GetPlaceholderInfoCallback) + ": Failed to register command");
                }

                FileOrNetworkRequest getPlaceholderInformationHandler = new FileOrNetworkRequest(
                    (blobSizesConnection) => this.GetPlaceholderInformationAsyncHandler(
                        cancellationSource.Token,
                        blobSizesConnection,
                        commandId,
                        virtualPath,
                        triggeringProcessId,
                        triggeringProcessImageFileName),
                    () => cancellationSource.Dispose());

                Exception e;
                if (!this.TryScheduleFileOrNetworkRequest(getPlaceholderInformationHandler, out e))
                {
                    EventMetadata metadata = this.CreateEventMetadata(virtualPath, e);
                    metadata.Add("commandId", commandId);
                    metadata.Add("triggeringProcessId", triggeringProcessId);
                    metadata.Add("triggeringProcessImageFileName", triggeringProcessImageFileName);
                    metadata.Add(TracingConstants.MessageKey.WarningMessage, nameof(this.GetPlaceholderInfoCallback) + ": Failed to schedule async handler");
                    this.Context.Tracer.RelatedEvent(EventLevel.Warning, nameof(this.GetPlaceholderInfoCallback) + "_FailedToScheduleAsyncHandler", metadata);

                    cancellationSource.Dispose();

                    return (HResult)HResultExtensions.HResultFromNtStatus.DeviceNotReady;
                }
            }
            catch (Exception e)
            {
                EventMetadata metadata = this.CreateEventMetadata(virtualPath, e);
                metadata.Add("commandId", commandId);
                metadata.Add("triggeringProcessId", triggeringProcessId);
                metadata.Add("triggeringProcessImageFileName", triggeringProcessImageFileName);
                this.LogUnhandledExceptionAndExit(nameof(this.GetPlaceholderInfoCallback), metadata);
            }

            return HResult.Pending;
        }

        public HResult GetFileDataCallback(
        int commandId,
        string virtualPath,
        ulong byteOffset,
        uint length,
        Guid streamGuid,
        byte[] contentId,
        byte[] providerId,
        uint triggeringProcessId,
        string triggeringProcessImageFileName)
        {
            try
            {
                if (contentId == null)
                {
                    this.Context.Tracer.RelatedError($"{nameof(this.GetFileDataCallback)} called with null contentId, path: " + virtualPath);
                    return HResult.InternalError;
                }

                if (providerId == null)
                {
                    this.Context.Tracer.RelatedError($"{nameof(this.GetFileDataCallback)} called with null epochId, path: " + virtualPath);
                    return HResult.InternalError;
                }

                string sha = GetShaFromContentId(contentId);
                byte placeholderVersion = GetPlaceholderVersionFromProviderId(providerId);

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

                if (byteOffset != 0)
                {
                    this.Context.Tracer.RelatedError(metadata, "Invalid Parameter: byteOffset must be 0");
                    return HResult.InternalError;
                }

                if (placeholderVersion != FileSystemVirtualizer.PlaceholderVersion)
                {
                    this.Context.Tracer.RelatedError(metadata, nameof(this.GetFileDataCallback) + ": Unexpected placeholder version");
                    return HResult.InternalError;
                }

                CancellationTokenSource cancellationSource;
                if (!this.TryRegisterCommand(commandId, out cancellationSource))
                {
                    metadata.Add(TracingConstants.MessageKey.WarningMessage, nameof(this.GetFileDataCallback) + ": Failed to register command");
                    this.Context.Tracer.RelatedEvent(EventLevel.Warning, nameof(this.GetFileDataCallback) + "_FailedToRegisterCommand", metadata);
                }

                FileOrNetworkRequest getFileStreamHandler = new FileOrNetworkRequest(
                    (blobSizesConnection) => this.GetFileStreamHandlerAsyncHandler(
                        cancellationSource.Token,
                        commandId,
                        length,
                        streamGuid,
                        sha,
                        metadata,
                        triggeringProcessImageFileName),
                    () =>
                    {
                        cancellationSource.Dispose();
                    });

                Exception e;
                if (!this.TryScheduleFileOrNetworkRequest(getFileStreamHandler, out e))
                {
                    metadata.Add("Exception", e?.ToString());
                    metadata.Add(TracingConstants.MessageKey.WarningMessage, nameof(this.GetFileDataCallback) + ": Failed to schedule async handler");
                    this.Context.Tracer.RelatedEvent(EventLevel.Warning, nameof(this.GetFileDataCallback) + "_FailedToScheduleAsyncHandler", metadata);

                    cancellationSource.Dispose();

                    return (HResult)HResultExtensions.HResultFromNtStatus.DeviceNotReady;
                }
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
                this.LogUnhandledExceptionAndExit(nameof(this.GetFileDataCallback), metadata);
            }

            return HResult.Pending;
        }

        public override bool TryStart(out string error)
        {
            error = string.Empty;

            this.InitializeEnumerationPatternMatcher();

            this.virtualizationInstance.OnNotifyFileOverwritten = this.NotifyFileOverwrittenHandler;
            this.virtualizationInstance.OnNotifyPreCreateHardlink = null;
            this.virtualizationInstance.OnQueryFileName = this.QueryFileNameHandler;
            this.virtualizationInstance.OnNotifyFileOpened = null;
            this.virtualizationInstance.OnNotifyNewFileCreated = this.NotifyNewFileCreatedHandler;
            this.virtualizationInstance.OnNotifyPreDelete = this.NotifyPreDeleteHandler;
            this.virtualizationInstance.OnNotifyPreRename = this.NotifyPreRenameHandler;
            this.virtualizationInstance.OnNotifyFileRenamed = this.NotifyFileRenamedHandler;
            this.virtualizationInstance.OnNotifyHardlinkCreated = this.NotifyHardlinkCreated;
            this.virtualizationInstance.OnNotifyFileHandleClosedNoModification = null;
            this.virtualizationInstance.OnNotifyFileHandleClosedFileModifiedOrDeleted = this.NotifyFileHandleClosedFileModifiedOrDeletedHandler;
            this.virtualizationInstance.OnNotifyFilePreConvertToFull = this.NotifyFilePreConvertToFullHandler;

            this.virtualizationInstance.OnCancelCommand = this.CancelCommandHandler;

            HResult result = this.virtualizationInstance.StartVirtualizing(this);

            if (result != HResult.Ok)
            {
                this.Context.Tracer.RelatedError($"{nameof(this.virtualizationInstance.StartVirtualizing)} failed: " + result.ToString("X") + "(" + result.ToString("G") + ")");
                error = "Failed to start virtualization instance (" + result.ToString() + ")";
                return false;
            }

            return true;
        }

        protected override void OnPossibleTombstoneFolderCreated(string relativePath)
        {
            this.FileSystemCallbacks.OnPossibleTombstoneFolderCreated(relativePath);
        }

        private static void StreamCopyBlockTo(Stream input, Stream destination, long numBytes, byte[] buffer)
        {
            int read;
            while (numBytes > 0)
            {
                int bytesToRead = Math.Min(buffer.Length, (int)numBytes);
                read = input.Read(buffer, 0, bytesToRead);
                if (read <= 0)
                {
                    break;
                }

                destination.Write(buffer, 0, read);
                numBytes -= read;
            }
        }

        private static bool ProjFSPatternMatchingWorks()
        {
            const char DOSQm = '>';
            if (Utils.IsFileNameMatch("Test", "Test" + DOSQm))
            {
                // The installed version of ProjFS has been fixed to handle the special DOS characters
                return true;
            }

            return false;
        }

        private void InitializeEnumerationPatternMatcher()
        {
            bool projFSPatternMatchingWorks = ProjFSPatternMatchingWorks();

            if (projFSPatternMatchingWorks)
            {
                ActiveEnumeration.SetWildcardPatternMatcher(Utils.IsFileNameMatch);
            }
            else
            {
                ActiveEnumeration.SetWildcardPatternMatcher(InternalFileNameMatchesFilter);
            }

            this.Context.Tracer.RelatedEvent(
                EventLevel.Informational,
                nameof(this.InitializeEnumerationPatternMatcher),
                new EventMetadata() { { nameof(projFSPatternMatchingWorks), projFSPatternMatchingWorks } },
                Keywords.Telemetry);
        }

        private bool TryRegisterCommand(int commandId, out CancellationTokenSource cancellationSource)
        {
            cancellationSource = new CancellationTokenSource();
            return this.activeCommands.TryAdd(commandId, cancellationSource);
        }

        private bool TryCompleteCommand(int commandId, HResult result)
        {
            CancellationTokenSource cancellationSource;
            if (this.activeCommands.TryRemove(commandId, out cancellationSource))
            {
                this.virtualizationInstance.CompleteCommand(commandId, result);
                return true;
            }

            return false;
        }

        private void StartDirectoryEnumerationAsyncHandler(
            CancellationToken cancellationToken,
            BlobSizes.BlobSizesConnection blobSizesConnection,
            int commandId,
            Guid enumerationId,
            string virtualPath)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return;
            }

            HResult result;
            try
            {
                ActiveEnumeration activeEnumeration = new ActiveEnumeration(this.FileSystemCallbacks.GitIndexProjection.GetProjectedItems(cancellationToken, blobSizesConnection, virtualPath));

                if (!this.activeEnumerations.TryAdd(enumerationId, activeEnumeration))
                {
                    this.Context.Tracer.RelatedError(
                        this.CreateEventMetadata(enumerationId, virtualPath),
                        nameof(this.StartDirectoryEnumerationAsyncHandler) + ": Failed to add enumeration ID to active collection");

                    result = HResult.InternalError;
                }
                else
                {
                    result = HResult.Ok;
                }
            }
            catch (OperationCanceledException)
            {
                EventMetadata metadata = this.CreateEventMetadata(virtualPath);
                metadata.Add(TracingConstants.MessageKey.InfoMessage, nameof(this.StartDirectoryEnumerationAsyncHandler) + ": Operation cancelled");
                this.Context.Tracer.RelatedEvent(
                    EventLevel.Informational,
                    nameof(this.StartDirectoryEnumerationAsyncHandler) + "_Cancelled",
                    metadata);

                return;
            }
            catch (SizesUnavailableException e)
            {
                result = (HResult)HResultExtensions.HResultFromNtStatus.FileNotAvailable;

                EventMetadata metadata = this.CreateEventMetadata(enumerationId, virtualPath, e);
                metadata.Add("commandId", commandId);
                metadata.Add(nameof(result), result.ToString("X") + "(" + result.ToString("G") + ")");
                this.Context.Tracer.RelatedError(metadata, nameof(this.StartDirectoryEnumerationAsyncHandler) + ": caught SizesUnavailableException");
            }
            catch (Exception e)
            {
                result = HResult.InternalError;

                EventMetadata metadata = this.CreateEventMetadata(enumerationId, virtualPath, e);
                metadata.Add("commandId", commandId);
                this.LogUnhandledExceptionAndExit(nameof(this.StartDirectoryEnumerationAsyncHandler), metadata);
            }

            if (!this.TryCompleteCommand(commandId, result))
            {
                // Command has already been canceled, and no EndDirectoryEnumeration callback will be received

                EventMetadata metadata = this.CreateEventMetadata(virtualPath);
                metadata.Add(TracingConstants.MessageKey.InfoMessage, $"{nameof(this.StartDirectoryEnumerationAsyncHandler)}: TryCompleteCommand returned false, command already canceled");
                metadata.Add("commandId", commandId);
                metadata.Add("enumerationId", enumerationId);
                metadata.Add(nameof(result), result.ToString("X") + "(" + result.ToString("G") + ")");

                ActiveEnumeration activeEnumeration;
                bool activeEnumerationsUpdated = this.activeEnumerations.TryRemove(enumerationId, out activeEnumeration);
                metadata.Add("activeEnumerationsUpdated", activeEnumerationsUpdated);
                this.Context.Tracer.RelatedEvent(EventLevel.Informational, $"{nameof(this.StartDirectoryEnumerationAsyncHandler)}_CommandAlreadyCanceled", metadata);
            }
        }

        /// <summary>
        /// QueryFileNameHandler is called by ProjFS when a file is being deleted or renamed.  It is an optimization so that ProjFS
        /// can avoid calling Start\Get\End enumeration to check if GVFS is still projecting a file.  This method uses the same
        /// rules for deciding what is projected as the enumeration callbacks.
        /// </summary>
        private HResult QueryFileNameHandler(string virtualPath)
        {
            try
            {
                if (FileSystemCallbacks.IsPathInsideDotGit(virtualPath))
                {
                    return HResult.FileNotFound;
                }

                bool isFolder;
                string fileName;
                if (!this.FileSystemCallbacks.GitIndexProjection.IsPathProjected(virtualPath, out fileName, out isFolder))
                {
                    return HResult.FileNotFound;
                }
            }
            catch (Exception e)
            {
                this.LogUnhandledExceptionAndExit(nameof(this.QueryFileNameHandler), this.CreateEventMetadata(virtualPath, e));
            }

            return HResult.Ok;
        }

        private void GetPlaceholderInformationAsyncHandler(
            CancellationToken cancellationToken,
            BlobSizes.BlobSizesConnection blobSizesConnection,
            int commandId,
            string virtualPath,
            uint triggeringProcessId,
            string triggeringProcessImageFileName)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return;
            }

            HResult result = HResult.Ok;

            try
            {
                ProjectedFileInfo fileInfo;
                string parentFolderPath;
                try
                {
                    fileInfo = this.FileSystemCallbacks.GitIndexProjection.GetProjectedFileInfo(cancellationToken, blobSizesConnection, virtualPath, out parentFolderPath);
                    if (fileInfo == null)
                    {
                        this.TryCompleteCommand(commandId, HResult.FileNotFound);
                        return;
                    }
                }
                catch (OperationCanceledException)
                {
                    EventMetadata metadata = this.CreateEventMetadata(virtualPath);
                    metadata.Add(TracingConstants.MessageKey.InfoMessage, nameof(this.GetPlaceholderInformationAsyncHandler) + ": Operation cancelled");
                    this.Context.Tracer.RelatedEvent(
                        EventLevel.Informational,
                        $"{nameof(this.GetPlaceholderInformationAsyncHandler)}_{nameof(this.FileSystemCallbacks.GitIndexProjection.GetProjectedFileInfo)}_Cancelled",
                        metadata);
                    return;
                }

                // The file name case in the virtualPath parameter might be different than the file name case in the repo.
                // Build a new virtualPath that preserves the case in the repo so that the placeholder file is created
                // with proper case.
                string gitCaseVirtualPath = Path.Combine(parentFolderPath, fileInfo.Name);

                string sha;
                FileSystemResult fileSystemResult;
                if (fileInfo.IsFolder)
                {
                    sha = string.Empty;
                    fileSystemResult = this.WritePlaceholderDirectory(gitCaseVirtualPath);
                }
                else
                {
                    sha = fileInfo.Sha.ToString();
                    fileSystemResult = this.WritePlaceholderFile(gitCaseVirtualPath, fileInfo.Size, sha);
                }

                result = (HResult)fileSystemResult.RawResult;
                if (result != HResult.Ok)
                {
                    EventMetadata metadata = this.CreateEventMetadata(virtualPath);
                    metadata.Add("gitCaseVirtualPath", gitCaseVirtualPath);
                    metadata.Add("triggeringProcessId", triggeringProcessId);
                    metadata.Add("triggeringProcessImageFileName", triggeringProcessImageFileName);
                    metadata.Add("FileName", fileInfo.Name);
                    metadata.Add("IsFolder", fileInfo.IsFolder);
                    metadata.Add(nameof(sha), sha);
                    metadata.Add(nameof(result), result.ToString("X") + "(" + result.ToString("G") + ")");
                    this.Context.Tracer.RelatedError(metadata, $"{nameof(this.GetPlaceholderInformationAsyncHandler)}: {nameof(this.virtualizationInstance.WritePlaceholderInfo)} failed");
                }
                else
                {
                    if (fileInfo.IsFolder)
                    {
                        this.FileSystemCallbacks.OnPlaceholderFolderCreated(gitCaseVirtualPath, triggeringProcessImageFileName);
                    }
                    else
                    {
                        this.FileSystemCallbacks.OnPlaceholderFileCreated(gitCaseVirtualPath, sha, triggeringProcessImageFileName);
                    }
                }
            }
            catch (SizesUnavailableException e)
            {
                result = (HResult)HResultExtensions.HResultFromNtStatus.FileNotAvailable;

                EventMetadata metadata = this.CreateEventMetadata(virtualPath, e);
                metadata.Add("commandId", commandId);
                metadata.Add("triggeringProcessId", triggeringProcessId);
                metadata.Add("triggeringProcessImageFileName", triggeringProcessImageFileName);
                metadata.Add(nameof(result), result.ToString("X") + "(" + result.ToString("G") + ")");
                this.Context.Tracer.RelatedError(metadata, nameof(this.GetPlaceholderInformationAsyncHandler) + ": caught SizesUnavailableException");
            }
            catch (Win32Exception e)
            {
                result = HResultExtensions.HResultFromWin32(e.NativeErrorCode);

                EventMetadata metadata = this.CreateEventMetadata(virtualPath, e);
                metadata.Add("commandId", commandId);
                metadata.Add("triggeringProcessId", triggeringProcessId);
                metadata.Add("triggeringProcessImageFileName", triggeringProcessImageFileName);
                metadata.Add(nameof(result), result.ToString("X") + "(" + result.ToString("G") + ")");
                metadata.Add("NativeErrorCode", e.NativeErrorCode.ToString("X") + "(" + e.NativeErrorCode.ToString("G") + ")");
                this.Context.Tracer.RelatedWarning(metadata, nameof(this.GetPlaceholderInformationAsyncHandler) + ": caught Win32Exception");
            }
            catch (Exception e)
            {
                EventMetadata metadata = this.CreateEventMetadata(virtualPath, e);
                metadata.Add("commandId", commandId);
                metadata.Add("triggeringProcessId", triggeringProcessId);
                metadata.Add("triggeringProcessImageFileName", triggeringProcessImageFileName);
                this.LogUnhandledExceptionAndExit(nameof(this.GetPlaceholderInformationAsyncHandler), metadata);
            }

            this.TryCompleteCommand(commandId, result);
        }

        private void GetFileStreamHandlerAsyncHandler(
            CancellationToken cancellationToken,
            int commandId,
            uint length,
            Guid streamGuid,
            string sha,
            EventMetadata requestMetadata,
            string triggeringProcessImageFileName)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return;
            }

            try
            {
                if (!this.GitObjects.TryCopyBlobContentStream(
                    sha,
                    cancellationToken,
                    GVFSGitObjects.RequestSource.FileStreamCallback,
                    (stream, blobLength) =>
                    {
                        if (blobLength != length)
                        {
                            requestMetadata.Add("blobLength", blobLength);
                            this.Context.Tracer.RelatedError(requestMetadata, $"{nameof(this.GetFileStreamHandlerAsyncHandler)}: Actual file length (blobLength) does not match requested length");

                            throw new GetFileStreamException(HResult.InternalError);
                        }

                        byte[] buffer = new byte[Math.Min(MaxBlobStreamBufferSize, blobLength)];
                        long remainingData = blobLength;

                        using (IWriteBuffer targetBuffer = this.virtualizationInstance.CreateWriteBuffer((uint)buffer.Length))
                        {
                            while (remainingData > 0)
                            {
                                cancellationToken.ThrowIfCancellationRequested();

                                uint bytesToCopy = (uint)Math.Min(remainingData, targetBuffer.Length);

                                try
                                {
                                    targetBuffer.Stream.Seek(0, SeekOrigin.Begin);
                                    StreamCopyBlockTo(stream, targetBuffer.Stream, bytesToCopy, buffer);
                                }
                                catch (IOException e)
                                {
                                    requestMetadata.Add("Exception", e.ToString());
                                    this.Context.Tracer.RelatedError(requestMetadata, "IOException while copying to unmanaged buffer.");

                                    throw new GetFileStreamException("IOException while copying to unmanaged buffer: " + e.Message, (HResult)HResultExtensions.HResultFromNtStatus.FileNotAvailable);
                                }

                                long writeOffset = length - remainingData;

                                HResult writeResult = this.virtualizationInstance.WriteFileData(streamGuid, targetBuffer, (ulong)writeOffset, bytesToCopy);
                                remainingData -= bytesToCopy;

                                if (writeResult != HResult.Ok)
                                {
                                    switch (writeResult)
                                    {
                                        case HResult.Handle:
                                            // HResult.Handle is expected, and occurs when an application closes a file handle before OnGetFileStream
                                            // is complete
                                            break;

                                        default:
                                            {
                                                this.Context.Tracer.RelatedError(requestMetadata, $"{nameof(this.virtualizationInstance.WriteFileData)} failed, error: " + writeResult.ToString("X") + "(" + writeResult.ToString("G") + ")");
                                            }

                                            break;
                                    }

                                    throw new GetFileStreamException(writeResult);
                                }
                            }
                        }
                    }))
                {
                    this.Context.Tracer.RelatedError(requestMetadata, $"{nameof(this.GetFileStreamHandlerAsyncHandler)}: TryCopyBlobContentStream failed");

                    this.TryCompleteCommand(commandId, (HResult)HResultExtensions.HResultFromNtStatus.FileNotAvailable);
                    return;
                }
            }
            catch (OperationCanceledException)
            {
                requestMetadata.Add(TracingConstants.MessageKey.InfoMessage, $"{nameof(this.GetFileStreamHandlerAsyncHandler)}: Operation cancelled");
                this.Context.Tracer.RelatedEvent(
                    EventLevel.Informational,
                    nameof(this.GetFileStreamHandlerAsyncHandler) + "_OperationCancelled",
                    requestMetadata);

                return;
            }
            catch (GetFileStreamException e)
            {
                this.TryCompleteCommand(commandId, (HResult)e.HResult);
                return;
            }
            catch (Exception e)
            {
                requestMetadata.Add("Exception", e.ToString());
                this.Context.Tracer.RelatedError(requestMetadata, $"{nameof(this.GetFileStreamHandlerAsyncHandler)}: TryCopyBlobContentStream failed");

                this.TryCompleteCommand(commandId, (HResult)HResultExtensions.HResultFromNtStatus.FileNotAvailable);
                return;
            }

            this.FileSystemCallbacks.OnPlaceholderFileHydrated(triggeringProcessImageFileName);
            this.TryCompleteCommand(commandId, HResult.Ok);
        }

        private void NotifyNewFileCreatedHandler(
            string virtualPath,
            bool isDirectory,
            uint triggeringProcessId,
            string triggeringProcessImageFileName,
            out NotificationType notificationMask)
        {
            notificationMask = NotificationType.UseExistingMask;
            try
            {
                if (!FileSystemCallbacks.IsPathInsideDotGit(virtualPath))
                {
                    if (isDirectory)
                    {
                        GitCommandLineParser gitCommand = new GitCommandLineParser(this.Context.Repository.GVFSLock.GetLockedGitCommand());
                        if (gitCommand.IsValidGitCommand)
                        {
                            this.MarkDirectoryAsPlaceholder(virtualPath, triggeringProcessId, triggeringProcessImageFileName);
                        }
                        else
                        {
                            this.FileSystemCallbacks.OnFolderCreated(virtualPath, out bool sparseFoldersUpdated);
                            if (sparseFoldersUpdated)
                            {
                                // When sparseFoldersUpdated is true it means the folder was previously excluded from the projection and was
                                // included so it needs to be marked as a placeholder so that it will start projecting items in the folder
                                this.MarkDirectoryAsPlaceholder(virtualPath, triggeringProcessId, triggeringProcessImageFileName);
                            }
                        }
                    }
                    else
                    {
                        this.FileSystemCallbacks.OnFileCreated(virtualPath);
                    }
                }
            }
            catch (Exception e)
            {
                EventMetadata metadata = this.CreateEventMetadata(virtualPath, e);
                metadata.Add("isDirectory", isDirectory);
                metadata.Add("triggeringProcessId", triggeringProcessId);
                metadata.Add("triggeringProcessImageFileName", triggeringProcessImageFileName);
                this.LogUnhandledExceptionAndExit(nameof(this.NotifyNewFileCreatedHandler), metadata);
            }
        }

        private void MarkDirectoryAsPlaceholder(
            string virtualPath,
            uint triggeringProcessId,
            string triggeringProcessImageFileName)
        {
            string directoryPath = Path.Combine(this.Context.Enlistment.WorkingDirectoryRoot, virtualPath);
            HResult hr = this.virtualizationInstance.MarkDirectoryAsPlaceholder(
                directoryPath,
                FolderContentId,
                PlaceholderVersionId);

            if (hr == HResult.Ok)
            {
                this.FileSystemCallbacks.OnPlaceholderFolderCreated(virtualPath, triggeringProcessImageFileName);
            }
            else
            {
                EventMetadata metadata = this.CreateEventMetadata(virtualPath);
                metadata.Add("isDirectory", true);
                metadata.Add("triggeringProcessId", triggeringProcessId);
                metadata.Add("triggeringProcessImageFileName", triggeringProcessImageFileName);
                metadata.Add("HResult", hr.ToString());
                this.Context.Tracer.RelatedError(metadata, nameof(this.MarkDirectoryAsPlaceholder) + " error");
            }
        }

        private void NotifyFileOverwrittenHandler(
            string virtualPath,
            bool isDirectory,
            uint triggeringProcessId,
            string triggeringProcessImageFileName,
            out NotificationType notificationMask)
        {
            notificationMask = NotificationType.UseExistingMask;
            try
            {
                if (!FileSystemCallbacks.IsPathInsideDotGit(virtualPath)
                    && !isDirectory)
                {
                    this.FileSystemCallbacks.OnFileOverwritten(virtualPath);
                }
            }
            catch (Exception e)
            {
                EventMetadata metadata = this.CreateEventMetadata(virtualPath, e);
                metadata.Add("isDirectory", isDirectory);
                metadata.Add("triggeringProcessId", triggeringProcessId);
                metadata.Add("triggeringProcessImageFileName", triggeringProcessImageFileName);
                this.LogUnhandledExceptionAndExit(nameof(this.NotifyFileOverwrittenHandler), metadata);
            }
        }

        private bool NotifyPreRenameHandler(string relativePath, string destinationPath, uint triggeringProcessId, string triggeringProcessImageFileName)
        {
            try
            {
                if (destinationPath.Equals(GVFSConstants.DotGit.Index, GVFSPlatform.Instance.Constants.PathComparison))
                {
                    string lockedGitCommand = this.Context.Repository.GVFSLock.GetLockedGitCommand();
                    if (string.IsNullOrEmpty(lockedGitCommand))
                    {
                        EventMetadata metadata = this.CreateEventMetadata(relativePath);
                        metadata.Add(TracingConstants.MessageKey.WarningMessage, "Blocked index rename outside the lock");
                        this.Context.Tracer.RelatedEvent(EventLevel.Warning, $"{nameof(this.NotifyPreRenameHandler)}_BlockedIndexRename", metadata);

                        return false;
                    }
                }
            }
            catch (Exception e)
            {
                EventMetadata metadata = this.CreateEventMetadata(relativePath, e);
                metadata.Add("destinationPath", destinationPath);
                this.LogUnhandledExceptionAndExit(nameof(this.NotifyPreRenameHandler), metadata);
            }

            return true;
        }

        private bool NotifyPreDeleteHandler(string virtualPath, bool isDirectory, uint triggeringProcessId, string triggeringProcessImageFileName)
        {
            // Only the path to the index should be registered for this handler
            return false;
        }

        private void NotifyFileRenamedHandler(
            string virtualPath,
            string destinationPath,
            bool isDirectory,
            uint triggeringProcessId,
            string triggeringProcessImageFileName,
            out NotificationType notificationMask)
        {
            notificationMask = NotificationType.UseExistingMask;
            this.OnFileRenamed(virtualPath, destinationPath, isDirectory);
        }

        private void NotifyHardlinkCreated(
            string relativeExistingFilePath,
            string relativeNewLinkPath,
            uint triggeringProcessId,
            string triggeringProcessImageFileName)
        {
            this.OnHardLinkCreated(relativeExistingFilePath, relativeNewLinkPath);
        }

        private void NotifyFileHandleClosedFileModifiedOrDeletedHandler(
            string virtualPath,
            bool isDirectory,
            bool isFileModified,
            bool isFileDeleted,
            uint triggeringProcessId,
            string triggeringProcessImageFileName)
        {
            try
            {
                bool pathInsideDotGit = FileSystemCallbacks.IsPathInsideDotGit(virtualPath);

                if (isFileModified)
                {
                    if (pathInsideDotGit)
                    {
                        // TODO 876861: See if ProjFS can provide process ID\name in this callback
                        this.OnDotGitFileOrFolderChanged(virtualPath);
                    }
                    else
                    {
                        this.FileSystemCallbacks.InvalidateGitStatusCache();
                    }
                }
                else if (isFileDeleted)
                {
                    if (pathInsideDotGit)
                    {
                        this.OnDotGitFileOrFolderDeleted(virtualPath);
                    }
                    else
                    {
                        this.OnWorkingDirectoryFileOrFolderDeleteNotification(virtualPath, isDirectory, isPreDelete: false);
                    }
                }
            }
            catch (Exception e)
            {
                EventMetadata metadata = this.CreateEventMetadata(virtualPath, e);
                metadata.Add("isDirectory", isDirectory);
                metadata.Add("isFileModified", isFileModified);
                metadata.Add("isFileDeleted", isFileDeleted);
                this.LogUnhandledExceptionAndExit(nameof(this.NotifyFileHandleClosedFileModifiedOrDeletedHandler), metadata);
            }
        }

        private bool NotifyFilePreConvertToFullHandler(string relativePath, uint triggeringProcessId, string triggeringProcessImageFileName)
        {
            this.OnFilePreConvertToFull(relativePath);
            return true;
        }

        private void CancelCommandHandler(int commandId)
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
                                EventMetadata metadata = this.CreateEventMetadata(relativePath: null, exception: innerException);
                                metadata.Add("commandId", commandId);
                                this.Context.Tracer.RelatedError(metadata, $"{nameof(this.CancelCommandHandler)}: AggregateException while requesting cancellation");
                            }
                        }
                    }
                }
            }
            catch (Exception e)
            {
                EventMetadata metadata = this.CreateEventMetadata(relativePath: null, exception: e);
                metadata.Add("commandId", commandId);
                this.LogUnhandledExceptionAndExit(nameof(this.CancelCommandHandler), metadata);
            }
        }

        private class GetFileStreamException : Exception
        {
            public GetFileStreamException(HResult errorCode)
                : this("GetFileStreamException exception, error: " + errorCode.ToString(), errorCode)
            {
            }

            public GetFileStreamException(string message, HResult result)
                : base(message)
            {
                this.HResult = (int)result;
            }
        }

        private class Notifications
        {
            public const NotificationType IndexFile =
                NotificationType.PreRename |
                NotificationType.PreDelete |
                NotificationType.FileRenamed |
                NotificationType.HardlinkCreated |
                NotificationType.FileHandleClosedFileModified;

            public const NotificationType LogsHeadFile =
                NotificationType.FileRenamed |
                NotificationType.HardlinkCreated |
                NotificationType.FileHandleClosedFileModified;

            public const NotificationType ExcludeAndHeadFile =
                NotificationType.FileRenamed |
                NotificationType.HardlinkCreated |
                NotificationType.FileHandleClosedFileDeleted |
                NotificationType.FileHandleClosedFileModified;

            public const NotificationType FilesAndFoldersInRefsHeads =
                NotificationType.FileRenamed |
                NotificationType.HardlinkCreated |
                NotificationType.FileHandleClosedFileDeleted |
                NotificationType.FileHandleClosedFileModified;

            public const NotificationType FilesInWorkingFolder =
                NotificationType.NewFileCreated |
                NotificationType.FileOverwritten |
                NotificationType.FileRenamed |
                NotificationType.HardlinkCreated |
                NotificationType.FileHandleClosedFileDeleted |
                NotificationType.FilePreConvertToFull |
                NotificationType.FileHandleClosedFileModified;

            public const NotificationType FoldersInWorkingFolder =
                NotificationType.NewFileCreated |
                NotificationType.FileRenamed |
                NotificationType.FileHandleClosedFileDeleted;
        }
    }
}
