using GVFS.Common;
using ProjFS;
using System;
using System.Collections.Generic;
using System.Threading;

namespace GVFS.UnitTests.Windows.Mock
{
    public class MockVirtualizationInstance : IVirtualizationInstance, IDisposable
    {
        private AutoResetEvent commandCompleted;
        private AutoResetEvent placeholderCreated;
        private ManualResetEvent unblockCreateWriteBuffer;
        private ManualResetEvent waitForCreateWriteBuffer;

        public MockVirtualizationInstance()
        {
            this.commandCompleted = new AutoResetEvent(false);
            this.placeholderCreated = new AutoResetEvent(false);
            this.CreatedPlaceholders = new ConcurrentHashSet<string>();

            this.unblockCreateWriteBuffer = new ManualResetEvent(true);
            this.waitForCreateWriteBuffer = new ManualResetEvent(true);

            this.WriteFileReturnResult = HResult.Ok;
        }

        public HResult CompletionResult { get; set; }

        public ConcurrentHashSet<string> CreatedPlaceholders { get; private set; }

        public CancelCommandCallback OnCancelCommand { get; set; }
        public EndDirectoryEnumerationCallback OnEndDirectoryEnumeration { get; set; }
        public GetDirectoryEnumerationCallback OnGetDirectoryEnumeration { get; set; }
        public GetFileStreamCallback OnGetFileStream { get; set; }
        public GetPlaceholderInformationCallback OnGetPlaceholderInformation { get; set; }
        public NotifyFileOpenedCallback OnNotifyFileOpened { get; set; }
        public NotifyNewFileCreatedCallback OnNotifyNewFileCreated { get; set; }
        public NotifyFileSupersededOrOverwrittenCallback OnNotifyFileSupersededOrOverwritten { get; set; }
        public NotifyFileHandleClosedNoModificationCallback OnNotifyFileHandleClosedNoModification { get; set; }
        public NotifyFileHandleClosedFileModifiedOrDeletedCallback OnNotifyFileHandleClosedFileModifiedOrDeleted { get; set; }
        public NotifyFilePreConvertToFullCallback OnNotifyFilePreConvertToFull { get; set; }
        public NotifyFileRenamedCallback OnNotifyFileRenamed { get; set; }
        public NotifyHardlinkCreatedCallback OnNotifyHardlinkCreated { get; set; }
        public NotifyPreDeleteCallback OnNotifyPreDelete { get; set; }
        public NotifyPreRenameCallback OnNotifyPreRename { get; set; }
        public NotifyPreSetHardlinkCallback OnNotifyPreSetHardlink { get; set; }
        public QueryFileNameCallback OnQueryFileName { get; set; }
        public StartDirectoryEnumerationCallback OnStartDirectoryEnumeration { get; set; }

        public HResult WriteFileReturnResult { get; set; }

        public uint NegativePathCacheCount { get; set; }

        public HResult DeleteFileResult { get; set; }
        public UpdateFailureCause DeleteFileUpdateFailureCause { get; set; }

        public HResult UpdatePlaceholderIfNeededResult { get; set; }
        public UpdateFailureCause UpdatePlaceholderIfNeededFailureCause { get; set; }

        public HResult StartVirtualizationInstance(
            string virtualizationRootPath,
            uint poolThreadCount,
            uint concurrentThreadCount,
            bool enableNegativePathCache,
            IReadOnlyCollection<NotificationMapping> notificationMappings)
        {
            return HResult.Ok;
        }

        public HResult StopVirtualizationInstance()
        {
            return HResult.Ok;
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

        public HResult UpdatePlaceholderIfNeeded(string relativePath, DateTime creationTime, DateTime lastAccessTime, DateTime lastWriteTime, DateTime changeTime, uint fileAttributes, long endOfFile, byte[] contentId, byte[] epochId, UpdateType updateFlags, out UpdateFailureCause failureReason)
        {
            failureReason = this.UpdatePlaceholderIfNeededFailureCause;
            return this.UpdatePlaceholderIfNeededResult;
        }

        public HResult CreatePlaceholderAsHardlink(string destinationFileName, string hardLinkTarget)
        {
            throw new NotImplementedException();
        }

        public HResult ConvertDirectoryToPlaceholder(string targetDirectoryPath, byte[] contentId, byte[] providerId)
        {
            throw new NotImplementedException();
        }

        public WriteBuffer CreateWriteBuffer(uint desiredBufferSize)
        {
            this.waitForCreateWriteBuffer.Set();
            this.unblockCreateWriteBuffer.WaitOne();

            return new WriteBuffer(desiredBufferSize, 1);
        }

        public HResult WriteFile(Guid streamGuid, WriteBuffer buffer, ulong byteOffset, uint length)
        {
            return this.WriteFileReturnResult;
        }

        public HResult WritePlaceholderInformation(
            string relativePath,
            DateTime creationTime,
            DateTime lastAccessTime,
            DateTime lastWriteTime,
            DateTime changeTime,
            uint fileAttributes,
            long endOfFile,
            bool isDirectory,
            byte[] contentId,
            byte[] epochId)
        {
            this.CreatedPlaceholders.Add(relativePath);
            this.placeholderCreated.Set();
            return HResult.Ok;
        }

        public void CompleteCommand(int commandId, HResult completionResult)
        {
            this.CompletionResult = completionResult;
            this.commandCompleted.Set();
        }

        public HResult WaitForCompletionStatus()
        {
            this.commandCompleted.WaitOne();
            return this.CompletionResult;
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

        public void Dispose()
        {
            this.Dispose(true);
            GC.SuppressFinalize(this);
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
