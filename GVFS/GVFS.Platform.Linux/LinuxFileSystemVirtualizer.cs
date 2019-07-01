using GVFS.Common;
using GVFS.Common.Git;
using GVFS.Common.Tracing;
using GVFS.Virtualization.BlobSize;
using GVFS.Virtualization.FileSystem;
using GVFS.Virtualization.Projection;
using PrjFSLib.Linux;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;

namespace GVFS.Platform.Linux
{
    public class LinuxFileSystemVirtualizer : FileSystemVirtualizer
    {
        public static readonly byte[] PlaceholderVersionId = ToVersionIdByteArray(new byte[] { PlaceholderVersion });

        private const int SymLinkTargetBufferSize = 4096;

        private const string ClassName = nameof(LinuxFileSystemVirtualizer);

        private VirtualizationInstance virtualizationInstance;

        public LinuxFileSystemVirtualizer(GVFSContext context, GVFSGitObjects gitObjects)
            : this(context, gitObjects, virtualizationInstance: null)
        {
        }

        public LinuxFileSystemVirtualizer(
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

                case Result.EDirectoryNotEmpty:
                    return FSResult.DirectoryNotEmpty;

                case Result.EVirtualizationInvalidOperation:
                    return FSResult.VirtualizationInvalidOperation;

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
            this.virtualizationInstance.StopVirtualizationInstance();
            this.Context.Tracer.RelatedEvent(EventLevel.Informational, $"{nameof(this.Stop)}_StopRequested", metadata: null);
        }

        /// <summary>
        /// Writes a placeholder file.
        /// </summary>
        /// <param name="relativePath">Placeholder's path relative to the root of the repo</param>
        /// <param name="endOfFile">Length of the file (ignored on this platform)</param>
        /// <param name="sha">The SHA of the placeholder's contents, stored as the content ID in the placeholder</param>
        public override FileSystemResult WritePlaceholderFile(
            string relativePath,
            long endOfFile,
            string sha)
        {
            // TODO(#223): Add functional tests that validate file mode is set correctly
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
            FileAttributes fileAttributes,
            long endOfFile,
            string shaContentId,
            UpdatePlaceholderType updateFlags,
            out UpdateFailureReason failureReason)
        {
            UpdateFailureCause failureCause = UpdateFailureCause.NoFailure;

            // TODO(#223): Add functional tests that include:
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

        public override FileSystemResult DehydrateFolder(string relativePath)
        {
            FileSystemResult result = new FileSystemResult(FSResult.Ok, 0);
            GitIndexProjection.PathSparseState sparseState = this.FileSystemCallbacks.GitIndexProjection.GetFolderPathSparseState(relativePath);

            if (sparseState == GitIndexProjection.PathSparseState.Included)
            {
                // When the folder is included we need to create the placeholder to make sure it is on disk for enumeration
                result = this.WritePlaceholderDirectory(relativePath);
                if (result.Result == FSResult.Ok)
                {
                    this.FileSystemCallbacks.OnPlaceholderFolderCreated(relativePath, string.Empty);
                }
                else if (result.Result == FSResult.FileOrPathNotFound)
                {
                    // This will happen when the parent folder is also in the dehydrate list and is no longer on disk.
                    result = new FileSystemResult(FSResult.Ok, 0);
                }
                else
                {
                    EventMetadata metadata = this.CreateEventMetadata(relativePath);
                    metadata.Add(nameof(result.Result), result.Result);
                    metadata.Add(nameof(result.RawResult), result.RawResult);
                    this.Context.Tracer.RelatedError(metadata, $"{nameof(this.DehydrateFolder)}: Write placeholder failed");
                }
            }

            return result;
        }

        public override bool TryStart(out string error)
        {
            error = string.Empty;

            // Callbacks
            this.virtualizationInstance.OnEnumerateDirectory = this.OnEnumerateDirectory;
            this.virtualizationInstance.OnGetFileStream = this.OnGetFileStream;
            this.virtualizationInstance.OnLogError = this.OnLogError;
            this.virtualizationInstance.OnLogWarning = this.OnLogWarning;
            this.virtualizationInstance.OnLogInfo = this.OnLogInfo;
            this.virtualizationInstance.OnFileModified = this.OnFileModified;
            this.virtualizationInstance.OnPreDelete = this.OnPreDelete;
            this.virtualizationInstance.OnPreRename = this.OnPreRename;
            this.virtualizationInstance.OnNewFileCreated = this.OnNewFileCreated;
            this.virtualizationInstance.OnFileDeleted = this.OnFileDeleted;
            this.virtualizationInstance.OnFileRenamed = this.OnFileRenamed;
            this.virtualizationInstance.OnHardLinkCreated = this.OnHardLinkCreated;
            this.virtualizationInstance.OnFilePreConvertToFull = this.NotifyFilePreConvertToFull;

            uint threadCount = (uint)Environment.ProcessorCount * 2;

            Result result = this.virtualizationInstance.StartVirtualizationInstance(
                this.Context.Enlistment.WorkingDirectoryBackingRoot,
                this.Context.Enlistment.WorkingDirectoryRoot,
                threadCount,
                this.IsWorkingDirectoryEmpty(this.Context.Enlistment.WorkingDirectoryBackingRoot));

            // TODO(Linux): note that most start errors are not reported
            // because they can only be retrieved from projfs_stop() at present
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

        private bool IsWorkingDirectoryEmpty(string dir)
        {
            bool foundDotGit = false,
                 foundDotGitattributes = false;

            foreach (string path in this.virtualizationInstance.EnumerateFileSystemEntries(dir))
            {
                string file = Path.GetFileName(path);
                if (file == ".git")
                {
                    foundDotGit = true;
                }
                else if (file == ".gitattributes")
                {
                    foundDotGitattributes = true;
                }
                else
                {
                    return false;
                }
            }

            return foundDotGit && foundDotGitattributes;
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

                        // TODO(#1361): Find a better solution than reading from the stream one byte at at time
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

                                throw new GetSymLinkTargetException("SymLink target exceeds buffer size");
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
            int fd)
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

                if (placeholderVersion != FileSystemVirtualizer.PlaceholderVersion)
                {
                    activity.RelatedError(metadata, nameof(this.OnGetFileStream) + ": Unexpected placeholder version");
                    activity.Dispose();

                    // TODO(#1362): Is this the correct Result to return?
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
                            // TODO(#1361): Find a better solution than reading from the stream one byte at at time
                            byte[] buffer = new byte[4096];
                            uint bufferIndex = 0;
                            int nextByte = stream.ReadByte();
                            int bytesWritten = 0;
                            while (nextByte != -1)
                            {
                                while (bufferIndex < buffer.Length && nextByte != -1)
                                {
                                    buffer[bufferIndex] = (byte)nextByte;
                                    nextByte = stream.ReadByte();
                                    ++bufferIndex;
                                }

                                Result result = this.virtualizationInstance.WriteFileContents(
                                    fd,
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
                                    bytesWritten += buffer.Length;
                                }
                            }
                            bytesWritten += Convert.ToInt32(bufferIndex);

                            if (bytesWritten != blobLength)
                            {
                                // If the read size does not match the expected size print an error and add the file to ModifiedPaths.dat
                                // This allows the user to see that something went wrong with file hydration
                                // Unfortunately we must do this check *after* the file is hydrated since the header isn't corrupt for trunctated objects on Linux
                                this.Context.Tracer.RelatedError($"Read {relativePath} to {bytesWritten}, not expected size of {blobLength}");
                                this.FileSystemCallbacks.OnFailedFileHydration(relativePath);
                            }
                        }))
                    {
                        activity.RelatedError(metadata, $"{nameof(this.OnGetFileStream)}: TryCopyBlobContentStream failed");

                        // TODO(#1362): Is this the correct Result to return?
                        return Result.EFileNotFound;
                    }
                }
                catch (GetFileStreamException e)
                {
                    return e.Result;
                }

                this.FileSystemCallbacks.OnPlaceholderFileHydrated(triggeringProcessName);
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

        private void OnLogError(string errorMessage)
        {
            this.Context.Tracer.RelatedError($"{nameof(LinuxFileSystemVirtualizer)}::{nameof(this.OnLogError)}: {errorMessage}");
        }

        private void OnLogWarning(string warningMessage)
        {
            this.Context.Tracer.RelatedWarning($"{nameof(LinuxFileSystemVirtualizer)}::{nameof(this.OnLogWarning)}: {warningMessage}");
        }

        private void OnLogInfo(string infoMessage)
        {
            this.Context.Tracer.RelatedInfo($"{nameof(LinuxFileSystemVirtualizer)}::{nameof(this.OnLogInfo)}: {infoMessage}");
        }

        private void OnFileModified(string relativePath)
        {
            try
            {
                if (Virtualization.FileSystemCallbacks.IsPathInsideDotGit(relativePath))
                {
                    this.OnDotGitFileOrFolderChanged(relativePath);
                }
            }
            catch (Exception e)
            {
                this.LogUnhandledExceptionAndExit(nameof(this.OnFileModified), this.CreateEventMetadata(relativePath, e));
            }
        }

        private Result NotifyFilePreConvertToFull(string relativePath)
        {
            this.OnFilePreConvertToFull(relativePath);
            return Result.Success;
        }

        private Result OnPreDelete(string relativePath, bool isDirectory)
        {
            try
            {
                if (relativePath.Equals(GVFSConstants.DotGit.Index, GVFSPlatform.Instance.Constants.PathComparison))
                {
                    string lockedGitCommand = this.Context.Repository.GVFSLock.GetLockedGitCommand();
                    if (string.IsNullOrEmpty(lockedGitCommand))
                    {
                        EventMetadata metadata = new EventMetadata();
                        metadata.Add("Area", this.EtwArea);
                        metadata.Add(TracingConstants.MessageKey.WarningMessage, "Blocked index delete outside the lock");
                        this.Context.Tracer.RelatedEvent(EventLevel.Warning, $"{nameof(this.OnPreDelete)}_BlockedIndexDelete", metadata);

                        return Result.EAccessDenied;
                    }
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

        private Result OnPreRename(string relativePath, string relativeDestinationPath, bool isDirectory)
        {
            try
            {
                if (relativePath.Equals(GVFSConstants.DotGit.Index, GVFSPlatform.Instance.Constants.PathComparison) ||
                    relativeDestinationPath.Equals(GVFSConstants.DotGit.Index, GVFSPlatform.Instance.Constants.PathComparison))
                {
                    string lockedGitCommand = this.Context.Repository.GVFSLock.GetLockedGitCommand();
                    if (string.IsNullOrEmpty(lockedGitCommand))
                    {
                        EventMetadata metadata = new EventMetadata();
                        metadata.Add("Area", this.EtwArea);
                        metadata.Add(TracingConstants.MessageKey.WarningMessage, "Blocked index rename outside the lock");
                        this.Context.Tracer.RelatedEvent(EventLevel.Warning, $"{nameof(this.OnPreRename)}_BlockedIndexDelete", metadata);

                        return Result.EAccessDenied;
                    }
                }
            }
            catch (Exception e)
            {
                EventMetadata metadata = this.CreateEventMetadata(relativePath, e);
                metadata.Add("destinationPath", relativeDestinationPath);
                metadata.Add("isDirectory", isDirectory);
                this.LogUnhandledExceptionAndExit(nameof(this.OnPreRename), metadata);
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
                        string lockedGitCommand = this.Context.Repository.GVFSLock.GetLockedGitCommand();
                        GitCommandLineParser gitCommand = new GitCommandLineParser(lockedGitCommand);
                        if (gitCommand.IsValidGitCommand)
                        {
                            EventMetadata metadata = this.CreateEventMetadata(relativePath);
                            metadata.Add(nameof(lockedGitCommand), lockedGitCommand);
                            metadata.Add(TracingConstants.MessageKey.InfoMessage, "Git command created new folder");
                            this.Context.Tracer.RelatedEvent(EventLevel.Informational, $"{nameof(this.OnNewFileCreated)}_GitCreatedFolder", metadata);

                            // Record this folder as expanded so that GitIndexProjection will re-expand the folder
                            // when the projection change completes.
                            //
                            // Git creates new folders when there are files that it needs to create.
                            // However, git will only create files that are in ModifiedPaths.dat.  There could
                            // be other files in the projection (that were not created by git) and so VFS must re-expand the
                            // newly created folder to ensure that all files are written to disk.
                            this.FileSystemCallbacks.OnPlaceholderFolderExpanded(relativePath);
                        }
                        else
                        {
                            this.FileSystemCallbacks.OnFolderCreated(relativePath, out bool sparseFoldersUpdated);
                            if (sparseFoldersUpdated)
                            {
                                // When sparseFoldersUpdated is true it means the folder was previously excluded from the projection and was
                                // included so it needs to enumerate the directory to get and create placeholders
                                // for all the directory items that are now included
                                this.OnEnumerateDirectory(0, relativePath, -1, $"{nameof(this.OnNewFileCreated)}_FolderIncluded");
                            }
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

        private void OnFileDeleted(string relativePath, bool isDirectory)
        {
            try
            {
                bool pathInsideDotGit = Virtualization.FileSystemCallbacks.IsPathInsideDotGit(relativePath);
                if (pathInsideDotGit)
                {
                    this.OnDotGitFileOrFolderDeleted(relativePath);
                }
                else
                {
                    this.OnWorkingDirectoryFileOrFolderDeleteNotification(relativePath, isDirectory, isPreDelete: false);
                }
            }
            catch (Exception e)
            {
                EventMetadata metadata = this.CreateEventMetadata(relativePath, e);
                metadata.Add("isDirectory", isDirectory);
                this.LogUnhandledExceptionAndExit(nameof(this.OnFileDeleted), metadata);
            }
        }

        private Result OnEnumerateDirectory(
            ulong commandId,
            string relativePath,
            int triggeringProcessId,
            string triggeringProcessName)
        {
            try
            {
                Result result;
                try
                {
                    IEnumerable<ProjectedFileInfo> projectedItems;

                    // TODO(Linux): Pool these connections or schedule this work to run asynchronously using TryScheduleFileOrNetworkRequest
                    using (BlobSizes.BlobSizesConnection blobSizesConnection = this.FileSystemCallbacks.BlobSizes.CreateConnection())
                    {
                        projectedItems = this.FileSystemCallbacks.GitIndexProjection.GetProjectedItems(CancellationToken.None, blobSizesConnection, relativePath);
                    }

                    result = this.CreatePlaceholders(relativePath, projectedItems, triggeringProcessName);
                }
                catch (SizesUnavailableException e)
                {
                    // TODO(Linux): Is this the correct Result to return?
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
                        this.FileSystemCallbacks.OnPlaceholderFolderCreated(childRelativePath, triggeringProcessName);
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
