using System;
using System.Runtime.InteropServices;

namespace PrjFSLib.Mac
{
    public class VirtualizationInstance
    {
        public const int PlaceholderIdLength = Interop.PrjFSLib.PlaceholderIdLength;

        // PrjFSLib delegate - We must hold a reference to prevent garbage collection
        private readonly NotifyOperationCallback notifyOperationCallback;

        public VirtualizationInstance()
        {
            this.notifyOperationCallback = this.OnNotifyOperation;
        }

        public virtual EnumerateDirectoryCallback OnEnumerateDirectory { get; set; }
        public virtual GetFileStreamCallback OnGetFileStream { get; set; }

        public virtual NotifyFileModified OnFileModified { get; set; }

        public static Result ConvertDirectoryToVirtualizationRoot(string fullPath)
        {
            return Interop.PrjFSLib.ConvertDirectoryToVirtualizationRoot(fullPath);
        }

        public virtual Result StartVirtualizationInstance(
            string virtualizationRootFullPath,
            uint poolThreadCount)
        {
            Interop.Callbacks callbacks = new Interop.Callbacks
            {
                OnEnumerateDirectory = this.OnEnumerateDirectory,
                OnGetFileStream = this.OnGetFileStream,

                // Note: We must use this.notifyOperationCallback and not this.OnNotifyOperation
                // to ensure we don't create a temporary delegate that will be garbage collected
                OnNotifyOperation = this.notifyOperationCallback,
            };

            return Interop.PrjFSLib.StartVirtualizationInstance(
                virtualizationRootFullPath,
                callbacks,
                poolThreadCount);
        }

        public virtual Result StopVirtualizationInstance()
        {
            throw new NotImplementedException();
        }

        public virtual Result WriteFileContents(
            IntPtr fileHandle,
            byte[] bytes,
            uint byteCount)
        {
            GCHandle bytesHandle = GCHandle.Alloc(bytes, GCHandleType.Pinned);
            try
            {
                return Interop.PrjFSLib.WriteFileContents(
                    fileHandle,
                    bytesHandle.AddrOfPinnedObject(),
                    byteCount);
            }
            finally
            {
                bytesHandle.Free();
            }
        }

        public virtual Result DeleteFile(
            string relativePath,
            UpdateType updateFlags,
            out UpdateFailureCause failureCause)
        {
            throw new NotImplementedException();
        }

        public virtual Result WritePlaceholderDirectory(
            string relativePath)
        {
            return Interop.PrjFSLib.WritePlaceholderDirectory(relativePath);
        }

        public virtual Result WritePlaceholderFile(
            string relativePath,
            byte[] providerId,
            byte[] contentId,
            ulong fileSize,
            ushort fileMode)
        {
            if (providerId.Length != Interop.PrjFSLib.PlaceholderIdLength ||
                contentId.Length != Interop.PrjFSLib.PlaceholderIdLength)
            {
                throw new ArgumentException();
            }

            return Interop.PrjFSLib.WritePlaceholderFile(
                relativePath,
                providerId,
                contentId,
                fileSize,
                fileMode);
        }

        public virtual Result UpdatePlaceholderIfNeeded(
            string relativePath,
            byte[] providerId,
            byte[] contentId,
            ulong fileSize,
            UpdateType updateFlags,
            out UpdateFailureCause failureCause)
        {
            throw new NotImplementedException();
        }

        public virtual Result CompleteCommand(
            ulong commandId,
            Result result)
        {
            throw new NotImplementedException();
        }

        public virtual Result ConvertDirectoryToPlaceholder(
            string relativeDirectoryPath)
        {
            throw new NotImplementedException();
        }

        private Result OnNotifyOperation(
            ulong commandId,
            string relativePath,
            byte[] providerId,
            byte[] contentId,
            int triggeringProcessId,
            string triggeringProcessName,
            bool isDirectory,
            NotificationType notificationType,
            string destinationRelativePath)
        {
            switch (notificationType)
            {
                case NotificationType.FileModified:
                    this.OnFileModified(relativePath);
                    return Result.Success;
            }

            return Result.ENotYetImplemented;
        }
    }
}
