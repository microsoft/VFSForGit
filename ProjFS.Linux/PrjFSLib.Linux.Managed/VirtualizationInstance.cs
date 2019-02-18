using System;
using System.Runtime.InteropServices;

namespace PrjFSLib.Linux
{
    public class VirtualizationInstance
    {
        public const int PlaceholderIdLength = Interop.PrjFSLib.PlaceholderIdLength;

        private IntPtr mountHandle = IntPtr.Zero;

        // We must hold a reference to the delegate to prevent garbage collection
        private NotifyOperationCallback preventGCOnNotifyOperationDelegate;

        // References held to these delegates via class properties
        public virtual EnumerateDirectoryCallback OnEnumerateDirectory { get; set; }
        public virtual GetFileStreamCallback OnGetFileStream { get; set; }

        public virtual NotifyFileModified OnFileModified { get; set; }
        public virtual NotifyPreDeleteEvent OnPreDelete { get; set; }
        public virtual NotifyNewFileCreatedEvent OnNewFileCreated { get; set; }
        public virtual NotifyFileRenamedEvent OnFileRenamed { get; set; }
        public virtual NotifyHardLinkCreatedEvent OnHardLinkCreated { get; set; }

        public virtual Result StartVirtualizationInstance(
            string storageRootFullPath,
            string virtualizationRootFullPath,
            uint poolThreadCount)
        {
            if (this.mountHandle != IntPtr.Zero)
            {
                throw new InvalidOperationException();
            }

            Interop.Callbacks callbacks = new Interop.Callbacks
            {
                OnEnumerateDirectory = this.OnEnumerateDirectory,
                OnGetFileStream = this.OnGetFileStream,
                OnNotifyOperation = this.preventGCOnNotifyOperationDelegate = new NotifyOperationCallback(this.OnNotifyOperation),
            };

            return Interop.PrjFSLib.StartVirtualizationInstance(
                storageRootFullPath,
                virtualizationRootFullPath,
                callbacks,
                poolThreadCount,
                ref this.mountHandle);
        }

        public virtual void StopVirtualizationInstance()
        {
            Interop.PrjFSLib.StopVirtualizationInstance(this.mountHandle);
            this.mountHandle = IntPtr.Zero;
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
            UpdateFailureCause deleteFailureCause = UpdateFailureCause.NoFailure;
            Result result = Interop.PrjFSLib.DeleteFile(
                relativePath,
                updateFlags,
                ref deleteFailureCause);

            failureCause = deleteFailureCause;
            return result;
        }

        public virtual Result WritePlaceholderDirectory(
            string relativePath)
        {
            return Interop.PrjFSLib.WritePlaceholderDirectory(
                this.mountHandle,
                relativePath);
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
                this.mountHandle,
                relativePath,
                providerId,
                contentId,
                fileSize,
                fileMode);
        }

        public virtual Result WriteSymLink(
            string relativePath,
            string symLinkTarget)
        {
            return Interop.PrjFSLib.WriteSymLink(
                this.mountHandle,
                relativePath,
                symLinkTarget);
        }

        public virtual Result UpdatePlaceholderIfNeeded(
            string relativePath,
            byte[] providerId,
            byte[] contentId,
            ulong fileSize,
            ushort fileMode,
            UpdateType updateFlags,
            out UpdateFailureCause failureCause)
        {
            if (providerId.Length != Interop.PrjFSLib.PlaceholderIdLength ||
                contentId.Length != Interop.PrjFSLib.PlaceholderIdLength)
            {
                throw new ArgumentException();
            }

            UpdateFailureCause updateFailureCause = UpdateFailureCause.NoFailure;
            Result result = Interop.PrjFSLib.UpdatePlaceholderFileIfNeeded(
                relativePath,
                providerId,
                contentId,
                fileSize,
                fileMode,
                updateFlags,
                ref updateFailureCause);

            failureCause = updateFailureCause;
            return result;
        }

        public virtual Result ReplacePlaceholderFileWithSymLink(
            string relativePath,
            string symLinkTarget,
            UpdateType updateFlags,
            out UpdateFailureCause failureCause)
        {
            UpdateFailureCause updateFailureCause = UpdateFailureCause.NoFailure;
            Result result = Interop.PrjFSLib.ReplacePlaceholderFileWithSymLink(
                relativePath,
                symLinkTarget,
                updateFlags,
                ref updateFailureCause);

            failureCause = updateFailureCause;
            return result;
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
            NotificationType notificationType)
        {
            switch (notificationType)
            {
                case NotificationType.PreDelete:
                    return this.OnPreDelete(relativePath, isDirectory);

                case NotificationType.FileModified:
                    this.OnFileModified(relativePath);
                    return Result.Success;

                case NotificationType.NewFileCreated:
                    this.OnNewFileCreated(relativePath, isDirectory);
                    return Result.Success;

                case NotificationType.FileRenamed:
                    this.OnFileRenamed(relativePath, isDirectory);
                    return Result.Success;

                case NotificationType.HardLinkCreated:
                    this.OnHardLinkCreated(relativePath);
                    return Result.Success;
            }

            return Result.ENotYetImplemented;
        }
    }
}
