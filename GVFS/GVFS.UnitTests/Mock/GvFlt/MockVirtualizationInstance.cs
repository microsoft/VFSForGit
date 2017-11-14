using GVFS.Common;
using GvLib;
using System;
using System.Threading;

namespace GVFS.UnitTests.Mock.GvFlt
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

            this.WriteFileReturnStatus = NtStatus.Success;
        }

        public NtStatus CompletionStatus { get; set; }

        public ConcurrentHashSet<string> CreatedPlaceholders { get; private set; }

        public CancelCommandEvent OnCancelCommand { get; set; }
        public EndDirectoryEnumerationEvent OnEndDirectoryEnumeration { get; set; }
        public GetDirectoryEnumerationEvent OnGetDirectoryEnumeration { get; set; }
        public GetFileStreamEvent OnGetFileStream { get; set; }
        public GetPlaceholderInformationEvent OnGetPlaceholderInformation { get; set; }
        public NotifyPostCreateHandleOnlyEvent OnNotifyPostCreateHandleOnly { get; set; }
        public NotifyPostCreateNewFileEvent OnNotifyPostCreateNewFile { get; set; }
        public NotifyPostCreateOverwrittenOrSupersededEvent OnNotifyPostCreateOverwrittenOrSuperseded { get; set; }
        public NotifyFileHandleClosedOnlyEvent OnNotifyFileHandleClosedOnly { get; set; }
        public NotifyFileHandleClosedModifiedOrDeletedEvent OnNotifyFileHandleClosedModifiedOrDeleted { get; set; }
        public NotifyFileRenamedEvent OnNotifyFileRenamed { get; set; }
        public NotifyFirstWriteEvent OnNotifyFirstWrite { get; set; }
        public NotifyHardlinkCreatedEvent OnNotifyHardlinkCreated { get; set; }
        public NotifyPreDeleteEvent OnNotifyPreDelete { get; set; }
        public NotifyPreRenameEvent OnNotifyPreRename { get; set; }
        public NotifyPreSetHardlinkEvent OnNotifyPreSetHardlink { get; set; }
        public QueryFileNameEvent OnQueryFileName { get; set; }
        public StartDirectoryEnumerationEvent OnStartDirectoryEnumeration { get; set; }

        public NtStatus WriteFileReturnStatus { get; set; }

        public HResult StartVirtualizationInstance(
            string virtualizationRootPath, 
            uint poolThreadCount, 
            uint concurrentThreadCount,
            bool enableNegativePathCache,
            NotificationType globalNotificationMask,
            ref uint logicalBytesPerSector,
            ref uint writeBufferAlignment)
        {
            logicalBytesPerSector = 1;
            writeBufferAlignment = 1;

            return HResult.Ok;
        }

        public HResult StopVirtualizationInstance()
        {
            throw new NotImplementedException();
        }

        public HResult DetachDriver()
        {
            throw new NotImplementedException();
        }

        public NtStatus ClearNegativePathCache(ref uint totalEntryNumber)
        {
            throw new NotImplementedException();
        }

        public NtStatus DeleteFile(string relativePath, UpdateType updateFlags, ref UpdateFailureCause failureReason)
        {
            throw new NotImplementedException();
        }

        public NtStatus UpdatePlaceholderIfNeeded(string relativePath, DateTime creationTime, DateTime lastAccessTime, DateTime lastWriteTime, DateTime changeTime, uint fileAttributes, long endOfFile, byte[] contentId, byte[] epochId, UpdateType updateFlags, ref UpdateFailureCause failureReason)
        {
            throw new NotImplementedException();
        }

        public NtStatus CreatePlaceholderAsHardlink(string destinationFileName, string hardLinkTarget)
        {
            throw new NotImplementedException();
        }

        public WriteBuffer CreateWriteBuffer(uint desiredBufferSize)
        {
            this.waitForCreateWriteBuffer.Set();
            this.unblockCreateWriteBuffer.WaitOne();

            return new WriteBuffer(desiredBufferSize, 1);
        }

        public NtStatus WriteFile(Guid streamGuid, WriteBuffer buffer, ulong byteOffset, uint length)
        {
            return this.WriteFileReturnStatus;
        }

        public NtStatus WritePlaceholderInformation(
            string relativePath, 
            DateTime creationTime, 
            DateTime lastAccessTime, 
            DateTime lastWriteTime, 
            DateTime changeTime, 
            uint fileAttributes, 
            long endOfFile, 
            bool directory, 
            byte[] contentId, 
            byte[] epochId)
        {
            this.CreatedPlaceholders.Add(relativePath);
            this.placeholderCreated.Set();
            return NtStatus.Success;
        }

        public void CompleteCommand(int commandId, NtStatus completionStatus)
        {
            this.CompletionStatus = completionStatus;
            this.commandCompleted.Set();
        }

        public NtStatus WaitForCompletionStatus()
        {
            this.commandCompleted.WaitOne();
            return this.CompletionStatus;
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
