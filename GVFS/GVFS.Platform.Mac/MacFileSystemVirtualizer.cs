using GVFS.Common;
using GVFS.Common.Git;
using GVFS.Common.Tracing;
using GVFS.Virtualization.BlobSize;
using GVFS.Virtualization.FileSystem;
using GVFS.Virtualization.Projection;
using PrjFSLib.Mac;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;

namespace GVFS.Platform.Mac
{
    public class MacFileSystemVirtualizer : FileSystemVirtualizer
    {
        public static readonly byte[] PlaceholderVersionId = ToVersionIdByteArray(new byte[] { PlaceholderVersion });

        private const int SymLinkTargetBufferSize = 4096;

        private const string ClassName = nameof(MacFileSystemVirtualizer);

        private VirtualizationInstance virtualizationInstance;

        public MacFileSystemVirtualizer(GVFSContext context, GVFSGitObjects gitObjects)
            : this(context, gitObjects, virtualizationInstance: null)
        {
        }

        public MacFileSystemVirtualizer(
            GVFSContext context,
            GVFSGitObjects gitObjects,
            VirtualizationInstance virtualizationInstance)
            : base(context, gitObjects)
        {
            this.virtualizationInstance = virtualizationInstance ?? new VirtualizationInstance();
        }

        protected override string EtwArea => ClassName;

        public static FSResult ResultToFSResult(Result result)
        {
            switch (result)
            {
                case Result.Invalid:
                    return FSResult.IOError;

                case Result.Success:
                    return FSResult.Ok;

                case Result.EFileNotFound:
                case Result.EPathNotFound:
                    return FSResult.FileOrPathNotFound;

                default:
                    return FSResult.IOError;
            }
        }

        public override FileSystemResult ClearNegativePathCache(out uint totalEntryCount)
        {
            totalEntryCount = 0;
            return new FileSystemResult(FSResult.Ok, rawResult: unchecked((int)Result.Success));
        }

        public override FileSystemResult DeleteFile(string relativePath, UpdatePlaceholderType updateFlags, out UpdateFailureReason failureReason)
        {
            UpdateFailureCause failureCause;
            Result result = this.virtualizationInstance.DeleteFile(relativePath, (UpdateType)updateFlags, out failureCause);
            failureReason = (UpdateFailureReason)failureCause;
            return new FileSystemResult(ResultToFSResult(result), unchecked((int)result));
        }

        public override void Stop()
        {
            this.Context.Tracer.RelatedEvent(EventLevel.Informational, $"{nameof(this.Stop)}_StopRequested", metadata: null);
        }

        public override FileSystemResult WritePlaceholderFile(
            string relativePath,
            long endOfFile,
            string sha)
        {
            // TODO(Mac): Add functional tests that validate file mode is set correctly
            GitIndexProjection.FileType fileType;
            ushort fileMode;
            this.FileSystemCallbacks.GitIndexProjection.GetFileTypeAndMode(relativePath, out fileType, out fileMode);

            if (fileType == GitIndexProjection.FileType.Regular)
            {
                Result result = this.virtualizationInstance.WritePlaceholderFile(
                    relativePath,
                    PlaceholderVersionId,
                    ToVersionIdByteArray(FileSystemVirtualizer.ConvertShaToContentId(sha)),
                    (ulong)endOfFile,
                    fileMode);

                return new FileSystemResult(ResultToFSResult(result), unchecked((int)result));
            }
            else if (fileType == GitIndexProjection.FileType.SymLink)
            {
                string symLinkTarget;
                if (this.TryGetSymLinkTarget(sha, out symLinkTarget))
                {
                    Result result = this.virtualizationInstance.WriteSymLink(relativePath, symLinkTarget);

                    this.FileSystemCallbacks.OnFileSymLinkCreated(relativePath);

                    return new FileSystemResult(ResultToFSResult(result), unchecked((int)result));
                }

                EventMetadata metadata = this.CreateEventMetadata(relativePath);
                metadata.Add(nameof(sha), sha);
                this.Context.Tracer.RelatedError(metadata, $"{nameof(this.WritePlaceholderFile)}: Failed to read contents of symlink object");
                return new FileSystemResult(FSResult.IOError, 0);
            }
            else
            {
                EventMetadata metadata = this.CreateEventMetadata(relativePath);
                metadata.Add(nameof(fileType), fileType);
                metadata.Add(nameof(fileMode), fileMode);
                this.Context.Tracer.RelatedError(metadata, $"{nameof(this.WritePlaceholderFile)}: Unsupported fileType");
                return new FileSystemResult(FSResult.IOError, 0);
            }
        }

        public override FileSystemResult WritePlaceholderDirectory(string relativePath)
        {
            Result result = this.virtualizationInstance.WritePlaceholderDirectory(relativePath);
            return new FileSystemResult(ResultToFSResult(result), unchecked((int)result));
        }

        public override FileSystemResult UpdatePlaceholderIfNeeded(
            string relativePath,
            DateTime creationTime,
            DateTime lastAccessTime,
            DateTime lastWriteTime,
            DateTime changeTime,
            uint fileAttributes,
            long endOfFile,
            string shaContentId,
            UpdatePlaceholderType updateFlags,
            out UpdateFailureReason failureReason)
        {
            UpdateFailureCause failureCause = UpdateFailureCause.NoFailure;

            // TODO(Mac): Add functional tests that include:
            //     - Mode + content changes between commits
            //     - Mode only changes (without any change to content, see issue #223)
            GitIndexProjection.FileType fileType;
            ushort fileMode;
            this.FileSystemCallbacks.GitIndexProjection.GetFileTypeAndMode(relativePath, out fileType, out fileMode);

            if (fileType == GitIndexProjection.FileType.Regular)
            {
                Result result = this.virtualizationInstance.UpdatePlaceholderIfNeeded(
                    relativePath,
                    PlaceholderVersionId,
                    ToVersionIdByteArray(ConvertShaToContentId(shaContentId)),
                    (ulong)endOfFile,
                    fileMode,
                    (UpdateType)updateFlags,
                    out failureCause);
                
                failureReason = (UpdateFailureReason)failureCause;
                return new FileSystemResult(ResultToFSResult(result), unchecked((int)result));
            }
            else if (fileType == GitIndexProjection.FileType.SymLink)
            {
                string symLinkTarget;
                if (this.TryGetSymLinkTarget(shaContentId, out symLinkTarget))
                {
                    Result result = this.virtualizationInstance.ReplacePlaceholderFileWithSymLink(
                        relativePath,
                        symLinkTarget,
                        (UpdateType)updateFlags,
                        out failureCause);

                    this.FileSystemCallbacks.OnFileSymLinkCreated(relativePath);

                    failureReason = (UpdateFailureReason)failureCause;
                    return new FileSystemResult(ResultToFSResult(result), unchecked((int)result));
                }

                EventMetadata metadata = this.CreateEventMetadata(relativePath);
                metadata.Add(nameof(shaContentId), shaContentId);
                this.Context.Tracer.RelatedError(metadata, $"{nameof(this.UpdatePlaceholderIfNeeded)}: Failed to read contents of symlink object");
                failureReason = UpdateFailureReason.NoFailure;
                return new FileSystemResult(FSResult.IOError, 0);
            }
            else
            {
                EventMetadata metadata = this.CreateEventMetadata(relativePath);
                metadata.Add(nameof(fileType), fileType);
                metadata.Add(nameof(fileMode), fileMode);
                this.Context.Tracer.RelatedError(metadata, $"{nameof(this.UpdatePlaceholderIfNeeded)}: Unsupported fileType");
                failureReason = UpdateFailureReason.NoFailure;
                return new FileSystemResult(FSResult.IOError, 0);
            }
        }

        protected override bool TryStart(out string error)
        {
            error = string.Empty;

            // Callbacks
            this.virtualizationInstance.OnEnumerateDirectory = this.OnEnumerateDirectory;
            this.virtualizationInstance.OnGetFileStream = this.OnGetFileStream;
            this.virtualizationInstance.OnFileModified = this.OnFileModified;
            this.virtualizationInstance.OnPreDelete = this.OnPreDelete;
            this.virtualizationInstance.OnNewFileCreated = this.OnNewFileCreated;
            this.virtualizationInstance.OnFileRenamed = this.OnFileRenamed;
            this.virtualizationInstance.OnHardLinkCreated = this.OnHardLinkCreated;

            uint threadCount = (uint)Environment.ProcessorCount * 2;

            Result result = this.virtualizationInstance.StartVirtualizationInstance(
                this.Context.Enlistment.WorkingDirectoryRoot,
                threadCount);

            if (result != Result.Success)
            {
                this.Context.Tracer.RelatedError($"{nameof(this.virtualizationInstance.StartVirtualizationInstance)} failed: " + result.ToString("X") + "(" + result.ToString("G") + ")");
                error = "Failed to start virtualization instance (" + result.ToString() + ")";
                return false;
            }

            this.Context.Tracer.RelatedEvent(EventLevel.Informational, $"{nameof(this.TryStart)}_StartedVirtualization", metadata: null);
            return true;
        }

        private static byte[] ToVersionIdByteArray(byte[] version)
        {
            byte[] bytes = new byte[VirtualizationInstance.PlaceholderIdLength];
            Buffer.BlockCopy(version, 0, bytes, 0, version.Length);
            return bytes;
        }

        /// <summary>
        /// Gets the target of the symbolic link. 
        /// </summary>
        /// <param name="sha">SHA of the loose object containing the target path of the symbolic link</param>
        /// <param name="symLinkTarget">Target path of the symbolic link</param>
        private bool TryGetSymLinkTarget(string sha, out string symLinkTarget)
        {
            symLinkTarget = null;

            string symLinkBlobContents = null;
            try
            {
                if (!this.GitObjects.TryCopyBlobContentStream(
                    sha,
                    CancellationToken.None,
                    GVFSGitObjects.RequestSource.SymLinkCreation,
                    (stream, blobLength) =>
                    {
                        byte[] buffer = new byte[SymLinkTargetBufferSize];
                        uint bufferIndex = 0;

                        // TODO(Mac): Find a better solution than reading from the stream one byte at at time
                        int nextByte = stream.ReadByte();
                        while (nextByte != -1)
                        {
                            while (bufferIndex < buffer.Length && nextByte != -1)
                            {
                                buffer[bufferIndex] = (byte)nextByte;
                                nextByte = stream.ReadByte();
                                ++bufferIndex;
                            }

                            if (bufferIndex < buffer.Length)
                            {
                                buffer[bufferIndex] = 0;
                                symLinkBlobContents = Encoding.UTF8.GetString(buffer);
                            }
                            else
                            {
                                buffer[bufferIndex - 1] = 0;

                                EventMetadata metadata = this.CreateEventMetadata();
                                metadata.Add(nameof(sha), sha);
                                metadata.Add("bufferContents", Encoding.UTF8.GetString(buffer));
                                this.Context.Tracer.RelatedError(metadata, $"{nameof(this.TryGetSymLinkTarget)}: SymLink target exceeds buffer size");

                                throw new GetSymLinkTargetException("SymLink target exceeds buffer size");;
                            }
                        }
                    }))
                {
                    EventMetadata metadata = this.CreateEventMetadata();
                    metadata.Add(nameof(sha), sha);
                    this.Context.Tracer.RelatedError(metadata, $"{nameof(this.TryGetSymLinkTarget)}: TryCopyBlobContentStream failed");

                    return false;
                }
            }
            catch (GetSymLinkTargetException e)
            {
                EventMetadata metadata = this.CreateEventMetadata(relativePath: null, exception: e);
                metadata.Add(nameof(sha), sha);
                this.Context.Tracer.RelatedError(metadata, $"{nameof(this.TryGetSymLinkTarget)}: TryCopyBlobContentStream caught GetSymLinkTargetException");

                return false;
            }
            catch (DecoderFallbackException e)
            {
                EventMetadata metadata = this.CreateEventMetadata(relativePath: null, exception: e);
                metadata.Add(nameof(sha), sha);
                this.Context.Tracer.RelatedError(metadata, $"{nameof(this.TryGetSymLinkTarget)}: TryCopyBlobContentStream caught DecoderFallbackException");

                return false;
            }

            symLinkTarget = symLinkBlobContents;

            return true;
        }

        private Result OnGetFileStream(
            ulong commandId,
            string relativePath,
            byte[] providerId,
            byte[] contentId,
            int triggeringProcessId,
            string triggeringProcessName,
            IntPtr fileHandle)
        {
            try
            {
                if (contentId == null)
                {
                    this.Context.Tracer.RelatedError($"{nameof(this.OnGetFileStream)} called with null contentId, path: " + relativePath);
                    return Result.EInvalidOperation;
                }

                if (providerId == null)
                {
                    this.Context.Tracer.RelatedError($"{nameof(this.OnGetFileStream)} called with null epochId, path: " + relativePath);
                    return Result.EInvalidOperation;
                }

                string sha = GetShaFromContentId(contentId);
                byte placeholderVersion = GetPlaceholderVersionFromProviderId(providerId);

                EventMetadata metadata = this.CreateEventMetadata(relativePath);
                metadata.Add(nameof(triggeringProcessId), triggeringProcessId);
                metadata.Add(nameof(triggeringProcessName), triggeringProcessName);
                metadata.Add(nameof(sha), sha);
                metadata.Add(nameof(placeholderVersion), placeholderVersion);
                metadata.Add(nameof(commandId), commandId);
                ITracer activity = this.Context.Tracer.StartActivity("GetFileStream", EventLevel.Verbose, Keywords.Telemetry, metadata);

                if (!this.FileSystemCallbacks.IsMounted)
                {
                    metadata.Add(TracingConstants.MessageKey.InfoMessage, $"{nameof(this.OnGetFileStream)} failed, mount has not yet completed");
                    activity.RelatedEvent(EventLevel.Informational, $"{nameof(this.OnGetFileStream)}_MountNotComplete", metadata);
                    activity.Dispose();

                    // TODO(Mac): Is this the correct Result to return?
                    return Result.EIOError;
                }

                if (placeholderVersion != FileSystemVirtualizer.PlaceholderVersion)
                {
                    activity.RelatedError(metadata, nameof(this.OnGetFileStream) + ": Unexpected placeholder version");
                    activity.Dispose();

                    // TODO(Mac): Is this the correct Result to return?
                    return Result.EIOError;
                }

                try
                {
                    if (!this.GitObjects.TryCopyBlobContentStream(
                        sha,
                        CancellationToken.None,
                        GVFSGitObjects.RequestSource.FileStreamCallback,
                        (stream, blobLength) =>
                        {
                            // TODO(Mac): Find a better solution than reading from the stream one byte at at time
                            byte[] buffer = new byte[4096];
                            uint bufferIndex = 0;
                            int nextByte = stream.ReadByte();
                            while (nextByte != -1)
                            {
                                while (bufferIndex < buffer.Length && nextByte != -1)
                                {
                                    buffer[bufferIndex] = (byte)nextByte;
                                    nextByte = stream.ReadByte();
                                    ++bufferIndex;
                                }

                                Result result = this.virtualizationInstance.WriteFileContents(
                                    fileHandle,
                                    buffer,
                                    bufferIndex);
                                if (result != Result.Success)
                                {
                                    activity.RelatedError(metadata, $"{nameof(this.virtualizationInstance.WriteFileContents)} failed, error: " + result.ToString("X") + "(" + result.ToString("G") + ")");
                                    throw new GetFileStreamException(result);
                                }

                                if (bufferIndex == buffer.Length)
                                {
                                    bufferIndex = 0;
                                }
                            }
                        }))
                    {
                        activity.RelatedError(metadata, $"{nameof(this.OnGetFileStream)}: TryCopyBlobContentStream failed");

                        // TODO(Mac): Is this the correct Result to return?
                        return Result.EFileNotFound;
                    }
                }
                catch (GetFileStreamException e)
                {
                    return e.Result;
                }

                return Result.Success;
            }
            catch (Exception e)
            {
                EventMetadata metadata = this.CreateEventMetadata(relativePath, e);
                metadata.Add(nameof(triggeringProcessId), triggeringProcessId);
                metadata.Add(nameof(triggeringProcessName), triggeringProcessName);
                metadata.Add(nameof(commandId), commandId);
                this.LogUnhandledExceptionAndExit(nameof(this.OnGetFileStream), metadata);
            }

            return Result.EIOError;
        }

        private void OnFileModified(string relativePath)
        {
            try
            {
                if (!this.FileSystemCallbacks.IsMounted)
                {
                    EventMetadata metadata = this.CreateEventMetadata(relativePath);
                    metadata.Add(TracingConstants.MessageKey.InfoMessage, nameof(this.OnFileModified) + ": Mount has not yet completed");
                    this.Context.Tracer.RelatedEvent(EventLevel.Informational, $"{nameof(this.OnFileModified)}_MountNotComplete", metadata);
                    return;
                }

                if (Virtualization.FileSystemCallbacks.IsPathInsideDotGit(relativePath))
                {
                    this.OnDotGitFileOrFolderChanged(relativePath);
                }
                else
                {
                    // TODO(Mac): As a temporary work around (until we have a ConvertToFull type notification) treat every modification
                    // as the first write to the file
                    bool isFolder;
                    string fileName;
                    bool isPathProjected = this.FileSystemCallbacks.GitIndexProjection.IsPathProjected(relativePath, out fileName, out isFolder);
                    if (isPathProjected)
                    {                        
                        this.FileSystemCallbacks.OnFileConvertedToFull(relativePath);
                    }
                }
            }
            catch (Exception e)
            {
                this.LogUnhandledExceptionAndExit(nameof(this.OnFileModified), this.CreateEventMetadata(relativePath, e));
            }
        }

        private Result OnPreDelete(string relativePath, bool isDirectory)
        {
            try
            {
                if (!this.FileSystemCallbacks.IsMounted)
                {
                    EventMetadata metadata = this.CreateEventMetadata(relativePath);
                    metadata.Add(nameof(isDirectory), isDirectory);
                    metadata.Add(TracingConstants.MessageKey.InfoMessage, $"{nameof(this.OnPreDelete)} failed, mount has not yet completed");
                    this.Context.Tracer.RelatedEvent(EventLevel.Informational, $"{nameof(this.OnPreDelete)}_MountNotComplete", metadata);

                    // TODO(Mac): Is this the correct Result to return?
                    return Result.EIOError;
                }

                bool pathInsideDotGit = Virtualization.FileSystemCallbacks.IsPathInsideDotGit(relativePath);
                if (pathInsideDotGit)
                {
                    if (relativePath.Equals(GVFSConstants.DotGit.Index, StringComparison.OrdinalIgnoreCase))
                    {
                        string lockedGitCommand = this.Context.Repository.GVFSLock.GetLockedGitCommand();
                        if (string.IsNullOrEmpty(lockedGitCommand))
                        {
                            EventMetadata metadata = new EventMetadata();
                            metadata.Add("Area", this.EtwArea);
                            metadata.Add(TracingConstants.MessageKey.WarningMessage, "Blocked index delete outside the lock");
                            this.Context.Tracer.RelatedEvent(EventLevel.Warning, $"{nameof(OnPreDelete)}_BlockedIndexDelete", metadata);

                            return Result.EAccessDenied;
                        }
                    }

                    this.OnDotGitFileOrFolderDeleted(relativePath);
                }
                else
                {
                    this.OnWorkingDirectoryFileOrFolderDeleteNotification(relativePath, isDirectory, isPreDelete: true);
                }
            }
            catch (Exception e)
            {
                EventMetadata metadata = this.CreateEventMetadata(relativePath, e);
                metadata.Add("isDirectory", isDirectory);
                this.LogUnhandledExceptionAndExit(nameof(this.OnPreDelete), metadata);
            }

            return Result.Success;
        }

        private void OnNewFileCreated(string relativePath, bool isDirectory)
        {
            try
            {
                if (!Virtualization.FileSystemCallbacks.IsPathInsideDotGit(relativePath))
                {
                    if (isDirectory)
                    {
                        GitCommandLineParser gitCommand = new GitCommandLineParser(this.Context.Repository.GVFSLock.GetLockedGitCommand());
                        if (gitCommand.IsValidGitCommand)
                        {
                            // TODO(Mac): Ensure that when git creates a folder all files\folders within that folder are written to disk
                            EventMetadata metadata = this.CreateEventMetadata(relativePath);
                            metadata.Add("isDirectory", isDirectory);
                            this.Context.Tracer.RelatedWarning(metadata, $"{nameof(this.OnNewFileCreated)}: Git created a folder, currently an unsupported scenario on Mac");
                        }
                        else
                        {
                            this.FileSystemCallbacks.OnFolderCreated(relativePath);
                        }
                    }
                    else
                    {
                        this.FileSystemCallbacks.OnFileCreated(relativePath);
                    }
                }
            }
            catch (Exception e)
            {
                EventMetadata metadata = this.CreateEventMetadata(relativePath, e);
                metadata.Add("isDirectory", isDirectory);
                this.LogUnhandledExceptionAndExit(nameof(this.OnNewFileCreated), metadata);
            }
        }
        
        private void OnFileRenamed(string relativeDestinationPath, bool isDirectory)
        {
            // ProjFS for Mac *could* be updated to provide us with relativeSourcePath as well,
            // but because VFSForGit doesn't need the source path on Mac for correct behavior
            // the relativeSourcePath is left out of the notification to keep the kext simple
            this.OnFileRenamed(
                relativeSourcePath: string.Empty, 
                relativeDestinationPath: relativeDestinationPath, 
                isDirectory: isDirectory);
        }

        private void OnHardLinkCreated(string relativeNewLinkPath)
        {
            this.OnHardLinkCreated(
                relativeExistingFilePath: string.Empty, 
                relativeNewLinkPath: relativeNewLinkPath);
        }

        private Result OnEnumerateDirectory(
            ulong commandId,
            string relativePath,
            int triggeringProcessId,
            string triggeringProcessName)
        {
            try
            {
                if (!this.FileSystemCallbacks.IsMounted)
                {
                    EventMetadata metadata = this.CreateEventMetadata(relativePath);
                    metadata.Add(TracingConstants.MessageKey.InfoMessage, nameof(this.OnEnumerateDirectory) + ": Failed enumeration, mount has not yet completed");
                    this.Context.Tracer.RelatedEvent(EventLevel.Informational, $"{nameof(this.OnEnumerateDirectory)}_MountNotComplete", metadata);

                    // TODO: Is this the correct Result to return?
                    return Result.EIOError;
                }

                Result result;
                try
                {
                    IEnumerable<ProjectedFileInfo> projectedItems;

                    // TODO: Pool these connections or schedule this work to run asynchronously using TryScheduleFileOrNetworkRequest
                    using (BlobSizes.BlobSizesConnection blobSizesConnection = this.FileSystemCallbacks.BlobSizes.CreateConnection())
                    {
                        projectedItems = this.FileSystemCallbacks.GitIndexProjection.GetProjectedItems(CancellationToken.None, blobSizesConnection, relativePath);
                    }

                    result = this.CreatePlaceholders(relativePath, projectedItems, triggeringProcessName);
                }
                catch (SizesUnavailableException e)
                {
                    // TODO: Is this the correct Result to return?
                    result = Result.EIOError;

                    EventMetadata metadata = this.CreateEventMetadata(relativePath, e);
                    metadata.Add("commandId", commandId);
                    metadata.Add(nameof(result), result.ToString("X") + "(" + result.ToString("G") + ")");
                    this.Context.Tracer.RelatedError(metadata, nameof(this.OnEnumerateDirectory) + ": caught SizesUnavailableException");
                }

                return result;
            }
            catch (Exception e)
            {
                EventMetadata metadata = this.CreateEventMetadata(relativePath, e);
                metadata.Add("commandId", commandId);
                this.LogUnhandledExceptionAndExit(nameof(this.OnEnumerateDirectory), metadata);
            }

            return Result.EIOError;
        }

        private Result CreatePlaceholders(string directoryRelativePath, IEnumerable<ProjectedFileInfo> projectedItems, string triggeringProcessName)
        {
            foreach (ProjectedFileInfo fileInfo in projectedItems)
            {
                string childRelativePath = Path.Combine(directoryRelativePath, fileInfo.Name);

                string sha;
                FileSystemResult fileSystemResult;
                if (fileInfo.IsFolder)
                {
                    sha = string.Empty;
                    fileSystemResult = this.WritePlaceholderDirectory(childRelativePath);
                }
                else
                {
                    sha = fileInfo.Sha.ToString();
                    fileSystemResult = this.WritePlaceholderFile(childRelativePath, fileInfo.Size, sha);
                }

                Result result = (Result)fileSystemResult.RawResult;
                if (result != Result.Success)
                {
                    EventMetadata metadata = this.CreateEventMetadata(childRelativePath);
                    metadata.Add("fileInfo.Name", fileInfo.Name);
                    metadata.Add("fileInfo.Size", fileInfo.Size);
                    metadata.Add("fileInfo.IsFolder", fileInfo.IsFolder);
                    metadata.Add(nameof(sha), sha);
                    this.Context.Tracer.RelatedError(metadata, $"{nameof(this.CreatePlaceholders)}: Write placeholder failed");

                    return result;
                }
                else
                {
                    if (fileInfo.IsFolder)
                    {
                        this.FileSystemCallbacks.OnPlaceholderFolderCreated(childRelativePath);
                    }
                    else
                    {
                        this.FileSystemCallbacks.OnPlaceholderFileCreated(childRelativePath, sha, triggeringProcessName);
                    }
                }
            }

            this.FileSystemCallbacks.OnPlaceholderFolderExpanded(directoryRelativePath);

            return Result.Success;
        }

        private class GetFileStreamException : Exception
        {
            public GetFileStreamException(Result errorCode)
                : this("GetFileStreamException exception, error: " + errorCode.ToString(), errorCode)
            {
            }

            public GetFileStreamException(string message, Result result)
                : base(message)
            {
                this.Result = result;
            }

            public Result Result { get; }
        }

        private class GetSymLinkTargetException : Exception
        {
            public GetSymLinkTargetException(string message)
                : base(message)
            {
            }
        }
    }
}
