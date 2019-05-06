using GVFS.Common;
using GVFS.UnitTests.Windows.Windows.Mock;
using Microsoft.Windows.ProjFS;
using System;
using System.IO;
using System.Threading;

namespace GVFS.UnitTests.Windows.Mock
{
    public class MockVirtualizationInstance : IVirtualizationInstance, IDisposable
    {
        private AutoResetEvent commandCompleted;
        private AutoResetEvent placeholderCreated;
        private ManualResetEvent unblockCreateWriteBuffer;
        private ManualResetEvent waitForCreateWriteBuffer;

        private volatile HResult completionResult;
        private volatile HResult writeFileReturnResult;

        public MockVirtualizationInstance()
        {
            this.commandCompleted = new AutoResetEvent(false);
            this.placeholderCreated = new AutoResetEvent(false);
            this.CreatedPlaceholders = new ConcurrentHashSet<string>();

            this.unblockCreateWriteBuffer = new ManualResetEvent(true);
            this.waitForCreateWriteBuffer = new ManualResetEvent(true);

            this.WriteFileReturnResult = HResult.Ok;
        }

        public ConcurrentHashSet<string> CreatedPlaceholders { get; private set; }

        public CancelCommandCallback OnCancelCommand { get; set; }

        public IRequiredCallbacks requiredCallbacks { get; set; }
        public NotifyFileOpenedCallback OnNotifyFileOpened { get; set; }
        public NotifyNewFileCreatedCallback OnNotifyNewFileCreated { get; set; }
        public NotifyFileOverwrittenCallback OnNotifyFileOverwritten { get; set; }
        public NotifyFileHandleClosedNoModificationCallback OnNotifyFileHandleClosedNoModification { get; set; }
        public NotifyFileHandleClosedFileModifiedOrDeletedCallback OnNotifyFileHandleClosedFileModifiedOrDeleted { get; set; }
        public NotifyFilePreConvertToFullCallback OnNotifyFilePreConvertToFull { get; set; }
        public NotifyFileRenamedCallback OnNotifyFileRenamed { get; set; }
        public NotifyHardlinkCreatedCallback OnNotifyHardlinkCreated { get; set; }
        public NotifyPreDeleteCallback OnNotifyPreDelete { get; set; }
        public NotifyPreRenameCallback OnNotifyPreRename { get; set; }
        public NotifyPreCreateHardlinkCallback OnNotifyPreCreateHardlink { get; set; }
        public QueryFileNameCallback OnQueryFileName { get; set; }

        public HResult WriteFileReturnResult
        {
            get { return this.writeFileReturnResult; }
            set { this.writeFileReturnResult = value; }
        }

        public uint NegativePathCacheCount { get; set; }

        public HResult DeleteFileResult { get; set; }
        public UpdateFailureCause DeleteFileUpdateFailureCause { get; set; }

        public HResult UpdateFileIfNeededResult { get; set; }
        public UpdateFailureCause UpdateFileIfNeededFailureCase { get; set; }

        public HResult StartVirtualizing(IRequiredCallbacks requiredCallbacks)
        {
            this.requiredCallbacks = requiredCallbacks;
            return HResult.Ok;
        }

        public void StopVirtualizing()
        {
        }

        public HResult DetachDriver()
        {
            return HResult.Ok;
        }

        public HResult ClearNegativePathCache(out uint totalEntryNumber)
        {
            totalEntryNumber = this.NegativePathCacheCount;
            this.NegativePathCacheCount = 0;
            return HResult.Ok;
        }

        public HResult DeleteFile(string relativePath, UpdateType updateFlags, out UpdateFailureCause failureReason)
        {
            failureReason = this.DeleteFileUpdateFailureCause;
            return this.DeleteFileResult;
        }

        public HResult UpdateFileIfNeeded(string relativePath, DateTime creationTime, DateTime lastAccessTime, DateTime lastWriteTime, DateTime changeTime, FileAttributes fileAttributes, long endOfFile, byte[] contentId, byte[] providerId, UpdateType updateFlags, out UpdateFailureCause failureReason)
        {
            failureReason = this.UpdateFileIfNeededFailureCase;
            return this.UpdateFileIfNeededResult;
        }

        public HResult CreatePlaceholderAsHardlink(string destinationFileName, string hardLinkTarget)
        {
            throw new NotImplementedException();
        }

        public HResult MarkDirectoryAsPlaceholder(string targetDirectoryPath, byte[] contentId, byte[] providerId)
        {
            throw new NotImplementedException();
        }

        public HResult WritePlaceholderInfo(
            string relativePath,
            DateTime creationTime,
            DateTime lastAccessTime,
            DateTime lastWriteTime,
            DateTime changeTime,
            FileAttributes fileAttributes,
            long endOfFile,
            bool isDirectory,
            byte[] contentId,
            byte[] epochId)
        {
            this.CreatedPlaceholders.Add(relativePath);
            this.placeholderCreated.Set();
            return HResult.Ok;
        }

        public HResult WaitForCompletionStatus()
        {
            this.commandCompleted.WaitOne();
            return this.completionResult;
        }

        public void WaitForPlaceholderCreate()
        {
            this.placeholderCreated.WaitOne();
        }

        public void BlockCreateWriteBuffer(bool willWaitForRequest)
        {
            if (willWaitForRequest)
            {
                this.waitForCreateWriteBuffer.Reset();
            }

            this.unblockCreateWriteBuffer.Reset();
        }

        public void UnblockCreateWriteBuffer()
        {
            this.unblockCreateWriteBuffer.Set();
        }

        public void WaitForCreateWriteBuffer()
        {
            this.waitForCreateWriteBuffer.WaitOne();
        }

        public HResult CompleteCommand(int commandId, NotificationType newNotificationMask)
        {
            throw new NotImplementedException();
        }

        public HResult CompleteCommand(int commandId, IDirectoryEnumerationResults results)
        {
            throw new NotImplementedException();
        }

        public HResult CompleteCommand(int commandId, HResult completionResult)
        {
            this.completionResult = completionResult;
            this.commandCompleted.Set();
            return HResult.Ok;
        }

        public HResult CompleteCommand(int commandId)
        {
            throw new NotImplementedException();
        }

        public void Dispose()
        {
            this.Dispose(true);
            GC.SuppressFinalize(this);
        }

        public HResult WriteFileData(Guid dataStreamId, IWriteBuffer buffer, ulong byteOffset, uint length)
        {
            return this.WriteFileReturnResult;
        }

        public IWriteBuffer CreateWriteBuffer(ulong byteOffset, uint length, out ulong alignedByteOffset, out uint alignedLength)
        {
            throw new NotImplementedException();
        }

        public IWriteBuffer CreateWriteBuffer(uint desiredBufferSize)
        {
            this.waitForCreateWriteBuffer.Set();
            this.unblockCreateWriteBuffer.WaitOne();

            return new MockWriteBuffer(desiredBufferSize);
        }

        protected void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (this.commandCompleted != null)
                {
                    this.commandCompleted.Dispose();
                    this.commandCompleted = null;
                }

                if (this.placeholderCreated != null)
                {
                    this.placeholderCreated.Dispose();
                    this.placeholderCreated = null;
                }

                if (this.unblockCreateWriteBuffer != null)
                {
                    this.unblockCreateWriteBuffer.Dispose();
                    this.unblockCreateWriteBuffer = null;
                }

                if (this.waitForCreateWriteBuffer != null)
                {
                    this.waitForCreateWriteBuffer.Dispose();
                    this.waitForCreateWriteBuffer = null;
                }
            }
        }
    }
}
